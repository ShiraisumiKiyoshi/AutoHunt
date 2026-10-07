using AutoHunt.Tasks;
using ECommons.GameHelpers;
using ECommons.Automation.NeoTaskManager;
namespace AutoHunt;

/// <summary>
/// 副本区控制器：
/// 1) 首次传送到可切副本区的地图时，保证自己处于 1 号副本区；
/// 2) 通过扫描战斗对象统计「玩家参与击杀」的怪物数量；
/// 3) 击杀满 N 只（默认 2 只）后设置 pendingSwitchInstance，等待车头发送新坐标后传送切换。
/// </summary>
internal static unsafe class InstanceController
{
    private static bool pendingEnsureInstanceOne = false;
    private static readonly HashSet<uint> ensuredTerritories = new();
    private static uint lastWorldId = 0; // 检测换服（跨区/回本区）：换服后"首次进图保证1号区"需对所有地图重新生效

    /// <summary>参与过（正在打/打过）的怪物</summary>
    private static readonly HashSet<ulong> engagedMobIds = new();
    /// <summary>已计入击杀数量的死亡怪物（防重复计数）</summary>
    private static readonly HashSet<ulong> countedMobIds = new();
    /// <summary>已评估为非狩猎怪、明确不计数过的死亡怪物（防重复评估）。
    /// 与 countedMobIds 分离：曾经评估为"不计数"不得阻塞后续 forceCount 补计
    /// （修复：数据库未收录的怪被 ScanKills 预标记后，HuntController 的主动补计被挡住）</summary>
    private static readonly HashSet<ulong> skippedMobIds = new();
    /// <summary>插件主动选中过的怪（HuntController.TrackTarget 标记）。
    /// 这些怪死亡时即使狩猎怪数据库未收录也照常计数。</summary>
    private static readonly HashSet<ulong> markedMobIds = new();

    private static int killCount = 0;
    private static uint lastInstanceId = 0;

    /// <summary>击杀满后等待车头新坐标再切换的目标副本区号；0 = 无待切换</summary>
    private static int pendingSwitchInstance = 0;

    /// <summary>pendingSwitchInstance 是否为本地图最后一个区的回绕（current≥count，next=1）。
    /// 回绕时不能立即切区——应等待车头发下一地图坐标，把"到新图切 1 号区"随传送带上；
    /// 只有非回绕（同图还有下一个区）才允许击杀满后立即切换。</summary>
    private static bool pendingSwitchImmediateOk = false;

    /// <summary>pendingSwitchInstance 被设置的时刻（用于兜底触发判断）。</summary>
    private static DateTime pendingSwitchSetAt = DateTime.MinValue;

    /// <summary>已知存在分线的地图（读到过非 0 区号 / 收到车头切线指令 / 成功切换过）。
    /// 原生 InstanceId 是"分线选择数据"的一部分，会间歇性读到 0（数据未加载时），
    /// 不能凭一次 0 就断定该地图没有分线。</summary>
    private static readonly HashSet<uint> lineCapableTerritories = new();

    /// <summary>各地图最近一次已知区号（原生读数或我方切换目标）。
    /// 原生读数偶发为 0 时用它兜底，避免"击杀满却算不出目标区号 → 不切区"。</summary>
    private static readonly Dictionary<uint, int> lastKnownLineByTerritory = new();

    /// <summary>车头指定、待到达对应地图后再执行的切线目标（车头常在换图时一并下达切线指令）。
    /// 带时间戳：超过 AnnouncedLineValidMinutes 未执行的指令视为过期（避免旧指令在很久后突然生效）。</summary>
    private static readonly Dictionary<uint, (int Line, DateTime At)> announcedLineByTerritory = new();

    /// <summary>车头切线指令的有效期（分钟）。</summary>
    private const double AnnouncedLineValidMinutes = 30.0;

    /// <summary>击杀满后等待多久仍无人消费切区计划（无新车头坐标且战斗流程早已结束）就兜底主动切区。</summary>
    private const double PendingSwitchStuckSeconds = 15.0;

    // 缓存的副本区信息（避免 UI / 高频逻辑反复调 IPC）
    private static int cachedInstanceCount = 0;
    private static int cachedCurrentInstance = 0;

    public static int KillCount => killCount;
    public static int PendingSwitchInstance => pendingSwitchInstance;
    /// <summary>击杀满后是否允许立即切换副本区（同图还有下一个区）</summary>
    public static bool PendingSwitchImmediateOk => pendingSwitchImmediateOk;
    public static int CachedInstanceCount => cachedInstanceCount;
    public static int CachedCurrentInstance => cachedCurrentInstance;

    /// <summary>
    /// 本区击杀是否已满（即状态页显示 2/2 的时刻）。
    /// 注意：可切区地图上击杀满时 killCount 会被清零转入待切换状态，
    /// 因此「击杀数 ≥ 配置值」或「存在待切换副本区」任一成立即为已满。
    /// 不可切区地图上击杀数会保持满值，同样能判定。
    /// </summary>
    public static bool ZoneCleared => killCount >= P.Config.KillsPerInstance || pendingSwitchInstance != 0;

    /// <summary>切换地图时调用。</summary>
    public static void OnTerritoryChanged(uint territory)
    {
        killCount = 0;
        engagedMobIds.Clear();
        countedMobIds.Clear();
        skippedMobIds.Clear();
        markedMobIds.Clear();
        cachedInstanceCount = 0;
        cachedCurrentInstance = 0;
        // 手动传送/切图后，"击杀满等待新坐标切区"的计划已过期：
        // 不清除的话 ZoneCleared 恒为 true，到达结束地图会立刻误触发解散跨区
        pendingSwitchInstance = 0;
        pendingSwitchImmediateOk = false;
        pendingSwitchSetAt = DateTime.MinValue;
        // 注意：首次进图"保证 1 号副本区"的检测不在事件里做——
        // TerritoryChanged 触发瞬间（读图中）副本区数据尚未就绪，GetInstanceCount 返回 1，
        // 在这里判定会错过时机且不会重试；改由 Update() 每秒重试直到读到有效数据。
    }

    /// <summary>
    /// 当前是否处于可切换副本区的地图（游戏原生判定：InstanceId≠0）。
    /// 不要用 Lifestream 的 GetInstanceCount 判定——它依赖 Lifestream 自己"学习"的地图数据，
    /// 未学习过的地图返回 0，会导致可切区的地图被误判为不可切。
    /// </summary>
    public static bool IsInstancedAreaNow()
    {
        var ui = FFXIVClientStructs.FFXIV.Client.Game.UI.UIState.Instance();
        return ui != null && ui->PublicInstance.InstanceId != 0;
    }

    /// <summary>当前所在副本区号（游戏原生，0=不可切区地图）。</summary>
    public static int GetNativeInstanceId()
    {
        var ui = FFXIVClientStructs.FFXIV.Client.Game.UI.UIState.Instance();
        return ui != null ? (int)ui->PublicInstance.InstanceId : 0;
    }

    /// <summary>外部请求保证 1 号副本区（跨图传送到达后）。</summary>
    public static void RequestEnsureInstanceOne() => pendingEnsureInstanceOne = true;

    /// <summary>当前地图是否有分线：原生读数（非 0）或已有历史证据（读到过/车头切线/切换过）。</summary>
    public static bool IsLineCapableHere()
        => IsInstancedAreaNow() || lineCapableTerritories.Contains(Svc.ClientState.TerritoryType);

    /// <summary>该地图是否有分线的历史证据。</summary>
    public static bool HasLineEvidence(uint territory) => lineCapableTerritories.Contains(territory);

    /// <summary>狩猎流程是否正处于"不可打断"的阶段（传送/选怪/攻击/输出/收尾）。
    /// 切分线会下坐骑并触发读图，必须避开这些阶段，否则会把正在进行的战斗打断在半途。
    /// 反过来 Idle / Mounting / Navigating / Arrived 阶段切区是安全的（切完由主循环重放坐标）。</summary>
    private static bool IsHuntFlowBusy() =>
        HuntController.CurrentState is HuntController.State.Teleporting
            or HuntController.State.Targeting
            or HuntController.State.Attacking
            or HuntController.State.Descending
            or HuntController.State.Dismounting
            or HuntController.State.Outputting
            or HuntController.State.Finished;

    /// <summary>该地图最近一次已知区号（0 = 未知）。</summary>
    public static int GetLastKnownLine(uint territory)
        => lastKnownLineByTerritory.TryGetValue(territory, out var v) ? v : 0;

    /// <summary>
    /// 当前区号：原生优先；原生读到 0（分线选择数据未加载，实测会间歇性发生）时，
    /// 用该地图最近一次已知区号兜底。两条都拿不到才返回 0（真未知）。
    /// </summary>
    public static int GetBestKnownInstanceId()
    {
        var territory = Svc.ClientState.TerritoryType;
        var native = GetNativeInstanceId();
        if (native != 0)
        {
            lineCapableTerritories.Add(territory);
            lastKnownLineByTerritory[territory] = native;
            return native;
        }
        return GetLastKnownLine(territory);
    }

    /// <summary>记录一次已知区号（原生读到 / 我方切换目标）。</summary>
    public static void NoteKnownLine(int line, uint? territory = null)
    {
        if (line <= 0) return;
        var t = territory ?? Svc.ClientState.TerritoryType;
        if (t == 0) return;
        lineCapableTerritories.Add(t);
        lastKnownLineByTerritory[t] = line;
    }

    /// <summary>
    /// 车头切线指令（"请在坐标X②的大水晶切换到"X②""/"该换线啦…换②线"）。
    /// 车头指令是权威依据：原生区号读数不可靠时也能据此切区。
    /// territory = 指令指向的地图；line = 目标分线号（1..9）。
    /// </summary>
    public static void OnConductorSwitchInstruction(uint territory, int line)
    {
        if (line <= 0) return;
        if (territory == 0) territory = Svc.ClientState.TerritoryType;
        if (territory == 0) return;

        lineCapableTerritories.Add(territory);
        announcedLineByTerritory[territory] = (line, DateTime.Now);
        PluginLog.Information(territory == Svc.ClientState.TerritoryType
            ? $"[AutoHunt] 车头切线指令：当前地图切到 {line} 号区（待执行）"
            : $"[AutoHunt] 车头切线指令：地图 {territory} 切到 {line} 号区（到达后执行）");
    }

    /// <summary>
    /// 标记一只怪为"插件主动选中过"：该怪死亡时即使狩猎怪数据库未收录
    /// （B 级被排除/新狩猎怪未收录）也照常计入击杀数。
    /// 由 HuntController.TrackTarget 调用——怪之后即使目标丢失、流程提前结束，
    /// 死亡时仍能被 ScanKills 识别并计数。
    /// </summary>
    public static void MarkEngaged(ulong mobId)
    {
        if (mobId != 0) markedMobIds.Add(mobId);
    }

    /// <summary>取出待切换的副本区号（并清空等待状态）。</summary>
    public static int ConsumePendingSwitch()
    {
        var n = pendingSwitchInstance;
        pendingSwitchInstance = 0;
        pendingSwitchImmediateOk = false;
        pendingSwitchSetAt = DateTime.MinValue;
        return n;
    }

    public static void Update()
    {
        if (S.LifestreamIPC == null) return;

        // 副本区变化时重置击杀计数（切换副本区/进入新地图）
        // 注意：登录/读图瞬间 UIState.Instance() 可能为 null，必须判空
        var uiState = FFXIVClientStructs.FFXIV.Client.Game.UI.UIState.Instance();
        if (uiState != null)
        {
            var instId = uiState->PublicInstance.InstanceId;
            // ⚠️ instId==0 只代表「当前地图不可切副本区」或「读图瞬间数据未就绪」，
            // 并不代表真的换了副本区。旧代码对 0 也走"已变化"分支，会把刚攒够的
            // 击杀计数与待切换计划一起清零（典型症状：击杀满却不切区、计数莫名归零）。
            // 因此这里只认非 0 的变化，并且只有从已知区号变化时才做重置。
            if (instId != 0 && instId != lastInstanceId)
            {
                var prev = lastInstanceId;
                lastInstanceId = instId;
                // 读到非 0 区号 = 该地图确有分线（记录证据 + 记住当前区号）
                lineCapableTerritories.Add(Svc.ClientState.TerritoryType);
                lastKnownLineByTerritory[Svc.ClientState.TerritoryType] = (int)instId;
                if (prev != 0)
                {
                    killCount = 0;
                    countedMobIds.Clear();
                    skippedMobIds.Clear();
                    engagedMobIds.Clear();
                    markedMobIds.Clear();
                    // 副本区已变化（含手动切换）：原计划的切区目标作废，
                    // 否则僵尸 pendingSwitch 会让 ZoneCleared 恒为 true（结束地图误解散）
                    pendingSwitchInstance = 0;
                    pendingSwitchImmediateOk = false;
                    pendingSwitchSetAt = DateTime.MinValue;
                    PluginLog.Information($"[AutoHunt] 副本区已切换：{prev} → {instId}，击杀计数与待切换计划已重置");
                }
                else
                {
                    PluginLog.Information($"[AutoHunt] 进入副本区 {instId}，击杀计数从 0 开始");
                }
            }
        }

        // 扫描战斗对象，统计玩家参与击杀的怪物（250ms 一次足够）
        if (EzThrottler.Throttle("WYKillScan", 250))
        {
            ScanKills();
        }

        // 定期刷新副本区信息缓存（供 UI 显示）
        if (EzThrottler.Throttle("WYInstanceCache", 2000))
        {
            cachedInstanceCount = S.LifestreamIPC.GetInstanceCount();
            cachedCurrentInstance = S.LifestreamIPC.GetCurrentInstanceNumber();
        }

        // 车头切线指令（权威依据）：车头常在报坐标时一并指定分线（"到遗产之地 ② 号区水晶…"）。
        // 原生 InstanceId 会间歇性读到 0，只靠它判断区号不可靠 —— 车头指令是更权威的来源。
        // 到达指令指向的地图、且狩猎流程处于"可打断"阶段（非战斗/输出）时执行。
        if (P.Config.Enabled && P.Config.AutoInstance && announcedLineByTerritory.Count > 0
            && pendingSwitchInstance == 0 && !P.SwitchInProgress && !P.TaskManager.IsBusy
            && !Svc.Condition[ConditionFlag.InCombat] && !Svc.Condition[ConditionFlag.Casting]
            && !IsHuntFlowBusy()
            && Player.Interactable && IsScreenReady()
            && !Svc.Condition[ConditionFlag.BetweenAreas] && !Svc.Condition[ConditionFlag.BetweenAreas51])
        {
            var here = Svc.ClientState.TerritoryType;
            if (announcedLineByTerritory.TryGetValue(here, out var announced))
            {
                announcedLineByTerritory.Remove(here);
                var (announcedLine, announcedAt) = announced;
                if ((DateTime.Now - announcedAt).TotalMinutes > AnnouncedLineValidMinutes)
                {
                    PluginLog.Information($"[AutoHunt] 车头切线指令（{announcedLine} 号区）已过期（{AnnouncedLineValidMinutes:0} 分钟），忽略");
                }
                else if (announcedLine > 0 && GetBestKnownInstanceId() != announcedLine)
                {
                    // 切区会触发传送读图、打断寻路：先把当前车头坐标暂存，
                    // 切区完成后由主循环统一重放 —— 否则切完线就停在原地不再去车头坐标。
                    P.HeldCoordinate ??= HuntController.CurrentPendingTarget;
                    P.SwitchInProgress = true;
                    P.SwitchStartTime = DateTime.Now;
                    Notify.Info($"按车头指令切换到 {announcedLine} 号副本区…");
                    PluginLog.Information($"[AutoHunt] 执行车头切线指令：地图 {here} → {announcedLine} 号区"
                        + $"（当前区号 {GetBestKnownInstanceId()}，暂存坐标 {(P.HeldCoordinate != null ? "有" : "无")}）");
                    TaskEnsureInstance.Enqueue(announcedLine);
                }
                else
                {
                    PluginLog.Information($"[AutoHunt] 车头切线指令（{announcedLine} 号区）与当前区号一致，跳过");
                }
            }
        }

        // 兜底触发切区：击杀已满 + 允许立即切区，但超过 15 秒仍无人消费该计划
        // （既没有车头发来新坐标，HuntController 也早已回到 Idle —— 典型是击杀发生在
        //  插件战斗流程之外）。没有这一步，pendingSwitch 会变成僵尸、表现为"击杀满却不切区"。
        if (pendingSwitchInstance != 0 && pendingSwitchImmediateOk && !P.SwitchInProgress
            && pendingSwitchSetAt != DateTime.MinValue
            && (DateTime.Now - pendingSwitchSetAt).TotalSeconds > PendingSwitchStuckSeconds
            && HuntController.CurrentState == HuntController.State.Idle
            && !P.TaskManager.IsBusy
            && Player.Interactable && IsScreenReady()
            && !Svc.Condition[ConditionFlag.BetweenAreas] && !Svc.Condition[ConditionFlag.BetweenAreas51]
            && IsLineCapableHere())
        {
            var fallbackTarget = ConsumePendingSwitch();
            P.SwitchInProgress = true;
            P.SwitchStartTime = DateTime.Now;
            Notify.Info($"击杀已满且长时间未收到新车头坐标，立即切换到 {fallbackTarget} 号副本区…");
            PluginLog.Information($"[AutoHunt] 兜底触发副本区切换：击杀满后 {PendingSwitchStuckSeconds:0} 秒无新坐标且战斗流程空闲 → 目标 {fallbackTarget} 号区");
            TaskEnsureInstance.Enqueue(fallbackTarget);
        }

        // 首次进入可切副本区的地图 → 保证 1 号副本区
        // 读图后副本区数据延迟就绪，这里每秒重试；用原生判定（Lifestream 的
        // GetInstanceCount 依赖其"学习"的地图数据，未学习过的地图返回 0，不可靠）
        if (P.Config.Enabled && P.Config.AutoInstance && EzThrottler.Throttle("WYEnsureScan", 1000))
        {
            // 换服检测：地图 ID 全大区通用，跨区后 ensuredTerritories 里的记录
            // 会让"首次进图保证 1 号区"被误跳过（本区去过 ≠ 新区去过）→ 换服即清空重新判定
            if (Player.Available)
            {
                var wid = Player.Object.CurrentWorld.RowId;
                if (wid != 0 && wid != lastWorldId)
                {
                    lastWorldId = wid;
                    ensuredTerritories.Clear();
                    Dbg.Log($" 检测到换服（WorldId={wid}），已重置各地图的首次进图副本区保证记录");
                }
            }

            var territory = Svc.ClientState.TerritoryType;
            if (territory != 0 && !ensuredTerritories.Contains(territory) && IsLineCapableHere())
            {
                ensuredTerritories.Add(territory);
                pendingEnsureInstanceOne = true;
            }
        }

        if (!pendingEnsureInstanceOne) return;
        if (P.TaskManager.IsBusy) return;
        if (!Player.Interactable || !IsScreenReady()) return;
        if (Svc.Condition[ConditionFlag.BetweenAreas] || Svc.Condition[ConditionFlag.BetweenAreas51]) return;

        pendingEnsureInstanceOne = false;
        if (!IsLineCapableHere()) return;

        // 车头已针对本图下达切线指令 → 以车头指令为准，不抢着切 1 号区
        // （否则刚落地就被切到 1 号区，与车头要求的区号打架，来回切）
        if (announcedLineByTerritory.ContainsKey(Svc.ClientState.TerritoryType))
        {
            P.HeldCoordinate ??= HuntController.CurrentPendingTarget;
            PluginLog.Information("[AutoHunt] 本图已有车头切线指令，跳过「首次进入保证 1 号区」，交由车头指令执行");
            return;
        }

        if (GetBestKnownInstanceId() != 1)
        {
            Notify.Info("首次进入该地图，切换到 1 号副本区…");
            // 切区会打断寻路：先暂存当前车头坐标，切区完成后由主循环重放
            P.HeldCoordinate ??= HuntController.CurrentPendingTarget;
            HuntController.Reset();
            TaskEnsureInstance.Enqueue(1);
        }
    }

    /// <summary>
    /// 扫描周围战斗对象：标记玩家参与的怪物，检测其死亡并计数。
    /// 参与判定（满足任一）：
    ///  - 怪物是自己的当前目标（我们正在打它）；
    ///  - 怪物的目标是玩家自己 / 队友 / 玩家的宠物或陆行鸟。
    /// 计数规则：只有狩猎怪（HuntMobDatabase 判定）的击杀才计入副本区切换计数。
    /// </summary>
    private static void ScanKills()
    {
        if (!P.Config.Enabled || !Player.Available) return;

        var me = Player.Object;
        if (me == null) return;
        ulong myId = me.GameObjectId;

        // 收集「我方阵营」的对象 ID：自己 + 队友 + 自己的宠物/陆行鸟
        var allyIds = new HashSet<ulong> { myId };
        foreach (var member in Svc.Party)
        {
            if (member.GameObject != null)
            {
                allyIds.Add(member.GameObject.GameObjectId);
            }
        }
        foreach (var obj in Svc.Objects)
        {
            if (obj is IBattleNpc pet && pet.OwnerId == myId)
            {
                allyIds.Add(pet.GameObjectId);
            }
        }

        ulong myTargetId = Svc.Targets.Target?.GameObjectId ?? 0;

        foreach (var obj in Svc.Objects)
        {
            if (obj is not IBattleNpc npc) continue;

            if (!npc.IsDead)
            {
                // 存活：判断是否为玩家参与的战斗对象
                bool engaged = npc.GameObjectId == myTargetId
                    || allyIds.Contains(npc.TargetObjectId);
                if (engaged) engagedMobIds.Add(npc.GameObjectId);
            }
            else
            {
                // 死亡：若之前参与过且未评估 → 尝试计数。
                // 注意：不再预先 countedMobIds.Add——集合管理统一交给 OnMobKilled，
                // 避免"评估为不计数"的怪被误标记成"已计数"而挡住 forceCount 补计。
                if (engagedMobIds.Contains(npc.GameObjectId)
                    && !countedMobIds.Contains(npc.GameObjectId)
                    && !skippedMobIds.Contains(npc.GameObjectId))
                {
                    // 插件主动选中过的怪（marked）即使数据库未收录也计数
                    OnMobKilled(npc.GameObjectId, npc.NameId, markedMobIds.Contains(npc.GameObjectId));
                }
            }
        }

        // 调试：每 3 秒汇总一次扫描结果。"击杀满却不切区"绝大多数是这里没数到怪，
        // 这一行能直接区分「参与判定没命中」/「不是狩猎怪」/「副本区号读不到」三种情况。
        if (P.Config.Debug && EzThrottler.Throttle("WYScanDiag", 3000))
        {
            var aliveEngaged = 0;
            var huntAlive = 0;
            var deadPending = 0;
            foreach (var o in Svc.Objects)
            {
                if (o is not IBattleNpc b) continue;
                if (b.IsDead)
                {
                    if (engagedMobIds.Contains(b.GameObjectId)
                        && !countedMobIds.Contains(b.GameObjectId)
                        && !skippedMobIds.Contains(b.GameObjectId)) deadPending++;
                    continue;
                }
                if (engagedMobIds.Contains(b.GameObjectId)) aliveEngaged++;
                if (HuntMobDatabase.IsHuntMob(b.NameId, P.Config.IncludeBRank)) huntAlive++;
            }
            Dbg.Log($"击杀扫描汇总: 参与中存活 {aliveEngaged} 只 / 待计数尸体 {deadPending} 具 / 视野内狩猎怪 {huntAlive} 只"
                + $" | 已计数 {killCount}/{P.Config.KillsPerInstance} | 待切换={(pendingSwitchInstance == 0 ? "无" : pendingSwitchInstance + " 号区")}"
                + $" | 原生副本区={GetNativeInstanceId()} | Lifestream区号={cachedCurrentInstance}/共{cachedInstanceCount}区"
                + $" | 当前目标={(Svc.Targets.Target?.Name.TextValue ?? "无")} | HuntState={HuntController.CurrentState}");
        }

        // 防止集合无限增长：定期清理已消失的对象
        if (countedMobIds.Count > 200)
        {
            countedMobIds.RemoveWhere(id => Svc.Objects.FirstOrDefault(o => o.GameObjectId == id) == null);
            skippedMobIds.RemoveWhere(id => Svc.Objects.FirstOrDefault(o => o.GameObjectId == id) == null);
            engagedMobIds.RemoveWhere(id => !Svc.Objects.Any(o => o.GameObjectId == id));
            markedMobIds.RemoveWhere(id => !Svc.Objects.Any(o => o.GameObjectId == id));
        }
    }

    /// <summary>
    /// 一只玩家参与的怪物被击杀后调用。
    /// 去重集合分为两个：
    ///  - countedMobIds：已真正计数（任何后续调用直接跳过）；
    ///  - skippedMobIds：已评估为"非狩猎怪、不计数"（仅拦普通调用，
    ///    不阻塞 forceCount 补计——插件主动选中的怪以 forceCount 为准）。
    /// 通过 nameId 判定是否为狩猎怪（非狩猎怪不计入副本区切换计数）。
    /// forceCount=true 时跳过狩猎怪判定（插件主动选中的目标即使数据库未收录也计数）。
    /// </summary>
    public static void OnMobKilled(ulong mobId = 0, uint nameId = 0, bool forceCount = false)
    {
        // 去重：同一只怪不重复计数
        if (mobId != 0)
        {
            if (countedMobIds.Contains(mobId)) return;
            // 曾经被 ScanKills 评估为"不计数"的怪，若插件主动选中过（forceCount）则照常补计
            if (!forceCount && skippedMobIds.Contains(mobId)) return;
        }

        // 只有狩猎怪才计入副本区切换计数
        bool isHunt = nameId != 0 && HuntMobDatabase.IsHuntMob(nameId, P.Config.IncludeBRank);
        // nameId=0 时（HuntController 路径，目标已丢失无法获取 NameId）
        // 回退：假设是狩猎怪（HuntController 只会选中狩猎怪）
        bool countAsHunt = forceCount || isHunt || nameId == 0;

        if (!countAsHunt)
        {
            if (mobId != 0) skippedMobIds.Add(mobId);
            Dbg.Log($" 非狩猎怪击杀，不计入副本区计数 (NameId={nameId})");
            return;
        }

        if (mobId != 0) countedMobIds.Add(mobId);
        killCount++;
        // 击杀计数是低频且最关键的事件：无条件写 Information 级日志（不受 Dalamud 日志级别影响）
        // 日志带上地图与区号：排查"击杀满却没切区"时能直接看出当时在哪张地图的第几线
        PluginLog.Information($"[AutoHunt] 副本区击杀计数: {killCount}/{P.Config.KillsPerInstance}"
            + $" (地图 {Svc.ClientState.TerritoryType} {GetBestKnownInstanceId()}号区, NameId={nameId}, forceCount={forceCount})");

        if (!P.Config.AutoInstance) return;
        if (killCount < P.Config.KillsPerInstance) return;
        if (pendingSwitchInstance != 0)
        {
            PluginLog.Information($"[AutoHunt] 击杀已满但已有待切换计划（{pendingSwitchInstance} 号区），本次不重复设置");
            return; // 已在等待切换
        }

        // 当前区号：原生 InstanceId 优先，读不到（分线数据未加载，实测会间歇性发生）时
        // 用该地图最近一次已知区号兜底（含我方切换目标、车头切线指令）
        var current = GetBestKnownInstanceId();
        if (current == 0)
        {
            // 原生读不到副本区号：要么当前地图确实不可切区，要么分线选择数据尚未加载。
            // 绝不能静默返回——否则 killCount 会停在满值、再无任何提示与后续机会。
            PluginLog.Warning($"[AutoHunt] 击杀已满但无法确定当前区号（原生 InstanceId=0 且无历史记录，地图 {Svc.ClientState.TerritoryType}），无法安排切区；"
                + "如该地图有分线，等车头切线指令或手动切一次线即可让插件记住");
            if (P.Config.Debug) Dbg.Warn("原生 InstanceId=0 且无该地图历史区号：本次不切区");
            return;
        }

        killCount = 0;
        // Lifestream 学习到的该地图副本区总数（未学习过 / 分线数据未加载时为 0 或 1，不可靠）
        var count = S.LifestreamIPC.GetInstanceCount();
        // 已知最大区号 = Lifestream 总数 与 当前区号 取大。作用有二：
        //  a) count 不可靠（读到 0/1）时，仅凭"当前已在 2 号区"就能判定应当回绕，
        //     绝不会算出 3 号区这种不存在的目标（那会让 NoteKnownLine 记下假区号并污染后续判断）；
        //  b) count 可信时行为与旧版完全一致（current >= count → 回绕到 1）。
        var knownMax = Math.Max(count, current);
        bool wrap = knownMax > 1 && current >= knownMax;
        var next = wrap ? 1 : current + 1;
        pendingSwitchInstance = next;
        // 仅当确认处于最后一个区（回绕）时才等待车头坐标；其余情况击杀满后立即切换
        pendingSwitchImmediateOk = !wrap;
        pendingSwitchSetAt = DateTime.Now;

        PluginLog.Information($"[AutoHunt] 击杀已满：当前 {current} 号区（Lifestream 已知共 {count} 区，判定用上限 {knownMax}）→ 计划切到 {next} 号区，"
            + (pendingSwitchImmediateOk ? "立即切换" : "等待车头下一地图坐标"));

        if (pendingSwitchImmediateOk)
            Notify.Info($"已击杀 {P.Config.KillsPerInstance} 只狩猎怪，即将切换到 {next} 号副本区…");
        else
            Notify.Info($"已击杀 {P.Config.KillsPerInstance} 只狩猎怪，等待车头前往下一地图后切换到 {next} 号副本区…");
    }

    /// <summary>重置副本区记录（/ah reset）。</summary>
    public static void Reset()
    {
        ensuredTerritories.Clear();
        killCount = 0;
        pendingEnsureInstanceOne = false;
        pendingSwitchInstance = 0;
        pendingSwitchImmediateOk = false;
        pendingSwitchSetAt = DateTime.MinValue;
        announcedLineByTerritory.Clear();
        lastKnownLineByTerritory.Clear();
        // 注意：不清 lineCapableTerritories——"哪些地图有分线"是游戏世界的事实，与本次流程无关
        engagedMobIds.Clear();
        countedMobIds.Clear();
        skippedMobIds.Clear();
        markedMobIds.Clear();
    }
}
