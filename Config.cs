namespace AutoHunt;

public class Config : IEzConfig
{
    /// <summary>插件总开关</summary>
    public bool Enabled = false; // 默认关闭：启动游戏后需手动开启总开关

    // ===== 车头（多车头） =====

    /// <summary>车头列表：任一车头发送坐标都会触发狩猎流程</summary>
    public List<ConductorEntry> Conductors = new();

    /// <summary>旧版单车头名字（仅用于配置迁移，迁移后置空不再使用）</summary>
    public string ConductorName = "";

    /// <summary>旧版单车头世界 ID（仅用于配置迁移）</summary>
    public uint ConductorWorldId = 0;

    /// <summary>悬浮窗显示开关</summary>
    public bool FloatingWindowEnable = true;

    /// <summary>悬浮窗屏幕位置 X（-1=默认位置）</summary>
    public float FloatingWindowX = -1f;

    /// <summary>悬浮窗屏幕位置 Y（-1=默认位置）</summary>
    public float FloatingWindowY = -1f;

    /// <summary>自动输出</summary>
    public bool AutoAttack = true;

    /// <summary>将 B 级狩猎怪也作为目标（默认仅 A/S 级，狩猎车场景 B 级常驻刷新易误选）</summary>
    public bool IncludeBRank = false;

    /// <summary>启用狩猎怪出生点辅助：车头坐标命中数据库出生点时，前往出生点等待并监控狩猎怪</summary>
    public bool UseSpawnPoints = true;

    /// <summary>车头坐标与出生点的匹配半径（米）</summary>
    public float SpawnMatchRadius = 100f;

    /// <summary>自动切换副本区</summary>
    public bool AutoInstance = true;

    /// <summary>上坐骑（vnavmesh 寻路前）</summary>
    public bool UseMount = true;

    /// <summary>使用 /vnav flyflag 飞向地图旗标寻路（推荐）；关闭则用 IPC 直接寻路到坐标+Z偏移</summary>
    public bool UseFlyFlag = true;

    /// <summary>飞向地图旗标的命令</summary>
    public string FlyFlagCommand = "/vnav flyflag";

    /// <summary>指定坐骑名称，留空使用随机坐骑</summary>
    public string MountName = "";

    /// <summary>收到车头坐标时自动打开地图插旗</summary>
    public bool AutoOpenMap = true;

    /// <summary>解析纯文本坐标（如 12.3, 45.6）</summary>
    public bool ParseTextCoordinates = true;

    /// <summary>同地图传送距离阈值：自己与目标距离减去最近水晶到目标距离大于此值时传送（单位：米）</summary>
    public float TeleportDistanceThreshold = 100f;

    /// <summary>接近目标坐标后，悬停点相对目标坐标地面高度的上移量（防止贴地卡地形/引怪，单位：米；不可飞地图自动忽略；附近无参照对象时回退为相对当前飞行高度）</summary>
    public float ZOffset = 30f;

    /// <summary>怪物血量低于此百分比时自动下坐骑开始输出（0-100）</summary>
    public float DismountHpPercent = 70f;

    /// <summary>每击杀多少只怪切换一次副本区</summary>
    public int KillsPerInstance = 2;

    /// <summary>开始输出的命令</summary>
    public string RotationStartCommand = "/rotation Manual";

    /// <summary>停止输出的命令</summary>
    public string RotationStopCommand = "/rotation off";

    // ===== 招募（队员招募） =====

    /// <summary>启用一键创建队员招募按钮（设置窗口顶部按钮）</summary>
    public bool PfinderEnable = true;

    /// <summary>创建招募时设置青魔占位（自动勾选平均品级限制并设为 531 + 青魔职业占位）</summary>
    public bool BluPlaceholder = false;

    /// <summary>队员招募自由留言（创建招募时自动填入，最长 2 行 / 191 字节）</summary>
    public string PfinderString = "";

    // ===== 跨区（数据中心传送） =====

    /// <summary>启用跨区功能（取消车头后按狩猎时间表自动跨区）</summary>
    public bool CrossRegionEnable = false;

    /// <summary>跨区前传送到的城市：0=格里达尼亚新街（默认）1=利姆萨·罗敏萨下层甲板 2=乌尔达哈现世回廊</summary>
    public int CrossRegionPreCity = 0;

    /// <summary>跨区后传送到的水晶 ID（Aetheryte RowId），0 表示不传送</summary>
    public uint CrossRegionPostAetheryteId = 0;

    /// <summary>跨区完成后自动获取车头（读取队员招募-怪物狩猎中的招募人并设为车头）</summary>
    public bool CrossRegionAutoFetchConductor = false;

    /// <summary>自动取消车头：结束地图击杀满后自动取消全部车头</summary>
    public bool CrossRegionAutoCancelConductor = false;

    /// <summary>结束地图水晶 ID（Aetheryte RowId），0=未选择（自动取消不生效）</summary>
    public uint CrossRegionEndAetheryteId = 0;

    /// <summary>跨区完成后自动开启队员招募（需同时启用「启用一键创建队员招募」）</summary>
    public bool CrossRegionAutoPF = false;

    /// <summary>狩猎时间表（时间 HHMM + 服务器名）</summary>
    public List<CrossRegionScheduleEntry> CrossRegionSchedule = new();

    /// <summary>调试模式</summary>
    public bool Debug = false;

    /// <summary>
    /// 旧版单车头配置迁移：Conductors 为空且旧字段非空时转换，迁移后清空旧字段。
    /// 在 EzConfig.Init 之后调用一次。
    /// </summary>
    public void MigrateLegacyConductor()
    {
        if (Conductors.Count == 0 && !string.IsNullOrWhiteSpace(ConductorName))
        {
            Conductors.Add(new ConductorEntry { Name = ConductorName.Trim(), WorldId = ConductorWorldId });
            Notify.Info($"已将旧版车头「{ConductorName.Trim()}」迁移到多车头列表。");
        }
        ConductorName = "";
        ConductorWorldId = 0;
    }
}

/// <summary>车头玩家条目：任一车头的聊天坐标都会触发狩猎流程。</summary>
public class ConductorEntry
{
    /// <summary>角色名（不带 @世界服 后缀）</summary>
    public string Name = "";

    /// <summary>所属世界（服务器）RowId，0=不校验世界服</summary>
    public uint WorldId = 0;
}

/// <summary>狩猎时间表条目：本地时间 HHMM + 目标服务器（世界名，取当前大区内的服务器）。</summary>
public class CrossRegionScheduleEntry
{
    /// <summary>时间，HHMM 格式（仅数字），如 0930</summary>
    public string Time = "";

    /// <summary>目标服务器（世界）名称</summary>
    public string World = "";
}
