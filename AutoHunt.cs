using AutoHunt.Tasks;
using ECommons.GameHelpers;
using ECommons.Automation.NeoTaskManager;
using ECommons.SimpleGui;
using ECommons.EzIpcManager;
using ECommons.ImGuiMethods;
using Lumina.Excel.Sheets;

namespace AutoHunt;

/// <summary>
/// 主入口：插件生命周期管理、主循环、传送状态机。
/// </summary>
public unsafe class AutoHunt : IDalamudPlugin
{
    internal static AutoHunt P;
    internal Config Config;
    internal TaskManager TaskManager;
    internal ArrivalData? TeleportTo = null;
    internal bool WasBetweenAreas = false;
    internal Vector3 LastPosition = Vector3.Zero;
    internal bool IsMoving = false;

    // 副本区切换流程：切区任务执行中，暂存此期间收到的车头坐标，
    // 切区完成后自动继续前往（防止重复坐标吞掉切区）
    internal bool SwitchInProgress = false;
    internal DateTime SwitchStartTime = DateTime.MinValue;
    internal TargetPosition? HeldCoordinate = null;

    // 到达后待执行的副本区号（>0 = 传送到达后需 /pdr insc N）。
    // 延迟到完全落地（退出读图过渡、可交互）再执行：跨图落地瞬间 TerritoryChanged 触发
    // OnArrival 时 BetweenAreas 往往仍有效，立刻发送会把上一段传送的过渡误判为切区的读图信号。
    internal int PendingArrivalSwitch = 0;

    public AutoHunt(IDalamudPluginInterface pi)
    {
        P = this;
        ECommonsMain.Init(pi, this);
        // 服务必须最先初始化，且构造过程不可能抛异常——事件订阅放在最后，
        // 保证一旦 Framework.Update 已订阅，所有服务字段必然非空。
        S.Initialize();
        EzConfig.Migrate<Config>();
        Config = EzConfig.Init<Config>();
        Config.MigrateLegacyConductor();
        EzConfigGui.Init(new PluginUI.MainWindow());
        EzConfigGui.Window.RespectCloseHotkey = false;
        EzConfigGui.WindowSystem.AddWindow(new PluginUI.FloatingWindow());
        EzCmd.Add("/ah", OnChatCommand, "AutoHunt 自动狩猎助手\n/ah: 打开设置界面\n/ah stop: 停止所有自动行为\n/ah clear: 清除车头\n/ah set <玩家名>: 手动设置车头\n/ah reset: 重置副本区记录");
        TaskManager = new(new TaskManagerConfiguration(timeLimitMS: 60000));
        Svc.Chat.ChatMessage += ChatMessageHandler.Chat_ChatMessage;
        Svc.Framework.Update += Framework_Update;
        Svc.ClientState.TerritoryChanged += ClientState_TerritoryChanged;
    }

    private void ClientState_TerritoryChanged(uint territory)
    {
        InstanceController.OnTerritoryChanged(territory);
        EndMapWatcher.OnTerritoryChanged();
        if (TeleportTo != null && (territory == 0 || territory == TeleportTo.Territory))
        {
            OnArrival();
        }
    }

    /// <summary>总开关关闭提示只发一次（重新开启后再次关闭才会再提示）。</summary>
    private bool masterSwitchArmed = true;

    /// <summary>
    /// 暂停标志（运行时状态，不入配置）：true 时冻结任务队列（TaskManager.StepMode）并跳过全部控制器推进，
    /// 保留现场；再次点击播放键恢复。与总开关不同——暂停不会清空队列/状态机。
    /// </summary>
    internal static bool Paused = false;

    /// <summary>暂停开始的时刻（恢复时据此计算暂停时长，补偿各状态机的墙钟计时器）。</summary>
    private static DateTime pausedAt = DateTime.MinValue;

    /// <summary>
    /// 总开关闸门：关闭时立即停止所有自动行为并清空操作队列（中止任务链、复位全部状态机、
    /// 停止寻路、取消招募监听），保证重新开启后处于干净的空闲状态、绝不继续之前被中断的流程。
    /// </summary>
    private void MasterSwitchGate()
    {
        if (Config.Enabled)
        {
            masterSwitchArmed = true;
            return;
        }
        if (!masterSwitchArmed) return;
        masterSwitchArmed = false;

        Paused = false; // 总开关关闭 = 彻底复位，暂停态一并清除
        pausedAt = DateTime.MinValue;
        TaskManager.Abort();
        CrossRegionController.Reset();
        HuntController.Reset();
        InstanceController.Reset();
        EndMapWatcher.Reset();
        ConductorFetchService.Cancel();
        TeleportTo = null;
        SwitchInProgress = false;
        HeldCoordinate = null;
        PendingArrivalSwitch = 0;
        WasBetweenAreas = false;
        try { S.VnavmeshIPC?.StopPath(); } catch { }
        Notify.Info("插件已关闭：已停止所有自动行为并清空操作队列。");
    }

    /// <summary>主循环异常兜底：任何控制器异常只提示一次，不让异常反复打断 Update。</summary>
    private DateTime lastErrorNotify = DateTime.MinValue;

    // 依赖插件检测：启动 10 秒后首次检查（等其他插件加载完成）；若发现缺失，60 秒时再复核一次（避免加载慢被误报）
    private DateTime depCheckTime = DateTime.Now.AddSeconds(10);
    private DateTime depRecheckTime = DateTime.MinValue;
    private bool depChecked = false;

    private void Framework_Update(object framework)
    {
        try
        {
            // 使用统计上报：登录角色后按间隔上报，失败静默忽略，与总开关无关
            Services.StatsReporter.Update();

            // 总开关闸门：关闭时停止一切并清空队列（提示一次），主循环不再推进任何控制器
            MasterSwitchGate();
            if (!Config.Enabled) return;

            // 暂停闸门：冻结任务队列（TaskManager 停止步进，现场保留）并跳过全部控制器推进；
            // 不清空队列/状态机，恢复时从暂停点继续
            if (Paused)
            {
                if (pausedAt == DateTime.MinValue) pausedAt = DateTime.Now;
                if (!TaskManager.StepMode) TaskManager.StepMode = true;
                return;
            }

            // 从暂停恢复：把暂停期间流逝的墙钟时间补偿给所有基于绝对时间的计时器，
            // 否则任务链（60s 超时）与跨区阶段（120s 超时）会在暂停期间"被超时"，
            // 导致恢复后流程被静默跳过（典型症状：获取车头阶段被跳过直接开招募）
            if (pausedAt != DateTime.MinValue)
            {
                var pauseMs = (long)(DateTime.Now - pausedAt).TotalMilliseconds;
                pausedAt = DateTime.MinValue;
                if (TaskManager.IsBusy && TaskManager.RemainingTimeMS is > 0 and < int.MaxValue / 2)
                    TaskManager.RemainingTimeMS += pauseMs; // 当前任务的超时线顺延（排队任务启动时会重新计时，无需处理）
                CrossRegionController.CompensatePause(pauseMs);
                ConductorFetchService.CompensatePause(pauseMs);
                InstanceController.CompensatePause(pauseMs);
                if (SwitchInProgress) SwitchStartTime += TimeSpan.FromMilliseconds(pauseMs);
            }
            if (TaskManager.StepMode) TaskManager.StepMode = false;

            if (!depChecked && DateTime.Now >= depCheckTime)
            {
                depChecked = true;
                if (DependencyChecker.CheckAndNotify())
                {
                    depRecheckTime = DateTime.Now.AddSeconds(50);
                }
            }
            else if (depRecheckTime != DateTime.MinValue && DateTime.Now >= depRecheckTime)
            {
                depRecheckTime = DateTime.MinValue;
                DependencyChecker.CheckAndNotify();
            }

            // 副本区切换完成检测（DR 快捷副本区切换 /pdr insc）：
            // 由 InstanceController 依据「读图过渡 / 原生区号 / DR 反馈 / 超时」判定切换是否结束，
            // 结束后继续暂存的车头坐标。必须等切换真正完成（含加载过渡）再派发，
            // 否则坐标流程会和切换的传送撞在一起，落地后处于未骑乘状态直接进寻路。
            if (SwitchInProgress && InstanceController.UpdateSwitchProgress())
            {
                SwitchInProgress = false;
                var held = HeldCoordinate;
                HeldCoordinate = null;
                if (held != null)
                {
                    Notify.Info("副本区切换完成，继续前往车头坐标…");
                    HuntController.OnNewCoordinate(held);
                }
                else if (HuntController.CurrentState == HuntController.State.Teleporting)
                {
                    // 跨图到达后切 1 号区的场景：坐标流程已在 Teleporting 中等待，
                    // 切区完成后继续前往（否则状态机会永远停在 Teleporting）
                    Notify.Info("副本区切换完成，继续前往车头坐标…");
                    HuntController.OnArrived();
                }
                else if (HuntController.CurrentState == HuntController.State.Idle
                    && HuntController.CurrentPendingTarget != null)
                {
                    // 兜底：流程被重置过但车头坐标仍在，直接续跑（否则要等车头下一条坐标才动）
                    Notify.Info("副本区切换完成，继续前往车头坐标…");
                    HuntController.OnNewCoordinate(HuntController.CurrentPendingTarget);
                }
            }

            if (!Player.Available) return;
            // IPC 服务未就绪（初始化失败/热重载竞态）时跳过本轮，避免 NullReferenceException
            if (S.LifestreamIPC == null || S.TeleporterIPC == null || S.VnavmeshIPC == null) return;

            // 记录移动状态（用于传送门控）
            IsMoving = Player.Position != LastPosition;
            LastPosition = Player.Position;

            // 到达后的副本区切换：延迟到完全落地（可交互、退出读图过渡）再执行，
            // 避免把上一段传送的 BetweenAreas 误当成切区自己的读图过渡（假完成）
            if (PendingArrivalSwitch > 0 && !SwitchInProgress && !CrossRegionController.Active
                && !P.TaskManager.IsBusy
                && Player.Interactable
                && !Svc.Condition[ConditionFlag.BetweenAreas] && !Svc.Condition[ConditionFlag.BetweenAreas51])
            {
                var line = PendingArrivalSwitch;
                PendingArrivalSwitch = 0;
                InstanceController.BeginDrSwitch(line);
            }

            InstanceController.Update();
            EndMapWatcher.Update();
            Conductor.EnsureFocus();
            CrossRegionController.Update();
            HuntController.Update();
            UpdateTeleport();
        }
        catch (Exception e)
        {
            // 10 秒最多提示一次，避免异常风暴刷屏
            if ((DateTime.Now - lastErrorNotify).TotalSeconds > 10)
            {
                lastErrorNotify = DateTime.Now;
                PluginLog.Error($"主循环异常: {e}\n如持续出现请反馈该日志。");
            }
        }
    }

    /// <summary>
    /// 传送状态机：等待安全状态后执行传送，到达后恢复任务链。
    /// </summary>
    private void UpdateTeleport()
    {
        var betweenAreas = Svc.Condition[ConditionFlag.BetweenAreas] || Svc.Condition[ConditionFlag.BetweenAreas51];
        if (betweenAreas)
        {
            WasBetweenAreas = true;
            return;
        }

        if (WasBetweenAreas)
        {
            // 到达目的地（同地图传送不会触发 TerritoryChanged，在这里兜底）
            WasBetweenAreas = false;
            if (TeleportTo != null) OnArrival();
            return;
        }

        if (TeleportTo == null) return;
        if (!Player.Interactable) return;

        if (Player.IsCasting)
        {
            EzThrottler.Throttle("WYTeleport", 500, true);
        }
        if (Svc.Condition[ConditionFlag.MountOrOrnamentTransition])
        {
            EzThrottler.Throttle("WYTeleport", 500, true);
        }

        if (!Svc.Condition[ConditionFlag.InCombat] && !Svc.Condition[ConditionFlag.Casting] && !IsMoving)
        {
            if (EzThrottler.Throttle("WYTeleport", 1000) && !Player.IsAnimationLocked)
            {
                var aetheryteId = TeleportTo.Aetheryte?.RowId ?? 0u;
                if (aetheryteId == 0) return;
                if (!S.TeleporterIPC.TryTeleport(aetheryteId, 0))
                {
                    if (!S.LifestreamIPC.TryTeleport(aetheryteId))
                    {
                        NativeTeleport(aetheryteId);
                    }
                }
            }
        }
    }

    internal static void NativeTeleport(uint aetheryteId)
    {
        var instance = FFXIVClientStructs.FFXIV.Client.Game.UI.Telepo.Instance();
        if (instance != null)
        {
            instance->Teleport(aetheryteId, 0);
        }
    }

    /// <summary>
    /// 到达传送目的地：中断任务链，按需切换副本区（DR /pdr insc），触发狩猎流程。
    /// </summary>
    private void OnArrival()
    {
        var data = TeleportTo;
        TeleportTo = null;
        WasBetweenAreas = false;
        TaskManager.Abort();
        if (data == null) return;

        // 传送会清掉焦点，到达后立刻恢复车头焦点
        Conductor.EnsureFocus();

        if (data.SwitchInstance > 0)
        {
            // 本区击杀满、车头新坐标仍在本图：已传送到坐标最近水晶，切下一副本区。
            // 切区期间车头坐标由 HeldCoordinate 暂存，完成后由主循环重放。
            PendingArrivalSwitch = data.SwitchInstance;
            return;
        }

        // 通过车头坐标传送到不同地图：自动切换到 1 号副本区。
        // DR 反馈「不存在可切换的副本区」= 本图不分线，插件会记住并跳过后续切区。
        if (P.Config.AutoInstance && data.FromTerritory != 0
            && data.FromTerritory != Svc.ClientState.TerritoryType
            && !InstanceController.IsKnownNonInstanced(Svc.ClientState.TerritoryType))
        {
            PluginLog.Information($"[AutoHunt] 跨图到达（{data.FromTerritory} → {Svc.ClientState.TerritoryType}），落地后自动切换到 1 号副本区");
            PendingArrivalSwitch = 1;
            return;
        }

        // 通知 HuntController 传送到达
        HuntController.OnArrived();
    }

    private void OnChatCommand(string command, string arguments)
    {
        var args = (arguments ?? "").Trim();
        var lower = args.ToLower();
        if (lower == "stop")
        {
            TaskManager.Abort();
            HuntController.Reset();
            TeleportTo = null;
            SwitchInProgress = false;
            HeldCoordinate = null;
            PendingArrivalSwitch = 0;
            CrossRegionController.Reset();
            EndMapWatcher.Reset();
            ConductorFetchService.Cancel();
            try { S.VnavmeshIPC?.StopPath(); } catch { }
            Notify.Info("已停止所有自动行为。");
        }
        else if (lower == "clear")
        {
            Conductor.ClearAll();
        }
        else if (lower.StartsWith("set "))
        {
            var name = args[4..].Trim();
            if (name.IsNullOrEmpty())
            {
                Notify.Error("用法: /ah set <玩家名>");
            }
            else
            {
                // 无需玩家在附近：找不到对象时同样按名字生效
                ContextMenuManager.SetConductorByName(name);
            }
        }
        else if (lower == "reset")
        {
            InstanceController.Reset();
            SwitchInProgress = false;
            HeldCoordinate = null;
            Notify.Info("已重置副本区记录。");
        }
        else
        {
            EzConfigGui.Open();
        }
    }

    public string Name => "AutoHunt";

    public void Dispose()
    {
        Svc.Chat.ChatMessage -= ChatMessageHandler.Chat_ChatMessage;
        Svc.Framework.Update -= Framework_Update;
        Svc.ClientState.TerritoryChanged -= ClientState_TerritoryChanged;
        ConductorFetchService.Dispose();
        S.Shutdown();
        ECommonsMain.Dispose();
    }
}
