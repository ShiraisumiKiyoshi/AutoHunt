using LuminaMap = Lumina.Excel.Sheets.Map;

namespace AutoHunt;

/// <summary>
/// 地图/水晶相关换算与查询。
/// 坐标公式与 Dalamud MapLinkPayload 保持一致：
///   display = 41/c * ((W + offset) * c + 1024) / 2048 + 1，其中 c = SizeFactor / 100
///   maplink RawX / 1000 = 世界坐标 W
///   MapMarker 网格坐标 → 世界：W = (marker - 1024) / c - offset
/// </summary>
internal static class MapManager
{
    /// <summary>获取指定地图的 Map 数据。</summary>
    public static LuminaMap? GetMapForTerritory(uint territoryId)
    {
        if (Svc.Data.GetExcelSheet<LuminaMap>().TryGetFirst(x => x.TerritoryType.RowId == territoryId, out var map))
        {
            return map;
        }
        return null;
    }

    /// <summary>获取当前地图的 Map 数据。</summary>
    public static LuminaMap? GetCurrentMap()
    {
        return GetMapForTerritory(Svc.ClientState.TerritoryType);
    }

    /// <summary>
    /// 获取指定地图上距离目标世界坐标最近的水晶。
    /// </summary>
    public static Aetheryte? GetNearestAetheryte(uint territoryId, Vector2 targetWorld)
    {
        Aetheryte? best = null;
        var bestDist = float.MaxValue;
        foreach (var a in Svc.Data.GetExcelSheet<Aetheryte>())
        {
            if (!a.IsAetheryte) continue;
            if (a.Territory.RowId != territoryId) continue;
            var w = GetAetheryteWorldPosition(a);
            if (w == null) continue;
            var d = Vector2.Distance(w.Value, targetWorld);
            if (d < bestDist)
            {
                bestDist = d;
                best = a;
            }
        }
        return best;
    }

    /// <summary>
    /// 计算水晶的世界坐标（通过 MapMarker 数据）。
    /// </summary>
    public static Vector2? GetAetheryteWorldPosition(Aetheryte aetheryte)
    {
        var map = GetMapForTerritory(aetheryte.Territory.RowId);
        if (map == null) return null;
        var c = map.Value.SizeFactor / 100f;
        foreach (var m in Svc.Data.GetSubrowExcelSheet<MapMarker>().Flatten())
        {
            if (m.DataType == 3 && m.DataKey.RowId == aetheryte.RowId)
            {
                return new Vector2(
                    (m.X - 1024) / c - map.Value.OffsetX,
                    (m.Y - 1024) / c - map.Value.OffsetY);
            }
        }
        if (P.Config.Debug) PluginLog.Warning($"未找到水晶 {aetheryte.PlaceName.ValueNullable?.Name} 的地图标记");
        return null;
    }

    /// <summary>
    /// 地图显示坐标（如 12.3, 45.6）转换为世界坐标。
    /// </summary>
    public static Vector2 DisplayToWorld(float x, float y, LuminaMap map)
    {
        var c = map.SizeFactor / 100f;
        var wx = ((x - 1f) * c / 41f * 2048f - 1024f) / c - map.OffsetX;
        var wy = ((y - 1f) * c / 41f * 2048f - 1024f) / c - map.OffsetY;
        return new Vector2(wx, wy);
    }

    /// <summary>
    /// 世界坐标转换为地图显示坐标（与 MapLinkPayload 公式一致）。
    /// </summary>
    public static Vector2 WorldToDisplay(float wx, float wy, LuminaMap map)
    {
        var c = map.SizeFactor / 100f;
        var x = 41f / c * ((wx + map.OffsetX) * c + 1024f) / 2048f + 1f;
        var y = 41f / c * ((wy + map.OffsetY) * c + 1024f) / 2048f + 1f;
        return new Vector2(x, y);
    }

    /// <summary>
    /// 打开地图并在指定世界坐标处插旗（与点击聊天地图链接效果一致）。
    /// </summary>
    public static void PlaceFlag(uint territoryId, Vector2 worldXZ)
    {
        try
        {
            if (Svc.ClientState.TerritoryType != territoryId)
            {
                // 不在目标地图时不插旗（旗标属于目标地图的 AgentMap，等传送到达后再插）
                if (P.Config.Debug) PluginLog.Debug($"[AutoHunt] 当前不在目标地图 {territoryId}，暂不插旗");
                return;
            }
            var map = GetMapForTerritory(territoryId);
            if (map == null) return;
            var display = WorldToDisplay(worldXZ.X, worldXZ.Y, map.Value);
            var link = new MapLinkPayload(territoryId, map.Value.RowId, display.X, display.Y, 0f);
            Svc.GameGui.OpenMapWithMapLink(link);
            if (P.Config.Debug) PluginLog.Debug($"[AutoHunt] 已插旗 ({display.X:0.0}, {display.Y:0.0}) @territory {territoryId}");
        }
        catch (Exception e)
        {
            PluginLog.Warning($"[AutoHunt] 插旗失败: {e.Message}");
        }
    }
}
