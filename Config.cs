namespace AutoHunt;

public class Config : IEzConfig
{
    /// <summary>插件总开关</summary>
    public bool Enabled = true;

    /// <summary>车头玩家名称</summary>
    public string ConductorName = "";

    /// <summary>车头玩家所属世界（服务器）ID，0 表示不校验</summary>
    public uint ConductorWorldId = 0;

    /// <summary>自动输出</summary>
    public bool AutoAttack = true;

    /// <summary>将 B 级狩猎怪也作为目标（默认仅 A/S 级，狩猎车场景 B 级常驻刷新易误选）</summary>
    public bool IncludeBRank = false;

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

    /// <summary>跨区完成后自动开启队员招募（需同时启用「启用一键创建队员招募」）</summary>
    public bool CrossRegionAutoPF = false;

    /// <summary>狩猎时间表（时间 HHMM + 服务器名）</summary>
    public List<CrossRegionScheduleEntry> CrossRegionSchedule = new();

    /// <summary>调试模式</summary>
    public bool Debug = false;
}

/// <summary>狩猎时间表条目：本地时间 HHMM + 目标服务器（世界名，取当前大区内的服务器）。</summary>
public class CrossRegionScheduleEntry
{
    /// <summary>时间，HHMM 格式（仅数字），如 0930</summary>
    public string Time = "";

    /// <summary>目标服务器（世界）名称</summary>
    public string World = "";
}
