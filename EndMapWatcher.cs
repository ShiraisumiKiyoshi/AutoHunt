namespace AutoHunt;

/// <summary>
/// 结束地图监控器：「自动取消车头」开关开启时，若当前地图为结束地图且本区击杀数已满，
/// 自动取消全部车头；跨区功能开启时由 ClearAll 衔接跨区流程。
/// </summary>
internal static class EndMapWatcher
{
    private static bool handled;
    private static uint cachedEndTerritory;

    /// <summary>切图后重置（新的地图重新允许触发一次）。</summary>
    public static void OnTerritoryChanged()
    {
        handled = false;
        cachedEndTerritory = 0;
    }

    /// <summary>/ah stop 等场景手动重置。</summary>
    public static void Reset() => handled = false;

    public static void Update()
    {
        if (handled) return;
        if (!P.Config.Enabled || !P.Config.CrossRegionAutoCancelConductor) return;
        var endId = P.Config.CrossRegionEndAetheryteId;
        if (endId == 0) return; // 未选择结束地图：开关不生效
        if (!Conductor.IsValid) return;
        if (CrossRegionController.Active || ConductorFetchService.Running || P.TaskManager.IsBusy) return;
        if (!InstanceController.ZoneCleared) return;

        var endTerritory = GetTerritory(endId);
        if (endTerritory == 0) return;
        if (Svc.ClientState.TerritoryType != endTerritory) return;

        handled = true;
        Notify.Info($"本区击杀已满（{InstanceController.KillCount}/{P.Config.KillsPerInstance}），自动取消全部车头。");
        Conductor.ClearAll();
    }

    /// <summary>查询水晶所在的地图（TerritoryType RowId），带缓存。</summary>
    private static uint GetTerritory(uint aetheryteId)
    {
        if (cachedEndTerritory != 0) return cachedEndTerritory;
        try
        {
            var row = Svc.Data.GetExcelSheet<Aetheryte>().GetRow(aetheryteId);
            cachedEndTerritory = row.Territory.RowId;
        }
        catch (Exception e)
        {
            PluginLog.Warning($"[AutoHunt] 读取结束地图水晶 {aetheryteId} 失败: {e.Message}");
        }
        return cachedEndTerritory;
    }
}
