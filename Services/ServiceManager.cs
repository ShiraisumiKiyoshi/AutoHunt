using Action = System.Action;

namespace AutoHunt.Services;

public static class ServiceManager
{
    public static TeleporterIPC TeleporterIPC;
    public static LifestreamIPC LifestreamIPC;
    public static VnavmeshIPC VnavmeshIPC;
    public static ContextMenuManager ContextMenuManager;

    /// <summary>所有 IPC 服务是否已就绪。</summary>
    public static bool Ready => LifestreamIPC != null && TeleporterIPC != null && VnavmeshIPC != null;

    public static void Initialize()
    {
        // 逐服务隔离：单个服务初始化失败不影响其他服务，也不会让插件加载失败
        TryInit("TeleporterIPC", () => TeleporterIPC = new TeleporterIPC());
        TryInit("LifestreamIPC", () => LifestreamIPC = new LifestreamIPC());
        TryInit("VnavmeshIPC", () => VnavmeshIPC = new VnavmeshIPC());
        TryInit("ContextMenuManager", () => ContextMenuManager = new ContextMenuManager());
    }

    private static void TryInit(string name, Action init)
    {
        try
        {
            init();
        }
        catch (Exception e)
        {
            PluginLog.Error($"[AutoHunt] 服务 {name} 初始化失败: {e.Message}");
        }
    }

    public static void Shutdown()
    {
        ContextMenuManager?.Dispose();
    }
}
