namespace AutoHunt;

/// <summary>
/// 调试日志统一出口。
/// ⚠️ 不能用 <c>PluginLog.Debug</c> 输出调试信息：Dalamud 自身的日志级别
/// （dalamudConfig.json 的 LogLevel，默认 2 = Information）会把 Debug 级日志整条丢弃
/// ——dalamud.log 里连一条 [DBG] 都没有，导致玩家开了插件「调试模式」也查不到任何线索。
/// 因此这里改为在开启调试模式时用 Information 级输出，并统一带 [AutoHunt/DBG] 前缀，
/// 便于在日志里直接 grepping 过滤。
/// </summary>
internal static class Dbg
{
    public const string Tag = "[AutoHunt/DBG] ";

    /// <summary>调试模式开启时输出（Information 级，能真正写进 dalamud.log）。</summary>
    public static void Log(string message)
    {
        if (P.Config.Debug) PluginLog.Information(Tag + message);
    }

    /// <summary>调试模式开启时的警告（Warning 级，便于一眼找到）。</summary>
    public static void Warn(string message)
    {
        if (P.Config.Debug) PluginLog.Warning(Tag + message);
    }
}
