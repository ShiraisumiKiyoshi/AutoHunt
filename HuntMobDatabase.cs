using Lumina.Excel.Sheets;

namespace AutoHunt;

/// <summary>
/// 狩猎怪数据库：从游戏数据表 NotoriousMonster 构建。
/// 该表原始 292 行，但同一只怪（同一 BNpcName/NameId）会在多个分区各有一条记录
/// （如"得到宽恕的流言"出现约 20 次），按 NameId 去重后实际为 207 只（B/A/S）。
/// 每行含：
///   BNpcName 引用（怪物名称行ID，即 GameObject.NameId）
///   Rank 字段：1 = B 级，2 = A 级，3 = S 级
/// 判定方式：npc.NameId 命中数据库即为狩猎怪（零误判，优于名称关键词匹配）。
/// </summary>
internal static class HuntMobDatabase
{
    /// <summary>NameId → Rank（1=B, 2=A, 3=S）</summary>
    private static Dictionary<uint, byte>? rankMap = null;
    private static readonly object lockObj = new();

    /// <summary>获取（懒加载）NameId→Rank 映射表。</summary>
    public static Dictionary<uint, byte> RankMap
    {
        get
        {
            if (rankMap == null)
            {
                lock (lockObj)
                {
                    rankMap ??= Build();
                }
            }
            return rankMap;
        }
    }

    private static Dictionary<uint, byte> Build()
    {
        var map = new Dictionary<uint, byte>();
        try
        {
            foreach (var row in Svc.Data.GetExcelSheet<NotoriousMonster>())
            {
                var nameId = row.BNpcName.RowId;
                if (nameId == 0) continue;
                // 同名 Id 取最高等级（理论不会重复）
                if (map.TryGetValue(nameId, out var existing) && existing >= row.Rank) continue;
                map[nameId] = row.Rank;
            }
            if (P.Config != null && P.Config.Debug)
                PluginLog.Debug($"[AutoHunt] 狩猎怪数据库已加载: {map.Count} 只 (B={CountByRank(1)}, A={CountByRank(2)}, S={CountByRank(3)})");
        }
        catch (Exception e)
        {
            PluginLog.Error($"[AutoHunt] 加载狩猎怪数据库失败: {e.Message}");
        }
        return map;
    }

    private static int CountByRank(byte rank)
    {
        var m = rankMap;
        if (m == null) return 0;
        int n = 0;
        foreach (var v in m.Values) if (v == rank) n++;
        return n;
    }

    /// <summary>是否为狩猎怪。includeB=false 时只认 A/S（狩猎车场景默认排除 B 级）。</summary>
    public static bool IsHuntMob(uint nameId, bool includeB = false)
    {
        if (nameId == 0) return false;
        if (!RankMap.TryGetValue(nameId, out var rank)) return false;
        return includeB || rank >= 2;
    }

    /// <summary>获取等级标签："B" / "A" / "S"，非狩猎怪返回空串。</summary>
    public static string GetRankLabel(uint nameId)
    {
        if (RankMap.TryGetValue(nameId, out var rank))
        {
            return rank switch
            {
                1 => "B",
                2 => "A",
                3 => "S",
                _ => "?",
            };
        }
        return "";
    }

    /// <summary>强制重建（调试用）。</summary>
    public static void Reload()
    {
        lock (lockObj)
        {
            rankMap = Build();
        }
    }
}
