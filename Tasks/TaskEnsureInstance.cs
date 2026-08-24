using ECommons.Automation.NeoTaskManager;
using Dalamud.Game.ClientState.Objects.Enums;
using ECommons.GameHelpers;
namespace AutoHunt.Tasks;

/// <summary>
/// 切换副本区任务链（参考 HTA TaskChangeInstanceAfterTeleport）：
/// 若无法直接切换（不在水晶旁），先锁定最近水晶并自动走向它，再执行切换。
/// </summary>
public static class TaskEnsureInstance
{
    /// <summary>
    /// 入队：切换到指定副本区。
    /// </summary>
    public static void Enqueue(int num)
    {
        P.TaskManager.Enqueue(() => Player.Interactable && IsScreenReady(), "等待加载完成");
        P.TaskManager.Enqueue(() =>
        {
            var count = S.LifestreamIPC.GetInstanceCount();
            if (count == 0 || num == 0 || S.LifestreamIPC.GetCurrentInstanceNumber() == num)
            {
                return true;
            }

            P.TaskManager.InsertStack(() =>
            {
                P.TaskManager.Enqueue(() => IsScreenReady() && Player.Interactable);
                P.TaskManager.Enqueue(() =>
                {
                    if (!S.LifestreamIPC.GetCanChangeInstance())
                    {
                        // 不在水晶旁：走向最近水晶
                        var nearestAetheryte = Svc.Objects
                            .Where(x => x.ObjectKind == ObjectKind.Aetheryte && x.IsTargetable)
                            .OrderBy(x => Vector3.Distance(Player.Position, x.Position))
                            .FirstOrDefault();
                        if (nearestAetheryte != null)
                        {
                            if (nearestAetheryte.IsTarget() && EzThrottler.Throttle("WYLockon"))
                            {
                                Chat.ExecuteCommand("/lockon");
                                P.TaskManager.Insert(() => Chat.ExecuteCommand("/automove on"));
                                return true;
                            }
                            if (EzThrottler.Throttle("WYSetTarget"))
                            {
                                Svc.Targets.Target = nearestAetheryte;
                            }
                            return false;
                        }
                        return null;
                    }
                    return true;
                });
                P.TaskManager.Enqueue(() =>
                {
                    if (S.LifestreamIPC.GetCurrentInstanceNumber() == num) return true;
                    if (S.LifestreamIPC.GetCanChangeInstance())
                    {
                        Chat.ExecuteCommand("/automove off");
                        S.LifestreamIPC.TryChangeInstance(num);
                        return true;
                    }
                    return false;
                }, new TaskManagerConfiguration(timeLimitMS: 15000));
            });
            return true;
        });
    }
}
