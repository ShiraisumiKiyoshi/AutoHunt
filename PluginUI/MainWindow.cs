using ImGui = Dalamud.Bindings.ImGui.ImGui;
using ECommons.SimpleGui;

namespace AutoHunt.PluginUI;

/// <summary>
/// 插件设置界面。
/// </summary>
public class MainWindow : ConfigWindow
{
    public MainWindow() : base("AutoHunt 设置")
    {
    }

    public override void Draw()
    {
        if (ImGui.BeginTabBar("AutoHuntTabs"))
        {
            if (ImGui.BeginTabItem("基本"))
            {
                DrawBasicTab();
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("高级"))
            {
                DrawAdvancedTab();
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("状态"))
            {
                DrawStatusTab();
                ImGui.EndTabItem();
            }
            ImGui.EndTabBar();
        }
    }

    private void DrawBasicTab()
    {
        var selfMode = Conductor.IsValid && Player.Available && P.Config.ConductorName == Player.Object.Name.TextValue;
        ImGui.Text($"车头: {(Conductor.IsValid ? P.Config.ConductorName + (selfMode ? "（自己）" : "") : "未设置")}");
        if (Conductor.IsValid && ImGui.Button("取消车头"))
        {
            Conductor.Clear();
        }
        if (selfMode && ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("自己即车头：你在任意频道（含 /echo）发送坐标都会触发自动狩猎");
        }
        ImGui.Separator();

        if (ImGui.Checkbox("插件总开关", ref P.Config.Enabled)) EzConfig.Save();
        if (ImGui.Checkbox("自动输出（/rotation Manual）", ref P.Config.AutoAttack)) EzConfig.Save();
        if (ImGui.Checkbox("自动切换副本区", ref P.Config.AutoInstance)) EzConfig.Save();
        if (ImGui.Checkbox("包含 B 级狩猎怪", ref P.Config.IncludeBRank)) EzConfig.Save();
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("默认仅锁定 A/S 级狩猎怪（按游戏数据表判定，零误判）；勾选后 B 级也作为目标");

        ImGui.Separator();
        ImGui.Text("狩猎流程参数");
        ImGui.SetNextItemWidth(200);
        if (ImGui.SliderFloat("传送距离阈值 (米)", ref P.Config.TeleportDistanceThreshold, 10f, 300f)) EzConfig.Save();
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("自己与目标距离减去水晶到目标距离大于此值时传送");

        ImGui.SetNextItemWidth(200);
        if (ImGui.SliderFloat("悬停高度偏移 (米)", ref P.Config.ZOffset, 0f, 100f)) EzConfig.Save();
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("接近车头坐标后，悬停点相对当前飞行高度的上移量（防止贴地卡地形/引怪）；不可飞地图自动忽略");

        ImGui.SetNextItemWidth(200);
        if (ImGui.SliderFloat("下坐骑血量 (%)", ref P.Config.DismountHpPercent, 10f, 100f)) EzConfig.Save();
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("怪物血量低于此百分比时自动下坐骑开始输出");

        ImGui.SetNextItemWidth(200);
        if (ImGui.SliderInt("每击杀几只怪切换副本区", ref P.Config.KillsPerInstance, 1, 10)) EzConfig.Save();

        ImGui.Separator();
        if (ImGui.Checkbox("上坐骑寻路", ref P.Config.UseMount)) EzConfig.Save();
        ImGui.SetNextItemWidth(200);
        if (ImGui.InputText("指定坐骑名称（留空随机）", ref P.Config.MountName, 64)) EzConfig.Save();
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("留空则使用游戏默认随机坐骑");
    }

    private void DrawAdvancedTab()
    {
        if (ImGui.Checkbox("收到坐标时自动打开地图插旗", ref P.Config.AutoOpenMap)) EzConfig.Save();
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("车头发送坐标时打开地图并在坐标处插旗");
        if (ImGui.Checkbox("解析纯文本坐标", ref P.Config.ParseTextCoordinates)) EzConfig.Save();
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("解析聊天中的纯文本坐标，如 12.3, 45.6");

        ImGui.Separator();
        if (ImGui.Checkbox("使用 /vnav flyflag 飞向旗标（推荐）", ref P.Config.UseFlyFlag)) EzConfig.Save();
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("开启后：插旗→上坐骑→执行飞旗命令，由 vnavmesh 飞向地图旗标；关闭则用 IPC 直接寻路到坐标+Z偏移");
        ImGui.SetNextItemWidth(300);
        if (ImGui.InputText("飞旗命令", ref P.Config.FlyFlagCommand, 64)) EzConfig.Save();

        ImGui.Separator();
        ImGui.SetNextItemWidth(300);
        if (ImGui.InputText("开始输出命令", ref P.Config.RotationStartCommand, 128)) EzConfig.Save();
        ImGui.SetNextItemWidth(300);
        if (ImGui.InputText("停止输出命令", ref P.Config.RotationStopCommand, 128)) EzConfig.Save();

        ImGui.Separator();
        if (ImGui.Checkbox("调试模式", ref P.Config.Debug)) EzConfig.Save();
    }

    private void DrawStatusTab()
    {
        var selfMode2 = Conductor.IsValid && Player.Available && P.Config.ConductorName == Player.Object.Name.TextValue;
        ImGui.TextUnformatted($"车头: {(Conductor.IsValid ? P.Config.ConductorName + (selfMode2 ? "（自己）" : "") : "未设置")}");
        ImGui.TextUnformatted($"车头焦点: {(Svc.Targets.FocusTarget?.Name.TextValue == P.Config.ConductorName ? "正常" : "已断开")}");

        ImGui.Separator();
        ImGui.TextUnformatted("狩猎状态:");
        ImGui.TextUnformatted($"  当前状态: {HuntController.CurrentState}");
        var rank = HuntController.CurrentTargetRank;
        ImGui.TextUnformatted($"  目标怪物: {(string.IsNullOrEmpty(HuntController.CurrentTargetName) ? "无" : string.IsNullOrEmpty(rank) ? HuntController.CurrentTargetName : $"[{rank}] {HuntController.CurrentTargetName}")}");
        ImGui.TextUnformatted($"  目标血量: {HuntController.CurrentTargetHpPercent:0}%");
        ImGui.TextUnformatted($"  狩猎怪库: {HuntMobDatabase.RankMap.Count} 只已加载");

        ImGui.Separator();
        ImGui.TextUnformatted("副本区状态:");
        ImGui.TextUnformatted($"  当前副本区: {InstanceController.CachedCurrentInstance}/{InstanceController.CachedInstanceCount}");
        ImGui.TextUnformatted($"  本区击杀数: {InstanceController.KillCount}/{P.Config.KillsPerInstance}");
        if (InstanceController.PendingSwitchInstance > 0)
        {
            ImGui.TextColored(new Vector4(1f, 0.8f, 0.2f, 1f),
                $"  待切换: {InstanceController.PendingSwitchInstance} 号副本区（等待车头新坐标）");
        }

        ImGui.Separator();
        ImGui.TextUnformatted("依赖插件:");
        foreach (var (label, feature, installed, required) in DependencyChecker.GetDependencyStatus())
        {
            var color = installed
                ? new Vector4(0.35f, 0.85f, 0.45f, 1f)
                : required
                    ? new Vector4(1f, 0.35f, 0.35f, 1f)
                    : new Vector4(1f, 0.8f, 0.2f, 1f);
            ImGui.TextColored(color, $"  {(installed ? "√" : "×")} {label}{(required ? "（必需）" : "")} — {feature}");
        }
    }
}
