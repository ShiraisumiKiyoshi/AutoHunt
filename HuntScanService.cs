using System.Numerics;
using Dalamud.Game.ClientState.Objects.Types;

namespace AutoHunt;

/// <summary>
/// 当前地图狩猎怪扫描：遍历对象表，NameId 命中 NotoriousMonster 数据库的即为狩猎怪（零误判）。
/// 注意：客户端对象表只包含玩家周围已加载的对象（约 100 米内），
/// 因此扫描范围是"周围可见范围"，并非整张地图的全部狩猎怪。
/// </summary>
internal static class HuntScanService
{
    /// <summary>单只狩猎怪的扫描结果。</summary>
    public class Entry
    {
        /// <summary>GameObjectId（用于点击选中和按 ID 反查）。</summary>
        public ulong GameObjectId;

        /// <summary>怪物名称。</summary>
        public string Name = "";

        /// <summary>等级标签：B / A / S。</summary>
        public string Rank = "";

        /// <summary>是否已死亡。</summary>
        public bool IsDead;

        /// <summary>血量百分比（0-100）。</summary>
        public float HpPercent;

        /// <summary>与玩家的直线距离（米）。</summary>
        public float Distance;

        /// <summary>地图显示坐标（与游戏地图面板一致，如 12.3）。</summary>
        public Vector2 MapPos;

        /// <summary>世界坐标。</summary>
        public Vector3 WorldPos;
    }

    private static List<Entry> cache = new();
    private static DateTime cacheTime = DateTime.MinValue;
    private static uint cachedTerritory = 0;

    /// <summary>最近一次扫描结果（供 UI 读取）。</summary>
    public static IReadOnlyList<Entry> Entries => cache;

    /// <summary>最近一次扫描时间。</summary>
    public static DateTime LastScanTime => cacheTime;

    /// <summary>
    /// 获取当前扫描快照。结果缓存 1 秒，可由 UI 每帧调用而不重复扫描；
    /// 切换地图后缓存自动失效。
    /// </summary>
    public static IReadOnlyList<Entry> GetSnapshot()
    {
        var territory = Svc.ClientState.TerritoryType;
        if (DateTime.Now - cacheTime < TimeSpan.FromSeconds(1) && territory == cachedTerritory)
            return cache;

        cacheTime = DateTime.Now;
        cachedTerritory = territory;
        cache = ScanNow();
        return cache;
    }

    /// <summary>强制下一次调用重新扫描。</summary>
    public static void Invalidate()
    {
        cacheTime = DateTime.MinValue;
    }

    /// <summary>扫描对象表中当前地图的全部狩猎怪。</summary>
    private static List<Entry> ScanNow()
    {
        var list = new List<Entry>();
        if (!Player.Available) return list;
        try
        {
            var map = MapManager.GetMapForTerritory(Svc.ClientState.TerritoryType);
            if (map == null) return list;
            var m = map.Value;

            foreach (var obj in Svc.Objects)
            {
                if (obj is not IBattleNpc npc) continue;
                if (!HuntMobDatabase.RankMap.TryGetValue(npc.NameId, out var rank)) continue;
                if (string.IsNullOrEmpty(npc.Name.TextValue)) continue;

                // 世界坐标 → 地图显示坐标（复用 MapManager 的 MapLinkPayload 公式）
                var disp = MapManager.WorldToDisplay(npc.Position.X, npc.Position.Z, m);

                list.Add(new Entry
                {
                    GameObjectId = npc.GameObjectId,
                    Name = npc.Name.TextValue,
                    Rank = rank switch { 1 => "B", 2 => "A", 3 => "S", _ => "?" },
                    IsDead = npc.IsDead,
                    HpPercent = npc.MaxHp > 0 ? npc.CurrentHp / (float)npc.MaxHp * 100f : 0f,
                    Distance = Vector3.Distance(Player.Position, npc.Position),
                    MapPos = disp,
                    WorldPos = npc.Position,
                });
            }

            // 存活的排前面，同状态按距离升序
            list.Sort((a, b) =>
            {
                int d = a.IsDead.CompareTo(b.IsDead);
                return d != 0 ? d : a.Distance.CompareTo(b.Distance);
            });
        }
        catch (Exception e)
        {
            PluginLog.Warning($"[AutoHunt] 扫描狩猎怪失败: {e.Message}");
        }
        return list;
    }

    /// <summary>按 GameObjectId 反查对象（对象已卸载返回 null）。</summary>
    public static IGameObject? FindById(ulong gameObjectId)
    {
        foreach (var obj in Svc.Objects)
        {
            if (obj.GameObjectId == gameObjectId) return obj;
        }
        return null;
    }
}
