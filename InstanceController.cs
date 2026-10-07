using ECommons.GameHelpers;
namespace AutoHunt;

/// <summary>
/// 副本区控制器（v2.4.0.25 起，副本区切换依赖 Daily Routines 的「快捷副本区切换」模块 /pdr insc）：
/// 1) 击杀数按「地图 + 副本区」分桶独立统计，换图 / 换副本区不清零；
///    只有跨区流程启动前、关闭插件总开关、手动「清空击杀数」才清零全部计数。
/// 2) 通过扫描战斗对象统计「玩家参与击杀」的怪物数量。
/// 3) 击杀满 N 只（默认 2 只）且车头下一坐标仍在本地图时：
///    传送到距离坐标最近的水晶 → 执行 /pdr insc (当前区号+1) → 切换完成后继续寻路。
///    通过车头坐标传送到不同地图时：执行 /pdr insc 1（DR 提示不存在可切换副本区 = 本图不分线）。
/// </summary>
internal static unsafe class InstanceController
{
    // ===== 击杀计数（按 地图+副本区 分桶） =====

    /// <summary>击杀计数桶：key = (地图 TerritoryType, 副本区号，0=未知/不分线)。</summary>
    private static readonly Dictionary<(uint Territory, int Line), int> killCounts = new();

    /// <summary>参与过（正在打/打过）的怪物</summary>
    private static readonly HashSet<ulong> engagedMobIds = new();
    /// <summary>已计入击杀数量的死亡怪物（防重复计数）</summary>
    private static readonly HashSet<ulong> countedMobIds = new();
    /// <summary>已评估为非狩猎怪、明确不计数过的死亡怪物（防重复评估）。
    /// 与 countedMobIds 分离：曾经评估为"不计数"不得阻塞后续 forceCount 补计。</summary>
    private static readonly HashSet<ulong> skippedMobIds = new();
    /// <summary>插件主动选中过的怪（HuntController.TrackTarget 标记）。
    /// 这些怪死亡时即使狩猎怪数据库未收录也照常计数。</summary>
    private static readonly HashSet<ulong> markedMobIds = new();

    private static uint lastInstanceId = 0;

    // 缓存的副本区信息（仅供 UI 展示；Lifestream 依赖内部"学习"状态，可能返回 0，不可用于流程判定）
    private static int cachedInstanceCount = 0;
    private static int cachedCurrentInstance = 0;

    // ===== 副本区号知识（原生读数 + 历史兜底） =====

    /// <summary>已知存在分线的地图（读到过非 0 区号 / 成功切换过）。
    /// 原生 InstanceId 是"分线选择数据"的一部分，会间歇性读到 0（数据未加载时），
    /// 不能凭一次 0 就断定该地图没有分线。</summary>
    private static readonly HashSet<uint> lineCapableTerritories = new();

    /// <summary>各地图最近一次已知区号（原生读数）。
    /// 原生读数偶发为 0 时用它兜底，避免"击杀满却算不出当前区号 → 无法切区"。</summary>
    private static readonly Dictionary<uint, int> lastKnownLineByTerritory = new();

    /// <summary>DR 已明确反馈「不存在可切换的副本区」的地图（本图不分线，跳过一切切区动作）。</summary>
    private static readonly HashSet<uint> nonInstancedTerritories = new();

    /// <summary>发出 /pdr insc 后无任何反应（无 DR 反馈、无读图过渡、区号未变）的累计次数。
    /// 达到 2 次才把地图记为"不分线"并持久化——避免 DR 模块未启用/一时无响应时误判。</summary>
    private static readonly Dictionary<uint, int> noResponseCount = new();

    private static bool knowledgeLoaded = false;

    // ===== DR 副本区切换状态机 =====

    private static int switchTargetLine = 0;
    private static DateTime switchStartedAt = DateTime.MinValue;
    private static bool switchSawBetweenAreas = false;
    private static bool drSaidNoInstance = false;
    /// <summary>DR 已通过聊天反馈确认切换成功（"当前所在副本区为…"）。</summary>
    private static bool drConfirmedSwitch = false;

    /// <summary>切换等待超时（秒）。DR 内部会走向水晶/传送（可自动重试），正常几十秒内完成。</summary>
    private const double SwitchTimeoutSeconds = 180.0;
    /// <summary>无响应判定窗口（秒）：发出 /pdr insc 后这么多秒内既无 DR 反馈也无读图过渡且区号未变，
    /// 视为本图没有可切换的副本区（DR 的失败提示是屏幕 toast，聊天频道收不到，只能靠行为判定）。</summary>
    private const double NoResponseSeconds = 8.0;
    /// <summary>同一地图连续无响应多少次才持久化"不分线"标记。</summary>
    private const int NoResponseThreshold = 2;

    public static int CachedInstanceCount => cachedInstanceCount;
    public static int CachedCurrentInstance => cachedCurrentInstance;
    public static int SwitchTargetLine => switchTargetLine;

    /// <summary>当前「地图 + 副本区」桶的击杀数。</summary>
    public static int KillCount
    {
        get
        {
            var t = Svc.ClientState.TerritoryType;
            if (t == 0) return 0;
            return killCounts.TryGetValue((t, GetBestKnownInstanceId()), out var v) ? v : 0;
        }
    }

    /// <summary>当前地图+副本区的击杀数是否已达配置值。</summary>
    public static bool IsKillCountFull => KillCount >= P.Config.KillsPerInstance;

    /// <summary>本区击杀是否已满（结束地图自动跨区的触发条件）。</summary>
    public static bool ZoneCleared => IsKillCountFull;

    /// <summary>清零所有地图副本区的击杀数（跨区前 / 关闭总开关 / 手动清空按钮）。</summary>
    public static void ClearAllKillCounts()
    {
        if (killCounts.Count == 0) return;
        killCounts.Clear();
        PluginLog.Information("[AutoHunt] 已清零所有地图副本区的击杀数");
    }

    /// <summary>切换地图时调用：击杀数分桶保留，仅清理对象去重集合（对象 ID 换图后失效）。</summary>
    public static void OnTerritoryChanged(uint territory)
    {
        engagedMobIds.Clear();
        countedMobIds.Clear();
        skippedMobIds.Clear();
        markedMobIds.Clear();
        lastInstanceId = 0;
        cachedInstanceCount = 0;
        cachedCurrentInstance = 0;
    }

    /// <summary>
    /// 当前是否处于可切换副本区的地图（游戏原生判定：InstanceId≠0）。
    /// 注意：读图/传送后原生读数会间歇性为 0，不能凭 0 断定不可切区——
    /// 权威判定来自 DR 的反馈（nonInstancedTerritories）。
    /// </summary>
    public static bool IsInstancedAreaNow()
    {
        var ui = FFXIVClientStructs.FFXIV.Client.Game.UI.UIState.Instance();
        return ui != null && ui->PublicInstance.InstanceId != 0;
    }

    /// <summary>当前所在副本区号（游戏原生，0=不可切区地图/数据未就绪）。</summary>
    public static int GetNativeInstanceId()
    {
        var ui = FFXIVClientStructs.FFXIV.Client.Game.UI.UIState.Instance();
        return ui != null ? (int)ui->PublicInstance.InstanceId : 0;
    }

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
            MarkLineCapable(territory);
            lastKnownLineByTerritory[territory] = native;
            return native;
        }
        return GetLastKnownLine(territory);
    }

    /// <summary>DR 是否已反馈该地图不存在可切换的副本区（不分线）。</summary>
    public static bool IsKnownNonInstanced(uint territory)
    {
        EnsureKnowledgeLoaded();
        return nonInstancedTerritories.Contains(territory);
    }

    /// <summary>该地图是否有分线（读到过区号 / Lifestream 已知多区 / DR 切换成功过）。</summary>
    public static bool IsKnownInstanced(uint territory)
    {
        EnsureKnowledgeLoaded();
        return lineCapableTerritories.Contains(territory);
    }

    /// <summary>已识别为有分线 / 不分线的地图数量（供 UI 展示）。</summary>
    public static int KnownInstancedCount { get { EnsureKnowledgeLoaded(); return lineCapableTerritories.Count; } }
    public static int KnownNonInstancedCount { get { EnsureKnowledgeLoaded(); return nonInstancedTerritories.Count; } }

    /// <summary>把配置中持久化的地图识别记录载入内存（进程内只执行一次）。</summary>
    private static void EnsureKnowledgeLoaded()
    {
        if (knowledgeLoaded) return;
        knowledgeLoaded = true;
        try
        {
            foreach (var t in P.Config.KnownInstancedTerritories)
                if (t != 0) lineCapableTerritories.Add(t);
            foreach (var t in P.Config.KnownNonInstancedTerritories)
                if (t != 0 && !lineCapableTerritories.Contains(t)) nonInstancedTerritories.Add(t);
            PluginLog.Information($"[AutoHunt] 副本地图识别已载入：有分线 {lineCapableTerritories.Count} 张 / 不分线 {nonInstancedTerritories.Count} 张");
        }
        catch (Exception e)
        {
            PluginLog.Warning($"[AutoHunt] 载入副本地图识别记录失败: {e.Message}");
        }
    }

    /// <summary>记录「该地图有分线」并持久化；若此前被误记为不分线则一并纠正。</summary>
    public static void MarkLineCapable(uint territory)
    {
        if (territory == 0) return;
        EnsureKnowledgeLoaded();
        var changed = lineCapableTerritories.Add(territory);
        if (nonInstancedTerritories.Remove(territory)) changed = true;
        noResponseCount.Remove(territory);
        if (!changed) return;
        try
        {
            if (!P.Config.KnownInstancedTerritories.Contains(territory))
                P.Config.KnownInstancedTerritories.Add(territory);
            P.Config.KnownNonInstancedTerritories.Remove(territory);
            EzConfig.Save();
        }
        catch (Exception e) { PluginLog.Warning($"[AutoHunt] 保存副本地图识别失败: {e.Message}"); }
        PluginLog.Information($"[AutoHunt] 地图 {territory} 确认为「有分线」（已记录，后续进图会切 1 号区）");
    }

    /// <summary>记录「该地图不分线」并持久化（有分线证据的地图不会被打标）。</summary>
    public static void MarkNonInstanced(uint territory)
    {
        if (territory == 0) return;
        EnsureKnowledgeLoaded();
        if (lineCapableTerritories.Contains(territory)) return;
        if (!nonInstancedTerritories.Add(territory)) return;
        try
        {
            if (!P.Config.KnownNonInstancedTerritories.Contains(territory))
                P.Config.KnownNonInstancedTerritories.Add(territory);
            P.Config.KnownInstancedTerritories.Remove(territory);
            EzConfig.Save();
        }
        catch (Exception e) { PluginLog.Warning($"[AutoHunt] 保存副本地图识别失败: {e.Message}"); }
        PluginLog.Information($"[AutoHunt] 地图 {territory} 确认为「不分线」（已记录，后续进图不再尝试切区）");
    }

    /// <summary>清空全部副本地图识别记录（UI「重置副本地图识别」按钮）。</summary>
    public static void ResetKnownMaps()
    {
        knowledgeLoaded = true;
        lineCapableTerritories.Clear();
        nonInstancedTerritories.Clear();
        noResponseCount.Clear();
        lastKnownLineByTerritory.Clear();
        try
        {
            P.Config.KnownInstancedTerritories.Clear();
            P.Config.KnownNonInstancedTerritories.Clear();
            EzConfig.Save();
        }
        catch { }
        PluginLog.Information("[AutoHunt] 已重置副本地图识别记录");
        Notify.Info("已重置副本地图识别记录。");
    }

    /// <summary>地图名称（TerritoryType → PlaceName，读取失败返回空串）。</summary>
    public static string GetMapName(uint territory)
    {
        try
        {
            return Svc.Data.GetExcelSheet<TerritoryType>().GetRow(territory)
                .PlaceName.ValueNullable?.Name.ToString() ?? "";
        }
        catch { return ""; }
    }

    /// <summary>主页「本区击杀」显示文本：地图名 + 副本区（如「遗产之地1线」）；不分线/区号未知时只显示地图名。</summary>
    public static string CurrentAreaLabel()
    {
        var name = GetMapName(Svc.ClientState.TerritoryType);
        if (name.IsNullOrEmpty()) return "";
        var line = GetBestKnownInstanceId();
        return line > 0 ? $"{name}{line}线" : name;
    }

    /// <summary>记录一次已知区号（原生读到 / 我方切换目标）。</summary>
    public static void NoteKnownLine(int line, uint? territory = null)
    {
        if (line <= 0) return;
        var t = territory ?? Svc.ClientState.TerritoryType;
        if (t == 0) return;
        MarkLineCapable(t);
        lastKnownLineByTerritory[t] = line;
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

    // ===== DR 副本区切换 =====

    /// <summary>
    /// 发起 DR 副本区切换：发送 /pdr insc N 并进入等待状态。
    /// 切换期间车头坐标由 P.HeldCoordinate 暂存，完成后由主循环重放。
    /// </summary>
    public static void BeginDrSwitch(int targetLine)
    {
        if (targetLine < 1)
        {
            PluginLog.Warning($"[AutoHunt] 收到非法副本区号 {targetLine}，跳过切区");
            return;
        }
        var here = Svc.ClientState.TerritoryType;
        if (IsKnownNonInstanced(here))
        {
            // 已记录为不分线：不发指令，直接续跑（否则暂存的车头坐标会一直卡在 HeldCoordinate）
            PluginLog.Information($"[AutoHunt] 地图 {here} 已记录为不分线，跳过 /pdr insc {targetLine}，直接继续流程");
            P.SwitchInProgress = false;
            var held0 = P.HeldCoordinate;
            P.HeldCoordinate = null;
            if (held0 != null) HuntController.OnNewCoordinate(held0);
            else if (HuntController.CurrentState == HuntController.State.Teleporting) HuntController.OnArrived();
            return;
        }
        switchTargetLine = targetLine;
        switchStartedAt = DateTime.Now;
        switchSawBetweenAreas = false;
        drSaidNoInstance = false;
        drConfirmedSwitch = false;
        P.SwitchInProgress = true;
        P.SwitchStartTime = DateTime.Now;
        Chat.ExecuteCommand($"/pdr insc {targetLine}");
        // 未知地图：这是一次"探测"，DR 若分线正常会在一两秒内反馈「当前所在副本区为…」
        if (!IsKnownInstanced(here))
            Notify.Info($"正在确认当前地图是否分线（约 {NoResponseSeconds:0} 秒）…");
        else
            Notify.Info($"正在切换到 {targetLine} 号副本区（DR 快捷副本区切换）…");
        PluginLog.Information($"[AutoHunt] 已发送 /pdr insc {targetLine}（地图 {here}），等待 DR 响应");
    }

    /// <summary>
    /// 切区等待推进。返回 true = 切换流程结束（成功 / 本图不分线 / 超时），可继续后续流程。
    /// 仅在 P.SwitchInProgress 为 true 时由主循环调用。
    /// </summary>
    public static bool UpdateSwitchProgress()
    {
        if (!P.SwitchInProgress) return true;
        EnsureKnowledgeLoaded();
        var here = Svc.ClientState.TerritoryType;
        var elapsed = (DateTime.Now - switchStartedAt).TotalSeconds;

        // 读图过渡中（DR 切区会传送/换区）：记住见过过渡，等它结束
        if (Svc.Condition[ConditionFlag.BetweenAreas] || Svc.Condition[ConditionFlag.BetweenAreas51])
        {
            switchSawBetweenAreas = true;
            return false;
        }

        // DR 反馈本图不存在可切换的副本区 → 本图不分线，直接继续
        if (drSaidNoInstance)
        {
            MarkNonInstanced(here);
            PluginLog.Information($"[AutoHunt] DR 反馈地图 {here} 不存在可切换的副本区，继续流程");
            return true;
        }

        // DR 通过聊天确认切换成功（"当前所在副本区为…"）——最可靠的信号，实测约 2 秒内到达。
        // 确认后等区号刷新/过渡结束即可继续流程。
        if (drConfirmedSwitch)
        {
            MarkLineCapable(here);
            NoteKnownLine(switchTargetLine, here);
            if (GetNativeInstanceId() == switchTargetLine)
            {
                PluginLog.Information($"[AutoHunt] 副本区切换完成（DR 已确认 + 原生区号 {switchTargetLine}，耗时 {elapsed:0}s）");
                return true;
            }
            if (elapsed > 20)
            {
                PluginLog.Information($"[AutoHunt] 副本区切换：DR 已确认切换，原生区号暂未刷新，直接继续流程（耗时 {elapsed:0}s）");
                return true;
            }
            return false;
        }

        // 已发生读图过渡且过渡结束 → 切区动作已完成
        if (switchSawBetweenAreas && elapsed > 3)
        {
            MarkLineCapable(here);
            NoteKnownLine(switchTargetLine, here);
            PluginLog.Information($"[AutoHunt] 副本区切换完成（观察到读图过渡，目标 {switchTargetLine} 号区，耗时 {elapsed:0}s）");
            return true;
        }

        // 未触发读图但原生区号已等于目标（典型：已在目标区，DR 无需动作）
        if (elapsed > 3 && GetNativeInstanceId() == switchTargetLine)
        {
            MarkLineCapable(here);
            NoteKnownLine(switchTargetLine, here);
            PluginLog.Information($"[AutoHunt] 副本区切换完成（已在 {switchTargetLine} 号区，无需切换，耗时 {elapsed:0}s）");
            return true;
        }

        var hasEvidence = lineCapableTerritories.Contains(here)
            || S.LifestreamIPC.GetInstanceCount() > 1
            || GetNativeInstanceId() > 0;

        // 无响应判定：DR 的失败提示是屏幕 toast（聊天频道收不到），只能靠行为判定——
        // 真正切区必有读图过渡或 DR 聊天反馈；两者都没有 = 本图没有可切换的副本区。
        if (elapsed > NoResponseSeconds && !hasEvidence)
        {
            var n = noResponseCount.TryGetValue(here, out var c) ? c + 1 : 1;
            noResponseCount[here] = n;
            if (n >= NoResponseThreshold)
            {
                MarkNonInstanced(here);
                PluginLog.Information($"[AutoHunt] /pdr insc {switchTargetLine} 无响应（第 {n} 次），地图 {here} 判定为不分线并已记录，继续流程");
                Notify.Info("当前地图不分副本区，直接前往车头坐标");
            }
            else
            {
                PluginLog.Information($"[AutoHunt] /pdr insc {switchTargetLine} 无响应（第 {n}/{NoResponseThreshold} 次），地图 {here} 暂按不分线处理（再确认一次后永久记录）");
                Notify.Info("当前地图未检测到可切换的副本区，直接前往车头坐标");
            }
            return true;
        }

        if (elapsed > SwitchTimeoutSeconds)
        {
            PluginLog.Warning($"[AutoHunt] 副本区切换超过 {SwitchTimeoutSeconds:0} 秒未完成，放弃等待并继续流程;"
                + "请确认已安装 Daily Routines 并启用「快捷副本区切换」模块");
            Notify.Error("副本区切换超时，已跳过并继续流程。请确认 DR 插件已启用「快捷副本区切换」模块。");
            return true;
        }

        return false;
    }

    /// <summary>
    /// 监听 DR（Daily Routines）对 /pdr insc 的聊天反馈。
    /// 由 ChatMessageHandler 对每条聊天消息调用（不受车头过滤影响）。
    /// 实测 DR 成功切区时会输出「当前所在副本区为“遗产之地”。」——这是最可靠的切换成功信号。
    /// </summary>
    public static void OnChatFeedback(IHandleableChatMessage cm)
    {
        var text = string.Concat(cm.Message.Payloads.OfType<TextPayload>().Select(p => p.Text));
        if (text.IsNullOrEmpty()) return;
        if (!text.Contains("副本") && !text.Contains("insc") && !text.Contains("切换")) return;

        // 非调试模式下低频记录，便于从日志核对 DR 的实际反馈措辞
        if (!P.Config.Debug && EzThrottler.Throttle("WYDrFeedback", 5000))
            PluginLog.Information($"[AutoHunt] DR/切区相关反馈: {text}");
        Dbg.Log($" DR/切区相关反馈: {text}");

        // 成功反馈（与是否处于切换流程无关：DR 也可能响应其他来源的 /pdr insc）
        if (text.Contains("所在副本区") || text.Contains("当前副本区"))
        {
            MarkLineCapable(Svc.ClientState.TerritoryType);
            if (P.SwitchInProgress) drConfirmedSwitch = true;
            return;
        }

        if (!P.SwitchInProgress) return;

        // 不存在可切换的副本区（本图不分线）。措辞按 DR 实际输出宽匹配。
        if (text.Contains("不存在") || text.Contains("不支持") || text.Contains("没有可") || text.Contains("无法切换"))
        {
            MarkNonInstanced(Svc.ClientState.TerritoryType);
            drSaidNoInstance = true;
        }
    }

    /// <summary>
    /// 击杀已满且车头新坐标仍在本地图 → 安排「传送最近水晶 → /pdr insc 当前区号+1」。
    /// 条件满足时设置 TeleportTo 与 HeldCoordinate 并返回 true；否则返回 false 走普通坐标流程。
    /// </summary>
    public static bool TryBeginSameMapSwitch(TargetPosition tp)
    {
        if (!P.Config.Enabled || !P.Config.AutoInstance) return false;
        if (P.SwitchInProgress || (P.TeleportTo != null && P.TeleportTo.SwitchInstance > 0)) return false;
        if (tp == null || tp.TerritoryId != Svc.ClientState.TerritoryType) return false;
        if (!IsKillCountFull) return false;
        if (nonInstancedTerritories.Contains(tp.TerritoryId)) return false;

        var cur = GetBestKnownInstanceId();
        if (cur < 1)
        {
            Dbg.Log(" 击杀已满且车头坐标在本图，但读不到当前副本区号（原生=0 且无历史记录），无法安排切区");
            return false;
        }

        // 已是本图最后一个副本区（Lifestream 已学习到区数且当前已到顶）：
        // 不切区——车头接下来大概率带去下一地图，直接前往坐标即可，
        // 也避免对不存在的「下一区」发 /pdr insc 空转
        var knownCount = S.LifestreamIPC.GetInstanceCount();
        if (knownCount > 1 && cur >= knownCount)
        {
            Dbg.Log($" 已在最后一个副本区（{cur}/{knownCount}），跳过切区，直接前往车头坐标");
            return false;
        }

        var next = cur + 1;
        P.HeldCoordinate = tp;
        P.TeleportTo = new ArrivalData
        {
            Aetheryte = tp.NearestAetheryte,
            Territory = tp.TerritoryId,
            SwitchInstance = next,
            FromTerritory = Svc.ClientState.TerritoryType,
        };
        HuntController.Reset();
        PluginLog.Information($"[AutoHunt] 击杀已满（{KillCount}/{P.Config.KillsPerInstance}）且车头新坐标仍在本地图："
            + $"先传送至 {tp.AetheryteName}，到达后 /pdr insc {next}（当前 {cur} 号区）");
        Notify.Info($"本区击杀已满，传送 {tp.AetheryteName} 后切换到 {next} 号副本区…");
        return true;
    }

    public static void Update()
    {
        if (S.LifestreamIPC == null) return;
        EnsureKnowledgeLoaded();

        // 副本区变化检测（只认非 0 读数变化；0 只是数据未就绪，不代表换区）。
        // 击杀数按「地图+副本区」分桶保存，换区不清零——仅清理对象去重集合
        //（换区后对象表已刷新，对象 ID 可能被复用）。
        var uiState = FFXIVClientStructs.FFXIV.Client.Game.UI.UIState.Instance();
        if (uiState != null)
        {
            var instId = uiState->PublicInstance.InstanceId;
            if (instId != 0 && instId != lastInstanceId)
            {
                var prev = lastInstanceId;
                lastInstanceId = instId;
                var territory = Svc.ClientState.TerritoryType;
                // 读到非 0 区号 = 该地图确有分线（记录证据 + 记住当前区号，并持久化）
                MarkLineCapable(territory);
                lastKnownLineByTerritory[territory] = (int)instId;
                engagedMobIds.Clear();
                countedMobIds.Clear();
                skippedMobIds.Clear();
                markedMobIds.Clear();
                if (prev != 0)
                    PluginLog.Information($"[AutoHunt] 副本区已切换：{prev} → {instId}"
                        + $"（当前桶击杀 {GetBucketCount(territory, (int)instId)}/{P.Config.KillsPerInstance}，计数分桶保留）");
                else
                    PluginLog.Information($"[AutoHunt] 进入副本区 {instId}（当前桶击杀 {GetBucketCount(territory, (int)instId)}/{P.Config.KillsPerInstance}）");
            }
        }

        // 扫描战斗对象，统计玩家参与击杀的怪物（250ms 一次足够）
        if (EzThrottler.Throttle("WYKillScan", 250))
        {
            ScanKills();
        }

        // 定期刷新副本区信息缓存（仅供 UI 显示）
        if (EzThrottler.Throttle("WYInstanceCache", 2000))
        {
            cachedInstanceCount = S.LifestreamIPC.GetInstanceCount();
            cachedCurrentInstance = S.LifestreamIPC.GetCurrentInstanceNumber();
        }
    }

    private static int GetBucketCount(uint territory, int line)
        => killCounts.TryGetValue((territory, line), out var v) ? v : 0;

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
                if (engagedMobIds.Contains(npc.GameObjectId)
                    && !countedMobIds.Contains(npc.GameObjectId)
                    && !skippedMobIds.Contains(npc.GameObjectId))
                {
                    // 插件主动选中过的怪（marked）即使数据库未收录也计数
                    OnMobKilled(npc.GameObjectId, npc.NameId, markedMobIds.Contains(npc.GameObjectId));
                }
            }
        }

        // 调试：每 3 秒汇总一次扫描结果，能直接区分「参与判定没命中」/「不是狩猎怪」/「区号读不到」。
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
            var territory = Svc.ClientState.TerritoryType;
            var line = GetBestKnownInstanceId();
            Dbg.Log($"击杀扫描汇总: 参与中存活 {aliveEngaged} 只 / 待计数尸体 {deadPending} 具 / 视野内狩猎怪 {huntAlive} 只"
                + $" | 本桶击杀 {GetBucketCount(territory, line)}/{P.Config.KillsPerInstance}（地图 {territory} {line}号区）"
                + $" | 原生副本区={GetNativeInstanceId()} | Lifestream区号={cachedCurrentInstance}/共{cachedInstanceCount}区"
                + $" | 切区中={P.SwitchInProgress}（目标 {switchTargetLine}）"
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
    /// 计入当前「地图 + 副本区」桶。击杀满后不再原地清零——等车头下一坐标：
    /// 坐标仍在本图 → 传送水晶后 /pdr insc 下一区；坐标在别的地图 → 正常传送，到达后 /pdr insc 1。
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

        var territory = Svc.ClientState.TerritoryType;
        var line = GetBestKnownInstanceId();
        var key = (territory, line);
        var count = GetBucketCount(territory, line) + 1;
        killCounts[key] = count;

        // 击杀计数是低频且最关键的事件：无条件写 Information 级日志（不受 Dalamud 日志级别影响）
        var areaText = line > 0 ? $"{line}号区" : "区号未知";
        PluginLog.Information($"[AutoHunt] 击杀计数: {count}/{P.Config.KillsPerInstance}"
            + $" (地图 {territory} {areaText}, NameId={nameId}, forceCount={forceCount})");

        if (!P.Config.AutoInstance) return;
        if (count < P.Config.KillsPerInstance) return;

        // 只在恰好攒满那一刻提示一次（计数继续累积时不重复刷屏）
        if (count > P.Config.KillsPerInstance) return;

        Notify.Info($"本区击杀已满（{count}/{P.Config.KillsPerInstance}），等待车头下一坐标后切换副本区…");
        PluginLog.Information("[AutoHunt] 本区击杀已满：车头下一坐标仍在本图则传送水晶后 /pdr insc 下一区；坐标在其他地图则正常传送（到达后 /pdr insc 1）");
    }

    /// <summary>暂停恢复补偿：把暂停时长加到切区等待的计时起点上。</summary>
    public static void CompensatePause(long pauseMs)
    {
        if (switchStartedAt != DateTime.MinValue) switchStartedAt += TimeSpan.FromMilliseconds(pauseMs);
    }

    /// <summary>重置副本区记录（关闭总开关 / /ah reset）：清零所有击杀数与切区状态。
    /// 不清 lineCapableTerritories / nonInstancedTerritories——"哪些地图有分线"是游戏世界的事实。</summary>
    public static void Reset()
    {
        killCounts.Clear();
        lastInstanceId = 0;
        switchTargetLine = 0;
        switchStartedAt = DateTime.MinValue;
        switchSawBetweenAreas = false;
        drSaidNoInstance = false;
        drConfirmedSwitch = false;
        lastKnownLineByTerritory.Clear();
        engagedMobIds.Clear();
        countedMobIds.Clear();
        skippedMobIds.Clear();
        markedMobIds.Clear();
    }
}
