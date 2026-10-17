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
        var tree = new List<(string Dc, List<(uint Id, string Name)> Worlds)>();
        try
        {
            foreach (var w in Svc.Data.GetExcelSheet<World>())
            {
                var name = w.Name.ToString();
                if (name.IsNullOrEmpty()) continue;
                if (!w.IsPublic) continue; // 跳过测试/开发服
                var dc = w.DataCenter.ValueNullable?.Name.ToString();
                if (dc.IsNullOrEmpty()) continue;

                var idx = tree.FindIndex(d => d.Dc == dc);
                if (idx < 0)
                {
                    tree.Add((dc, new List<(uint, string)>()));
                    idx = tree.Count - 1;
                }
                tree[idx].Worlds.Add((w.RowId, name));
            }
        }
        catch (Exception e)
        {
            PluginLog.Warning($"[AutoHunt] 读取大区/服务器列表失败: {e.Message}");
        }

        // 国服四大区排前面，其余按名称排序
        string[] preferred = { "陆行鸟", "莫古力", "猫小胖", "豆豆柴" };
        tree.Sort((a, b) =>
        {
            var ai = Array.IndexOf(preferred, a.Dc);
            var bi = Array.IndexOf(preferred, b.Dc);
            if (ai < 0) ai = int.MaxValue;
            if (bi < 0) bi = int.MaxValue;
            return ai != bi ? ai.CompareTo(bi) : string.CompareOrdinal(a.Dc, b.Dc);
        });
        foreach (var (_, worlds) in tree)
            worlds.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));

        if (tree.Count > 0) editWorldTree = tree; // 数据未就绪时不留空缓存，下次重试
        return tree;
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
            if (ImGui.Button("关闭", new Vector2(100, 0))) editMapOpen = false;
            ImGui.EndPopup();
            return;
        }

        ImGui.TextColored(ColSub, "选择大区 / 服务器 / 地图版本号，逐张设置该服务器上是否有副本区。");
        ImGui.TextColored(ColSub, "默认值来自腾讯文档《副本地图识别表》；标为「未识别」的地图进图后会探测一次，识别结果会自动写回这里。");
        ImGui.Dummy(new Vector2(0, 4));

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
        if (ImGui.Button("关闭", new Vector2(110, 0)))
            editMapOpen = false;

        ImGui.EndPopup();
    }
}
