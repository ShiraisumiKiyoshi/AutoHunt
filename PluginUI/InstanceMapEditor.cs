using ImGui = Dalamud.Bindings.ImGui.ImGui;
using Dalamud.Bindings.ImGui;

namespace AutoHunt.PluginUI;

/// <summary>
/// 「编辑副本地图识别」弹窗（高级页）：按「大区 → 服务器 → 地图版本号」浏览该版本的全部地图，
/// 逐张设置是否有副本区。默认值来自腾讯文档《副本地图识别表》，未收录的地图显示"未识别"
/// （进图后自动探测一次，探测成功后结果会同步显示在这里，来源标为"自动识别"）。
/// 手动修改立即写入 Config.ManualInstanceState 并保存，优先级高于自动识别。
/// </summary>
public partial class MainWindow
{
    private const string EditMapPopupId = "编辑副本地图识别###autoHuntEditMap";

    private bool editMapOpen;
    private bool editMapRequested;
    private uint editWorldSel;
    private string editDcSel = "";
    private int editVersionIdx;

    private static List<(string Dc, List<(uint Id, string Name)> Worlds)> editWorldTree;

    private static List<(string Dc, List<(uint Id, string Name)> Worlds)> GetWorldTree()
    {
        if (editWorldTree != null) return editWorldTree;

        // 国服世界直接按「服务器中文名单」匹配：服务器名稳定已知（InstanceMapDefaults.DcServers），
        // 与客户端 World 表里不可靠的字段（大区名/Region/IsPublic）完全无关。
        // 实测 CN 客户端的 World 表大区字段与国际服对不上、国服行还会被 IsPublic 过滤漏掉。
        var tree = InstanceMapDefaults.DcServers
            .Select(d => (d.Dc, Worlds: new List<(uint Id, string Name)>()))
            .ToList();
        var nameToDc = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < tree.Count; i++)
            foreach (var s in InstanceMapDefaults.DcServers[i].Servers)
                nameToDc[s] = i;

        var matched = 0;
        try
        {
            foreach (var w in Svc.Data.GetExcelSheet<World>())
            {
                var name = w.Name.ToString().Trim();
                if (name.IsNullOrEmpty() || !nameToDc.TryGetValue(name, out var di)) continue;
                tree[di].Worlds.Add((w.RowId, name));
                matched++;
            }
        }
        catch (Exception e)
        {
            PluginLog.Warning($"[AutoHunt] 读取服务器列表失败: {e.Message}");
        }

        foreach (var t in tree)
            t.Worlds.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        var result = tree.Where(t => t.Worlds.Count > 0).ToList();

        PluginLog.Information("[AutoHunt] 编辑副本地图识别：匹配到国服服务器 " + matched + " 个"
            + (result.Count > 0 ? "：" + string.Join("，", result.Select(t => $"{t.Dc} {t.Worlds.Count} 服")) : ""));

        if (result.Count > 0)
        {
            editWorldTree = result;
            return result;
        }

        // 兜底：名单一个都没匹配上（客户端数据异常）→ 回退为列出表内全部大区，弹窗保持可用，
        // 并把实际情况写进日志便于排查
        PluginLog.Warning("[AutoHunt] 国服服务器名单匹配为空，回退为列出全部大区");
        var fallback = new List<(string Dc, List<(uint Id, string Name)> Worlds)>();
        try
        {
            var seen = new SortedSet<string>();
            var notPublic = 0;
            foreach (var w in Svc.Data.GetExcelSheet<World>())
            {
                var name = w.Name.ToString().Trim();
                if (name.IsNullOrEmpty()) continue;
                if (!w.IsPublic) { notPublic++; continue; }
                var dc = w.DataCenter.ValueNullable?.Name.ToString();
                if (dc.IsNullOrEmpty()) continue;
                seen.Add($"{dc}(Region={w.Region})");
                var idx = fallback.FindIndex(d => d.Dc == dc);
                if (idx < 0) { fallback.Add((dc, new List<(uint, string)>())); idx = fallback.Count - 1; }
                fallback[idx].Worlds.Add((w.RowId, name));
            }
            if (seen.Count > 0)
                PluginLog.Warning("[AutoHunt] 表内大区: " + string.Join(", ", seen)
                    + $"；非公开世界行 {notPublic} 个");
        }
        catch (Exception e)
        {
            PluginLog.Warning($"[AutoHunt] 读取大区/服务器列表失败: {e.Message}");
        }
        foreach (var (_, worlds) in fallback)
            worlds.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        if (fallback.Count > 0) editWorldTree = fallback; // 数据未就绪时不留空缓存，下次重试
        return fallback;
    }

    private void ResetEditorSelection()
    {
        var tree = GetWorldTree();
        if (tree.Count == 0)
        {
            editWorldSel = 0;
            editDcSel = "";
            editVersionIdx = 0;
            return;
        }

        var curWorld = InstanceController.GetCurrentWorldId();
        var worldExists = tree.Any(d => d.Worlds.Any(w => w.Id == curWorld));
        editDcSel = worldExists
            ? tree.First(d => d.Worlds.Any(w => w.Id == curWorld)).Dc
            : tree[0].Dc;
        editWorldSel = worldExists
            ? curWorld
            : tree.First(d => d.Dc == editDcSel).Worlds.First().Id;

        // 默认定位到当前地图所属版本
        var curMap = InstanceController.GetMapName(Svc.ClientState.TerritoryType);
        editVersionIdx = Array.FindIndex(InstanceMapDefaults.Versions, v => v.Maps.Contains(curMap));
        if (editVersionIdx < 0) editVersionIdx = 0;
    }

    private void DrawInstanceMapEditor()
    {
        if (editMapRequested)
        {
            editMapRequested = false;
            ResetEditorSelection();
            ImGui.OpenPopup(EditMapPopupId);
        }
        if (!editMapOpen) return;

        ImGui.SetNextWindowSize(new Vector2(720, 545), ImGuiCond.Appearing);
        if (!ImGui.BeginPopupModal(EditMapPopupId, ref editMapOpen,
                ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoCollapse))
            return;

        var tree = GetWorldTree();
        if (tree.Count == 0)
        {
            ImGui.TextColored(ColAmber, "读不到客户端的大区/服务器数据，请登录角色后重试。");
            ImGui.Dummy(new Vector2(0, 8));
            ImGui.PushStyleColor(ImGuiCol.Button, ColAccent);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Rgba(ColAccent, 0.85f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, Rgba(ColAccent, 0.7f));
            ImGui.PushStyleColor(ImGuiCol.Text, C(10, 18, 26, 255));
            if (ImGui.Button("关闭", new Vector2(100, 0))) editMapOpen = false;
            ImGui.PopStyleColor(4);
            ImGui.EndPopup();
            return;
        }

        ImGui.TextColored(ColSub, "选择大区 / 服务器 / 地图版本号，逐张设置该服务器上是否有副本区。");
        ImGui.TextColored(ColSub, "默认值来自腾讯文档《副本地图识别表》；标为「未识别」的地图进图后会探测一次，识别结果会自动写回这里。");
        ImGui.Dummy(new Vector2(0, 4));

        // 与主窗口一致的控件配色（按钮不推色会走到 Dalamud 主题默认色，与改版窗口风格不符）
        ImGui.PushStyleColor(ImGuiCol.Button, C(255, 255, 255, 18));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, C(255, 255, 255, 31));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, C(255, 255, 255, 41));
        ImGui.PushStyleColor(ImGuiCol.TableHeaderBg, C(255, 255, 255, 14));
        ImGui.PushStyleColor(ImGuiCol.TableBorderStrong, C(255, 255, 255, 22));
        ImGui.PushStyleColor(ImGuiCol.TableBorderLight, C(255, 255, 255, 12));
        const int styledPops = 6;

        // ===== 大区 =====
        ImGui.SetNextItemWidth(150);
        if (ImGui.BeginCombo("##editDc", editDcSel))
        {
            foreach (var (dc, worlds) in tree)
            {
                if (ImGui.Selectable(dc, dc == editDcSel))
                {
                    editDcSel = dc;
                    editWorldSel = worlds.FirstOrDefault().Id;
                }
            }
            ImGui.EndCombo();
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("大区");

        // ===== 服务器 =====
        ImGui.SameLine(0, 8);
        var dcEntry = tree.FirstOrDefault(d => d.Dc == editDcSel);
        var dcWorlds = dcEntry.Worlds ?? new List<(uint Id, string Name)>();
        var worldName = dcWorlds.FirstOrDefault(w => w.Id == editWorldSel).Name ?? "（请选择）";
        ImGui.SetNextItemWidth(170);
        if (ImGui.BeginCombo("##editWorld", worldName))
        {
            foreach (var (id, name) in dcWorlds)
            {
                if (ImGui.Selectable(name, id == editWorldSel))
                    editWorldSel = id;
            }
            ImGui.EndCombo();
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("服务器（同一张图在不同服务器结论可以不同）");

        // ===== 地图版本号 =====
        ImGui.SameLine(0, 8);
        ImGui.SetNextItemWidth(110);
        if (ImGui.BeginCombo("##editVersion", InstanceMapDefaults.Versions[editVersionIdx].Version))
        {
            for (var i = 0; i < InstanceMapDefaults.Versions.Length; i++)
            {
                if (ImGui.Selectable(InstanceMapDefaults.Versions[i].Version, i == editVersionIdx))
                    editVersionIdx = i;
            }
            ImGui.EndCombo();
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("地图版本号");

        ImGui.Dummy(new Vector2(0, 6));

        // ===== 地图表 =====
        var maps = InstanceMapDefaults.Versions[editVersionIdx].Maps;
        ImGui.BeginChild("##editMapList", new Vector2(-1, -ImGui.GetFrameHeightWithSpacing() * 2.2f), true);
        var configured = 0;
        if (ImGui.BeginTable("##editMapTable", 4,
                ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders | ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("地图", ImGuiTableColumnFlags.WidthFixed, 190);
            ImGui.TableSetupColumn("分区情况", ImGuiTableColumnFlags.WidthFixed, 120);
            ImGui.TableSetupColumn("来源", ImGuiTableColumnFlags.WidthFixed, 90);
            ImGui.TableSetupColumn("操作");
            ImGui.TableHeadersRow();

            foreach (var mapName in maps)
            {
                var territory = InstanceController.GetTerritoryIdByMapName(mapName);
                ImGui.TableNextRow();

                ImGui.TableSetColumnIndex(0);
                ImGui.TextUnformatted(mapName);

                ImGui.TableSetColumnIndex(1);
                if (territory == 0)
                {
                    ImGui.TextColored(ColGray, "客户端未收录");
                }
                else
                {
                    var state = InstanceController.GetEffectiveState(editWorldSel, territory);
                    var source = InstanceController.GetStateSource(editWorldSel, territory);
                    if (state >= 0) configured++;
                    var (text, col) = state switch
                    {
                        1 => ("有分区", ColGreen),
                        0 => ("无分区", ColSub),
                        _ => ("未识别", ColAmber),
                    };
                    ImGui.TextColored(col, text);

                    ImGui.TableSetColumnIndex(2);
                    ImGui.TextColored(source == "手动" ? ColAccent : ColSub, source);

                    ImGui.TableSetColumnIndex(3);
                    if (ImGui.SmallButton($"有##set{territory}"))
                        InstanceController.SetManualState(editWorldSel, territory, 1);
                    ImGui.SameLine(0, 6);
                    if (ImGui.SmallButton($"无##set{territory}"))
                        InstanceController.SetManualState(editWorldSel, territory, 0);
                    ImGui.SameLine(0, 6);
                    if (ImGui.SmallButton($"默认##set{territory}"))
                        InstanceController.SetManualState(editWorldSel, territory, null);
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("清除手动设置，恢复为腾讯文档默认值（表中没有则回到未识别）");
                }
            }
            ImGui.EndTable();
        }
        ImGui.EndChild();

        ImGui.TextColored(ColSub, $"本版本共 {maps.Length} 张地图，已有分区状态 {configured} 张");
        ImGui.SameLine();
        ImGui.SetCursorPosX(ImGui.GetWindowWidth() - 130);

        ImGui.PopStyleColor(styledPops);

        // 主按钮：与主窗口强调色一致的实心按钮
        ImGui.PushStyleColor(ImGuiCol.Button, ColAccent);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Rgba(ColAccent, 0.85f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, Rgba(ColAccent, 0.7f));
        ImGui.PushStyleColor(ImGuiCol.Text, C(10, 18, 26, 255));
        if (ImGui.Button("关闭", new Vector2(110, 0)))
            editMapOpen = false;
        ImGui.PopStyleColor(4);

        ImGui.EndPopup();
    }
}
