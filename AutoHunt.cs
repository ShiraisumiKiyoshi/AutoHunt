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

    public AutoHunt(IDalamudPluginInterface pi)
    {
        P = this;
        ECommonsMain.Init(pi, this);
        // 服务必须最先初始化，且构造过程不可能抛异常——事件订阅放在最后，
        // 保证一旦 Framework.Update 已订阅，所有服务字段必然非空。
        S.Initialize();
        EzConfig.Migrate<Config>();
        Config = EzConfig.Init<Config>();
        EzConfigGui.Init(new PluginUI.MainWindow());
        EzConfigGui.Window.RespectCloseHotkey = false;
        EzCmd.Add("/ah", OnChatCommand, "AutoHunt 自动狩猎助手\n/ah: 打开设置界面\n/ah stop: 停止所有自动行为\n/ah clear: 清除车头\n/ah set <玩家名>: 手动设置车头\n/ah reset: 重置副本区记录");
        TaskManager = new(new TaskManagerConfiguration(timeLimitMS: 60000));
        Svc.Chat.ChatMessage += ChatMessageHandler.Chat_ChatMessage;
        Svc.Framework.Update += Framework_Update;
        Svc.ClientState.TerritoryChanged += ClientState_TerritoryChanged;
    }

    private void ClientState_TerritoryChanged(uint territory)
    {
        InstanceController.OnTerritoryChanged(territory);
        if (TeleportTo != null && (territory == 0 || territory == TeleportTo.Territory))
        {
            OnArrival();
        }
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

            // 副本区切换完成检测：任务链结束（≥2 秒避免启动帧误判）→ 继续暂存的车头坐标
            if (SwitchInProgress && (DateTime.Now - SwitchStartTime).TotalSeconds > 2 && !TaskManager.IsBusy)
            {
                SwitchInProgress = false;
                var held = HeldCoordinate;
                HeldCoordinate = null;
                if (held != null)
                {
                    Notify.Info("副本区切换完成，继续前往车头坐标…");
                    HuntController.OnNewCoordinate(held);
                }
            }

            if (!Player.Available) return;
            // IPC 服务未就绪（初始化失败/热重载竞态）时跳过本轮，避免 NullReferenceException
            if (S.LifestreamIPC == null || S.TeleporterIPC == null || S.VnavmeshIPC == null) return;

            // 记录移动状态（用于传送门控）
            IsMoving = Player.Position != LastPosition;
            LastPosition = Player.Position;

            InstanceController.Update();
            Conductor.EnsureFocus();
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

    private static void NativeTeleport(uint aetheryteId)
    {
        var instance = FFXIVClientStructs.FFXIV.Client.Game.UI.Telepo.Instance();
        if (instance != null)
        {
            instance->Teleport(aetheryteId, 0);
        }
    }

    /// <summary>
    /// 到达传送目的地：中断任务链，按需切换副本区，触发狩猎流程。
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
            if (S.LifestreamIPC.GetInstanceCount() > 1)
            {
                Notify.Info($"到达目的地，切换到 {data.SwitchInstance} 号副本区…");
                SwitchInProgress = true;
                SwitchStartTime = DateTime.Now;
                HeldCoordinate = null;
                TaskEnsureInstance.Enqueue(data.SwitchInstance);
            }
            else
            {
                Notify.Error($"地图副本区数据不可用（数量 ≤ 1），跳过切区，直接前往坐标。");
            }
            // 切区完成后，HuntController 继续等待/前往车头坐标
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
            Notify.Info("已停止所有自动行为。");
        }
        else if (lower == "clear")
        {
            Conductor.Clear();
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
        S.Shutdown();
        ECommonsMain.Dispose();
    }
}
