using ImGui = Dalamud.Bindings.ImGui.ImGui;
using Dalamud.Bindings.ImGui;
using ECommons.SimpleGui;

namespace AutoHunt.PluginUI;

/// <summary>
/// 插件设置界面（v2.3.0 重构）：指标卡摘要栏 + 当前操作条 + 卡片分组 + 标签重排。
/// </summary>
public class MainWindow : ConfigWindow
{
    // ===== 配色 =====
    private static uint CardBg => ImGui.ColorConvertFloat4ToU32(new(0.42f, 0.50f, 0.62f, 0.10f));
    private static uint ColGreen => ImGui.ColorConvertFloat4ToU32(new(0.31f, 0.82f, 0.48f, 1f));
    private static uint ColAmber => ImGui.ColorConvertFloat4ToU32(new(0.94f, 0.71f, 0.36f, 1f));
    private static uint ColRed => ImGui.ColorConvertFloat4ToU32(new(0.89f, 0.36f, 0.36f, 1f));
    private static uint ColCyan => ImGui.ColorConvertFloat4ToU32(new(0.36f, 0.78f, 0.96f, 1f));
    private static uint ColPurple => ImGui.ColorConvertFloat4ToU32(new(0.71f, 0.54f, 0.96f, 1f));
    private static uint ColGray => ImGui.ColorConvertFloat4ToU32(new(0.58f, 0.61f, 0.67f, 1f));

    public MainWindow() : base("AutoHunt 设置")
    {
    }

    public override void Draw()
    {
        DrawMetricCards();
        DrawMainButtons();
        DrawOperationStrip();
        ImGui.Spacing();

        if (ImGui.BeginTabBar("AutoHuntTabs"))
        {
            if (ImGui.BeginTabItem("状态"))
            {
                DrawStatusTab();
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("狩猎"))
            {
                DrawHuntTab();
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
            ImGui.EndTabBar();
        }
    }

    // ===== 顶部：指标卡 =====

    private void DrawMetricCards()
    {
        var avail = ImGui.GetContentRegionAvail().X;
        var w = (avail - 24f) / 4f;

        // 车头
        BeginCard(w);
        ImGui.TextDisabled("车头");
        var online = Conductor.FindNearest() != null;
        ImGui.TextColored(online ? ColGreen : ColGray, online ? "●" : "○");
        ImGui.SameLine();
        ImGui.Text($"{P.Config.Conductors.Count} 人");
        EndCard();
        ImGui.SameLine();

        // 狩猎状态
        var (kind, text) = OperationTracker.Current;
        BeginCard(w);
        ImGui.TextDisabled("狩猎状态");
        var huntText = kind == OperationTracker.Kind.Hunt || kind == OperationTracker.Kind.Move
            ? Shorten(text["狩猎 — ".Length..])
            : "待机";
        ImGui.TextColored(kind == OperationTracker.Kind.Hunt ? ColCyan : ColGray, huntText);
        EndCard();
        ImGui.SameLine();

        // 本区击杀
        BeginCard(w);
        ImGui.TextDisabled("本区击杀");
        ImGui.TextColored(InstanceController.ZoneCleared ? ColAmber : ColGreen,
            $"{InstanceController.KillCount} / {P.Config.KillsPerInstance}");
        EndCard();
        ImGui.SameLine();

        // 副本区
        BeginCard(w);
        ImGui.TextDisabled("副本区");
        ImGui.TextUnformatted($"{InstanceController.CachedCurrentInstance} / {InstanceController.CachedInstanceCount}");
        EndCard();
    }

    private static string Shorten(string s)
    {
        var i = s.IndexOf('(');
        return i > 0 ? s[..i].Trim() : s;
    }

    // ===== 顶部：主操作按钮 =====

    private void DrawMainButtons()
    {
        ImGui.Spacing();
        if (ButtonColored("自动获取车头", new(0.16f, 0.25f, 0.43f, 1f)))
        {
            ConductorFetchService.Enqueue();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("读取队员招募 → 全部 → 怪物狩猎中的全部招募人，设为车头（去重、替换现有列表、跳过自己）");
        ImGui.SameLine();
        if (ButtonColored("创建怪物狩猎招募", new(0.16f, 0.25f, 0.43f, 1f)))
        {
            if (P.Config.PfinderEnable)
                Tasks.TaskCreateHuntPF.Enqueue();
            else
                Notify.Error("一键创建招募按钮未启用，请在「招募」标签中开启。");
        }
        ImGui.SameLine();
        if (ButtonColored("全部取消车头", new(0.30f, 0.16f, 0.22f, 1f)))
        {
            Conductor.ClearAll();
        }
        ImGui.Spacing();
    }

    private static bool ButtonColored(string label, Vector4 bg)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, bg);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, bg with { W = Math.Min(1f, bg.W + 0.15f) });
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, bg with { W = Math.Min(1f, bg.W + 0.25f) });
        var w = ImGui.CalcTextSize(label).X + ImGui.GetStyle().FramePadding.X * 4;
        var clicked = ImGui.Button(label, new(w, 0));
        ImGui.PopStyleColor(3);
        return clicked;
    }

    // ===== 顶部：当前操作条 =====

    private void DrawOperationStrip()
    {
        var (kind, text) = OperationTracker.Current;
        var col = OperationTrackerUi.KindColor(kind);
        BeginCard();
        ImGui.TextColored(col, "●");
        ImGui.SameLine();
        ImGui.TextColored(col, OperationTrackerUi.KindTag(kind));
        ImGui.SameLine();
        ImGui.TextUnformatted("—");
        ImGui.SameLine();
        ImGui.TextUnformatted(text);

        // 跨区阶段进度链
        var (names, cur) = CrossRegionController.PhaseSteps;
        if (cur >= 0)
        {
            ImGui.SameLine();
            ImGui.TextColored(ColGray, "｜");
            ImGui.SameLine();
            for (var i = 0; i < names.Length; i++)
            {
                if (i > 0) ImGui.SameLine();
                var mark = i < cur ? "✓" : i == cur ? "▶" : "○";
                var c = i < cur ? ColGreen : i == cur ? ColCyan : ColGray;
                ImGui.TextColored(c, $"{mark}{names[i]}");
            }
        }
        EndCard();
    }

    // ===== 状态页 =====

    private void DrawStatusTab()
    {
        BeginCard();
        ImGui.TextUnformatted("当前操作");
        ImGui.SameLine(GetContentRight(120));
        ImGui.TextColored(ColGray, $"车头焦点: {(Svc.Targets.FocusTarget != null && Conductor.IsConductor(Svc.Targets.FocusTarget.Name.TextValue) ? "正常" : "已断开")}");
        ImGui.Indent(12);
        ImGui.TextUnformatted("· 车头: " + (Conductor.IsValid
            ? string.Join("、", P.Config.Conductors.Select(c => c.Name)) + (IsSelfConductor() ? "（含自己）" : "")
            : "未设置"));
        var (kind, opText) = OperationTracker.Current;
        ImGui.TextColored(OperationTrackerUi.KindColor(kind), $"· 当前操作: {OperationTrackerUi.KindTag(kind)} — {opText}");
        ImGui.Unindent(12);
        EndCard();

        BeginCard();
        ImGui.TextUnformatted("狩猎");
        ImGui.SameLine(GetContentRight(120));
        ImGui.TextColored(ColGray, $"狩猎怪库: {HuntMobDatabase.RankMap.Count} 只已加载");
        ImGui.Indent(12);
        var rank = HuntController.CurrentTargetRank;
        ImGui.TextUnformatted($"· 目标怪物: {(string.IsNullOrEmpty(HuntController.CurrentTargetName) ? "无" : string.IsNullOrEmpty(rank) ? HuntController.CurrentTargetName : $"[{rank}] {HuntController.CurrentTargetName}")}");
        ImGui.TextUnformatted($"· 目标血量: {HuntController.CurrentTargetHpPercent:0}%");
        if (HuntController.SpawnMatched)
            ImGui.TextColored(ColCyan, "· 出生点辅助: 命中，前往出生点等待/监控中");
        ImGui.Unindent(12);
        EndCard();

        BeginCard();
        ImGui.TextUnformatted("副本区");
        ImGui.Indent(12);
        ImGui.TextUnformatted($"· 当前副本区: {InstanceController.CachedCurrentInstance}/{InstanceController.CachedInstanceCount}");
        ImGui.TextColored(InstanceController.ZoneCleared ? ColAmber : ColGray,
            $"· 本区击杀数: {InstanceController.KillCount}/{P.Config.KillsPerInstance}{(InstanceController.ZoneCleared ? "（已满）" : "")}");
        if (InstanceController.PendingSwitchInstance > 0)
            ImGui.TextColored(ColAmber, $"· 待切换: {InstanceController.PendingSwitchInstance} 号副本区（等待车头新坐标）");
        ImGui.Unindent(12);
        EndCard();

        BeginCard();
        ImGui.TextUnformatted("跨区流程");
        ImGui.SameLine(GetContentRight(80));
        ImGui.TextColored(CrossRegionController.Active ? ColPurple : ColGray, CrossRegionController.CurrentState);
        ImGui.Indent(12);
        ImGui.TextUnformatted($"· 结束地图自动取消车头: {(P.Config.CrossRegionAutoCancelConductor ? (P.Config.CrossRegionEndAetheryteId == 0 ? "未选择结束地图（不生效）" : "已开启") : "未开启")}");
        ImGui.Unindent(12);
        EndCard();

        DrawHuntScanSection();

        BeginCard();
        ImGui.TextUnformatted("依赖插件");
        ImGui.Indent(12);
        foreach (var (label, feature, installed, required) in DependencyChecker.GetDependencyStatus())
        {
            var color = installed ? ColGreen : required ? ColRed : ColAmber;
            ImGui.TextColored(color, $"  {(installed ? "√" : "×")} {label}{(required ? "（必需）" : "")} — {feature}");
        }
        ImGui.Unindent(12);
        EndCard();
    }

    private static bool IsSelfConductor() =>
        Player.Available && Conductor.IsConductor(Player.Object.Name.TextValue);

    // ===== 狩猎页 =====

    private string manualConductorName = "";

    private void DrawHuntTab()
    {
        // 车头卡片
        BeginCard();
        ImGui.TextUnformatted($"车头（{P.Config.Conductors.Count}）");
        ImGui.SameLine(GetContentRight(10));
        ImGui.TextColored(ColGray, "任一车头发送坐标都会触发狩猎");
        ImGui.Indent(12);

        var list = P.Config.Conductors;
        for (var i = 0; i < list.Count; i++)
        {
            var c = list[i];
            ImGui.PushID(i);
            var pc = Conductor.FindByName(c.Name);
            ImGui.TextColored(pc != null ? ColGreen : ColGray, pc != null ? "●" : "○");
            ImGui.SameLine();
            ImGui.TextUnformatted(c.Name);
            ImGui.SameLine(220);
            ImGui.TextColored(ColGray, WorldName(c.WorldId) + (pc != null ? " · 在附近" : " · 未在附近"));
            ImGui.SameLine(GetContentRight(4));
            if (ImGui.SmallButton("取消"))
            {
                Conductor.Remove(c.Name);
                ImGui.PopID();
                break; // 列表被修改，跳过本帧剩余行
            }
            ImGui.PopID();
        }
        if (list.Count == 0) ImGui.TextColored(ColGray, "  未设置车头：右键聊天玩家名「设为车头」，或点击「自动获取车头」");

        ImGui.Spacing();
        if (ButtonColored("自动获取车头", new(0.16f, 0.25f, 0.43f, 1f))) ConductorFetchService.Enqueue();
        ImGui.SameLine();
        ImGui.SetNextItemWidth(160);
        var name = manualConductorName ?? "";
        if (ImGui.InputTextWithHint("##manualconductor", "手动添加车头名", ref name, 32))
            manualConductorName = name;
        ImGui.SameLine();
        if (ImGui.SmallButton("添加") && !manualConductorName.IsNullOrEmpty())
        {
            Conductor.Add(manualConductorName);
            manualConductorName = "";
        }
        ImGui.SameLine();
        if (ImGui.SmallButton("全部取消") && Conductor.IsValid) Conductor.ClearAll();
        ImGui.Unindent(12);
        EndCard();

        // 功能开关卡片
        BeginCard();
        ImGui.TextUnformatted("功能开关");
        ImGui.Indent(12);
        ToggleRow("插件总开关", null, "##tEnabled", ref P.Config.Enabled);
        ToggleRow("自动输出（/rotation Manual）", null, "##tAutoAttack", ref P.Config.AutoAttack);
        ToggleRow("自动切换副本区", null, "##tAutoInstance", ref P.Config.AutoInstance);
        ToggleRow("包含 B 级狩猎怪", "默认仅锁定 A/S 级（游戏数据表判定，零误判）", "##tIncludeB", ref P.Config.IncludeBRank);
        ToggleRow("启用狩猎怪出生点辅助", "车头坐标命中数据库出生点时，前往出生点等待并监控怪物", "##tSpawn", ref P.Config.UseSpawnPoints);
        if (P.Config.UseSpawnPoints)
        {
            ImGui.Indent(12);
            ImGui.SetNextItemWidth(220);
            if (ImGui.SliderFloat("出生点匹配半径 (米)", ref P.Config.SpawnMatchRadius, 30f, 300f)) EzConfig.Save();
            ImGui.Unindent(12);
        }
        ImGui.Unindent(12);
        EndCard();

        // 狩猎流程参数卡片（可折叠）
        if (ImGui.TreeNodeEx("流程参数"))
        {
            BeginCard();
            ImGui.TextUnformatted("狩猎流程参数");
            ImGui.Indent(12);
            ImGui.SetNextItemWidth(220);
            if (ImGui.SliderFloat("传送距离阈值 (米)", ref P.Config.TeleportDistanceThreshold, 10f, 300f)) EzConfig.Save();
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("自己与目标距离减去最近水晶到目标距离大于此值时传送");
            ImGui.SetNextItemWidth(220);
            if (ImGui.SliderFloat("悬停高度偏移 (米)", ref P.Config.ZOffset, 0f, 100f)) EzConfig.Save();
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("接近车头坐标后，悬停点相对目标坐标地面高度的上移量；不可飞地图自动忽略");
            ImGui.SetNextItemWidth(220);
            if (ImGui.SliderFloat("下坐骑血量 (%)", ref P.Config.DismountHpPercent, 10f, 100f)) EzConfig.Save();
            ImGui.SetNextItemWidth(220);
            if (ImGui.SliderInt("每击杀几只怪切换副本区", ref P.Config.KillsPerInstance, 1, 10)) EzConfig.Save();
            ToggleRow("上坐骑寻路", null, "##tMount", ref P.Config.UseMount);
            ImGui.SetNextItemWidth(220);
            if (ImGui.InputText("指定坐骑名称（留空随机）", ref P.Config.MountName, 64)) EzConfig.Save();
            ImGui.Unindent(12);
            EndCard();
            ImGui.TreePop();
        }
    }

    private static string WorldName(uint worldId)
    {
        if (worldId == 0) return "";
        try
        {
            return Svc.Data.GetExcelSheet<World>().GetRow(worldId).Name.ToString();
        }
        catch { return ""; }
    }

    // ===== 招募页 =====

    private void DrawRecruitTab()
    {
        BeginCard();
        ImGui.TextUnformatted("队员招募");
        ImGui.Indent(12);
        ToggleRow("启用一键创建队员招募", "启用后，主窗口「创建怪物狩猎招募」按钮可用", "##tPf", ref P.Config.PfinderEnable);
        ToggleRow("启用创建招募时设置青魔占位", "自动设置青魔职业占位并限定平均品级 531", "##tBlu", ref P.Config.BluPlaceholder);
        ImGui.TextUnformatted("队员招募自由留言");
        ImGui.SetNextItemWidth(-1);
        var comment = P.Config.PfinderString ?? "";
        if (ImGui.InputText("##pfindercomment", ref comment, 150))
        {
            P.Config.PfinderString = comment;
            EzConfig.Save();
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("创建招募时自动填入招募留言，最长 2 行（约 50 个汉字）");
        ImGui.Unindent(12);
        EndCard();
    }

    // ===== 跨区页 =====

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
        BeginCard();
        ImGui.TextUnformatted("跨区流程");
        ImGui.SameLine(GetContentRight(160));
        ImGui.TextColored(CrossRegionController.Active ? ColPurple : ColGray, $"状态: {CrossRegionController.CurrentState}");
        ImGui.Indent(12);
        ToggleRow("启用跨区功能",
            "取消车头 → 解散小队 → 传送到跨区前城市 → 立即跨区到下一车次服务器 → 传送到跨区后水晶 →（可选）获取车头 →（可选）自动开启招募",
            "##tCross", ref P.Config.CrossRegionEnable);

        ImGui.TextUnformatted("跨区前传送到城市");
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

        ImGui.TextUnformatted("跨区后传送到水晶");
        DrawAetheryteCombo("##crosspost", P.Config.CrossRegionPostAetheryteId,
            "跨区完成后的传送目的地；选择「不传送」则跨界后不进行传送操作",
            v => { P.Config.CrossRegionPostAetheryteId = v; EzConfig.Save(); });

        ToggleRow("跨区完成后自动获取车头",
            "传送到跨区后水晶之后，自动读取队员招募-怪物狩猎中的招募人并设为车头（已有车头时跳过）",
            "##tCrossFetch", ref P.Config.CrossRegionAutoFetchConductor);
        ToggleRow("自动取消车头（结束地图击杀满后）",
            "当前地图为结束地图且本区击杀数已满时，自动取消全部车头；跨区功能开启时自动衔接跨区流程",
            "##tCrossCancel", ref P.Config.CrossRegionAutoCancelConductor);
        ImGui.TextUnformatted("结束地图");
        DrawAetheryteCombo("##crossend", P.Config.CrossRegionEndAetheryteId,
            P.Config.CrossRegionAutoCancelConductor
                ? "自动取消车头的触发地图；当前地图与该水晶所在地图相同且击杀满时触发"
                : "先开启「自动取消车头」后此地图才会生效；选项与「跨区后传送到水晶」相同",
            v => { P.Config.CrossRegionEndAetheryteId = v; EzConfig.Save(); });
        if (P.Config.CrossRegionAutoCancelConductor && P.Config.CrossRegionEndAetheryteId == 0)
            ImGui.TextColored(ColAmber, "  未选择结束地图，自动取消车头不生效");

        ToggleRow("自动开启招募",
            "跨区流程完成后，按「招募」页配置自动创建队员招募（需同时开启「启用一键创建队员招募」）",
            "##tCrossPF", ref P.Config.CrossRegionAutoPF);
        ImGui.Unindent(12);
        EndCard();

        // 狩猎时间表（表格独立于卡片绘制，避免绘制通道冲突）
        ImGui.Spacing();
        ImGui.TextUnformatted("狩猎时间表");
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("取消车头后，到达跨区前城市立即跨区到时间表中「本地时间的下一个」时间点对应的服务器；服务器列表为当前角色所在大区内的全部服务器");
        if (ImGui.BeginTable("##crossschedule", 3, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
        {
            ImGui.TableSetupColumn("时间", ImGuiTableColumnFlags.WidthFixed, 100);
            ImGui.TableSetupColumn("大区", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("操作", ImGuiTableColumnFlags.WidthFixed, 70);
            ImGui.TableHeadersRow();

            var schedule = P.Config.CrossRegionSchedule;
            var nextMinutes = GetNextScheduleMinutes();
            for (var i = 0; i < schedule.Count; i++)
            {
                var e = schedule[i];
                ImGui.PushID(i);
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                var parsed = CrossRegionController.ParseHHMM(e.Time);
                if (parsed != null && parsed.Value == nextMinutes)
                {
                    ImGui.TextColored(ColGreen, "▶");
                    ImGui.SameLine();
                }
                ImGui.SetNextItemWidth(-1);
                var time = e.Time ?? "";
                if (ImGui.InputText("##time", ref time, 4, ImGuiInputTextFlags.CharsDecimal))
                {
                    e.Time = time;
                    EzConfig.Save();
                }
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(parsed == null
                        ? "本地时间，HHMM 格式（仅数字），如 0930 表示 09:30"
                        : $"{parsed.Value / 60:00}:{parsed.Value % 60:00}（▶ 为下一车次）");
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
    }

    /// <summary>水晶下拉框（含「不传送」选项，值为 0）。</summary>
    private static void DrawAetheryteCombo(string id, uint current, string tooltip, Action<uint> onChange)
    {
        ImGui.SetNextItemWidth(250);
        var aetherytes = GetAetherytesCached();
        var name = current == 0
            ? "不传送"
            : aetherytes.FirstOrDefault(a => a.Id == current).Name ?? $"未知水晶 ({current})";
        if (ImGui.BeginCombo(id, name))
        {
            if (ImGui.Selectable("不传送", current == 0))
                onChange(0);
            foreach (var a in aetherytes)
            {
                if (ImGui.Selectable(a.Name, a.Id == current))
                    onChange(a.Id);
            }
            ImGui.EndCombo();
        }
        if (ImGui.IsItemHovered() && tooltip != null) ImGui.SetTooltip(tooltip);
    }

    private static int? GetNextScheduleMinutes()
    {
        var now = DateTime.Now;
        int? best = null;
        var nowMin = now.Hour * 60 + now.Minute;
        foreach (var e in P.Config.CrossRegionSchedule)
        {
            var t = CrossRegionController.ParseHHMM(e?.Time);
            if (t == null) continue;
            if (t.Value > nowMin && (best == null || t.Value < best.Value)) best = t.Value;
        }
        if (best != null) return best;
        foreach (var e in P.Config.CrossRegionSchedule)
        {
            var t = CrossRegionController.ParseHHMM(e?.Time);
            if (t == null) continue;
            if (best == null || t.Value < best.Value) best = t.Value;
        }
        return best;
    }

    // ===== 高级页 =====

    private void DrawAdvancedTab()
    {
        BeginCard();
        ImGui.TextUnformatted("坐标与寻路");
        ImGui.Indent(12);
        ToggleRow("收到坐标时自动打开地图插旗", null, "##tOpenMap", ref P.Config.AutoOpenMap);
        ToggleRow("解析纯文本坐标", "解析聊天中的纯文本坐标，如 12.3, 45.6", "##tParseText", ref P.Config.ParseTextCoordinates);
        ToggleRow("使用 /vnav flyflag 飞向旗标（推荐）",
            "开启后：插旗→上坐骑→执行飞旗命令；关闭则用 IPC 直接寻路到坐标+Z偏移",
            "##tFlyFlag", ref P.Config.UseFlyFlag);
        ImGui.SetNextItemWidth(300);
        if (ImGui.InputText("飞旗命令", ref P.Config.FlyFlagCommand, 64)) EzConfig.Save();
        ImGui.Unindent(12);
        EndCard();

        BeginCard();
        ImGui.TextUnformatted("输出命令");
        ImGui.Indent(12);
        ImGui.SetNextItemWidth(300);
        if (ImGui.InputText("开始输出命令", ref P.Config.RotationStartCommand, 128)) EzConfig.Save();
        ImGui.SetNextItemWidth(300);
        if (ImGui.InputText("停止输出命令", ref P.Config.RotationStopCommand, 128)) EzConfig.Save();
        ImGui.Unindent(12);
        EndCard();

        BeginCard();
        ImGui.TextUnformatted("界面");
        ImGui.Indent(12);
        ToggleRow("显示悬浮窗", "屏幕上的当前操作悬浮窗，点击打开设置窗口，可拖动", "##tFloat", ref P.Config.FloatingWindowEnable);
        ImGui.Unindent(12);
        EndCard();

        BeginCard();
        ImGui.TextUnformatted("其他");
        ImGui.Indent(12);
        ToggleRow("调试模式", null, "##tDebug", ref P.Config.Debug);
        ImGui.Unindent(12);
        EndCard();
    }

    // ===== 状态页：狩猎怪扫描（表格独立于卡片绘制） =====

    /// <summary>状态页：当前地图狩猎怪扫描（进图后自动扫描，每秒刷新）。</summary>
    private void DrawHuntScanSection()
    {
        ImGui.Spacing();
        ImGui.TextUnformatted("地图狩猎怪扫描:");
        if (!Player.Available)
        {
            ImGui.TextDisabled("  请登录角色后扫描");
            return;
        }

        var entries = HuntScanService.GetSnapshot();
        var alive = entries.Count(e => !e.IsDead);
        ImGui.TextUnformatted($"  上次扫描: {HuntScanService.LastScanTime:HH:mm:ss} ｜ 存活 {alive} 只 / 共 {entries.Count} 只 ｜ 每秒自动刷新，切图后自动重新扫描");
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("扫描范围：客户端对象表已加载区域（角色周围约 100 米内）\n点击表格行可选中对应怪物；坐标为游戏地图显示坐标");

        if (entries.Count == 0)
        {
            ImGui.TextDisabled("  周围未发现狩猎怪（对象加载范围内）");
            return;
        }

        if (ImGui.Button("复制全部信息"))
        {
            var lines = entries.Select(e =>
                $"{e.Name} [{e.Rank}] ({e.MapPos.X:0.0}, {e.MapPos.Y:0.0}) 血量{e.HpPercent:0}% {(e.IsDead ? "已死亡" : "存活")}");
            ImGui.SetClipboardText(string.Join("\n", lines));
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("按「名称 [等级] (X, Y) 血量 状态」格式复制到剪贴板");

        ImGui.BeginChild("huntScanList", new Vector2(0, ImGui.GetTextLineHeightWithSpacing() * Math.Min(entries.Count, 8) + 8), true);
        if (ImGui.BeginTable("huntScanTable", 5, ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY))
        {
            ImGui.TableSetupColumn("等级", ImGuiTableColumnFlags.WidthFixed, 36);
            ImGui.TableSetupColumn("名称");
            ImGui.TableSetupColumn("血量", ImGuiTableColumnFlags.WidthFixed, 64);
            ImGui.TableSetupColumn("距离", ImGuiTableColumnFlags.WidthFixed, 64);
            ImGui.TableSetupColumn("地图坐标", ImGuiTableColumnFlags.WidthFixed, 96);
            ImGui.TableHeadersRow();

            foreach (var e in entries)
            {
                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0);
                var rankColor = e.Rank switch
                {
                    "S" => ColPurple,
                    "A" => ImGui.ColorConvertFloat4ToU32(new(1f, 0.6f, 0.3f, 1f)),
                    _ => ColGray,
                };
                ImGui.TextColored(rankColor, e.Rank);
                ImGui.TableSetColumnIndex(1);
                if (ImGui.Selectable(e.Name + "##" + e.GameObjectId))
                {
                    var mob = HuntScanService.FindById(e.GameObjectId);
                    if (mob != null && mob.IsTargetable)
                    {
                        Svc.Targets.Target = mob;
                    }
                }
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip($"点击选中「{e.Name}」\n地图坐标 ({e.MapPos.X:0.0}, {e.MapPos.Y:0.0})\n对象ID: {e.GameObjectId:X}");
                }
                ImGui.TableSetColumnIndex(2);
                ImGui.TextUnformatted(e.IsDead ? "死亡" : $"{e.HpPercent:0}%");
                ImGui.TableSetColumnIndex(3);
                ImGui.TextUnformatted($"{e.Distance:0}m");
                ImGui.TableSetColumnIndex(4);
                ImGui.TextUnformatted($"({e.MapPos.X:0.0}, {e.MapPos.Y:0.0})");
            }
            ImGui.EndTable();
        }
        ImGui.EndChild();
    }

    // ===== 绘制辅助：卡片（绘制通道分层实现圆角背景） =====

    private Dalamud.Bindings.ImGui.ImDrawListPtr cardDl;

    private void BeginCard()
    {
        cardDl = ImGui.GetWindowDrawList();
        cardDl.ChannelsSplit(2);
        cardDl.ChannelsSetCurrent(0);
        ImGui.BeginGroup();
        ImGui.Dummy(new Vector2(0, 3f));
        ImGui.Indent(12);
    }

    private void BeginCard(float fixedWidth)
    {
        cardDl = ImGui.GetWindowDrawList();
        cardDl.ChannelsSplit(2);
        cardDl.ChannelsSetCurrent(0);
        ImGui.BeginGroup();
        ImGui.Dummy(new Vector2(0, 3f));
        ImGui.Indent(10);
    }

    private void EndCard()
    {
        ImGui.Dummy(new Vector2(0, 3f));
        ImGui.Unindent(ImGui.GetStyle().IndentSpacing);
        ImGui.EndGroup();
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        cardDl.ChannelsSetCurrent(1);
        cardDl.AddRectFilled(min, max, CardBg, 10f);
        cardDl.ChannelsMerge();
    }

    /// <summary>内容区右对齐 X 坐标（预留 reserve 像素）。</summary>
    private static float GetContentRight(float reserve = 0f) =>
        ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - reserve;

    // ===== 绘制辅助：toggle 开关行 =====

    private void ToggleRow(string label, string? sub, string id, ref bool value, string? tooltip = null)
    {
        ImGui.TextUnformatted(label);
        if (sub != null)
        {
            ImGui.TextColored(ColGray, sub);
        }
        ImGui.SameLine(GetContentRight(48));
        var v = DrawSwitch(id, value);
        if (v != value)
        {
            value = v;
            EzConfig.Save();
        }
        if (tooltip != null && ImGui.IsItemHovered())
            ImGui.SetTooltip(tooltip);
    }

    private static bool DrawSwitch(string id, bool value)
    {
        var h = ImGui.GetFrameHeight() * 0.72f;
        var w = h * 1.8f;
        var pos = ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton(id, new Vector2(w, h));
        if (ImGui.IsItemClicked(ImGuiMouseButton.Left)) value = !value;
        var dl = ImGui.GetWindowDrawList();
        var bg = value
            ? ImGui.ColorConvertFloat4ToU32(new(0.22f, 0.48f, 0.30f, 1f))
            : ImGui.ColorConvertFloat4ToU32(new(0.45f, 0.48f, 0.55f, 0.45f));
        dl.AddRectFilled(pos, pos + new Vector2(w, h), bg, h * 0.5f);
        var cx = value ? pos.X + w - h / 2 : pos.X + h / 2;
        dl.AddCircleFilled(new Vector2(cx, pos.Y + h / 2), h / 2 - 2.5f,
            ImGui.ColorConvertFloat4ToU32(new(0.95f, 0.96f, 0.98f, 1f)));
        return value;
    }
}
