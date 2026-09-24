using System.Numerics;
using Newtonsoft.Json;
using LuminaMap = Lumina.Excel.Sheets.Map;

namespace AutoHunt;

/// <summary>
/// 狩猎怪出生点数据库（构建期生成，内嵌资源 Data/HuntSpawns.json）：
///   数据来源：社区整理的精英怪出生点（英文名 + 地图显示坐标 + 地图文件名），
///   构建时通过 xivapi 将英文名解析为 BNpcName RowId（即游戏内 NameId）。
/// 运行时：把「地图文件名（Map.Id，如 n4f300）+ 显示坐标」通过游戏 Map 表
/// 转换为 TerritoryType + 世界坐标，供车头坐标命中匹配。
/// 数据覆盖 A/S/SS/SS+ 及 B 级约 200 只怪的多个出生点（同一只怪可有多处）。
/// 注意：同名怪在不同区域是不同 BNpcName，但出生点按地图归属，
/// 运行时按当前区域过滤，同名歧义自然消除。
/// </summary>
internal static class HuntSpawnDatabase
{
    private class RawMob
    {
        [JsonProperty("r")] public string Rank = "";
        /// <summary>地图文件名（Map.Id）→ 出生点显示坐标列表</summary>
        [JsonProperty("m")] public Dictionary<string, float[][]> Maps = new();
    }

    /// <summary>某区域内的单只怪出生点。</summary>
    public class MobSpawn
    {
        public uint NameId;
        public string Rank = "";
        public List<Vector2> WorldSpawns = new();
    }

    private static Dictionary<uint, List<MobSpawn>>? worldDb;
    private static int rawMobCount;
    private static int rawSpawnCount;
    private static bool initFailed;

    /// <summary>已加载的怪数量（含未解析到出生点区域的）。</summary>
    public static int MobCount { get { EnsureReady(); return worldDb?.Values.SelectMany(x => x).Select(x => x.NameId).Distinct().Count() ?? 0; } }

    /// <summary>已加载的出生点总数（世界坐标已解析的）。</summary>
    public static int SpawnPointCount { get { EnsureReady(); return worldDb?.Values.Sum(l => l.Sum(m => m.WorldSpawns.Count)) ?? 0; } }

    private static void EnsureReady()
    {
        if (worldDb != null || initFailed) return;
        try
        {
            var asm = typeof(HuntSpawnDatabase).Assembly;
            using var stream = asm.GetManifestResourceStream("AutoHunt.Data.HuntSpawns.json");
            if (stream == null)
            {
                PluginLog.Error("[AutoHunt] 内嵌资源 HuntSpawns.json 不存在，出生点辅助不可用");
                initFailed = true;
                return;
            }
            using var reader = new StreamReader(stream);
            var raw = JsonConvert.DeserializeObject<Dictionary<string, RawMob>>(reader.ReadToEnd());
            if (raw == null || raw.Count == 0)
            {
                PluginLog.Error("[AutoHunt] HuntSpawns.json 解析结果为空");
                initFailed = true;
                return;
            }
            rawMobCount = raw.Count;
            rawSpawnCount = raw.Values.Sum(m => m.Maps.Values.Sum(pts => pts.Length));

            // 建立 Map.Id → (TerritoryType, Map) 索引
            var mapIndex = new Dictionary<string, (uint territory, LuminaMap map)>();
            foreach (var map in Svc.Data.GetExcelSheet<LuminaMap>())
            {
                var id = map.Id.ToString();
                if (string.IsNullOrEmpty(id) || map.TerritoryType.RowId == 0) continue;
                mapIndex[id] = (map.TerritoryType.RowId, map);
            }

            var db = new Dictionary<uint, List<MobSpawn>>();
            foreach (var (idStr, mob) in raw)
            {
                if (!uint.TryParse(idStr, out var nameId) || nameId == 0) continue;
                foreach (var (mapId, pts) in mob.Maps)
                {
                    if (!mapIndex.TryGetValue(mapId, out var mi)) continue; // 未知地图（如未开放区域）跳过
                    if (!db.TryGetValue(mi.territory, out var list))
                    {
                        list = new List<MobSpawn>();
                        db[mi.territory] = list;
                    }
                    var entry = list.FirstOrDefault(x => x.NameId == nameId);
                    if (entry == null)
                    {
                        entry = new MobSpawn { NameId = nameId, Rank = mob.Rank };
                        list.Add(entry);
                    }
                    foreach (var pt in pts)
                    {
                        if (pt == null || pt.Length < 2) continue;
                        entry.WorldSpawns.Add(MapManager.DisplayToWorld(pt[0], pt[1], mi.map));
                    }
                }
            }

            worldDb = db;
            PluginLog.Information($"[AutoHunt] 狩猎怪出生点数据库已加载: {db.Values.SelectMany(x => x).Select(x => x.NameId).Distinct().Count()} 只 / {db.Values.Sum(l => l.Sum(m => m.WorldSpawns.Count))} 个出生点 (原始 {rawMobCount} 只 / {rawSpawnCount} 点)");
        }
        catch (Exception e)
        {
            PluginLog.Error($"[AutoHunt] 加载狩猎怪出生点数据库失败: {e.Message}");
            initFailed = true;
        }
    }

    /// <summary>
    /// 在指定区域内寻找距离 worldXZ 最近的出生点。
    /// </summary>
    /// <returns>命中返回 true；nameId 为该出生点对应的 BNpcName RowId，rank 为等级标签（A/S/SS/SS+/B），spawnWorld 为出生点世界坐标。</returns>
    public static bool TryMatchSpawn(uint territory, Vector2 worldXZ, float radius, out uint nameId, out string rank, out Vector2 spawnWorld)
    {
        nameId = 0;
        rank = "";
        spawnWorld = Vector2.Zero;
        EnsureReady();
        if (worldDb == null || !worldDb.TryGetValue(territory, out var list)) return false;

        float best = float.MaxValue;
        foreach (var mob in list)
        {
            foreach (var sp in mob.WorldSpawns)
            {
                var d = Vector2.Distance(sp, worldXZ);
                if (d < best)
                {
                    best = d;
                    nameId = mob.NameId;
                    rank = mob.Rank;
                    spawnWorld = sp;
                }
            }
        }
        return best <= radius;
    }
}
