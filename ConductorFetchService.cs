using Dalamud.Game.Gui.PartyFinder.Types;
using ECommons.Automation.NeoTaskManager;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace AutoHunt;

/// <summary>
/// 自动获取车头：打开队员招募面板 → 请求「怪物狩猎」分类招募 → 通过 Dalamud
/// PartyFinder 网络包监听收集全部怪物狩猎招募的招募人 → 去重后**替换**车头列表（跳过自己）。
/// </summary>
public static unsafe class ConductorFetchService
{
    /// <summary>读取流程是否在执行中（TaskManager 之外的状态，供跨区流程/操作条查询）。</summary>
    public static bool Running { get; private set; }

    /// <summary>当前执行状态描述（操作条/状态页显示）。</summary>
    public static string CurrentState { get; private set; } = "";

    private static readonly List<(ulong Id, string Name, uint HomeWorld)> Received = new();
    private static bool collecting;
    private static bool subscribed;
    private static DateTime lastPacket = DateTime.MinValue;
    private static int packetCount;

    /// <summary>
    /// 启动自动获取车头任务链。前置条件不满足时提示并放弃。
    /// </summary>
    public static void Enqueue()
    {
        if (!Player.Available)
        {
            Notify.Error("现在不能这么做（角色不可用）。");
            return;
        }
        if (Running)
        {
            Notify.Error("正在获取车头，请稍候。");
            return;
        }
        if (P.TaskManager.IsBusy)
        {
            Notify.Error("当前有任务正在执行（如副本区切换/创建招募），请稍后再试。");
            return;
        }

        Received.Clear();
        collecting = false;
        packetCount = 0;
        Running = true;
        CurrentState = "准备打开招募面板";

        var cfg = new TaskManagerConfiguration(timeLimitMS: 60000);

        // 招募面板已打开则先关闭，再重新打开（确保状态干净）
        P.TaskManager.Enqueue(() =>
        {
            if (TryGetAddonByName<AtkUnitBase>("LookingForGroup", out _) && EzThrottler.Throttle("WYFetchClosePf1"))
            {
                Chat.Instance.ExecuteCommand("/pfinder");
            }
        }, cfg);
        P.TaskManager.Enqueue(() => !TryGetAddonByName<AtkUnitBase>("LookingForGroup", out _), cfg);
        P.TaskManager.Enqueue(() => Chat.Instance.ExecuteCommand("/pfinder"), cfg);

        // 等待面板就绪
        P.TaskManager.Enqueue(() => TryGetAddonByName<AtkUnitBase>("LookingForGroup", out _), cfg);

        // 订阅招募包监听并请求「怪物狩猎」分类（tab 索引 = DutyCategory 位序 = 11）
        P.TaskManager.Enqueue(() =>
        {
            Subscribe();
            collecting = true;
            lastPacket = DateTime.Now;
            CurrentState = "搜索怪物狩猎招募…";
            var agent = AgentLookingForGroup.Instance();
            if (agent == null) return true;
            var ok = agent->RequestCategoryListings(11);
            if (P.Config.Debug) PluginLog.Debug($"[AutoHunt] 获取车头：RequestCategoryListings(11) → {ok}");
            return true;
        }, cfg);

        // 等待收集完成：1.5 秒无新招募包即认为搜索完成（最长 10 秒）
        P.TaskManager.Enqueue(() =>
        {
            var idle = (DateTime.Now - lastPacket).TotalMilliseconds > 1500;
            return idle && packetCount > 0;
        }, new TaskManagerConfiguration(timeLimitMS: 10000));

        // 一个分类没搜到 → 回退请求「全部」分类再收集一轮
        P.TaskManager.Enqueue(() =>
        {
            if (Received.Count > 0) return true;
            CurrentState = "怪物狩猎分类无结果，搜索全部招募…";
            lastPacket = DateTime.Now;
            var agent = AgentLookingForGroup.Instance();
            if (agent == null) return true;
            agent->RequestCategoryListings(0);
            return true;
        }, cfg);
        P.TaskManager.Enqueue(() =>
        {
            if (packetCount == 0) return false; // 尚未收到第二轮数据包
            var idle = (DateTime.Now - lastPacket).TotalMilliseconds > 1500;
            return idle;
        }, new TaskManagerConfiguration(timeLimitMS: 10000));

        // 完成应用结果
        P.TaskManager.Enqueue(() => Finish(), cfg);
    }

    private static void Subscribe()
    {
        if (subscribed) return;
        Svc.PfGui.ReceiveListing += OnListing;
        subscribed = true;
    }

    private static void Unsubscribe()
    {
        if (!subscribed) return;
        subscribed = false;
        collecting = false;
        Svc.PfGui.ReceiveListing -= OnListing;
    }

    private static void OnListing(IPartyFinderListing listing, IPartyFinderListingEventArgs e)
    {
        try
        {
            if (!collecting) return;
            lastPacket = DateTime.Now;
            packetCount++;
            if (listing.Category != DutyCategory.TheHunt) return;

            var name = listing.Name?.TextValue ?? "";
            if (name.IsNullOrEmpty()) return;
            Received.Add((listing.Id, name, (uint)listing.HomeWorld.RowId));
        }
        catch (Exception ex)
        {
            PluginLog.Error($"[AutoHunt] 处理招募数据失败: {ex}");
        }
    }

    /// <summary>应用结果：去重、跳过自己、替换车头列表。返回 true 结束任务链。</summary>
    private static bool Finish()
    {
        try
        {
            Unsubscribe();

            var self = Player.Available ? Player.Object.Name.TextValue : "";
            var names = new HashSet<string>();
            var added = new List<ConductorEntry>();
            foreach (var (_, name, world) in Received)
            {
                if (name == self) continue;
                if (!names.Add(name)) continue;
                added.Add(new ConductorEntry { Name = name, WorldId = world });
            }

            var oldNames = P.Config.Conductors.Select(c => c.Name).ToHashSet();
            P.Config.Conductors.Clear();
            P.Config.Conductors.AddRange(added);
            EzConfig.Save();

            var skippedSelf = Received.Any(r => r.Name == self) ? "（招募人中的自己已跳过）" : "";
            var summary = added.Count > 0 ? string.Join("、", added.Select(c => c.Name)) : "无";
            if (added.Count > 0)
            {
                var changed = added.Count != oldNames.Count || added.Any(c => !oldNames.Contains(c.Name));
                if (P.Config.Debug || changed) Notify.Info($"已获取 {added.Count} 个车头：{summary}{skippedSelf}");
                else Notify.Info($"车头未变化（{added.Count} 个）。");
            }
            else
            {
                Notify.Warning($"未在队员招募中找到怪物狩猎招募，车头列表已清空{skippedSelf}。");
            }
            return true;
        }
        catch (Exception ex)
        {
            PluginLog.Error($"[AutoHunt] 应用车头结果失败: {ex}");
            Notify.Error("获取车头失败，详见日志。");
            return true;
        }
        finally
        {
            Running = false;
            CurrentState = "";
        }
    }

    /// <summary>取消进行中的获取流程（总开关关闭 / /ah stop 时调用）：解除监听并复位状态。</summary>
    public static void Cancel()
    {
        if (!Running && !subscribed) return;
        Unsubscribe();
        Running = false;
        CurrentState = "";
    }

    /// <summary>暂停恢复补偿：收集空闲判定基于 lastPacket 墙钟，把暂停时长补偿进去。</summary>
    public static void CompensatePause(long pauseMs)
    {
        if (pauseMs <= 0 || lastPacket == DateTime.MinValue) return;
        lastPacket += TimeSpan.FromMilliseconds(pauseMs);
    }

    /// <summary>插件卸载兜底清理。</summary>
    public static void Dispose()
    {
        Unsubscribe();
        Running = false;
        CurrentState = "";
    }
}
