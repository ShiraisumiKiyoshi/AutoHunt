namespace AutoHunt;

/// <summary>
/// 车头玩家管理（多车头）：查找、增删、焦点保持。
/// 任一车头发送的坐标都会触发狩猎流程；只有列表清空时才衔接跨区流程。
/// </summary>
internal static class Conductor
{
    public static bool IsValid => P.Config.Conductors.Count > 0;

    /// <summary>按名字（去 @后缀 前的裸名）判断是否车头。</summary>
    public static bool IsConductor(string name) =>
        !string.IsNullOrEmpty(name) && P.Config.Conductors.Any(c => c.Name == name);

    /// <summary>
    /// 添加车头（同名去重）。玩家在附近时记录其世界服并焦点；不在附近时只按名字记录。
    /// </summary>
    public static void Add(string rawName, uint worldId = 0, bool focusIfNearby = true)
    {
        var name = ContextMenuManager.StripWorldSuffix(rawName);
        if (name.IsNullOrEmpty()) return;

        var existing = P.Config.Conductors.FirstOrDefault(c => c.Name == name);
        if (existing != null)
        {
            if (worldId != 0 && existing.WorldId == 0) existing.WorldId = worldId;
            EzConfig.Save();
            Notify.Info($"{name} 已经是车头了~");
            return;
        }

        IPlayerCharacter? pc = null;
        if (Player.Available && focusIfNearby)
        {
            pc = Svc.Objects.FirstOrDefault(x => x is IPlayerCharacter p && p.Name.TextValue == name) as IPlayerCharacter;
        }

        P.Config.Conductors.Add(new ConductorEntry
        {
            Name = name,
            WorldId = worldId != 0 ? worldId : pc?.HomeWorld.RowId ?? 0,
        });
        EzConfig.Save();

        if (pc != null && focusIfNearby)
        {
            Svc.Targets.FocusTarget = pc;
            Notify.Info($"你已选中{name}为车头~（现有 {P.Config.Conductors.Count} 个车头）");
        }
        else
        {
            Notify.Info($"你已选中{name}为车头~（现有 {P.Config.Conductors.Count} 个车头；玩家当前不在附近，靠近后会自动焦点）");
        }
    }

    /// <summary>移除指定车头。返回 true 表示列表已清空（此时会衔接跨区流程）。</summary>
    public static bool Remove(string name)
    {
        var list = P.Config.Conductors;
        var n = list.RemoveAll(c => c.Name == name);
        if (n == 0) return false;
        EzConfig.Save();
        if (list.Count > 0)
        {
            Notify.Info($"已取消车头：{name}（剩余 {list.Count} 个车头）。");
            return false;
        }
        Notify.Info("已取消全部车头设置。");
        Svc.Targets.FocusTarget = null;
        if (P.Config.CrossRegionEnable) CrossRegionController.Begin();
        return true;
    }

    /// <summary>取消全部车头（跨区功能开启时自动衔接跨区流程）。</summary>
    public static void ClearAll()
    {
        if (!IsValid) return;
        P.Config.Conductors.Clear();
        EzConfig.Save();
        Svc.Targets.FocusTarget = null;
        Notify.Info("已取消全部车头设置。");
        if (P.Config.CrossRegionEnable) CrossRegionController.Begin();
    }

    /// <summary>在对象表中查找名字匹配的车头玩家。</summary>
    public static IPlayerCharacter? FindByName(string name)
    {
        foreach (var obj in Svc.Objects)
        {
            if (obj is IPlayerCharacter pc && pc.Name.TextValue == name)
                return pc;
        }
        return null;
    }

    /// <summary>查找对象表中距离最近的车头玩家（可能为 null）。</summary>
    public static IPlayerCharacter? FindNearest()
    {
        if (!IsValid || !Player.Available) return null;
        IPlayerCharacter? best = null;
        var bestDist = float.MaxValue;
        var names = P.Config.Conductors.Select(c => c.Name).ToHashSet();
        foreach (var obj in Svc.Objects)
        {
            if (obj is not IPlayerCharacter pc || !names.Contains(pc.Name.TextValue)) continue;
            var d = Vector3.Distance(Player.Position, pc.Position);
            if (d < bestDist)
            {
                bestDist = d;
                best = pc;
            }
        }
        return best;
    }

    /// <summary>当前焦点是否已经是某个车头。</summary>
    private static bool FocusIsConductor()
    {
        var ft = Svc.Targets.FocusTarget;
        if (ft == null) return false;
        return IsConductor(ft.Name.TextValue);
    }

    /// <summary>
    /// 每 500ms 检查焦点目标：焦点不是车头（丢失/被改）时，自动焦点到对象表中最近的车头。
    /// 注意：只恢复焦点目标（FocusTarget），绝不改动当前选中目标（Target）——
    /// 车头玩家不是 IBattleNpc，一旦覆盖 Target 会把狩猎流程选中的怪顶掉，
    /// 造成"明明已选中目标却一直寻找中/目标丢失直至超时"。
    /// </summary>
    public static void EnsureFocus()
    {
        if (!IsValid) return;
        if (!EzThrottler.Throttle("WYFocus", 500)) return;
        if (FocusIsConductor()) return;

        var nearest = FindNearest();
        if (nearest != null)
        {
            Svc.Targets.FocusTarget = nearest;
        }
    }
}
