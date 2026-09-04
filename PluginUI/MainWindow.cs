using ImGui = Dalamud.Bindings.ImGui.ImGui;
using Dalamud.Bindings.ImGui;
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
        // 顶部：一键创建怪物狩猎招募
        if (ImGui.Button("创建怪物狩猎招募", new Vector2(-1, 0)))
        {
            if (P.Config.PfinderEnable)
            {
                Tasks.TaskCreateHuntPF.Enqueue();
            }
            else
            {
                Notify.Error("一键创建招募按钮未启用，请在「招募」标签中开启。");
            }
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip($"一键创建队员招募（青魔占位: {(P.Config.BluPlaceholder ? "开" : "关")}，留言: {(P.Config.PfinderString.IsNullOrEmpty() ? "无" : P.Config.PfinderString)}）");
        }
        ImGui.Separator();

        if (ImGui.BeginTabBar("AutoHuntTabs"))
        {
            if (ImGui.BeginTabItem("基本"))
            {
                DrawBasicTab();
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("招募"))
            {
                DrawRecruitTab();
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("跨区"))
            {
                DrawCrossRegionTab();
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

    private void DrawRecruitTab()
    {
        if (ImGui.Checkbox("启用一键创建队员招募", ref P.Config.PfinderEnable)) EzConfig.Save();
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("启用后，设置窗口顶部的「创建怪物狩猎招募」按钮可用");

        if (ImGui.Checkbox("启用创建招募时设置青魔占位", ref P.Config.BluPlaceholder)) EzConfig.Save();
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("启用后，在创建队员招募时会自动设置青魔占位（选择青魔职业占位），并限定平均品级 531");

        ImGui.Separator();
        ImGui.Text("队员招募自由留言");
        ImGui.SetNextItemWidth(-1);
        var comment = P.Config.PfinderString ?? "";
        if (ImGui.InputText("##pfindercomment", ref comment, 150))
        {
            P.Config.PfinderString = comment;
            EzConfig.Save();
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("创建招募时自动填入招募留言，最长 2 行（约 50 个汉字）");
    }

    private static List<(uint Id, string Name)> cachedAetherytes;
    private static List<string> cachedDcWorlds;

    private static List<(uint Id, string Name)> GetAetherytesCached()
    {
        if (cachedAetherytes == null) cachedAetherytes = CrossRegionController.GetTeleportableAetherytes();
        return cachedAetherytes;
    }

    private static List<string> GetDcWorldsCached()
    {
        if (cachedDcWorlds == null) cachedDcWorlds = CrossRegionController.GetCurrentDcWorlds();
        return cachedDcWorlds;
    }

    private void DrawCrossRegionTab()
    {
        if (ImGui.Checkbox("启用跨区功能", ref P.Config.CrossRegionEnable)) EzConfig.Save();
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("启用后，取消车头时自动执行跨区流程：\n取消车头 → 传送到跨区前城市 → 到达狩猎时间表中本地时间的下一个时间点后跨区到对应大区 → 传送到跨区后水晶 →（可选）自动开启招募");

        ImGui.TextUnformatted($"跨区流程状态: {CrossRegionController.CurrentState}");
        ImGui.Separator();

        // ===== 跨区前传送到城市 =====
        ImGui.Text("跨区前传送到城市");
        ImGui.SetNextItemWidth(250);
        var cities = CrossRegionController.PreCities;
        var preIdx = Math.Clamp(P.Config.CrossRegionPreCity, 0, cities.Length - 1);
        if (ImGui.BeginCombo("##crosspre", cities[preIdx].Name))
        {
            for (var i = 0; i < cities.Length; i++)
            {
                if (ImGui.Selectable(cities[i].Name, i == preIdx))
                {
                    P.Config.CrossRegionPreCity = i;
                    EzConfig.Save();
                }
            }
            ImGui.EndCombo();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("只有利姆萨·罗敏萨下层甲板、格里达尼亚新街、乌尔达哈现世回廊这三个地方可以进行跨区操作，默认格里达尼亚新街");

        // ===== 跨区后传送到水晶 =====
        ImGui.Text("跨区后传送到水晶");
        ImGui.SetNextItemWidth(250);
        var aetherytes = GetAetherytesCached();
        var postId = P.Config.CrossRegionPostAetheryteId;
        var postName = postId == 0
            ? "不传送"
            : aetherytes.FirstOrDefault(a => a.Id == postId).Name ?? $"未知水晶 ({postId})";
        if (ImGui.BeginCombo("##crosspost", postName))
        {
            if (ImGui.Selectable("不传送", postId == 0))
            {
                P.Config.CrossRegionPostAetheryteId = 0;
                EzConfig.Save();
            }
            foreach (var a in aetherytes)
            {
                if (ImGui.Selectable(a.Name, a.Id == postId))
                {
                    P.Config.CrossRegionPostAetheryteId = a.Id;
                    EzConfig.Save();
                }
            }
            ImGui.EndCombo();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("跨区完成后的传送目的地；选择「不传送」则跨界后不进行传送操作");

        ImGui.Separator();

        // ===== 狩猎时间表 =====
        ImGui.Text("狩猎时间表");
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("取消车头后，将选择时间表中「本地时间的下一个」时间点对应的服务器进行跨区（列表为当前角色所在大区内的全部服务器）；到点前会停留在跨区前城市等待");
        if (ImGui.BeginTable("##crossschedule", 3, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
        {
            ImGui.TableSetupColumn("时间", ImGuiTableColumnFlags.WidthFixed, 100);
            ImGui.TableSetupColumn("大区", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("操作", ImGuiTableColumnFlags.WidthFixed, 70);
            ImGui.TableHeadersRow();

            var schedule = P.Config.CrossRegionSchedule;
            for (var i = 0; i < schedule.Count; i++)
            {
                var e = schedule[i];
                ImGui.PushID(i);
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                var time = e.Time ?? "";
                ImGui.SetNextItemWidth(-1);
                if (ImGui.InputText("##time", ref time, 4, ImGuiInputTextFlags.CharsDecimal))
                {
                    e.Time = time;
                    EzConfig.Save();
                }
                if (ImGui.IsItemHovered())
                {
                    var parsed = CrossRegionController.ParseHHMM(e.Time);
                    ImGui.SetTooltip(parsed == null
                        ? "本地时间，HHMM 格式（仅数字），如 0930 表示 09:30"
                        : $"{parsed.Value / 60:00}:{parsed.Value % 60:00}");
                }

                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(-1);
                var worlds = GetDcWorldsCached();
                var comboLabel = string.IsNullOrEmpty(e.World) ? "请选择服务器" : e.World;
                if (worlds.Count == 0 && !string.IsNullOrEmpty(e.World)) comboLabel += "（当前大区列表不可用）";
                if (ImGui.BeginCombo("##dc", comboLabel))
                {
                    if (worlds.Count == 0)
                    {
                        ImGui.TextDisabled("无法读取当前大区的服务器列表");
                        ImGui.TextDisabled("请登录角色后再编辑时间表");
                    }
                    else
                    {
                        foreach (var w in worlds)
                        {
                            if (ImGui.Selectable(w, w == e.World))
                            {
                                e.World = w;
                                EzConfig.Save();
                            }
                        }
                    }
                    ImGui.EndCombo();
                }
                if (worlds.Count > 0 && ImGui.IsItemHovered())
                    ImGui.SetTooltip("服务器列表取自角色当前所在大区；切换大区后重新打开本页面，列表会随之更新");

                ImGui.TableNextColumn();
                if (ImGui.SmallButton("删除"))
                {
                    schedule.RemoveAt(i);
                    EzConfig.Save();
                    ImGui.PopID();
                    continue;
                }
                ImGui.PopID();
            }
            ImGui.EndTable();
        }
        if (ImGui.Button("新增车次", new Vector2(150, 0)))
        {
            P.Config.CrossRegionSchedule.Add(new CrossRegionScheduleEntry());
            EzConfig.Save();
        }

        ImGui.Separator();

        // ===== 自动开启招募 =====
        if (ImGui.Checkbox("自动开启招募", ref P.Config.CrossRegionAutoPF)) EzConfig.Save();
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("跨区流程完成后，按照「招募」标签内配置的信息（留言、青魔占位）自动开启队员招募。\n注意：需同时开启「招募」标签中的「启用一键创建队员招募」，否则只会提示而不会自动开启招募。");
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
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("接近车头坐标后，悬停点相对目标坐标地面高度的上移量（防止贴地卡地形/引怪、悬停过高选不到目标）；不可飞地图自动忽略");

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
