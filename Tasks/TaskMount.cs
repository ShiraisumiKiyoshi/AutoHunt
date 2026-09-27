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

        // 战斗/咏唱中无法上坐骑：返回 false 继续等待。
        // 若不先挡掉，下面 GetActionStatus 的检查在战斗中同样返回非 0，
        // 会被误判为「区域禁止骑乘」而直接放弃——击杀狩猎怪后脱战标记会持续数秒，
        // 正是「未上坐骑就进入寻路、原地发呆」的元凶。
        if (Svc.Condition[ConditionFlag.InCombat] || Svc.Condition[ConditionFlag.Casting])
        {
            return false;
        }

        // 传送/切副本区落地后的动画锁定、加载过渡等瞬态下同样无法上马，但并非区域禁骑：
        // 必须视为「等待」返回 false，否则 GetActionStatus 在这些瞬态下返回非 0，
        // 会被误判为禁骑而永久放弃上马（表现为切区后不上坐骑、寻路原地发呆）。
        if (!Player.Interactable || Player.IsAnimationLocked
            || Svc.Condition[ConditionFlag.BetweenAreas] || Svc.Condition[ConditionFlag.BetweenAreas51]
            || Svc.Condition[ConditionFlag.MountOrOrnamentTransition]
            || (S.LifestreamIPC?.GetIsBusy() ?? false))
        {
            return false;
        }

        if (!EzThrottler.Throttle("WYCheckMount", 200)) return false;

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
