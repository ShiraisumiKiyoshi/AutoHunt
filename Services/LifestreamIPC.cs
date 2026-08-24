using Action = System.Action;
using ECommons.EzIpcManager;

namespace AutoHunt.Services;

/// <summary>
/// Lifestream 插件 IPC（IPC 名：Lifestream.*）。
/// 注意：EzIPC 订阅字段在提供方（Lifestream）未加载或未就绪时为 null，
/// 必须通过下方 *Safe / Try* 包装方法调用，不可直接调用字段。
/// </summary>
public class LifestreamIPC
{
    [EzIPC]
    public Func<bool> CanChangeInstance;

    [EzIPC]
    public Func<int> GetNumberOfInstances;

    [EzIPC(actionLastGenericType: typeof(object))]
    public Action<int> ChangeInstance;

    [EzIPC]
    public Func<int> GetCurrentInstance;

    [EzIPC]
    public Func<bool> IsBusy;

    [EzIPC]
    public Func<uint, bool> Teleport;

    public LifestreamIPC()
    {
        try
        {
            EzIPC.Init(this, "Lifestream", SafeWrapper.AnyException);
        }
        catch (Exception e)
        {
            // 初始化失败不抛出：字段保持 null，包装方法会安全返回默认值
            PluginLog.Error($"[AutoHunt] Lifestream IPC 初始化失败: {e.Message}");
        }
    }

    /// <summary>当前地图可切换的副本区数量（Lifestream 不可用返回 0）。</summary>
    public int GetInstanceCount()
    {
        try { return GetNumberOfInstances?.Invoke() ?? 0; }
        catch { return 0; }
    }

    /// <summary>当前所在副本区号（不可用返回 0）。</summary>
    public int GetCurrentInstanceNumber()
    {
        try { return GetCurrentInstance?.Invoke() ?? 0; }
        catch { return 0; }
    }

    /// <summary>当前是否可以切换副本区（不可用返回 false）。</summary>
    public bool GetCanChangeInstance()
    {
        try { return CanChangeInstance?.Invoke() ?? false; }
        catch { return false; }
    }

    /// <summary>Lifestream 是否正忙（不可用视为不忙）。</summary>
    public bool GetIsBusy()
    {
        try { return IsBusy?.Invoke() ?? false; }
        catch { return false; }
    }

    /// <summary>通过 Lifestream 传送（不可用返回 false）。</summary>
    public bool TryTeleport(uint aetheryteId)
    {
        try { return Teleport?.Invoke(aetheryteId) ?? false; }
        catch { return false; }
    }

    /// <summary>切换副本区（不可用则无操作）。</summary>
    public void TryChangeInstance(int number)
    {
        try { ChangeInstance?.Invoke(number); }
        catch { }
    }
}
