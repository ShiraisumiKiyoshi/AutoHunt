namespace AutoHunt;

/// <summary>
/// 车头玩家管理：查找、焦点保持。
/// </summary>
internal static class Conductor
{
    public static bool IsValid => !string.IsNullOrEmpty(P.Config.ConductorName);

    public static IPlayerCharacter? Find()
    {
        if (!IsValid) return null;
        foreach (var obj in Svc.Objects)
        {
            if (obj is IPlayerCharacter pc && pc.Name.TextValue == P.Config.ConductorName)
                return pc;
        }
        return null;
    }

    public static void Clear()
    {
        P.Config.ConductorName = "";
        P.Config.ConductorWorldId = 0;
        EzConfig.Save();
        Svc.Targets.FocusTarget = null;
        Notify.Info("已取消车头设置。");
    }

    /// <summary>
    /// 每 500ms 检查焦点目标，丢失或被改时自动恢复车头焦点。
    /// 注意：只恢复焦点目标（FocusTarget），绝不改动当前选中目标（Target）——
    /// 车头玩家不是 IBattleNpc，一旦覆盖 Target 会把狩猎流程选中的怪顶掉，
    /// 造成"明明已选中目标却一直寻找中/目标丢失直至超时"。
    /// 狩猎车人多时车头玩家频繁进出对象表，焦点会反复掉线，此劫持会持续发生。
    /// </summary>
    public static void EnsureFocus()
    {
        if (!IsValid) return;
        if (!EzThrottler.Throttle("WYFocus", 500)) return;

        var ft = Svc.Targets.FocusTarget;
        var expected = Find();
        if (expected == null) return;

        if (ft == null || ft.GameObjectId != expected.GameObjectId)
        {
            // 只恢复焦点，不动当前选中目标
            Svc.Targets.FocusTarget = expected;
        }
    }
}
