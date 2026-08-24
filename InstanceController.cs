using AutoHunt.Tasks;
using ECommons.GameHelpers;
using ECommons.Automation.NeoTaskManager;
namespace AutoHunt;

/// <summary>
/// 副本区控制器：
/// 1) 首次传送到可切副本区的地图时，保证自己处于 1 号副本区；
/// 2) 通过扫描战斗对象统计「玩家参与击杀」的怪物数量；
/// 3) 击杀满 N 只（默认 2 只）后设置 pendingSwitchInstance，等待车头发送新坐标后传送切换。
/// </summary>
internal static unsafe class InstanceController
{
    private static bool pendingEnsureInstanceOne = false;
    private static readonly HashSet<uint> ensuredTerritories = new();

    /// <summary>参与过（正在打/打过）的怪物</summary>
    private static readonly HashSet<ulong> engagedMobIds = new();
    /// <summary>已计入击杀数量的死亡怪物（防重复计数）</summary>
    private static readonly HashSet<ulong> countedMobIds = new();

    private static int killCount = 0;
    private static uint lastInstanceId = 0;

    /// <summary>击杀满后等待车头新坐标再切换的目标副本区号；0 = 无待切换</summary>
    private static int pendingSwitchInstance = 0;

    // 缓存的副本区信息（避免 UI / 高频逻辑反复调 IPC）
    private static int cachedInstanceCount = 0;
    private static int cachedCurrentInstance = 0;

    public static int KillCount => killCount;
    public static int PendingSwitchInstance => pendingSwitchInstance;
    public static int CachedInstanceCount => cachedInstanceCount;
    public static int CachedCurrentInstance => cachedCurrentInstance;

    /// <summary>切换地图时调用。</summary>
    public static void OnTerritoryChanged(uint territory)
    {
        killCount = 0;
        engagedMobIds.Clear();
        countedMobIds.Clear();
        cachedInstanceCount = 0;
        cachedCurrentInstance = 0;
        if (!P.Config.Enabled || !P.Config.AutoInstance || territory == 0) return;

        // 首次进入可切副本区的地图 → 保证 1 号副本区
        if (!ensuredTerritories.Contains(territory) && S.LifestreamIPC.GetInstanceCount() > 1)
        {
            ensuredTerritories.Add(territory);
            pendingEnsureInstanceOne = true;
        }
    }

    /// <summary>外部请求保证 1 号副本区（跨图传送到达后）。</summary>
    public static void RequestEnsureInstanceOne() => pendingEnsureInstanceOne = true;

    /// <summary>取出待切换的副本区号（并清空等待状态）。</summary>
    public static int ConsumePendingSwitch()
    {
        var n = pendingSwitchInstance;
        pendingSwitchInstance = 0;
        return n;
    }

    public static void Update()
    {
        if (S.LifestreamIPC == null) return;

        // 副本区变化时重置击杀计数（切换副本区/进入新地图）
        // 注意：登录/读图瞬间 UIState.Instance() 可能为 null，必须判空
        var uiState = FFXIVClientStructs.FFXIV.Client.Game.UI.UIState.Instance();
        if (uiState != null)
        {
            var instId = uiState->PublicInstance.InstanceId;
            if (instId != lastInstanceId)
            {
                lastInstanceId = instId;
                killCount = 0;
                countedMobIds.Clear();
                engagedMobIds.Clear();
            }
        }

        // 扫描战斗对象，统计玩家参与击杀的怪物（250ms 一次足够）
        if (EzThrottler.Throttle("WYKillScan", 250))
        {
            ScanKills();
        }

        // 定期刷新副本区信息缓存（供 UI 显示）
        if (EzThrottler.Throttle("WYInstanceCache", 2000))
        {
            cachedInstanceCount = S.LifestreamIPC.GetInstanceCount();
            cachedCurrentInstance = S.LifestreamIPC.GetCurrentInstanceNumber();
        }

        if (!pendingEnsureInstanceOne) return;
        if (P.TaskManager.IsBusy) return;
        if (!Player.Interactable || !IsScreenReady()) return;
        if (Svc.Condition[ConditionFlag.BetweenAreas] || Svc.Condition[ConditionFlag.BetweenAreas51]) return;

        pendingEnsureInstanceOne = false;
        if (S.LifestreamIPC.GetInstanceCount() > 1 && S.LifestreamIPC.GetCurrentInstanceNumber() != 1)
        {
            Notify.Info("首次进入该地图，切换到 1 号副本区…");
            HuntController.Reset();
            TaskEnsureInstance.Enqueue(1);
        }
    }

    /// <summary>
    /// 扫描周围战斗对象：标记玩家参与的怪物，检测其死亡并计数。
    /// 参与判定（满足任一）：
    ///  - 怪物是自己的当前目标（我们正在打它）；
    ///  - 怪物的目标是玩家自己 / 队友 / 玩家的宠物或陆行鸟。
    /// </summary>
    private static void ScanKills()
    {
        if (!P.Config.Enabled || !Player.Available) return;

        var me = Player.Object;
        if (me == null) return;
        ulong myId = me.GameObjectId;

        // 收集「我方阵营」的对象 ID：自己 + 队友 + 自己的宠物/陆行鸟
        var allyIds = new HashSet<ulong> { myId };
        foreach (var member in Svc.Party)
        {
            if (member.GameObject != null)
            {
                allyIds.Add(member.GameObject.GameObjectId);
            }
        }
        foreach (var obj in Svc.Objects)
        {
            if (obj is IBattleNpc pet && pet.OwnerId == myId)
            {
                allyIds.Add(pet.GameObjectId);
            }
        }

        ulong myTargetId = Svc.Targets.Target?.GameObjectId ?? 0;

        foreach (var obj in Svc.Objects)
        {
            if (obj is not IBattleNpc npc) continue;

            if (!npc.IsDead)
            {
                // 存活：判断是否为玩家参与的战斗对象
                bool engaged = npc.GameObjectId == myTargetId
                    || allyIds.Contains(npc.TargetObjectId);
                if (engaged) engagedMobIds.Add(npc.GameObjectId);
            }
            else
            {
                // 死亡：若之前参与过且未计数 → 计数
                if (engagedMobIds.Contains(npc.GameObjectId) && !countedMobIds.Contains(npc.GameObjectId))
                {
                    countedMobIds.Add(npc.GameObjectId);
                    OnMobKilled();
                }
            }
        }

        // 防止集合无限增长：定期清理已消失的对象
        if (countedMobIds.Count > 200)
        {
            countedMobIds.RemoveWhere(id => Svc.Objects.FirstOrDefault(o => o.GameObjectId == id) == null);
            engagedMobIds.RemoveWhere(id => !Svc.Objects.Any(o => o.GameObjectId == id));
        }
    }

    /// <summary>一只玩家参与的怪物被击杀后调用。</summary>
    public static void OnMobKilled()
    {
        killCount++;
        if (P.Config.Debug) PluginLog.Debug($"副本区击杀计数: {killCount}/{P.Config.KillsPerInstance}");

        if (!P.Config.AutoInstance) return;
        if (killCount < P.Config.KillsPerInstance) return;
        if (pendingSwitchInstance != 0) return; // 已在等待切换

        var count = S.LifestreamIPC.GetInstanceCount();
        if (count <= 1) return; // 当前地图不可切副本区

        killCount = 0;
        var current = S.LifestreamIPC.GetCurrentInstanceNumber();
        var next = current >= count ? 1 : current + 1;
        pendingSwitchInstance = next;

        Notify.Info($"已击杀 {P.Config.KillsPerInstance} 只怪，等待车头发送新坐标后切换到 {next} 号副本区…");
    }

    /// <summary>重置副本区记录（/ah reset）。</summary>
    public static void Reset()
    {
        ensuredTerritories.Clear();
        killCount = 0;
        pendingEnsureInstanceOne = false;
        pendingSwitchInstance = 0;
        engagedMobIds.Clear();
        countedMobIds.Clear();
    }
}
