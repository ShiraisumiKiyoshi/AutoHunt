namespace AutoHunt;

/// <summary>
/// 结束地图监控器：当前地图为结束地图且本区击杀数已满时，自动触发跨区流程。
/// 触发与「自动取消车头」开关无关——该开关仅决定跨区流程内解散小队前是否先取消全部车头
/// （CancelConductors 阶段），开关关闭时流程照常执行：解散小队 → 传送城市 → 跨区 → 传送水晶。
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
        if (!P.Config.Enabled || !P.Config.CrossRegionEnable) return;
        var endId = P.Config.CrossRegionEndAetheryteId;
        if (endId == 0) return; // 未选择结束地图：无触发地图，不生效
        if (CrossRegionController.Active || ConductorFetchService.Running || P.TaskManager.IsBusy) return;
        if (!InstanceController.ZoneCleared) return;

        var endTerritory = GetTerritory(endId);
        if (endTerritory == 0) return;
        if (Svc.ClientState.TerritoryType != endTerritory) return;

        handled = true;
        Notify.Info($"本区击杀已满（{InstanceController.KillCount}/{P.Config.KillsPerInstance}），自动触发跨区流程。");
        CrossRegionController.Begin();
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
