namespace AutoHunt;

/// <summary>
/// 副本地图分区情况的「默认表」（来自腾讯文档《副本地图识别表》）：
/// 国服 4.0 ~ 7.0 的野外地图，在各「大区 - 服务器」上是否区分副本区。
///
/// 使用优先级：手动编辑 &gt; 自动识别（原生区号 / DR 反馈） &gt; 本表默认 &gt; 未识别。
/// 表中未收录的地图 = 未识别，进图后仍走自动探测（约 8 秒）。
/// 本表只提供默认值，不写入配置；用户在「编辑副本地图识别」里改过的条目存到
/// Config.ManualInstanceState，永远优先于本表与自动识别。
/// </summary>
internal static class InstanceMapDefaults
{
    public sealed record VersionMaps(string Version, string[] Maps);

    /// <summary>地图版本号 → 该版本野外地图（顺序同腾讯文档）。</summary>
    public static readonly VersionMaps[] Versions =
    {
        new("7.0", new[] { "奥阔帕恰山", "克扎玛乌卡湿地", "亚克特尔树海", "夏劳尼荒野", "遗产之地", "活着的记忆" }),
        new("6.0", new[] { "萨维奈岛", "加雷马", "迷津", "叹息海", "天外天垓", "厄尔庇斯" }),
        new("5.0", new[] { "雷克兰德", "珂露西亚岛", "安穆·艾兰", "伊尔美格", "拉凯提卡大森林", "黑风海" }),
        new("4.0", new[] { "基拉巴尼亚边区", "基拉巴尼亚山区", "基拉巴尼亚湖区", "红玉海", "延夏", "太阳神草原" }),
    };

    /// <summary>国服大区 → 服务器名单（顺序即 UI 显示顺序）。</summary>
    public static readonly (string Dc, string[] Servers)[] DcServers =
    {
        ("陆行鸟", new[] { "红玉海", "拉诺西亚", "幻影群岛", "神意之地", "萌芽池", "宇宙和音", "沃仙曦染", "晨曦王座" }),
        ("莫古力", new[] { "潮风亭", "神拳痕", "白银乡", "白金幻象", "旅人栈桥", "拂晓之间", "龙巢神殿", "梦羽宝境" }),
        ("猫小胖", new[] { "紫水栈桥", "延夏", "静语庄园", "摩杜纳", "海猫茶屋", "柔风海湾", "琥珀原" }),
        ("豆豆柴", new[] { "水晶塔", "银泪湖", "太阳海岸", "伊修加德", "红茶川" }),
    };

    /// <summary>文档覆盖的国服服务器（4 个大区共 28 个）。</summary>
    public static readonly string[] Servers =
        DcServers.SelectMany(d => d.Servers).ToArray();

    /// <summary>文档中标记为「有分区」的组合（陆行鸟 8 个服务器的遗产之地）。
    /// 注：红玉海为文档遗漏后补充，同样只有遗产之地有分区。</summary>
    private static readonly HashSet<(string World, string Map)> lineCapable = new()
    {
        ("红玉海", "遗产之地"),
        ("拉诺西亚", "遗产之地"),
        ("幻影群岛", "遗产之地"),
        ("神意之地", "遗产之地"),
        ("萌芽池", "遗产之地"),
        ("宇宙和音", "遗产之地"),
        ("沃仙曦染", "遗产之地"),
        ("晨曦王座", "遗产之地"),
    };

    private static readonly HashSet<string> coveredMaps =
        new(Versions.SelectMany(v => v.Maps), StringComparer.Ordinal);

    private static readonly HashSet<string> coveredWorlds =
        new(Servers, StringComparer.Ordinal);

    /// <summary>该服务器是否被文档收录（国服 28 个服务器之一）。</summary>
    public static bool IsCoveredWorld(string worldName)
        => !worldName.IsNullOrEmpty() && coveredWorlds.Contains(worldName);

    /// <summary>
    /// 文档默认值：1=有分区，0=无分区，-1=未收录（服务器不在国服列表 / 地图不在版本表里）。
    /// </summary>
    public static int GetDefaultState(string worldName, string mapName)
    {
        if (worldName.IsNullOrEmpty() || mapName.IsNullOrEmpty()) return -1;
        if (!coveredWorlds.Contains(worldName)) return -1;
        if (!coveredMaps.Contains(mapName)) return -1;
        return lineCapable.Contains((worldName, mapName)) ? 1 : 0;
    }
}
