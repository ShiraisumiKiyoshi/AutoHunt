using ECommons.EzIpcManager;

namespace AutoHunt.Services;

/// <summary>
/// Teleporter 插件 IPC（IPC 名：Teleport.Teleport）。
/// 注意：EzIPC 订阅字段在提供方（Teleporter）未加载或未就绪时为 null，
/// 必须通过 TryTeleport 包装方法调用，不可直接调用字段。
/// </summary>
public class TeleporterIPC
{
    [EzIPC]
    public Func<uint, byte, bool> Teleport;

    public TeleporterIPC()
    {
        try
        {
            EzIPC.Init(this, "Teleport", SafeWrapper.AnyException);
        }
        catch (Exception e)
        {
            PluginLog.Error($"[AutoHunt] Teleporter IPC 初始化失败: {e.Message}");
        }
    }

    /// <summary>通过 Teleporter 插件传送（不可用返回 false）。</summary>
    public bool TryTeleport(uint aetheryteId, byte subIndex)
    {
        try { return Teleport?.Invoke(aetheryteId, subIndex) ?? false; }
        catch { return false; }
    }
}
