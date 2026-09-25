using Dalamud.Interface.Windowing;
using ImGui = Dalamud.Bindings.ImGui.ImGui;
using Dalamud.Bindings.ImGui;
using ECommons.SimpleGui;

namespace AutoHunt.PluginUI;

/// <summary>
/// 悬浮窗：显示当前操作简述，可拖动，点击打开设置窗口，位置持久化。
/// </summary>
public class FloatingWindow : Window
{
    private bool mouseDown;
    private bool dragged;
    private Vector2 dragStartMouse;
    private Vector2 dragStartWinPos;

    public FloatingWindow() : base(
        "AutoHunt 悬浮窗###AutoHuntFloat",
        ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.AlwaysAutoResize
        | ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoFocusOnAppearing
        | ImGuiWindowFlags.NoScrollbar)
    {
        IsOpen = true;
    }

    public override void PreDraw()
    {
        ImGui.SetNextWindowBgAlpha(0.86f);
        if (P.Config.FloatingWindowX >= 0)
            ImGui.SetNextWindowPos(new Vector2(P.Config.FloatingWindowX, P.Config.FloatingWindowY), ImGuiCond.Always);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 10f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(12, 7));
    }

    public override void PostDraw()
    {
        ImGui.PopStyleVar(2);
    }

    public override void Draw()
    {
        var (kind, text) = OperationTracker.Current;
        var col = OperationTrackerUi.KindColor(kind);
        ImGui.TextColored(col, "●");
        ImGui.SameLine();
        ImGui.TextUnformatted(text);

        // 点击打开设置 / 拖动移动位置
        if (ImGui.IsWindowHovered())
        {
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                mouseDown = true;
                dragged = false;
                dragStartMouse = ImGui.GetIO().MousePos;
                dragStartWinPos = ImGui.GetWindowPos();
            }
            if (mouseDown && ImGui.IsMouseDragging(ImGuiMouseButton.Left, 4f))
            {
                dragged = true;
                ImGui.SetWindowPos(dragStartWinPos + (ImGui.GetIO().MousePos - dragStartMouse));
            }
            if (ImGui.IsMouseReleased(ImGuiMouseButton.Left) && mouseDown)
            {
                mouseDown = false;
                if (dragged)
                {
                    var p = ImGui.GetWindowPos();
                    P.Config.FloatingWindowX = p.X;
                    P.Config.FloatingWindowY = p.Y;
                    EzConfig.Save();
                    dragged = false;
                }
                else
                {
                    EzConfigGui.Toggle();
                }
            }
        }
        if (ImGui.IsItemHovered() || ImGui.IsWindowHovered())
            ImGui.SetTooltip("点击打开设置窗口\n拖动移动位置");
    }
}

/// <summary>操作类别配色（主窗口操作条与悬浮窗共用）。</summary>
internal static class OperationTrackerUi
{
    public static Vector4 KindColor(OperationTracker.Kind kind) => kind switch
    {
        OperationTracker.Kind.CrossRegion => new(0.71f, 0.54f, 0.96f, 1f),
        OperationTracker.Kind.InstanceSwitch => new(0.94f, 0.71f, 0.36f, 1f),
        OperationTracker.Kind.FetchConductor => new(0.36f, 0.78f, 0.96f, 1f),
        OperationTracker.Kind.CreatePF => new(0.44f, 0.62f, 0.96f, 1f),
        OperationTracker.Kind.Hunt => new(0.31f, 0.82f, 0.48f, 1f),
        OperationTracker.Kind.Move => new(0.62f, 0.68f, 0.78f, 1f),
        _ => new(0.58f, 0.61f, 0.67f, 1f),
    };

    public static string KindTag(OperationTracker.Kind kind) => kind switch
    {
        OperationTracker.Kind.CrossRegion => "跨区",
        OperationTracker.Kind.InstanceSwitch => "切区",
        OperationTracker.Kind.FetchConductor => "获取车头",
        OperationTracker.Kind.CreatePF => "招募",
        OperationTracker.Kind.Hunt => "狩猎",
        OperationTracker.Kind.Move => "移动",
        _ => "空闲",
    };
}
