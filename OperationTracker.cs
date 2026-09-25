namespace AutoHunt;

/// <summary>
/// 当前操作汇聚器：从各控制器实时状态中按优先级裁决出插件此刻正在执行的操作，
/// 供主窗口顶部操作条与悬浮窗显示。全部为廉价属性读取，无轮询开销。
/// </summary>
internal static class OperationTracker
{
    public enum Kind { Idle, CrossRegion, InstanceSwitch, FetchConductor, CreatePF, Hunt, Move }

    /// <summary>当前操作（类别 + 文本）。按优先级取最高优先级的活动操作。</summary>
    public static (Kind Kind, string Text) Current
    {
        get
        {
            try
            {
                // 总开关关闭：明确显示已关闭（此时主循环闸门已复位一切状态）
                if (!P.Config.Enabled)
                    return (Kind.Idle, "插件已关闭");

                // 1. 跨区流程（独占控制权，优先级最高）
                if (CrossRegionController.Active)
                    return (Kind.CrossRegion, $"跨区 — {CrossRegionController.CurrentState}");

                // 2. 副本区切换（切区任务执行中 / 待切换传送中）
                if (P.SwitchInProgress)
                    return (Kind.InstanceSwitch, "副本区切换 — 执行中");
                if (P.TeleportTo != null && P.TeleportTo.SwitchInstance > 0)
                    return (Kind.InstanceSwitch, $"副本区切换 — 传送中（目标 {P.TeleportTo.SwitchInstance} 号区）");
                if (InstanceController.PendingSwitchInstance != 0)
                    return (Kind.InstanceSwitch, $"副本区切换 — 等待车头新坐标（目标 {InstanceController.PendingSwitchInstance} 号区）");

                // 3. 自动获取车头
                if (ConductorFetchService.Running)
                    return (Kind.FetchConductor, $"获取车头 — {ConductorFetchService.CurrentState}");

                // 4. 创建招募等 TaskManager 任务链
                if (P.TaskManager.IsBusy)
                    return (Kind.CreatePF, "创建队员招募…");

                // 5. 狩猎流程
                var hunt = DescribeHunt();
                if (hunt != null)
                    return (Kind.Hunt, hunt);

                // 6. 传送 / 寻路移动
                if (P.TeleportTo != null)
                {
                    var name = P.TeleportTo.Aetheryte?.PlaceName.ValueNullable?.Name.ToString() ?? "水晶";
                    return (Kind.Move, $"传送中 — 前往 {name}");
                }
                if (S.VnavmeshIPC != null && S.VnavmeshIPC.GetPathIsRunning())
                    return (Kind.Move, "移动中 — 寻路前往坐标");

                // 7. 空闲
                return (Kind.Idle, "空闲 — 等待车头坐标");
            }
            catch
            {
                return (Kind.Idle, "空闲 — 等待车头坐标");
            }
        }
    }

    /// <summary>狩猎流程进行中返回描述文本，空闲返回 null。</summary>
    private static string? DescribeHunt()
    {
        if (!P.Config.Enabled) return null;
        var s = HuntController.CurrentState;
        var target = HuntController.CurrentTargetName;
        var rank = HuntController.CurrentTargetRank;
        var t = string.IsNullOrEmpty(target) ? "" : string.IsNullOrEmpty(rank) ? target : $"[{rank}] {target}";

        return s switch
        {
            HuntController.State.Idle => null,
            HuntController.State.Teleporting => "狩猎 — 传送中",
            HuntController.State.Mounting => "狩猎 — 上坐骑",
            HuntController.State.Navigating => $"狩猎 — 寻路中{(t.Length > 0 ? $"（{t}）" : "")}",
            HuntController.State.Targeting => $"狩猎 — 寻找目标{(t.Length > 0 ? $"（{t}）" : "")}",
            HuntController.State.Arrived => "狩猎 — 已到达，监控怪物刷新",
            HuntController.State.Attacking => $"狩猎 — 锁定目标{(t.Length > 0 ? $"（{t}）" : "")}",
            HuntController.State.Descending => $"狩猎 — 下降中{(t.Length > 0 ? $"（{t}）" : "")}",
            HuntController.State.Dismounting => $"狩猎 — 下坐骑{(t.Length > 0 ? $"（{t}）" : "")}",
            HuntController.State.Outputting => $"狩猎 — 输出中{(t.Length > 0 ? $"（{t} {HuntController.CurrentTargetHpPercent:0}%）" : "")}",
            HuntController.State.Finished => null,
            _ => null,
        };
    }
}
