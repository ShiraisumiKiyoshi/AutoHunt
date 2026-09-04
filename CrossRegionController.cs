using AutoHunt.Tasks;
using ECommons.ExcelServices;
using ECommons.GameHelpers;
using Lumina.Excel.Sheets;

namespace AutoHunt;

/// <summary>
/// 跨区（数据中心传送）控制器。
/// 流程：取消车头 → 传送到「跨区前传送到城市」→ 立即跨区到狩猎时间表中「本地时间下一个时间点」对应的服务器（无需等待到点）
/// → 传送到「跨区后传送到水晶」（可选）
/// → 按招募标签配置自动开启队员招募（可选，需同时启用「启用一键创建队员招募」）。
/// 自带状态机，不占用 TaskManager（留给招募任务链等任务使用）。
/// </summary>
internal static class CrossRegionController
{
    private enum Phase { Inactive, ToPreCity, DcTravel, ToPostCrystal, CreatePF }

    private static Phase phase = Phase.Inactive;
    private static DateTime stepStart = DateTime.MinValue;
    private static string targetWorld = "";
    private static bool pfEnqueued = false;
    private static bool wasBetweenAreas = false;
    private static DateTime citySince = DateTime.MinValue;
    private static int preTpAttempts = 0;

    internal static bool Active => phase != Phase.Inactive;

    /// <summary>当前流程状态（状态页展示用）。</summary>
    internal static string CurrentState => phase switch
    {
        Phase.Inactive => "未运行",
        Phase.ToPreCity => $"前往跨区城市（{PreCityName}）",
        Phase.DcTravel => $"跨区传送中（→ {targetWorld}）",
        Phase.ToPostCrystal => "传送到跨区后水晶",
        Phase.CreatePF => "自动开启招募",
        _ => "未知",
    };

    /// <summary>跨区前城市定义：显示名 / Aetheryte RowId / TerritoryType。</summary>
    internal static readonly (string Name, uint AetheryteId, uint Territory)[] PreCities =
    {
        ("格里达尼亚新街", 2, 133),
        ("利姆萨·罗敏萨下层甲板", 8, 129),
        ("乌尔达哈现世回廊", 9, 130),
    };

    private static (string Name, uint AetheryteId, uint Territory) PreCity => PreCities[Math.Clamp(P.Config.CrossRegionPreCity, 0, PreCities.Length - 1)];
    private static string PreCityName => PreCity.Name;

    /// <summary>
    /// 启动跨区流程（取消车头后调用）。前置条件不满足时提示并放弃。
    /// </summary>
    public static void Begin()
    {
        if (!P.Config.CrossRegionEnable) return;
        if (Active)
        {
            Notify.Info("跨区：跨区流程已在进行中，忽略本次触发。");
            return;
        }
        if (!Player.Available)
        {
            Notify.Error("跨区：角色当前不可用，已取消跨区流程。");
            return;
        }
        if (P.Config.CrossRegionSchedule.Count == 0)
        {
            Notify.Error("跨区：狩猎时间表为空，请先在「跨区」标签中添加车次，已取消跨区流程。");
            return;
        }
        if (GetNextEntry() == null)
        {
            Notify.Error("跨区：狩猎时间表中没有有效的时间条目（需为 HHMM 且时间合法），已取消跨区流程。");
            return;
        }
        if (!DependencyChecker.IsInstalled("Lifestream"))
        {
            Notify.Error("跨区：未安装 Lifestream，无法跨区传送，已取消跨区流程。");
            return;
        }

        // 中断狩猎相关状态，独占控制权
        P.TaskManager.Abort();
        P.TeleportTo = null;
        P.SwitchInProgress = false;
        P.HeldCoordinate = null;
        HuntController.Reset();

        Reset();
        phase = Phase.ToPreCity;
        stepStart = DateTime.Now;
        wasBetweenAreas = false;
        targetWorld = "";
        pfEnqueued = false;
        citySince = DateTime.MinValue;
        preTpAttempts = 0;
        Notify.Info($"跨区流程已启动：先传送至 {PreCityName}，之后按狩猎时间表跨区。");
    }

    /// <summary>停止并复位跨区流程。</summary>
    public static void Reset()
    {
        phase = Phase.Inactive;
        targetWorld = "";
        pfEnqueued = false;
        wasBetweenAreas = false;
        citySince = DateTime.MinValue;
        preTpAttempts = 0;
    }

    public static void Update()
    {
        if (phase == Phase.Inactive) return;
        if (!Player.Available) return;
        if (S.LifestreamIPC == null || S.TeleporterIPC == null) return;

        // 运行中随时尊重开关：关闭「启用跨区功能」立即中止流程
        if (!P.Config.CrossRegionEnable)
        {
            Reset();
            Notify.Info("跨区：已关闭「启用跨区功能」，跨区流程中止。");
            return;
        }
        // 时间表被清空时中止，避免空转
        if (P.Config.CrossRegionSchedule.Count == 0)
        {
            Reset();
            Notify.Info("跨区：狩猎时间表已清空，跨区流程中止。");
            return;
        }

        try
        {
            switch (phase)
            {
                case Phase.ToPreCity: UpdateToPreCity(); break;
                    case Phase.DcTravel: UpdateDcTravel(); break;
                case Phase.ToPostCrystal: UpdateToPostCrystal(); break;
                case Phase.CreatePF: UpdateCreatePF(); break;
            }
        }
        catch (Exception e)
        {
            PluginLog.Error($"[AutoHunt] 跨区流程异常: {e}");
            Notify.Error("跨区流程出现异常，已中止。详见日志。");
            Reset();
        }
    }

    // ===== 阶段 1：传送到跨区前城市 =====

    private static void UpdateToPreCity()
    {
        var city = PreCity;
        var betweenAreas = Svc.Condition[ConditionFlag.BetweenAreas] || Svc.Condition[ConditionFlag.BetweenAreas51];
        // 连续停留在城市地图的时长（Interactable 可能因 UI 占用短暂失败，停留 5 秒即视为已到达）
        var inCity = Svc.ClientState.TerritoryType == city.Territory && !betweenAreas;
        if (inCity && citySince == DateTime.MinValue) citySince = DateTime.Now;
        if (!inCity) citySince = DateTime.MinValue;
        var arrived = inCity && (Player.Interactable || (DateTime.Now - citySince).TotalSeconds >= 5);

        if (arrived)
        {
            // 已在城市 → 立即跨区到本地时间下一个时间点对应的服务器（无需等待到点）
            var next = GetNextEntry();
            if (next == null)
            {
                Notify.Error("跨区：找不到有效的时间表条目，已中止。");
                Reset();
                return;
            }
            targetWorld = next.Value.entry.World;
            if (!S.LifestreamIPC.TryExecuteCommand(targetWorld))
            {
                Notify.Error($"跨区：Lifestream 命令不可用，无法跨区到「{targetWorld}」，已中止。");
                Reset();
                return;
            }
            stepStart = DateTime.Now;
            wasBetweenAreas = false;
            phase = Phase.DcTravel;
            Notify.Info($"跨区：已到达 {city.Name}，直接开始跨区至「{targetWorld}」…");
            return;
        }

        if ((DateTime.Now - stepStart).TotalSeconds > 180)
        {
            Notify.Error($"跨区：传送前往跨区城市超时（当前地图 {Svc.ClientState.TerritoryType}，目标 {city.Territory}），已中止。");
            Reset();
            return;
        }

        if (betweenAreas) return;
        if (Svc.Condition[ConditionFlag.InCombat] || Svc.Condition[ConditionFlag.Casting]) return;
        if (!Player.Interactable || Player.IsAnimationLocked) return;

        // 节流尝试传送（8 秒一次、最多 5 次，避免重复传送刷屏/烧钱）
        if (EzThrottler.Throttle("WYCrossPreTp", 8000))
        {
            if (++preTpAttempts > 5)
            {
                Notify.Error($"跨区：多次传送未能到达 {city.Name}（当前地图 {Svc.ClientState.TerritoryType}），已中止。");
                Reset();
                return;
            }
            if (!S.TeleporterIPC.TryTeleport(city.AetheryteId, 0)
                && !S.LifestreamIPC.TryTeleport(city.AetheryteId))
            {
                AutoHunt.NativeTeleport(city.AetheryteId);
            }
        }
    }

    // ===== 阶段 2：等待跨区传送完成 =====

    private static void UpdateDcTravel()
    {
        var betweenAreas = Svc.Condition[ConditionFlag.BetweenAreas] || Svc.Condition[ConditionFlag.BetweenAreas51];
        if (betweenAreas) wasBetweenAreas = true;

        var elapsed = (DateTime.Now - stepStart).TotalSeconds;
        // 跨区至少需要约 1 分钟（读图/切服）；要求至少经历过一次读图或切区，且 Lifestream 空闲、角色可操作
        if (elapsed > 60 && wasBetweenAreas && !betweenAreas
            && Player.Interactable && !S.LifestreamIPC.GetIsBusy())
        {
            phase = Phase.ToPostCrystal;
            stepStart = DateTime.Now;
            wasBetweenAreas = false;
            Notify.Info($"跨区：已到达「{targetWorld}」。");
            return;
        }

        if (elapsed > 600)
        {
            Notify.Error("跨区：等待跨区传送超时（10 分钟），已中止。");
            Reset();
        }
    }

    // ===== 阶段 3：传送到跨区后水晶 =====

    private static void UpdateToPostCrystal()
    {
        var post = P.Config.CrossRegionPostAetheryteId;
        if (post == 0)
        {
            // 未配置跨区后水晶，直接进入招募阶段
            phase = Phase.CreatePF;
            stepStart = DateTime.Now;
            return;
        }

        var betweenAreas = Svc.Condition[ConditionFlag.BetweenAreas] || Svc.Condition[ConditionFlag.BetweenAreas51];
        if (wasBetweenAreas && !betweenAreas && Player.Interactable)
        {
            phase = Phase.CreatePF;
            stepStart = DateTime.Now;
            wasBetweenAreas = false;
            Notify.Info("跨区：已传送到目标水晶。");
            return;
        }

        if ((DateTime.Now - stepStart).TotalSeconds > 120)
        {
            Notify.Error("跨区：传送前往跨区后水晶超时，跳过该步骤。");
            phase = Phase.CreatePF;
            stepStart = DateTime.Now;
            wasBetweenAreas = false;
            return;
        }

        if (betweenAreas) return;
        if (Svc.Condition[ConditionFlag.InCombat] || Svc.Condition[ConditionFlag.Casting]) return;
        if (!Player.Interactable || Player.IsAnimationLocked) return;

        if (EzThrottler.Throttle("WYCrossPostTp", 2000))
        {
            if (!S.TeleporterIPC.TryTeleport(post, 0)
                && !S.LifestreamIPC.TryTeleport(post))
            {
                AutoHunt.NativeTeleport(post);
            }
            wasBetweenAreas = true;
        }
    }

    // ===== 阶段 4：自动开启招募 =====

    private static void UpdateCreatePF()
    {
        if (P.Config.CrossRegionAutoPF && !pfEnqueued)
        {
            pfEnqueued = true;
            if (P.Config.PfinderEnable)
            {
                Notify.Info("跨区：流程完成，自动开启队员招募…");
                TaskCreateHuntPF.Enqueue();
            }
            else
            {
                Notify.Error("跨区：流程完成。已启用「自动开启招募」，但「启用一键创建队员招募」未开启，不会自动开启招募。");
            }
        }
        else if (!pfEnqueued)
        {
            pfEnqueued = true;
            Notify.Info("跨区：流程完成（未启用自动开启招募）。");
        }

        // TaskCreateHuntPF 会占用 TaskManager，等它跑完即可结束状态机
        if (!P.TaskManager.IsBusy)
        {
            Reset();
        }
    }

    // ===== 时间表解析 =====

    /// <summary>
    /// 找到狩猎时间表中「本地时间的下一个」条目：
    /// 时间晚于当前时间的最小者；若都不晚于当前时间，则取时间最小者（视为明天）。
    /// 返回 (解析后的分钟数, 条目)；无有效条目返回 null。
    /// </summary>
    private static (int time, CrossRegionScheduleEntry entry)? GetNextEntry()
    {
        var now = DateTime.Now;
        int? bestFuture = null;
        CrossRegionScheduleEntry bestFutureEntry = null;
        int? bestAny = null;
        CrossRegionScheduleEntry bestAnyEntry = null;

        foreach (var e in P.Config.CrossRegionSchedule)
        {
            var t = ParseHHMM(e?.Time);
            if (t == null) continue;
            if (bestAny == null || t.Value < bestAny.Value)
            {
                bestAny = t.Value;
                bestAnyEntry = e;
            }
            var nowMinutes = now.Hour * 60 + now.Minute;
            if (t.Value > nowMinutes && (bestFuture == null || t.Value < bestFuture.Value))
            {
                bestFuture = t.Value;
                bestFutureEntry = e;
            }
        }

        if (bestFuture != null) return (bestFuture.Value, bestFutureEntry);
        if (bestAny != null) return (bestAny.Value, bestAnyEntry);
        return null;
    }

    /// <summary>把解析出的分钟数换算为下一次到达的本地时间（今天已过则视为明天）。</summary>
    private static DateTime ResolveNextTime(int minutes)
    {
        var now = DateTime.Now;
        var target = now.Date.AddMinutes(minutes);
        if (target <= now) target = target.AddDays(1);
        return target;
    }

    /// <summary>解析 HHMM 字符串为分钟数（0-1439），非法返回 null。</summary>
    public static int? ParseHHMM(string s)
    {
        if (string.IsNullOrWhiteSpace(s) || s.Length != 4 || !s.All(char.IsDigit)) return null;
        var h = int.Parse(s[..2]);
        var m = int.Parse(s[2..]);
        if (h > 23 || m > 59) return null;
        return h * 60 + m;
    }

    /// <summary>
    /// 获取玩家当前所在大区（数据中心）内的全部公共服务器（世界）名称。
    /// 角色不可用时返回空列表（UI 会回退为手动输入）。
    /// </summary>
    public static List<string> GetCurrentDcWorlds()
    {
        var list = new List<string>();
        try
        {
            var dcRowId = 0u;
            if (Player.Available)
            {
                var cw = Player.Object.CurrentWorld;
                if (cw.RowId != 0) dcRowId = cw.Value.DataCenter.Value.RowId;
                else
                {
                    var hw = Player.Object.HomeWorld;
                    if (hw.RowId != 0) dcRowId = hw.Value.DataCenter.Value.RowId;
                }
            }
            if (dcRowId == 0) return list;

            foreach (var w in ExcelWorldHelper.GetPublicWorlds(dcRowId))
            {
                var name = w.Name.ToString();
                if (!string.IsNullOrEmpty(name)) list.Add(name);
            }
        }
        catch (Exception e)
        {
            PluginLog.Warning($"[AutoHunt] 读取当前大区服务器列表失败: {e.Message}");
        }
        return list.OrderBy(x => x).ToList();
    }

    /// <summary>获取角色当前所在服务器（世界）名称，用于流程提示。</summary>
    public static string GetCurrentWorldName()
    {
        try
        {
            if (!Player.Available) return "";
            var cw = Player.Object.CurrentWorld;
            return cw.RowId != 0 ? cw.Value.Name.ToString() : "";
        }
        catch { return ""; }
    }

    /// <summary>获取可传送水晶列表（RowId, 显示名），按名称排序。</summary>
    public static List<(uint Id, string Name)> GetTeleportableAetherytes()
    {
        var list = new List<(uint, string)>();
        try
        {
            foreach (var a in Svc.Data.GetExcelSheet<Aetheryte>())
            {
                if (!a.IsAetheryte) continue;
                var name = a.PlaceName.ValueNullable?.Name.ToString();
                if (string.IsNullOrEmpty(name)) continue;
                list.Add((a.RowId, name));
            }
        }
        catch (Exception e)
        {
            PluginLog.Warning($"[AutoHunt] 读取水晶列表失败: {e.Message}");
        }
        return list.OrderBy(x => x.Item2).ToList();
    }
}
