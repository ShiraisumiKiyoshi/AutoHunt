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

    // 目标追踪（防止玩家过多导致目标丢失后流程中断）
    private static ulong lastTargetId = 0;
    private static Vector3 lastTargetPos = Vector3.Zero;
    private static DateTime targetLostSince = DateTime.MinValue;
    private static DateTime lastRetargetNotify = DateTime.MinValue;

    /// <summary>收到新车头坐标时触发状态机。</summary>
    public static void OnNewCoordinate(TargetPosition target)
    {
        if (target == null) return;

        // 如果正在输出中，先停止（车头发新坐标 = 当前怪已处理完或要换目标）
        if (CurrentState == State.Outputting || CurrentState == State.Attacking || CurrentState == State.Dismounting || CurrentState == State.Descending)
        {
            StopOutput();
        }

        pendingTarget = target;
        navStarted = false;
        notifiedNoHunt = false;
        dismountPending = false;
        preciseStarted = false;
        preciseDest = Vector3.Zero;
        lastTargetId = 0;
        lastTargetPos = Vector3.Zero;
        targetLostSince = DateTime.MinValue;

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

    /// <summary>计算精确悬停目标点：XZ 为车头坐标，Y = 当前飞行高度 + ZOffset（不可飞地图不加偏移）。</summary>
    private static Vector3 ComputePreciseDest(TargetPosition target)
    {
        float y = Player.Position.Y;
        if (Player.CanFly && P.Config.ZOffset > 0f) y += P.Config.ZOffset;
        return new Vector3(target.WorldXZ.X, y, target.WorldXZ.Y);
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
        // 先尝试找周围已知的狩猎怪
        var huntMob = FindNearestHuntMob();
        if (huntMob != null)
        {
            Svc.Targets.Target = huntMob;
            CurrentTargetName = huntMob.Name.TextValue;
            CurrentTargetRank = HuntMobDatabase.GetRankLabel(huntMob.NameId);
            CurrentTargetHpPercent = huntMob.CurrentHp / (float)huntMob.MaxHp * 100f;
            TrackTarget(huntMob);
            targetLostSince = DateTime.MinValue;
            CurrentState = State.Attacking;
            stateStartTime = DateTime.Now;
            Notify.Info($"已选中狩猎怪: [{CurrentTargetRank}] {CurrentTargetName} (HP {CurrentTargetHpPercent:0}%)");
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

        // 持续搜索（每秒一次）
        if (EzThrottler.Throttle("WYHuntScan", 1000))
        {
            huntMob = FindNearestHuntMob();
            if (huntMob != null)
            {
                Svc.Targets.Target = huntMob;
                CurrentTargetName = huntMob.Name.TextValue;
                CurrentTargetRank = HuntMobDatabase.GetRankLabel(huntMob.NameId);
                CurrentTargetHpPercent = huntMob.CurrentHp / (float)huntMob.MaxHp * 100f;
                TrackTarget(huntMob);
                targetLostSince = DateTime.MinValue;
                CurrentState = State.Attacking;
                stateStartTime = DateTime.Now;
                Notify.Info($"已选中狩猎怪: [{CurrentTargetRank}] {CurrentTargetName} (HP {CurrentTargetHpPercent:0}%)");
            }
        }

        // 超时放弃
        if ((DateTime.Now - stateStartTime).TotalSeconds > 30)
        {
            Notify.Error("30秒内未找到狩猎怪，放弃当前目标。");
            Reset();
        }
    }

    /// <summary>攻击中：监测血量，低于阈值则下坐骑输出。目标丢失时自动重新选中。</summary>
    private static void UpdateAttacking()
    {
        var target = Svc.Targets.Target as IBattleNpc;

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
            if ((DateTime.Now - targetLostSince).TotalSeconds > 30)
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

        // 血量低于阈值 → 下坐骑准备输出
        if (CurrentTargetHpPercent <= P.Config.DismountHpPercent)
        {
            if (Player.Mounted && P.Config.AutoAttack)
            {
                // 飞行中悬停在 ZOffset 高度，需要先降到地面再下坐骑
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
        var target = Svc.Targets.Target as IBattleNpc;

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

        // 接近地面 → 进入下坐骑（要求真正贴地：Y 差 < 1.5m，或已脱离飞行状态）
        bool grounded = !Svc.Condition[ConditionFlag.InFlight];
        if (distXZ < 15f && (distY < 1.5f || (distY < 5f && grounded)))
        {
            S.VnavmeshIPC.StopPath();
            CurrentState = State.Dismounting;
            dismountPending = true;
            dismountStartTime = DateTime.Now;
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
            navStarted = false;
            Notify.Info("下降超时，强制下坐骑…");
        }
    }

    /// <summary>下坐骑流程。
    /// FFXIV 机制：飞行中按坐骑键只触发「自动降落」，落地后需再按一次才真正下坐骑。
    /// 因此持续按坐骑键：飞行中第一下触发降落，落地后下一击完成下坐骑。
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

        // 每秒按一次坐骑键（避开动画锁）：
        // 飞行中 → 触发自动降落；已落地 → 真正下坐骑
        if (EzThrottler.Throttle("WYHuntDismount", 1000) && !Player.IsAnimationLocked)
        {
            var am = FFXIVClientStructs.FFXIV.Client.Game.ActionManager.Instance();
            if (am != null && am->GetActionStatus(FFXIVClientStructs.FFXIV.Client.Game.ActionType.GeneralAction, 9) == 0)
            {
                am->UseAction(FFXIVClientStructs.FFXIV.Client.Game.ActionType.GeneralAction, 9);
            }
            else
            {
                Chat.ExecuteCommand("/dismount");
            }
        }

        // 超时兜底：飞行自动降落约需 3~10 秒，给足 15 秒
        if ((DateTime.Now - dismountStartTime).TotalSeconds > 15)
        {
            Notify.Error("下坐骑超时（15秒），仍处于骑乘状态，直接开始输出。");
            dismountPending = false;
            CurrentState = State.Outputting;
            StartOutput();
        }
    }

    /// <summary>输出中：监测目标死亡。目标丢失时保持输出并自动重新选中。</summary>
    private static void UpdateOutputting()
    {
        var target = Svc.Targets.Target as IBattleNpc;

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
            // 长时间无目标且周围已无存活狩猎怪 → 视为已击杀，结束输出
            if ((DateTime.Now - targetLostSince).TotalSeconds > 60 && FindNearestHuntMob() == null)
            {
                Notify.Info("目标长时间丢失且周围无存活狩猎怪，视为已击杀，停止输出。");
                StopOutput();
                CurrentState = State.Finished;
                stateStartTime = DateTime.Now;
            }
            return;
        }

        targetLostSince = DateTime.MinValue;
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
        var mobId = target?.GameObjectId ?? 0;
        var nameId = target?.NameId ?? 0;
        InstanceController.OnMobKilled(mobId, nameId);
    }

    /// <summary>记录当前目标的 ID 与位置，供目标丢失后找回/继续下降用。</summary>
    private static void TrackTarget(IBattleNpc target)
    {
        lastTargetId = target.GameObjectId;
        lastTargetPos = target.Position;
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
        preciseStarted = false;
        preciseDest = Vector3.Zero;
        lastTargetId = 0;
        lastTargetPos = Vector3.Zero;
        targetLostSince = DateTime.MinValue;
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
