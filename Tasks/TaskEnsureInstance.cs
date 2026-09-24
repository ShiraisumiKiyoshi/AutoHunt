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
            // 不可切副本区的地图（原生判定）或已在目标副本区 → 跳过
            if (num == 0 || !InstanceController.IsInstancedAreaNow()
                || S.LifestreamIPC.GetCurrentInstanceNumber() == num)
            {
                return true;
            }

            P.TaskManager.InsertStack(() =>
            {
                P.TaskManager.Enqueue(() => IsScreenReady() && Player.Interactable);
                P.TaskManager.Enqueue(() =>
                {
                    // 不在水晶旁且尚不可切区 → 先走向最近水晶
                    if (!S.LifestreamIPC.GetCanChangeInstance() && !NearInstanceAetheryte())
                    {
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
                    // 就近水晶判定（与 Lifestream 内部交互条件一致，11 码）：
                    // 不依赖 GetCanChangeInstance——它受 Lifestream「显示副本区切换器」设置影响
                    if (!NearInstanceAetheryte()) return false;
                    Chat.ExecuteCommand("/automove off");
                    S.LifestreamIPC.TryChangeInstance(num);
                    return true;
                }, new TaskManagerConfiguration(timeLimitMS: 30000));
            });
            return true;
        });
    }

    /// <summary>是否站在可交互的副本区水晶旁（与 Lifestream GetAetheryte 的 11 码判定一致）。</summary>
    private static bool NearInstanceAetheryte() =>
        Svc.Objects.Any(x => x.ObjectKind == ObjectKind.Aetheryte && x.IsTargetable
            && Vector3.Distance(Player.Position, x.Position) < 11f);
}
