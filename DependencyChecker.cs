namespace AutoHunt;

/// <summary>
/// 依赖插件检测：检查 vnavmesh（必需）/ RotationSolver / Lifestream（可选）是否已安装，
/// 缺失时聊天提示，并在设置界面状态页展示。
/// 注：Teleporter 不检测——传送链为 Teleporter → Lifestream → 原生 Telepo，
/// 任意一级缺失都有完整兜底，无需提示用户安装。
/// </summary>
internal static class DependencyChecker
{
    public readonly record struct Dep(string InternalName, string Label, string Feature, bool Required);

    private static readonly Dep[] Dependencies =
    {
        new("vnavmesh", "vnavmesh", "寻路", true),
        new("RotationSolver", "RotationSolver", "自动输出", false),
        new("Lifestream", "Lifestream", "副本区切换/跨区传送", false),
    };

    /// <summary>插件是否已安装并加载（被禁用的插件不会加载，同样视为缺失）。检测失败时返回 true（不误报）。</summary>
    public static bool IsInstalled(string internalName)
    {
        try
        {
            return Svc.PluginInterface.InstalledPlugins.Any(p => p.InternalName == internalName && p.IsLoaded);
        }
        catch (Exception e)
        {
            PluginLog.Warning($"检测插件 {internalName} 是否安装失败: {e.Message}");
            return true;
        }
    }

    /// <summary>检查全部依赖并聊天提示缺失项。返回是否存在缺失（供延迟复核用）。</summary>
    public static bool CheckAndNotify()
    {
        var missingRequired = Dependencies.Where(d => d.Required && !IsInstalled(d.InternalName)).ToList();
        var missingOptional = Dependencies.Where(d => !d.Required && !IsInstalled(d.InternalName)).ToList();

        if (missingRequired.Count > 0)
        {
            Notify.Error("缺少必需依赖插件: " + string.Join("、", missingRequired.Select(d => $"{d.Label}（{d.Feature}）"))
                + "。请先安装，否则对应功能无法工作！");
        }

        if (missingOptional.Count > 0)
        {
            Notify.Info("未安装可选依赖: " + string.Join("、", missingOptional.Select(d => $"{d.Label}（{d.Feature}）"))
                + "，对应功能不可用。");
        }

        return missingRequired.Count > 0 || missingOptional.Count > 0;
    }

    /// <summary>状态页展示用的依赖状态列表。</summary>
    public static List<(string Label, string Feature, bool Installed, bool Required)> GetDependencyStatus()
    {
        return Dependencies
            .Select(d => (d.Label, d.Feature, IsInstalled(d.InternalName), d.Required))
            .ToList();
    }
}
