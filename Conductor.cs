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

    /// <summary>每帧检查焦点目标，丢失或被改时自动恢复车头焦点。</summary>
    public static void EnsureFocus()
    {
        if (!IsValid) return;
        if (!EzThrottler.Throttle("WYFocus", 500)) return;

        var ft = Svc.Targets.FocusTarget;
        var expected = Find();
        if (expected == null) return;

        if (ft == null || ft.GameObjectId != expected.GameObjectId)
        {
            Svc.Targets.Target = expected;
            Svc.Targets.FocusTarget = expected;
        }
    }
}
