using ECommons.GameHelpers;
using ECommons.GameHelpers;
using ECommons.Automation.NeoTaskManager;
namespace AutoHunt;

/// <summary>
/// 独立输出控制器：
/// 1. 已选中狩猎怪后监测血量
/// 2. 血量低于阈值 → 下坐骑 → /rotation Manual
/// 3. 怪物死亡 → /rotation off
/// 
/// 注：选怪逻辑已整合到 HuntController，本类保留作为 HuntController 的辅助。
/// </summary>
internal static class AttackController
{
    /// <summary>获取当前目标的血量百分比。</summary>
    public static float GetTargetHpPercent()
    {
        var target = Svc.Targets.Target as IBattleNpc;
        if (target == null || target.IsDead) return 0f;
        return target != null ? (target.CurrentHp / (float)target.MaxHp * 100f) : 0f;
    }

    /// <summary>执行输出命令。</summary>
    public static void StartRotation()
    {
        if (!P.Config.AutoAttack) return;
        Chat.ExecuteCommand(P.Config.RotationStartCommand);
        if (P.Config.Debug) PluginLog.Debug("执行输出命令: " + P.Config.RotationStartCommand);
    }

    /// <summary>停止输出命令。</summary>
    public static void StopRotation()
    {
        Chat.ExecuteCommand(P.Config.RotationStopCommand);
        if (P.Config.Debug) PluginLog.Debug("执行停止输出命令: " + P.Config.RotationStopCommand);
    }
}
