using System.Numerics;
using Lumina.Excel.Sheets;

namespace AutoHunt;

/// <summary>
/// 传送到达数据：用于标记传送后需要执行的操作（如切换副本区）。
/// </summary>
internal class ArrivalData
{
    /// <summary>目标水晶</summary>
    public Aetheryte? Aetheryte;
    /// <summary>目标地图 TerritoryType</summary>
    public uint Territory;
    /// <summary>到达后需要切换到的副本区号，0 = 不切区</summary>
    public int SwitchInstance = 0;
}

/// <summary>
/// 坐标目标数据：解析到的车头发送坐标，用于传送/寻路/输出流程。
/// </summary>
internal class TargetPosition
{
    /// <summary>目标地图 TerritoryType RowId</summary>
    public uint TerritoryId;
    /// <summary>目标世界坐标（XZ 平面）</summary>
    public Vector2 WorldXZ;
    /// <summary>目标 Y 高度（由 ZOffset 计算后的实际寻路高度）</summary>
    public float WorldY;
    /// <summary>该坐标对应的水晶（用于传送判断）</summary>
    public Aetheryte? NearestAetheryte;
    /// <summary>水晶名称（用于提示）</summary>
    public string AetheryteName = "";

    public static TargetPosition? CreateOrNull(uint territoryId, Vector2 worldXZ, Aetheryte? aetheryte, string aetheryteName)
    {
        if (territoryId == 0) return null;
        var tp = new TargetPosition
        {
            TerritoryId = territoryId,
            WorldXZ = worldXZ,
            NearestAetheryte = aetheryte,
            AetheryteName = aetheryteName,
        };
        // 默认 Y = 玩家当前 Y + ZOffset（寻路时会重新计算）
        tp.WorldY = Player.Available ? Player.Position.Y + P.Config.ZOffset : worldXZ.Y + P.Config.ZOffset;
        return tp;
    }

    public Vector3 Position => new(WorldXZ.X, WorldY, WorldXZ.Y);
}
