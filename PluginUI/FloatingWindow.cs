using Dalamud.Interface.Windowing;
using ImGui = Dalamud.Bindings.ImGui.ImGui;
using Dalamud.Bindings.ImGui;
using ECommons.SimpleGui;

namespace AutoHunt.PluginUI;

/// <summary>
/// 悬浮窗：显示当前操作简述，可拖动（ImGui 原生窗口拖动），点击（未拖动）打开设置窗口，位置持久化。
/// </summary>
public class FloatingWindow : Window
{
    // 点击/拖动区分：记录按下时的窗口位置，释放时位移超过阈值视为拖动
    private bool mouseDown;
    private Vector2 pressWinPos;

    public FloatingWindow() : base(
        "AutoHunt 悬浮窗###AutoHuntFloat",
        // 不加 NoMove：靠 ImGui 原生「拖动空白处移动窗口」实现拖动，比自绘可靠
        ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.AlwaysAutoResize
        | ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoFocusOnAppearing
        | ImGuiWindowFlags.NoScrollbar)
    {
        IsOpen = true;
        // ESC 不应关闭悬浮窗（点击获得焦点后按 ESC 会触发 Windowing 的关闭热键）
        RespectCloseHotkey = false;
    }

    public override void PreDraw()
    {
        ImGui.SetNextWindowBgAlpha(0.86f);
        // 仅首次出现时应用保存的位置；Always 会导致每帧拉回、窗口拖不动
        if (P.Config.FloatingWindowX >= 0)
            ImGui.SetNextWindowPos(new Vector2(P.Config.FloatingWindowX, P.Config.FloatingWindowY), ImGuiCond.FirstUseEver);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 10f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(12, 7));
    }

    public override void PostDraw()
    {
        ImGui.PopStyleVar(2);
    }

    public override void Draw()
    {
        var (kind, tag, detail) = OperationTracker.CurrentParts;
        var col = OperationTrackerUi.KindColor(kind);
        ImGui.TextColored(col, "●");
        ImGui.SameLine();
        ImGui.TextColored(col, tag);
        if (!string.IsNullOrEmpty(detail))
        {
            ImGui.SameLine();
            ImGui.TextUnformatted("— " + detail);
        }

        // 拖动由 ImGui 原生处理（空白区域拖动窗口）；这里只区分“点击”
        if (ImGui.IsWindowHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            mouseDown = true;
            pressWinPos = ImGui.GetWindowPos();
        }
        if (mouseDown && ImGui.IsMouseReleased(ImGuiMouseButton.Left))
        {
            mouseDown = false;
            var moved = Vector2.Distance(ImGui.GetWindowPos(), pressWinPos);
            if (moved < 5f)
            {
                // 视为点击：打开设置窗口
                EzConfigGui.Toggle();
            }
            else
            {
                // 拖动结束：持久化位置
                var p = ImGui.GetWindowPos();
                P.Config.FloatingWindowX = p.X;
                P.Config.FloatingWindowY = p.Y;
                EzConfig.Save();
            }
        }
        if (ImGui.IsWindowHovered())
            ImGui.SetTooltip("拖动移动位置\n点击打开设置窗口");
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

    public static string KindTag(OperationTracker.Kind kind) => OperationTracker.Tag(kind);
}
