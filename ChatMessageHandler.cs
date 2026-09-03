namespace AutoHunt;

/// <summary>
/// 监听车头玩家的聊天内容：解析坐标链接 / 纯文本坐标，触发狩猎流程。
/// </summary>
internal static class ChatMessageHandler
{
    private static readonly Regex CoordRegex = new(
        @"[(（]?\s*(\d{1,2})[.．](\d)\s*[,，、]\s*(\d{1,2})[.．](\d)\s*[)）]?",
        RegexOptions.Compiled);

    internal static void Chat_ChatMessage(IHandleableChatMessage cm)
    {
        try
        {
            Handle(cm);
        }
        catch (Exception e)
        {
            PluginLog.Error($"处理聊天消息时出错: {e}");
        }
    }

    private static void Handle(IHandleableChatMessage cm)
    {
        if (P.Config == null || !P.Config.Enabled) return;
        if (!Conductor.IsValid) return;

        var localName = Player.Available ? Player.Object.Name.TextValue : "";
        // “自己即车头”模式：自己发出消息的聊天回显里，sender 往往不带世界服信息
        // （自己的名字不显示服务器）；用 /echo 自言自语时 sender 甚至完全为空。
        // 这些情况都不能被当成“非车头消息”丢弃，否则自己当车头发坐标永远不触发。
        var selfMode = localName.Length > 0 && P.Config.ConductorName == localName;

        // 解析发送者：优先用 ECommons 解码，失败时回退到纯文本解析（“名字@世界服”或裸名字）
        string senderName = "";
        uint senderWorld = 0;
        if (TryDecodeSender(cm.Sender, out var s))
        {
            senderName = s.Name;
            senderWorld = s.HomeWorld;
        }

        var rawSender = cm.Sender?.ToString() ?? "";
        if (senderName.IsNullOrEmpty())
        {
            var at = rawSender.LastIndexOf('@');
            senderName = (at > 0 ? rawSender[..at] : rawSender).Trim();
        }

        var nameMatch = senderName == P.Config.ConductorName;
        if (!nameMatch)
        {
            // 解码结果可能带“@世界服”后缀，去掉后再比对一次
            var at = senderName.LastIndexOf('@');
            if (at > 0) nameMatch = senderName[..at].Trim() == P.Config.ConductorName;
        }
        if (!nameMatch)
        {
            // 自己即车头：sender 为空（/echo、部分系统回显）时视为本人消息
            if (selfMode && senderName.IsNullOrEmpty())
            {
                nameMatch = true;
            }
            else
            {
                // 调试模式下低频打印非车头消息，方便排查“为什么没触发”
                if (P.Config.Debug && EzThrottler.Throttle("WYDbgSender", 3000))
                {
                    PluginLog.Debug($"[AutoHunt] 忽略非车头消息: \"{rawSender}\" ({cm.LogKind}): {GetText(cm)}");
                }
                return;
            }
        }

        // 世界服校验：仅当消息里确实携带世界服信息（senderWorld != 0）时才比对。
        // 自己发的消息回显通常不带世界服；带着比对会把“自己即车头”误杀。
        if (!selfMode && P.Config.ConductorWorldId != 0 && senderWorld != 0 && senderWorld != P.Config.ConductorWorldId)
        {
            if (P.Config.Debug) PluginLog.Debug($"[AutoHunt] 车头世界服不匹配: {senderWorld} != {P.Config.ConductorWorldId}");
            return;
        }

        if (P.Config.Debug) PluginLog.Debug($"[AutoHunt] 收到车头消息 ({cm.LogKind}){(selfMode ? " [自己即车头]" : "")}: {GetText(cm)}");

        // 优先处理地图链接坐标
        foreach (var payload in cm.Message.Payloads)
        {
            if (payload is MapLinkPayload m)
            {
                HandleMapLink(m);
                return;
            }
        }

        // 其次尝试解析纯文本坐标
        if (P.Config.ParseTextCoordinates)
        {
            HandleTextCoordinates(GetText(cm));
        }
    }

    private static string GetText(IHandleableChatMessage cm)
    {
        return string.Concat(cm.Message.Payloads.OfType<TextPayload>().Select(p => p.Text));
    }

    /// <summary>
    /// 处理车头发送的地图链接坐标。
    /// </summary>
    private static void HandleMapLink(MapLinkPayload m)
    {
        var targetTerritory = m.TerritoryType.RowId;

        // 自动打开地图并插旗（车头可能连发坐标，3 秒节流足够）
        if (P.Config.AutoOpenMap && EzThrottler.Throttle("WYOpenMap", 3000))
        {
            Svc.GameGui.OpenMapWithMapLink(m);
        }

        // RawX/RawY / 1000 即世界坐标（X, Z）
        var targetWorld = new Vector2(m.RawX / 1000f, m.RawY / 1000f);
        HandleTargetPosition(targetTerritory, targetWorld);
    }

    /// <summary>
    /// 处理纯文本坐标（仅限车头与自己在同一地图时有意义）。
    /// </summary>
    private static void HandleTextCoordinates(string text)
    {
        if (text.IsNullOrEmpty()) return;
        var match = CoordRegex.Match(text);
        if (!match.Success) return;

        float x = int.Parse(match.Groups[1].Value) + int.Parse(match.Groups[2].Value) / 10f;
        float y = int.Parse(match.Groups[3].Value) + int.Parse(match.Groups[4].Value) / 10f;

        var map = MapManager.GetCurrentMap();
        if (map == null) return;
        var world = MapManager.DisplayToWorld(x, y, map.Value);
        if (P.Config.Debug) PluginLog.Debug($"解析文本坐标 ({x}, {y}) → 世界 {world}");

        if (P.Config.AutoOpenMap && EzThrottler.Throttle("WYOpenMap", 3000))
        {
            var link = new MapLinkPayload(Svc.ClientState.TerritoryType, map.Value.RowId, x, y, 0f);
            Svc.GameGui.OpenMapWithMapLink(link);
        }

        HandleTargetPosition(Svc.ClientState.TerritoryType, world);
    }

    /// <summary>
    /// 核心：收到坐标后，先判断击杀数是否已满 → 切区 / 传送 / 直接寻路。
    /// </summary>
    internal static void HandleTargetPosition(uint targetTerritory, Vector2 targetWorld)
    {
        var nearest = MapManager.GetNearestAetheryte(targetTerritory, targetWorld);
        if (nearest == null)
        {
            Notify.Error("未找到目标坐标附近的水晶。");
            return;
        }
        var aetheryteName = nearest.Value.PlaceName.ValueNullable?.Name.ToString() ?? "水晶";

        var tp = TargetPosition.CreateOrNull(targetTerritory, targetWorld, nearest, aetheryteName);
        if (tp == null) return;

        // 判断击杀数是否已满，需要切换副本区
        var pendingSwitch = InstanceController.PendingSwitchInstance;

        // 副本区切换流程进行中（切区传送中 / 切区任务执行中）：
        // 车头会为迟到玩家重复发送坐标，此时绝不能走普通路径——
        // 否则 OnNewCoordinate 会用 SwitchInstance=0 覆盖 TeleportTo，切区被吞掉，
        // 表现为"传送到达后没切副本区，直接寻路去坐标"。
        // 把最新坐标暂存，切区完成后由主循环自动继续前往。
        if (pendingSwitch == 0 && (P.SwitchInProgress || (P.TeleportTo != null && P.TeleportTo.SwitchInstance > 0)))
        {
            P.HeldCoordinate = tp;
            if (P.Config.Debug) PluginLog.Debug($"[AutoHunt] 副本区切换流程进行中，暂存车头坐标 ({targetWorld.X:0.0}, {targetWorld.Y:0.0})");
            return;
        }

        if (pendingSwitch != 0)
        {
            InstanceController.ConsumePendingSwitch();
            // 传送到目标坐标最近的水晶，到达后切换副本区
            // 标记到达后需要切换副本区
            P.TeleportTo = new ArrivalData
            {
                Aetheryte = nearest,
                Territory = targetTerritory,
                SwitchInstance = pendingSwitch,
            };
            HuntController.Reset();
            Notify.Info($"准备切换 {pendingSwitch} 号副本区，传送到 {aetheryteName}…");
            return;
        }

        HuntController.OnNewCoordinate(tp);
    }
}
