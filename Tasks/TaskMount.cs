using ECommons.GameHelpers;
namespace AutoHunt.Tasks;

public static unsafe class TaskMount
{
    /// <summary>
    /// 入队：等待就绪后上坐骑。
    /// </summary>
    public static void EnqueueIfEnabled()
    {
        if (!P.Config.UseMount) return;
        P.TaskManager.Enqueue(() => !S.LifestreamIPC.GetIsBusy() && IsScreenReady() && Player.Interactable, "等待玩家就绪");
        P.TaskManager.Enqueue(MountIfCan);
    }

    /// <summary>
    /// 上坐骑（任务步骤）。返回 true 表示完成。
    /// </summary>
    public static bool MountIfCan()
    {
        if (Svc.Condition[ConditionFlag.Mounted])
        {
            return true;
        }

        if (Svc.Condition[ConditionFlag.MountOrOrnamentTransition] || Svc.Condition[ConditionFlag.Casting])
        {
            EzThrottler.Throttle("WYCheckMount", 2000, true);
        }
        if (!EzThrottler.Check("WYCheckMount")) return false;

        // 无法使用坐骑动作（如区域内禁止骑乘）→ 放弃
        if (FFXIVClientStructs.FFXIV.Client.Game.ActionManager.Instance()
            ->GetActionStatus(FFXIVClientStructs.FFXIV.Client.Game.ActionType.GeneralAction, 9) != 0)
        {
            return true;
        }

        if (!Player.IsAnimationLocked && EzThrottler.Throttle("WYSummonMount"))
        {
            if (P.Config.MountName.IsNullOrEmpty())
            {
                Chat.ExecuteGeneralAction(9);
            }
            else
            {
                Chat.ExecuteCommand($"/mount \"{P.Config.MountName}\"");
            }
        }
        return false;
    }
}
