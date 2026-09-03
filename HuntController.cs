using ECommons.GameHelpers;
using AutoHunt.Tasks;
using ECommons.GameHelpers;
using ECommons.Automation.NeoTaskManager;
namespace AutoHunt;

/// <summary>
/// 状态机：管理收到坐标后的完整流程。
/// 状态：Idle → Teleporting → Mounting → Navigating → Arrived → Targeting → Attacking → Dismounting → Outputting → Finished
/// 简化后核心逻辑：
/// 1. 收到坐标 → 判断传送/直接寻路
/// 2. 到达坐标区域 → 选中狩猎怪（必须是狩猎怪）→ 若未找到则跟随队友直到选中
/// 3. 怪物血量 < 70% → 下坐骑 → /rotation Manual
/// 4. 怪物死亡 → /rotation off → 回到 Idle 等待下一个坐标
/// </summary>
internal static unsafe class HuntController
{
    public enum State
    {
        Idle,           // 等待车头发送坐标
        Teleporting,    // 正在传送
        Mounting,       // 正在上坐骑
        Navigating,     // vnavmesh 寻路到坐标
        Arrived,        // 到达坐标，准备选怪
        Targeting,      // 寻找并选中狩猎怪
        Attacking,      // 已选中，等待血量下降到阈值
        Descending,     // 血量达标，正在下降到地面（飞行悬停 → 地面）
        Dismounting,    // 已到地面，正在下坐骑
        Outputting,     // 执行 /rotation Manual 输出中
        Finished,       // 怪物死亡，停止输出，回到 Idle
    }

    public static State CurrentState { get; private set; } = State.Idle;
    public static string CurrentTargetName { get; private set; } = "";
    public static string CurrentTargetRank { get; private set; } = "";
    public static float CurrentTargetHpPercent { get; private set; } = 100f;

    private static TargetPosition? pendingTarget = null;
    private static DateTime stateStartTime = DateTime.MinValue;
    private static Vector3 lastNavDest = Vector3.Zero;
    private static bool navStarted = false;
    private static bool notifiedNoHunt = false;
    private static DateTime lastFlyFlagTime = DateTime.MinValue;

    // 精确悬停（接近目标后按 ZOffset 定位）
    private static bool preciseStarted = false;
    private static Vector3 preciseDest = Vector3.Zero;
    private static DateTime lastPreciseRetry = DateTime.MinValue;

    // 下坐骑状态
    private static bool dismountPending = false;
    private static DateTime dismountStartTime = DateTime.MinValue;
    private static bool dismountWarned = false; // 下坐骑受阻提示只发一次

    // 目标追踪（防止玩家过多导致目标丢失后流程中断）
    private static ulong lastTargetId = 0;
    private static Vector3 lastTargetPos = Vector3.Zero;
    private static DateTime targetLostSince = DateTime.MinValue;
    private static DateTime lastRetargetNotify = DateTime.MinValue;

    // 输出阶段脱战计时（脱战 + 无怪 → 视为已击杀；战斗中说明仇恨还在，怪大概率活着）
    private static DateTime outOfCombatSince = DateTime.MinValue;

    /// <summary>收到新车头坐标时触发状态机。</summary>
    public static void OnNewCoordinate(TargetPosition target)
    {
        if (target == null) return;

        // 重复坐标过滤：车头为迟到玩家补发坐标时，若当前正处于
        // 攻击/下降/下坐骑/输出阶段，且新坐标与当前目标相近（同图 < 50m），
        // 视为同一只怪的重复坐标，忽略之——避免 StopOutput 打断正在进行的战斗，
        // 以及重启流程后因怪被挤出对象表找不到而超时停止。
        if ((CurrentState == State.Attacking || CurrentState == State.Descending
                || CurrentState == State.Dismounting || CurrentState == State.Outputting)
            && pendingTarget != null
            && target.TerritoryId == pendingTarget.TerritoryId
            && Vector2.Distance(target.WorldXZ, pendingTarget.WorldXZ) < 50f)
        {
            if (P.Config.Debug) PluginLog.Debug($"[AutoHunt] 忽略重复坐标（与当前目标距离 < 50m，不打断当前战斗）: ({target.WorldXZ.X:0.0}, {target.WorldXZ.Y:0.0})");
            return;
        }

        // 如果正在输出中，先停止（车头发新坐标 = 当前怪已处理完或要换目标）
        if (CurrentState == State.Outputting || CurrentState == State.Attacking || CurrentState == State.Dismounting || CurrentState == State.Descending)
        {
            StopOutput();
        }

        pendingTarget = target;
        navStarted = false;
        notifiedNoHunt = false;
        dismountPending = false;
        dismountWarned = false;
        preciseStarted = false;
        preciseDest = Vector3.Zero;
        lastTargetId = 0;
        lastTargetPos = Vector3.Zero;
        targetLostSince = DateTime.MinValue;
        outOfCombatSince = DateTime.MinValue;

        // 判断是否需要传送
        if (target.TerritoryId != Svc.ClientState.TerritoryType)
        {
            // 跨地图：必须传送
            CurrentState = State.Teleporting;
            P.TeleportTo = new ArrivalData
            {
                Aetheryte = target.NearestAetheryte,
                Territory = target.TerritoryId,
                SwitchInstance = 0,
            };
            Notify.Info($"目标在其他地图，传送到 {target.AetheryteName}…");
        }
        else
        {
            // 同地图：判断距离阈值
            var selfPos = new Vector2(Player.Position.X, Player.Position.Z);
            var distSelf = Vector2.Distance(selfPos, target.WorldXZ);
            var aethPos = target.NearestAetheryte != null
                ? MapManager.GetAetheryteWorldPosition(target.NearestAetheryte.Value)
                : null;
            var distAeth = aethPos != null ? Vector2.Distance(aethPos.Value, target.WorldXZ) : 0f;

            if (P.Config.Debug) PluginLog.Debug($"同图距离: 自己→目标 {distSelf:0.0}m, 水晶→目标 {distAeth:0.0}m, 差值 {distSelf - distAeth:0.0}m");

            if (distSelf > distAeth + P.Config.TeleportDistanceThreshold)
            {
                CurrentState = State.Teleporting;
                P.TeleportTo = new ArrivalData
                {
                    Aetheryte = target.NearestAetheryte,
                    Territory = target.TerritoryId,
                    SwitchInstance = 0,
                };
                Notify.Info($"距离目标较远，传送到 {target.AetheryteName}…");
            }
            else
            {
                // 直接寻路
                CurrentState = State.Mounting;
                Notify.Info($"距离合适，直接前往坐标 ({target.WorldXZ.X:0.0}, {target.WorldXZ.Y:0.0})…");
            }
        }

        stateStartTime = DateTime.Now;
    }

    /// <summary>传送到达后调用（由 AutoHunt.OnArrival 触发）。</summary>
    public static void OnArrived()
    {
        if (CurrentState == State.Teleporting)
        {
            CurrentState = State.Mounting;
            stateStartTime = DateTime.Now;
            Notify.Info("传送到达，准备上坐骑寻路…");
        }
    }

    /// <summary>主循环每帧调用。</summary>
    public static void Update()
    {
        if (!P.Config.Enabled) return;
        if (pendingTarget == null && CurrentState == State.Idle) return;

        switch (CurrentState)
        {
            case State.Teleporting:
                // 等待传送完成（由 UpdateTeleport / OnArrived 处理）
                if ((DateTime.Now - stateStartTime).TotalSeconds > 60)
                {
                    Notify.Error("传送超时，放弃当前目标。");
                    Reset();
                }
                break;

            case State.Mounting:
                if (!Player.Interactable || !IsScreenReady()) return;
                if (Svc.Condition[ConditionFlag.BetweenAreas] || Svc.Condition[ConditionFlag.BetweenAreas51]) return;

                // 上坐骑：直接调用任务逻辑（内部自带节流），等待真正骑上（最多 20 秒）
                if (P.Config.UseMount && !Player.Mounted)
                {
                    if ((DateTime.Now - stateStartTime).TotalSeconds < 20)
                    {
                        var mounted = TaskMount.MountIfCan();
                        if (!mounted) return; // 还没骑上，继续等
                    }
                    // 超时或该区域禁止骑乘 → 照样进入寻路（vnavmesh 会地面寻路）
                    if (P.Config.Debug) PluginLog.Debug($"[AutoHunt] 上坐骑结束: Mounted={Player.Mounted}");
                }

                // 进入寻路
                CurrentState = State.Navigating;
                stateStartTime = DateTime.Now;
                navStarted = false;
                break;

            case State.Navigating:
                UpdateNavigating();
                break;

            case State.Arrived:
            case State.Targeting:
                CurrentState = State.Targeting;
                FindAndTargetHuntMob();
                break;

            case State.Attacking:
                UpdateAttacking();
                break;

            case State.Descending:
                UpdateDescending();
                break;

            case State.Dismounting:
                UpdateDismounting();
                break;

            case State.Outputting:
                UpdateOutputting();
                break;

            case State.Finished:
                // 短暂延迟后回到 Idle
                if ((DateTime.Now - stateStartTime).TotalSeconds > 2)
                {
                    Reset();
                }
                break;
        }
    }

    /// <summary>
    /// 寻路逻辑：
    /// - 推荐模式（UseFlyFlag）：重新插旗到当前地图 → 执行 /vnav flyflag 让 vnavmesh 飞向旗标（长距离绕障）；
    ///   接近目标（XZ < 60m）后切换为 IPC 精确寻路到「坐标 + ZOffset 高度」悬停，使 Z 轴偏移真正生效。
    /// - 回退模式：IPC 直接寻路到坐标 + Z 轴偏移。
    /// </summary>
    private static void UpdateNavigating()
    {
        if (!S.VnavmeshIPC.GetIsReady())
        {
            if (!notifiedNoHunt)
            {
                notifiedNoHunt = true;
                Notify.Error("vnavmesh 未就绪，无法寻路。");
            }
            return;
        }

        var target = pendingTarget!;
        var selfXZ = new Vector2(Player.Position.X, Player.Position.Z);
        float distXZ = Vector2.Distance(selfXZ, target.WorldXZ);

        // ===== 精确悬停阶段（接近目标后按 ZOffset 定位，两种模式共用） =====
        if (preciseStarted)
        {
            UpdatePreciseHover();
            return;
        }

        if (P.Config.UseFlyFlag)
        {
            // ===== 插旗 + /vnav flyflag 模式（长距离） =====
            // 接近目标：切换到精确悬停，让 ZOffset 生效（不可飞地图同样切换，Y 不加偏移走地面寻路）
            if (distXZ < 60f && navStarted)
            {
                StartPreciseHover(target);
                return;
            }

            if (!navStarted)
            {
                navStarted = true;
                // 重新插旗（传送后旗标可能丢失，且 vnav flyflag 依赖当前地图旗标）
                MapManager.PlaceFlag(target.TerritoryId, target.WorldXZ);
                Chat.ExecuteCommand(P.Config.FlyFlagCommand);
                lastFlyFlagTime = DateTime.Now;
                Notify.Info($"已插旗，执行 {P.Config.FlyFlagCommand} 飞向坐标…");
                if (P.Config.Debug) PluginLog.Debug($"[AutoHunt] 执行 {P.Config.FlyFlagCommand}，目标世界坐标 ({target.WorldXZ.X:0.0}, {target.WorldXZ.Y:0.0})");
            }

            bool pathRunning = S.VnavmeshIPC.GetPathIsRunning();
            bool pathfinding = S.VnavmeshIPC.GetPathfindInProgress();

            // 路径已结束且距离足够近 → 到达
            if (!pathRunning && !pathfinding && distXZ < 15f)
            {
                S.VnavmeshIPC.StopPath();
                CurrentState = State.Arrived;
                stateStartTime = DateTime.Now;
                navStarted = false;
                Notify.Info("到达坐标区域，开始寻找狩猎怪…");
                return;
            }

            // 路径没在跑且没到 → 5 秒重试一次 flyflag
            if (!pathRunning && !pathfinding && (DateTime.Now - lastFlyFlagTime).TotalSeconds > 5)
            {
                Chat.ExecuteCommand(P.Config.FlyFlagCommand);
                lastFlyFlagTime = DateTime.Now;
                if (P.Config.Debug) PluginLog.Debug($"[AutoHunt] 重试 {P.Config.FlyFlagCommand} (dist={distXZ:0.0}m)");
            }

            // 120 秒超时
            if ((DateTime.Now - stateStartTime).TotalSeconds > 120)
            {
                Notify.Error("寻路超时，放弃当前目标。");
                Reset();
            }
            return;
        }

        // ===== IPC 直接寻路模式（回退） =====
        var dest = target.Position;

        if (!navStarted)
        {
            navStarted = true;
            // 重新计算 Y：当前飞行高度 + ZOffset（可飞）；不可飞则地面高度由 vnavmesh 自行贴合
            dest = ComputePreciseDest(target);
            lastNavDest = dest;
            bool fly = Player.CanFly && (Math.Abs(Player.Position.Y - dest.Y) > 5f || Vector3.Distance(Player.Position, dest) > 30f);
            S.VnavmeshIPC.TryPathfindAndMoveTo(dest, fly);
            if (P.Config.Debug) PluginLog.Debug($"开始寻路到 {dest} (fly={fly})");
        }

        bool running = S.VnavmeshIPC.GetPathIsRunning();
        bool finding = S.VnavmeshIPC.GetPathfindInProgress();
        float distToDest = Vector3.Distance(Player.Position, dest);

        if (!running && !finding && distToDest < 15f)
        {
            S.VnavmeshIPC.StopPath();
            CurrentState = State.Arrived;
            stateStartTime = DateTime.Now;
            navStarted = false;
            Notify.Info("到达坐标区域，开始寻找狩猎怪…");
        }
        else if ((DateTime.Now - stateStartTime).TotalSeconds > 60)
        {
            Notify.Error("寻路超时，放弃当前目标。");
            Reset();
        }
    }

    /// <summary>
    /// 计算精确悬停目标点：XZ 为车头坐标。
    /// Y 优先 = 目标坐标附近估算的地面高度 + ZOffset（悬停高度相对目标地面生效，
    /// 修复飞flag 长途飞行巡航高度过高导致悬停在目标上空太高、选不到目标的问题）。
    /// 附近没有任何可参照对象时回退 = 当前飞行高度 + ZOffset；不可飞地图不加偏移。
    /// </summary>
    private static Vector3 ComputePreciseDest(TargetPosition target)
    {
        float y = Player.Position.Y;
        if (Player.CanFly && P.Config.ZOffset > 0f)
        {
            if (TryEstimateGroundY(target.WorldXZ, out var groundY))
            {
                y = groundY + P.Config.ZOffset;
                if (P.Config.Debug) PluginLog.Debug($"[AutoHunt] 悬停高度相对目标地面: 地面Y≈{groundY:0.0} → 悬停Y={y:0.0} (ZOffset={P.Config.ZOffset:0}m)");
            }
            else
            {
                y += P.Config.ZOffset;
                if (P.Config.Debug) PluginLog.Debug($"[AutoHunt] 目标附近无可参照对象，悬停高度回退为当前飞行高度+ZOffset: Y={y:0.0}");
            }
        }
        return new Vector3(target.WorldXZ.X, y, target.WorldXZ.Y);
    }

    /// <summary>
    /// 估算目标坐标处的地面高度：取目标 XZ 附近 60m 内玩家/战斗对象中的最低 Y。
    /// 狩猎车场合目标周围几乎总有落地玩家，最低值最接近真实地面；
    /// 顺便规避少数悬空飞行玩家的干扰（他们的 Y 不是地面）。
    /// </summary>
    private static bool TryEstimateGroundY(Vector2 worldXZ, out float groundY)
    {
        groundY = 0f;
        bool found = false;
        float minY = float.MaxValue;

        foreach (var obj in Svc.Objects)
        {
            if (obj is not (IPlayerCharacter or IBattleNpc)) continue;
            if (obj.Position.Y >= minY) continue;
            float dxz = Vector2.Distance(new Vector2(obj.Position.X, obj.Position.Z), worldXZ);
            if (dxz > 60f) continue;
            minY = obj.Position.Y;
            found = true;
        }

        if (found) groundY = minY;
        return found;
    }

    /// <summary>进入精确悬停阶段：停止 flyflag 路径，IPC 寻路到坐标+ZOffset 点。</summary>
    private static void StartPreciseHover(TargetPosition target)
    {
        preciseStarted = true;
        preciseDest = ComputePreciseDest(target);
        S.VnavmeshIPC.StopPath();
        S.VnavmeshIPC.TryPathfindAndMoveTo(preciseDest, Player.CanFly);
        lastPreciseRetry = DateTime.Now;
        if (P.Config.Debug) PluginLog.Debug($"[AutoHunt] 接近目标，切换精确悬停: {preciseDest} (ZOffset={P.Config.ZOffset:0}m)");
    }

    /// <summary>精确悬停阶段：监测路径与到达（3D 距离 < 15m），路径中断 5 秒重试，纳入整体 120 秒超时。</summary>
    private static void UpdatePreciseHover()
    {
        bool running = S.VnavmeshIPC.GetPathIsRunning();
        bool finding = S.VnavmeshIPC.GetPathfindInProgress();
        float dist3D = Vector3.Distance(Player.Position, preciseDest);

        if (!running && !finding && dist3D < 15f)
        {
            // 到达悬停点
            S.VnavmeshIPC.StopPath();
            CurrentState = State.Arrived;
            stateStartTime = DateTime.Now;
            navStarted = false;
            Notify.Info($"已悬停在坐标上方 (高度偏移 {P.Config.ZOffset:0}m)，开始寻找狩猎怪…");
            return;
        }

        // 路径中断且未到达 → 5 秒重试
        if (!running && !finding && (DateTime.Now - lastPreciseRetry).TotalSeconds > 5)
        {
            S.VnavmeshIPC.TryPathfindAndMoveTo(preciseDest, Player.CanFly);
            lastPreciseRetry = DateTime.Now;
            if (P.Config.Debug) PluginLog.Debug($"[AutoHunt] 重试精确悬停寻路 (dist3D={dist3D:0.0}m)");
        }

        // 整体超时沿用 120 秒
        if ((DateTime.Now - stateStartTime).TotalSeconds > 120)
        {
            Notify.Error("寻路超时，放弃当前目标。");
            Reset();
        }
    }

    /// <summary>寻找并选中狩猎怪。必须是狩猎怪（A 怪 / S 怪 / 特殊狩猎怪）。</summary>
    private static void FindAndTargetHuntMob()
    {
        // 0. 优先检查当前目标：目标已经是存活的战斗怪时（无论是本插件上一轮、
        //    RotationSolver 还是其他来源选中的）直接采用，不再依赖数据库扫描。
        //    修复：目标明明已选中，却因 NotoriousMonster 表未命中（B 级被排除、
        //    新狩猎怪未收录等）而一直"寻找中"直至超时的问题。
        var current = Svc.Targets.Target as IBattleNpc;
        if (current != null && !current.IsDead)
        {
            bool knownHunt = HuntMobDatabase.IsHuntMob(current.NameId, P.Config.IncludeBRank);
            if (!knownHunt && P.Config.Debug)
                PluginLog.Debug($"[AutoHunt] 当前目标未命中狩猎怪数据库，仍直接采用: {current.Name.TextValue} (NameId={current.NameId})");
            AdoptTarget(current, knownHunt ? "" : "（当前目标）");
            return;
        }

        // 1. 扫描对象表寻找周围已知的狩猎怪
        var huntMob = FindNearestHuntMob();
        if (huntMob != null)
        {
            AdoptTarget(huntMob);
            return;
        }

        // 未找到：跟随一名队友（选择最近队友）直到选中狩猎怪
        if (!notifiedNoHunt)
        {
            notifiedNoHunt = true;
            Notify.Info("未发现狩猎怪，跟随队友直到选中…");
        }

        // 跟随最近队友（简化：向最近队友移动）
        var nearestAlly = FindNearestAlly();
        if (nearestAlly != null)
        {
            float dist = Vector3.Distance(Player.Position, nearestAlly.Position);
            if (dist > 5f && S.VnavmeshIPC.GetIsReady() && EzThrottler.Throttle("WYAllyFollow", 500))
            {
                S.VnavmeshIPC.TryPathfindAndMoveTo(nearestAlly.Position, false);
            }
        }

        // 持续搜索（每秒一次）：先看当前目标是否已被选中，再扫描对象表
        if (EzThrottler.Throttle("WYHuntScan", 1000))
        {
            current = Svc.Targets.Target as IBattleNpc;
            if (current != null && !current.IsDead)
            {
                AdoptTarget(current, HuntMobDatabase.IsHuntMob(current.NameId, P.Config.IncludeBRank) ? "" : "（当前目标）");
                return;
            }

            huntMob = FindNearestHuntMob();
            if (huntMob != null)
            {
                AdoptTarget(huntMob);
                return;
            }
        }

        // 超时放弃（Debug 模式输出附近战斗怪诊断，便于定位为何扫描不到）
        if ((DateTime.Now - stateStartTime).TotalSeconds > 30)
        {
            if (P.Config.Debug)
            {
                foreach (var obj in Svc.Objects)
                {
                    if (obj is IBattleNpc npc && !npc.IsDead)
                        PluginLog.Debug($"[AutoHunt] 超时诊断: 附近战斗怪 {npc.Name.TextValue} (NameId={npc.NameId}, IsHunt(含B)={HuntMobDatabase.IsHuntMob(npc.NameId, true)})");
                }
            }
            Notify.Error("30秒内未找到狩猎怪，放弃当前目标。");
            Reset();
        }
    }

    /// <summary>采用目标为当前狩猎目标并进入攻击阶段。note 为附加提示（如"（当前目标）"）。</summary>
    private static void AdoptTarget(IBattleNpc huntMob, string note = "")
    {
        Svc.Targets.Target = huntMob;
        CurrentTargetName = huntMob.Name.TextValue;
        CurrentTargetRank = HuntMobDatabase.GetRankLabel(huntMob.NameId);
        if (string.IsNullOrEmpty(CurrentTargetRank)) CurrentTargetRank = "?";
        CurrentTargetHpPercent = huntMob.CurrentHp / (float)huntMob.MaxHp * 100f;
        TrackTarget(huntMob);
        targetLostSince = DateTime.MinValue;
        CurrentState = State.Attacking;
        stateStartTime = DateTime.Now;
        Notify.Info($"已选中狩猎怪: [{CurrentTargetRank}] {CurrentTargetName}{note} (HP {CurrentTargetHpPercent:0}%)");
    }

    /// <summary>攻击中：监测血量，低于阈值则下坐骑输出。目标丢失时自动重新选中。</summary>
    private static void UpdateAttacking()
    {
        var target = GetValidTarget();

        // 目标死亡 → 结束
        if (target != null && target.IsDead)
        {
            OnMobKilled();
            CurrentState = State.Finished;
            stateStartTime = DateTime.Now;
            StopOutput();
            return;
        }

        // 目标丢失（玩家过多的场合怪物会被挤出对象表）→ 尝试重新选中
        if (target == null)
        {
            if (targetLostSince == DateTime.MinValue)
            {
                targetLostSince = DateTime.Now;
                if (P.Config.Debug) PluginLog.Debug("[AutoHunt] 攻击阶段目标丢失，尝试重新选中…");
            }
            TryRetarget();
            // 战斗中（仇恨还在）说明怪大概率存活，只是被挤出对象表选不中 → 不放弃
            bool inCombat = Svc.Condition[ConditionFlag.InCombat];
            if (!inCombat && (DateTime.Now - targetLostSince).TotalSeconds > 30)
            {
                Notify.Error("目标丢失超过30秒，放弃当前目标。");
                CurrentState = State.Finished;
                stateStartTime = DateTime.Now;
                StopOutput();
            }
            return;
        }

        targetLostSince = DateTime.MinValue;
        TrackTarget(target);
        CurrentTargetName = target.Name.TextValue;
        CurrentTargetHpPercent = target.CurrentHp / (float)target.MaxHp * 100f;

        // 悬停高度校正：已选中目标后，若悬停高度超过 目标Y + ZOffset + 8m（估算偏差或回退锚点不准），
        // 持续下调到目标正上方 ZOffset 高度，保证目标始终在可选中/可输出范围内。
        // 注意：血量已达标时不校正——进入下坐骑阶段后不能再有自身移动指令
        // （BossMod 等躲避插件会在骑乘状态移动角色，自身寻路会加剧干扰下坐骑）。
        if (Player.Mounted && Player.CanFly
            && CurrentTargetHpPercent > P.Config.DismountHpPercent
            && Player.Position.Y - target.Position.Y > P.Config.ZOffset + 8f
            && S.VnavmeshIPC.GetIsReady()
            && EzThrottler.Throttle("WYHoverAdjust", 3000))
        {
            var hoverPoint = new Vector3(target.Position.X, target.Position.Y + P.Config.ZOffset, target.Position.Z);
            S.VnavmeshIPC.TryPathfindAndMoveTo(hoverPoint, true);
            if (P.Config.Debug) PluginLog.Debug($"[AutoHunt] 悬停过高(ΔY={Player.Position.Y - target.Position.Y:0.0}m)，下调至目标上方 {P.Config.ZOffset:0}m");
        }

        // 血量低于阈值 → 下坐骑准备输出
        if (CurrentTargetHpPercent <= P.Config.DismountHpPercent)
        {
            if (Player.Mounted && P.Config.AutoAttack)
            {
                // 立即停止自身寻路：下坐骑阶段不允许自身移动指令干扰
                // （飞行中按坐骑键触发自动降落，任何移动输入都会打断降落）
                S.VnavmeshIPC.StopPath();
                float heightDiff = Math.Abs(Player.Position.Y - target.Position.Y);
                if (Player.CanFly && heightDiff > 5f)
                {
                    CurrentState = State.Descending;
                    stateStartTime = DateTime.Now;
                    navStarted = false;
                    Notify.Info($"目标血量 {CurrentTargetHpPercent:0}% ≤ {P.Config.DismountHpPercent:0}%，正在下降到地面…");
                }
                else
                {
                    // 已在地面或不可飞行，直接下坐骑
                    CurrentState = State.Dismounting;
                    dismountPending = true;
                    dismountStartTime = DateTime.Now;
                    dismountWarned = false;
                    Notify.Info($"目标血量 {CurrentTargetHpPercent:0}% ≤ {P.Config.DismountHpPercent:0}%，正在下坐骑…");
                }
            }
            else
            {
                // 没骑乘或没开自动攻击，直接进入输出
                CurrentState = State.Outputting;
                StartOutput();
            }
        }
    }

    /// <summary>
    /// 下降阶段：从飞行悬停高度（ZOffset）下降到目标怪地面位置。
    /// 使用 vnavmesh IPC 寻路到 (mob.X, mob.Y, mob.Z)，到地后转入 Dismounting。
    /// </summary>
    private static void UpdateDescending()
    {
        var target = GetValidTarget();

        // 目标死亡 → 无需下降，直接结束
        if (target != null && target.IsDead)
        {
            S.VnavmeshIPC.StopPath();
            OnMobKilled();
            StopOutput();
            CurrentState = State.Finished;
            stateStartTime = DateTime.Now;
            return;
        }

        Vector3 groundDest;
        if (target != null)
        {
            TrackTarget(target);
            groundDest = target.Position;
        }
        else
        {
            // 目标丢失：按最后记录的位置继续下降，同时尝试重新选中
            if (targetLostSince == DateTime.MinValue)
            {
                targetLostSince = DateTime.Now;
                if (P.Config.Debug) PluginLog.Debug("[AutoHunt] 下降阶段目标丢失，按最后位置继续下降并尝试重新选中…");
            }
            TryRetarget();
            if (lastTargetPos != Vector3.Zero)
            {
                groundDest = lastTargetPos;
            }
            else
            {
                // 从未记录过位置（理论不会发生）：跳过下降直接下坐骑
                S.VnavmeshIPC.StopPath();
                CurrentState = State.Dismounting;
                dismountPending = true;
                dismountStartTime = DateTime.Now;
                dismountWarned = false;
                navStarted = false;
                return;
            }
        }
        float distY = Math.Abs(Player.Position.Y - groundDest.Y);
        float distXZ = Vector2.Distance(
            new Vector2(Player.Position.X, Player.Position.Z),
            new Vector2(groundDest.X, groundDest.Z));

        if (!navStarted)
        {
            navStarted = true;
            S.VnavmeshIPC.StopPath();
            S.VnavmeshIPC.TryPathfindAndMoveTo(groundDest, Player.CanFly);
            lastPreciseRetry = DateTime.Now;
            if (P.Config.Debug) PluginLog.Debug($"[AutoHunt] 开始下降到地面: {groundDest} (当前 Y={Player.Position.Y:0.0}, 目标 Y={groundDest.Y:0.0})");
        }

        bool running = S.VnavmeshIPC.GetPathIsRunning();
        bool finding = S.VnavmeshIPC.GetPathfindInProgress();

        // 已贴地 → 进入下坐骑。
        // 只要脱离飞行状态（InFlight=false，飞行坐骑已真正落地）即可，
        // 不再要求横向距离 <15m：BossMod 等躲避插件会在骑乘状态拖着角色横向移动，
        // 旧判定（distXZ < 15m）在躲避期间永远无法满足，只能干等超时。
        bool grounded = !Svc.Condition[ConditionFlag.InFlight];
        if (grounded || (distXZ < 15f && distY < 1.5f))
        {
            S.VnavmeshIPC.StopPath();
            CurrentState = State.Dismounting;
            dismountPending = true;
            dismountStartTime = DateTime.Now;
            dismountWarned = false;
            navStarted = false;
            if (P.Config.Debug) PluginLog.Debug($"[AutoHunt] 已下降到地面 (Y={Player.Position.Y:0.0})，开始下坐骑");
            return;
        }

        // 路径中断且未到地 → 3 秒重试
        if (!running && !finding && (DateTime.Now - lastPreciseRetry).TotalSeconds > 3)
        {
            S.VnavmeshIPC.TryPathfindAndMoveTo(groundDest, Player.CanFly);
            lastPreciseRetry = DateTime.Now;
            if (P.Config.Debug) PluginLog.Debug($"[AutoHunt] 重试下降寻路 (distY={distY:0.0}m, distXZ={distXZ:0.0}m)");
        }

        // 超时 15 秒：强制进入下坐骑（可能在地面附近卡住）
        if ((DateTime.Now - stateStartTime).TotalSeconds > 15)
        {
            S.VnavmeshIPC.StopPath();
            CurrentState = State.Dismounting;
            dismountPending = true;
            dismountStartTime = DateTime.Now;
            dismountWarned = false;
            navStarted = false;
            Notify.Info("下降超时，强制下坐骑…");
        }
    }

    /// <summary>下坐骑流程。
    /// FFXIV 机制：飞行中按坐骑键只触发「自动降落」，落地后需再按一次才真正下坐骑。
    /// 因此持续按坐骑键：飞行中第一下触发降落，落地后下一击完成下坐骑。
    /// BossMod 等躲避插件会在骑乘状态下移动角色躲避技能，反复打断自动降落/下坐骑——
    /// 旧版 15 秒超时后带着坐骑直接开始输出（骑乘状态无法输出，等于整个循环废掉）。
    /// 现改为：每 0.5 秒重试一次，持续清掉残留寻路，绝不带着坐骑进入输出阶段，
    /// 一直重试到真正下坐骑、目标死亡，或 60 秒极端兜底放弃为止。
    /// </summary>
    private static void UpdateDismounting()
    {
        if (!Player.Mounted)
        {
            // 下坐骑成功
            dismountPending = false;
            CurrentState = State.Outputting;
            StartOutput();
            return;
        }

        // 目标死亡 → 无需继续下坐骑，直接结束（下坐骑途中怪被打死很常见）
        var target = GetValidTarget();
        if (target != null && target.IsDead)
        {
            S.VnavmeshIPC.StopPath();
            OnMobKilled();
            StopOutput();
            CurrentState = State.Finished;
            stateStartTime = DateTime.Now;
            return;
        }

        // 持续清掉残留/被复活的寻路路径：下坐骑阶段不允许任何自身移动指令
        // （飞行中任何移动输入都会打断自动降落）
        if (EzThrottler.Throttle("WYHuntDismountStopNav", 1000))
            S.VnavmeshIPC.StopPath();

        // 每 0.5 秒按一次坐骑键（避开动画锁）：
        // 飞行中 → 触发自动降落；已落地 → 真正下坐骑（地面移动中也可下坐骑）。
        // 躲避插件打断降落后下一次按键会重新触发，读到技能间隙即可成功。
        if (EzThrottler.Throttle("WYHuntDismount", 500) && !Player.IsAnimationLocked)
        {
            var am = FFXIVClientStructs.FFXIV.Client.Game.ActionManager.Instance();
            if (am != null && am->GetActionStatus(FFXIVClientStructs.FFXIV.Client.Game.ActionType.GeneralAction, 9) == 0)
            {
                am->UseAction(FFXIVClientStructs.FFXIV.Client.Game.ActionType.GeneralAction, 9);
            }
            else
            {
                // 官方命令表无 /dismount；/mount 在骑乘中执行即为解散坐骑
                Chat.ExecuteCommand("/mount");
            }
        }

        double elapsed = (DateTime.Now - dismountStartTime).TotalSeconds;

        // 15 秒提示一次（可能正被躲避插件持续控制移动），但绝不放弃
        if (elapsed > 15 && !dismountWarned)
        {
            dismountWarned = true;
            Notify.Info("下坐骑被持续打断（躲避插件正在控制角色移动），继续重试直到成功…");
        }
        if (P.Config.Debug && elapsed > 15 && EzThrottler.Throttle("WYHuntDismountDbg", 5000))
        {
            PluginLog.Debug($"[AutoHunt] 下坐骑重试中: InFlight={Svc.Condition[ConditionFlag.InFlight]}, Pos=({Player.Position.X:0.0}, {Player.Position.Y:0.0}, {Player.Position.Z:0.0})");
        }

        // 60 秒极端兜底：被持续控制移动等异常场合放弃该目标
        if (elapsed > 60)
        {
            Notify.Error("下坐骑超时（60秒），放弃当前目标。");
            S.VnavmeshIPC.StopPath();
            StopOutput();
            CurrentState = State.Finished;
            stateStartTime = DateTime.Now;
        }
    }

    /// <summary>输出中：监测目标死亡。目标丢失时保持输出并自动重新选中。</summary>
    private static void UpdateOutputting()
    {
        var target = GetValidTarget();

        // 目标死亡 → 停止输出
        if (target != null && target.IsDead)
        {
            OnMobKilled();
            StopOutput();
            CurrentState = State.Finished;
            stateStartTime = DateTime.Now;
            Notify.Info($"狩猎怪 {CurrentTargetName} 已死亡，停止输出。");
            return;
        }

        // 目标丢失：保持输出开启，持续重新选中（重新选中后 RotationSolver 自动继续输出）
        if (target == null)
        {
            if (targetLostSince == DateTime.MinValue)
            {
                targetLostSince = DateTime.Now;
                if (P.Config.Debug) PluginLog.Debug("[AutoHunt] 输出阶段目标丢失，尝试重新选中…");
            }
            TryRetarget();

            // 战斗中（我们打过它、仇恨还在）→ 怪大概率还活着，只是被挤出对象表
            // 选不中而已。绝不停止输出，持续等待重新选中。
            bool inCombat = Svc.Condition[ConditionFlag.InCombat];
            if (inCombat)
            {
                outOfCombatSince = DateTime.MinValue;
                return;
            }

            // 脱战（怪的仇恨表已清空 = 已死亡）+ 周围无存活狩猎怪 → 视为已击杀。
            // 补调 OnMobKilled 计数（按记录的目标 ID），修复"视为已击杀"却不计数的问题。
            if (outOfCombatSince == DateTime.MinValue) outOfCombatSince = DateTime.Now;
            bool noHuntNearby = FindNearestHuntMob() == null;
            bool lostLongEnough = (DateTime.Now - targetLostSince).TotalSeconds > 60;
            bool outOfCombatLongEnough = (DateTime.Now - outOfCombatSince).TotalSeconds > 10;
            if (noHuntNearby && outOfCombatLongEnough)
            {
                Notify.Info("目标已丢失且脱离战斗，视为已击杀，停止输出。");
                OnMobKilled();
                StopOutput();
                CurrentState = State.Finished;
                stateStartTime = DateTime.Now;
            }
            else if (lostLongEnough && noHuntNearby)
            {
                // 兜底：长时间丢失且无怪（脱战判定异常时的保险路径）
                Notify.Info("目标长时间丢失且周围无存活狩猎怪，视为已击杀，停止输出。");
                OnMobKilled();
                StopOutput();
                CurrentState = State.Finished;
                stateStartTime = DateTime.Now;
            }
            return;
        }

        targetLostSince = DateTime.MinValue;
        outOfCombatSince = DateTime.MinValue;
        TrackTarget(target);
        CurrentTargetHpPercent = target.CurrentHp / (float)target.MaxHp * 100f;
    }

    private static void StartOutput()
    {
        if (!P.Config.AutoAttack) return;
        Chat.ExecuteCommand(P.Config.RotationStartCommand);
        if (P.Config.Debug) PluginLog.Debug("执行输出命令: " + P.Config.RotationStartCommand);
    }

    private static void StopOutput()
    {
        Chat.ExecuteCommand(P.Config.RotationStopCommand);
        if (P.Config.Debug) PluginLog.Debug("执行停止输出命令: " + P.Config.RotationStopCommand);
    }

    private static void OnMobKilled()
    {
        var target = Svc.Targets.Target as IBattleNpc;
        var mobId = target?.GameObjectId ?? lastTargetId;
        var nameId = target?.NameId ?? 0;
        // 插件主动选中的目标被击杀时，即使狩猎怪数据库未收录该怪也照常计数
        bool forceCount = target != null && mobId != 0 && mobId == lastTargetId;
        InstanceController.OnMobKilled(mobId, nameId, forceCount);
    }

    /// <summary>记录当前目标的 ID 与位置，供目标丢失后找回/继续下降用。</summary>
    private static void TrackTarget(IBattleNpc target)
    {
        lastTargetId = target.GameObjectId;
        lastTargetPos = target.Position;
        // 主动标记参与：插件选过的怪即使之后目标丢失/流程提前结束，
        // 死亡时也能被 InstanceController 计数（修复本区击杀数不增加）
        InstanceController.MarkEngaged(target.GameObjectId);
    }

    /// <summary>
    /// 获取经过校验的当前目标：
    /// 目标 ID 与追踪 ID（lastTargetId）不一致时返回 null（视为目标丢失）。
    /// 狩猎车人多时怪物会被挤出对象表，Svc.Targets.Target 的引用可能失效——
    /// 底层内存槽位被其他对象复用后，IsDead/CurrentHp 读到的是复用对象的数据，
    /// 会把活怪误判成"已死亡"从而提前停止输出。ID 校验可彻底规避。
    /// </summary>
    private static IBattleNpc GetValidTarget()
    {
        var t = Svc.Targets.Target as IBattleNpc;
        if (t == null) return null;
        if (lastTargetId != 0 && t.GameObjectId != lastTargetId) return null;
        return t;
    }

    /// <summary>
    /// 目标丢失后重新选中（每秒尝试一次）：
    /// 优先按记录的 GameObjectId 找回原目标，找不到再搜索最近的存活狩猎怪。
    /// </summary>
    private static void TryRetarget()
    {
        if (!EzThrottler.Throttle("WYHuntRetarget", 1000)) return;

        IBattleNpc? mob = null;
        if (lastTargetId != 0)
            mob = Svc.Objects.FirstOrDefault(x => x.GameObjectId == lastTargetId) as IBattleNpc;
        if (mob == null || mob.IsDead)
            mob = FindNearestHuntMob();

        if (mob == null) return;

        Svc.Targets.Target = mob;
        CurrentTargetName = mob.Name.TextValue;
        CurrentTargetRank = HuntMobDatabase.GetRankLabel(mob.NameId);
        CurrentTargetHpPercent = mob.CurrentHp / (float)mob.MaxHp * 100f;
        TrackTarget(mob);
        targetLostSince = DateTime.MinValue;
        if ((DateTime.Now - lastRetargetNotify).TotalSeconds > 10)
        {
            lastRetargetNotify = DateTime.Now;
            Notify.Info($"目标丢失，已重新选中: [{CurrentTargetRank}] {CurrentTargetName} (HP {CurrentTargetHpPercent:0}%)");
        }
    }

    public static void Reset()
    {
        CurrentState = State.Idle;
        pendingTarget = null;
        navStarted = false;
        notifiedNoHunt = false;
        dismountPending = false;
        dismountWarned = false;
        preciseStarted = false;
        preciseDest = Vector3.Zero;
        lastTargetId = 0;
        lastTargetPos = Vector3.Zero;
        targetLostSince = DateTime.MinValue;
        outOfCombatSince = DateTime.MinValue;
        CurrentTargetName = "";
        CurrentTargetRank = "";
        CurrentTargetHpPercent = 100f;
        S.VnavmeshIPC.StopPath();
    }

    /// <summary>寻找最近的狩猎怪（通过 NotoriousMonster 数据表判定 B/A/S 级）。</summary>
    private static IBattleNpc? FindNearestHuntMob()
    {
        IBattleNpc? best = null;
        float bestDist = float.MaxValue;
        bool includeB = P.Config.IncludeBRank;
        bool dbEmpty = HuntMobDatabase.RankMap.Count == 0;

        foreach (var obj in Svc.Objects)
        {
            if (obj is not IBattleNpc npc) continue;
            if (npc.IsDead) continue;

            // 首选判定：NameId 是否命中狩猎怪数据库（NotoriousMonster 表，Rank>=2 为 A/S）
            bool isHunt = HuntMobDatabase.IsHuntMob(npc.NameId, includeB);
            // 数据库不可用时回退到名称关键词匹配
            if (!isHunt && dbEmpty && IsHuntMobName(npc.Name.TextValue)) isHunt = true;
            if (!isHunt) continue;

            float dist = Vector3.Distance(Player.Position, npc.Position);
            if (dist < bestDist)
            {
                bestDist = dist;
                best = npc;
            }
        }

        if (best != null && P.Config.Debug)
        {
            var label = HuntMobDatabase.GetRankLabel(best.NameId);
            PluginLog.Debug($"[AutoHunt] 锁定狩猎怪: {best.Name.TextValue} (NameId={best.NameId}, 等级={label}, 距离={bestDist:0}m)");
        }

        return best;
    }

    /// <summary>通过名称判断是否为狩猎怪（仅作数据库不可用时的回退，可靠性低）。</summary>
    private static bool IsHuntMobName(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        var lower = name.ToLower();
        return lower.Contains("狩猎") || lower.Contains("恶名") || lower.Contains(" notorious");
    }

    /// <summary>寻找最近的队友（用于未找到狩猎怪时跟随）。</summary>
    private static IGameObject? FindNearestAlly()
    {
        IGameObject? best = null;
        float bestDist = float.MaxValue;
        ulong myId = Player.Available ? Player.Object.GameObjectId : 0;

        foreach (var obj in Svc.Objects)
        {
            if (obj is not IPlayerCharacter pc) continue;
            if (pc.GameObjectId == myId) continue;

            float dist = Vector3.Distance(Player.Position, pc.Position);
            if (dist < bestDist)
            {
                bestDist = dist;
                best = pc;
            }
        }

        return best;
    }
}
