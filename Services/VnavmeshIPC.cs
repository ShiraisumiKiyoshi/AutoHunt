using Action = System.Action;
using ECommons.EzIpcManager;

namespace AutoHunt.Services;

/// <summary>
/// vnavmesh 插件 IPC（IPC 名：vnavmesh.*）。
/// 注意：EzIPC 订阅字段在提供方（vnavmesh）未加载或未就绪时为 null，
/// 必须通过下方 *Safe 包装方法调用，不可直接调用字段。
/// </summary>
public class VnavmeshIPC
{
    [EzIPC("Nav.IsReady")]
    public Func<bool> IsReady;

    [EzIPC("SimpleMove.PathfindAndMoveTo")]
    public Func<Vector3, bool, bool> PathfindAndMoveTo;

    [EzIPC("SimpleMove.PathfindInProgress")]
    public Func<bool> PathfindInProgress;

    [EzIPC("Path.IsRunning")]
    public Func<bool> PathIsRunning;

    [EzIPC("Path.Stop", actionLastGenericType: typeof(object))]
    public Action PathStop;

    public VnavmeshIPC()
    {
        try
        {
            EzIPC.Init(this, "vnavmesh", SafeWrapper.AnyException);
        }
        catch (Exception e)
        {
            PluginLog.Error($"[AutoHunt] vnavmesh IPC 初始化失败: {e.Message}");
        }
    }

    /// <summary>vnavmesh 导航是否就绪（不可用返回 false）。</summary>
    public bool GetIsReady()
    {
        try { return IsReady?.Invoke() ?? false; }
        catch { return false; }
    }

    /// <summary>寻路并移动到目标点（不可用返回 false）。</summary>
    public bool TryPathfindAndMoveTo(Vector3 pos, bool fly)
    {
        try { return PathfindAndMoveTo?.Invoke(pos, fly) ?? false; }
        catch { return false; }
    }

    /// <summary>是否正在寻路计算中（不可用返回 false）。</summary>
    public bool GetPathfindInProgress()
    {
        try { return PathfindInProgress?.Invoke() ?? false; }
        catch { return false; }
    }

    /// <summary>是否有路径正在执行（不可用返回 false）。</summary>
    public bool GetPathIsRunning()
    {
        try { return PathIsRunning?.Invoke() ?? false; }
        catch { return false; }
    }

    /// <summary>停止当前路径移动（不可用则无操作）。</summary>
    public void StopPath()
    {
        try { PathStop?.Invoke(); }
        catch { }
    }
}
