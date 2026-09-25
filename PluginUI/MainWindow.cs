using ImGui = Dalamud.Bindings.ImGui.ImGui;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ManagedFontAtlas;
using ECommons.SimpleGui;

namespace AutoHunt.PluginUI;

/// <summary>
/// 插件设置界面（v2.4.0 MissFisher 风格重构）：
/// 无边框深色玻璃窗 + 居中标题栏 + 左侧图标导航（白色描边胶囊高亮）+ 行式卡片 + 底部状态栏。
/// 图标使用 Dalamud 内置 FontAwesome（IconFontFixedWidthHandle）。
/// </summary>
public class MainWindow : ConfigWindow
{
    // ===== 页面 =====
    private enum Page { Home, Hunt, Recruit, Cross, Advanced }
    private Page page = Page.Home;

    // ===== 配色（对应原型 CSS 变量） =====
    private static uint ColCard => C(255, 255, 255, 12);        // rgba(255,255,255,.045)
    private static uint ColCard2 => C(255, 255, 255, 18);       // hover .07
    private static uint ColLine => C(255, 255, 255, 23);        // .09
    private static uint ColLine2 => C(255, 255, 255, 41);       // .16
    private static uint ColTxt => C(232, 237, 244, 255);
    private static uint ColSub => C(139, 150, 165, 255);
    private static uint ColAccent => C(76, 194, 255, 255);
    private static uint ColGreen => C(61, 220, 132, 255);
    private static uint ColAmber => C(255, 194, 75, 255);
    private static uint ColRed => C(255, 107, 107, 255);
    private static uint ColPurple => C(183, 139, 255, 255);
    private static uint ColGray => ColSub;
    private static uint ColWinBg => C(17, 24, 35, 247);         // #111823 .97

    // ===== FontAwesome 图标（Dalamud 内置） =====
    private const string IcHome = "\uf015";
    private const string IcCross = "\uf05b";        // crosshairs → 狩猎
    private const string IcClip = "\uf46d";         // clipboard-list → 招募
    private const string IcGlobe = "\uf0ac";        // 跨区
    private const string IcGear = "\uf013";
    private const string IcX = "\uf00d";
    private const string IcPlay = "\uf04b";
    private const string IcPause = "\uf04c";
    private const string IcStop = "\uf04d";
    private const string IcPlus = "\uf067";
    private const string IcThumb = "\uf164";
    private const string IcSkull = "\uf1c2";
    private const string IcFire = "\uf06d";
    private const string IcAlert = "\uf071";
    private const string IcUser = "\uf007";
    private const string IcRadar = "\uf7c0";        // satellite-dish → 扫描
    private const string IcFile = "\uf15c";
    private const string IcMega = "\uf0a1";         // bullhorn
    private const string IcKbd = "\uf11c";
    private const string IcDb = "\uf1c0";           // database
    private const string IcTarget = "\uf192";       // dot-circle → 状态/目标
    private const string IcChevD = "\uf078";

    private static ImFontPtr _iconFont;
    private static DateTime _iconFontNextTry = DateTime.MinValue;

    /// <summary>Dalamud 内置 FontAwesome 字体（IconFontHandle 仅提供 Push/Pop，在 Push 期间抓取 ImFont 缓存；失败后每 5 秒重试）。</summary>
    private static ImFontPtr IconFont()
    {
        if (_iconFont.IsNull && DateTime.Now >= _iconFontNextTry)
        {
            try
            {
                var h = Svc.PluginInterface.UiBuilder.IconFontFixedWidthHandle;
                if (h != null)
                {
                    h.Push();
                    _iconFont = ImGui.GetFont();
                    h.Pop();
                }
            }
            catch { _iconFont = default; }
            _iconFontNextTry = DateTime.Now.AddSeconds(5);
        }
        return _iconFont;
    }

    /// <summary>操作状态色（Vector4 → u32）。</summary>
    private static uint KindCol(OperationTracker.Kind k) => ImGui.ColorConvertFloat4ToU32(OperationTrackerUi.KindColor(k));

    // ===== 布局常量 =====
    private const float RightPad = 16f;             // 右侧安全边距（防贴死右缘/滚动条）
    private const float TitleH = 44f;
    private const float BottomH = 58f;
    private const float SideW = 168f;

    public MainWindow() : base("AutoHunt###AutoHuntMain")
    {
        // 固定尺寸无边框窗口（原型 920x640 的比例）
        Flags = ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse
              | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoDocking;
        Size = new Vector2(900f, 620f);
        SizeCondition = ImGuiCond.Always;
    }

    public override void PreDraw()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 16f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 14f);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 8f);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(10, 6));
        ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, 12f);
        ImGui.PushStyleVar(ImGuiStyleVar.GrabRounding, 6f);
        ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarRounding, 8f);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, ColWinBg);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, 0u);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, C(255, 255, 255, 13));
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, C(255, 255, 255, 23));
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, C(255, 255, 255, 31));
        ImGui.PushStyleColor(ImGuiCol.PopupBg, C(13, 20, 29, 247));
        ImGui.PushStyleColor(ImGuiCol.Header, C(255, 255, 255, 20));
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, C(255, 255, 255, 31));
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, C(255, 255, 255, 41));
        ImGui.PushStyleColor(ImGuiCol.Text, ColTxt);
        ImGui.PushStyleColor(ImGuiCol.SliderGrab, ColAccent);
        ImGui.PushStyleColor(ImGuiCol.SliderGrabActive, ColAccent);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarBg, 0u);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab, C(255, 255, 255, 36));
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabHovered, C(255, 255, 255, 56));
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabActive, C(255, 255, 255, 71));
    }

    public override void PostDraw()
    {
        ImGui.PopStyleColor(16);
        ImGui.PopStyleVar(9);
    }

    public override void Draw()
    {
        // 全部子区域用绝对定位，杜绝 ItemSpacing 累积把底栏推出窗外（曾导致外层滚动条+底栏按钮被裁）
        var W = ImGui.GetWindowSize().X;
        var H = ImGui.GetWindowSize().Y;
        var sp = ImGui.GetStyle().ItemSpacing.Y;

        // 标题栏
        ImGui.SetCursorPos(Vector2.Zero);
        ImGui.BeginChild("##titlebar", new Vector2(W, TitleH), false, ImGuiWindowFlags.NoScrollbar);
        DrawTitleBar();
        ImGui.EndChild();

        // 主体：左侧导航 + 内容
        ImGui.SetCursorPos(new Vector2(0, TitleH + sp));
        ImGui.BeginChild("##body", new Vector2(W, H - TitleH - BottomH - sp * 2), false, ImGuiWindowFlags.NoScrollbar);
        {
            ImGui.BeginChild("##side", new Vector2(SideW, -1), false, ImGuiWindowFlags.NoScrollbar);
            DrawSideNav();
            ImGui.EndChild();

            ImGui.SameLine(0, 0);
            // 页面内容统一 18px 左、12px 顶内边距（此前 SetCursorPos 只偏移首个元素，导致顶部卡片与下方卡片左侧不对齐）
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(18, 12));
            ImGui.BeginChild("##main", ImGui.GetContentRegionAvail(), false, ImGuiWindowFlags.None);
            DrawPage();
            ImGui.EndChild();
            ImGui.PopStyleVar();
        }
        ImGui.EndChild();

        // 底部状态栏
        ImGui.SetCursorPos(new Vector2(0, H - BottomH));
        ImGui.BeginChild("##bottombar", new Vector2(W, BottomH), false, ImGuiWindowFlags.NoScrollbar);
        DrawBottomBar();
        ImGui.EndChild();
    }

    // ===== 标题栏 =====

    private void DrawTitleBar()
    {
        var dl = ImGui.GetWindowDrawList();
        var p = ImGui.GetWindowPos();
        var w = ImGui.GetWindowSize().X;
        dl.AddLine(new(p.X, p.Y + TitleH - 1), new(p.X + w, p.Y + TitleH - 1), ColLine);

        // 左侧：版本（读程序集版本，随 csproj 升版自动更新）
        ImGui.SetCursorPos(new Vector2(16, (TitleH - ImGui.GetTextLineHeight()) / 2));
        ImGui.TextColored(ColSub, $"AutoHunt v{typeof(MainWindow).Assembly.GetName().Version}");

        // 中间：状态点 + 状态文字
        var (kind, tag, _) = OperationTracker.CurrentParts;
        var statusText = !P.Config.Enabled ? "已停止" : kind == OperationTracker.Kind.Idle ? "待机中" : tag;
        var statusCol = !P.Config.Enabled ? ColGray : KindCol(kind);
        var stw = ImGui.CalcTextSize(statusText).X;
        var cx = p.X + w / 2 - (stw + 16) / 2;
        dl.AddCircleFilled(new(cx + 4, p.Y + TitleH / 2), 4.5f, statusCol);
        ImGui.SetCursorPos(new Vector2(cx + 16 - p.X, (TitleH - ImGui.GetTextLineHeight()) / 2));
        ImGui.TextColored(statusCol, statusText);

        // 右侧：设置 / 关闭
        var btnS = new Vector2(30, 30);
        ImGui.SetCursorPos(new Vector2(w - 14 - btnS.X * 2 - 6, (TitleH - btnS.Y) / 2));
        if (IconButton("##tbGear", IcGear, btnS)) page = Page.Advanced;
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("打开「高级」设置");
        ImGui.SameLine(0, 6);
        if (IconButton("##tbClose", IcX, btnS)) IsOpen = false;
    }

    // ===== 左侧导航 =====

    private void DrawSideNav()
    {
        ImGui.SetCursorPos(new Vector2(10, 10));
        NavItem(Page.Home, "主页", IcHome);
        NavItem(Page.Hunt, "狩猎", IcCross);
        NavItem(Page.Recruit, "招募", IcClip);
        NavItem(Page.Cross, "跨区", IcGlobe);
        NavItem(Page.Advanced, "高级", IcGear);
    }

    private void NavItem(Page pg, string label, string glyph)
    {
        var dl = ImGui.GetWindowDrawList();
        ImGui.SetCursorPosX(10);
        var pos = ImGui.GetCursorScreenPos();
        var w = 148f;
        var h = 34f;
        var rect = new Vector2(w, h);
        var active = page == pg;

        if (ImGui.IsMouseHoveringRect(pos, pos + rect) || active)
            dl.AddRectFilled(pos, pos + rect, active ? ColCard2 : ColCard, 11f);
        if (active)
            dl.AddRect(pos, pos + rect, C(255, 255, 255, 140), 11f, 0, 1f);

        if (ImGui.InvisibleButton("##nav" + label, rect)) page = pg;

        var fs = ImGui.GetFontSize();
        var iconW = DrawIconAt(glyph, new(pos.X + 14, pos.Y), new(fs * 1.4f, h), active ? 0xFFFFFFFFu : C(185, 195, 207, 255));
        var lts = ImGui.CalcTextSize(label);
        dl.AddText(ImGui.GetFont(), fs,
            new(pos.X + 14 + iconW + (iconW > 0 ? 12 : 0), pos.Y + (h - lts.Y) / 2),
            active ? 0xFFFFFFFFu : C(185, 195, 207, 255), label);

        ImGui.Dummy(new Vector2(0, 4));
    }

    // ===== 底部状态栏 =====

    private void DrawBottomBar()
    {
        var dl = ImGui.GetWindowDrawList();
        var p = ImGui.GetWindowPos();
        var w = ImGui.GetWindowSize().X;
        dl.AddLine(new(p.X, p.Y), new(p.X + w, p.Y), ColLine);

        var pad = 14f;
        var innerH = BottomH - 20f;
        var y = p.Y + 10f;

        // 右侧：方形停止键 + 圆形播放键（先算右缘，左侧文字据此截断）
        var s = 34f;
        var playW = 44f;
        var stopX = p.X + w - pad - s;
        var playX = stopX - 6f - playW;

        // 左侧：图标 + AutoHunt + 当前操作（单行，无胶囊背景/无标签）
        var (kind, _, detail) = OperationTracker.CurrentParts;
        var stCol = KindCol(kind);
        var subText = AutoHunt.Paused ? "已暂停（点击播放键继续）"
            : string.IsNullOrEmpty(detail) ? (P.Config.Enabled ? "等待车头坐标" : "插件已关闭") : detail;

        var fs = ImGui.GetFontSize();
        var font = ImGui.GetFont();
        var midY = y + innerH / 2;

        var icS = 26f;
        var icPos = new Vector2(p.X + pad, midY - icS / 2);
        DrawIconCentered(IcTarget, icPos, new(icS, icS), stCol, 0.95f);

        var tx = icPos.X + icS + 12f;
        var autoW = ImGui.CalcTextSize("AutoHunt").X;
        dl.AddText(font, fs, new(tx, midY - fs / 2), 0xFFFFFFFFu, "AutoHunt");
        var tx2 = tx + autoW + 14f;
        var opText = TruncateTo(subText, Math.Max(80f, playX - 14f - tx2));
        dl.AddText(font, fs * 0.95f, new(tx2, midY - fs * 0.95f / 2), ColSub, opText);

        // 圆形播放键：三态——未启用=开始（绿）；运行中=暂停（琥珀，点击冻结队列）；已暂停=继续（绿，恢复现场）
        var playPos = new Vector2(playX, y + (innerH - playW) / 2);
        ImGui.SetCursorScreenPos(playPos);
        if (ImGui.InvisibleButton("##bbPlay", new(playW, playW)))
        {
            if (!P.Config.Enabled)
            {
                P.Config.Enabled = true;
                AutoHunt.Paused = false;
            }
            else
            {
                AutoHunt.Paused = !AutoHunt.Paused;
            }
            EzConfig.Save();
        }
        var pausedNow = P.Config.Enabled && AutoHunt.Paused; // 点击后的最新状态
        var playHovered = ImGui.IsItemHovered();
        if (playHovered)
            dl.AddCircleFilled(playPos + new Vector2(playW / 2, playW / 2), playW / 2, C(255, 255, 255, 20));
        dl.AddCircle(playPos + new Vector2(playW / 2, playW / 2), playW / 2, C(255, 255, 255, 115), 0, 1.5f);
        DrawIconCentered(!P.Config.Enabled || pausedNow ? IcPlay : IcPause, playPos, new(playW, playW),
            !P.Config.Enabled || pausedNow ? ColGreen : ColAmber, 0.85f);
        if (playHovered)
            ImGui.SetTooltip(!P.Config.Enabled ? "启动（开启总开关）"
                : pausedNow ? "继续自动操作（从暂停点恢复）"
                : "暂停自动操作（队列冻结，可继续）");

        // 方形停止键：总开关关闭
        var stopPos = new Vector2(stopX, y + (innerH - s) / 2);
        ImGui.SetCursorScreenPos(stopPos);
        if (ImGui.InvisibleButton("##bbStop", new(s, s)) && P.Config.Enabled)
        {
            P.Config.Enabled = false;
            AutoHunt.Paused = false;
            EzConfig.Save();
        }
        if (ImGui.IsItemHovered())
        {
            dl.AddRectFilled(stopPos, stopPos + new Vector2(s, s), ColCard2, 9f);
            ImGui.SetTooltip("停止所有自动行为");
        }
        DrawIconCentered(IcStop, stopPos, new(s, s), C(207, 216, 226, 255), 0.68f);
    }

    // ===== 页面路由 =====

    private void DrawPage()
    {
        rowCounter = 0; // 每帧复位，保证行卡 ID 稳定（页面内边距由 ##main 的 WindowPadding 统一提供）
        switch (page)
        {
            case Page.Home: DrawHomePage(); break;
            case Page.Hunt: DrawHuntPage(); break;
            case Page.Recruit: DrawRecruitPage(); break;
            case Page.Cross: DrawCrossPage(); break;
            case Page.Advanced: DrawAdvancedPage(); break;
        }
        ImGui.Dummy(new Vector2(0, 10));
    }

    private void Sect(string text)
    {
        ImGui.Dummy(new Vector2(0, 4));
        ImGui.TextColored(ColSub, text);
        ImGui.Dummy(new Vector2(0, 2));
    }

    // ===== 主页 =====

    private void DrawHomePage()
    {
        // Hero 大卡
        RowBegin(86f);
        {
            var dl = ImGui.GetWindowDrawList();
            var p = rowRectMin;
            var ic = new Vector2(56, 56);
            var ip = new Vector2(16, (86 - ic.Y) / 2);
            dl.AddRectFilled(p + ip, p + ip + ic, C(255, 157, 69, 255), 14f);
            DrawIconCentered(IcTarget, p + ip, ic, 0xFFFFFFFFu, 1.1f);
            ImGui.SetCursorScreenPos(p + new Vector2(ip.X + ic.X + 14, 16));
            ImGui.TextUnformatted("自动狩猎车助手");
            ImGui.SetCursorScreenPos(p + new Vector2(ip.X + ic.X + 14, 44));
            ImGui.TextColored(ColSub, "解析车头坐标 · 自动传送 · 副本区切换 · 血量阈值自动输出");
        }
        RowEnd();

        // 指标卡（四张横排，等分宽度）
        var avail = ImGui.GetContentRegionAvail().X;
        var mw = Math.Max(96f, (avail - 30f) / 4f);
        var online = Conductor.FindNearest() != null;
        MetricCard(mw, "车头", online, $"{P.Config.Conductors.Count} 人", online ? ColGreen : ColGray);
        ImGui.SameLine(0, 10);
        MetricCard(mw, "本区击杀", null,
            $"{InstanceController.KillCount} / {P.Config.KillsPerInstance}", InstanceController.ZoneCleared ? ColAmber : ColGreen);
        ImGui.SameLine(0, 10);
        MetricCard(mw, "副本区", null, $"{InstanceController.CachedCurrentInstance} / {InstanceController.CachedInstanceCount}", ColTxt);
        ImGui.SameLine(0, 10);
        MetricCard(mw, "狩猎怪库", null, $"{HuntMobDatabase.RankMap.Count} 只", ColTxt);
        ImGui.Dummy(new Vector2(0, 8));

        // 主按钮 + 幽灵按钮
        ImGui.Dummy(new Vector2(0, 6));
        DrawMainButton();
        ImGui.SameLine(0, 10);
        if (GhostButton("##gbFetch", IcThumb, "自动获取车头")) ConductorFetchService.Enqueue();
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("读取队员招募 → 全部 → 怪物狩猎中的全部招募人，设为车头（去重、替换现有列表、跳过自己）");
        ImGui.SameLine(0, 10);
        if (GhostButton("##gbCancel", IcX, "全部取消车头", warn: true)) Conductor.ClearAll();

        // 当前操作
        var (kind, tagOp, detail) = OperationTracker.CurrentParts;
        Sect("当前操作");
        Row(IcTarget, KindCol(kind),
            tagOp,
            string.IsNullOrEmpty(detail) ? "等待车头坐标" : detail,
            tag: P.Config.Enabled ? null : "已停止", tagCol: ColGray);

        // 当前目标
        Sect("当前目标");
        var rank = HuntController.CurrentTargetRank;
        if (!string.IsNullOrEmpty(HuntController.CurrentTargetName))
        {
            var title = string.IsNullOrEmpty(rank) ? HuntController.CurrentTargetName : $"[{rank}] {HuntController.CurrentTargetName}";
            Row(rank == "S" ? IcFire : IcSkull, rank == "S" ? ColAmber : ColGreen, title,
                $"血量 {HuntController.CurrentTargetHpPercent:0}% · 监控中",
                rightText: $"{HuntController.CurrentTargetHpPercent:0}%", rightCol: ColGreen,
                border: C(61, 220, 132, 90));
            if (HuntController.SpawnMatched)
                Row(IcRadar, ColAccent, "出生点辅助已命中",
                    "前往出生点等待并监控怪物刷新", tag: "辅助中", tagCol: ColAccent);
        }
        else
        {
            Row(IcTarget, ColGray, "等待车头坐标",
                "任一车头在聊天中发送坐标后自动开始");
        }

        // 状态提醒
        Sect("状态提醒");
        var anyWarn = false;
        if (InstanceController.ZoneCleared)
        {
            anyWarn = true;
            Row(IcAlert, ColAmber, "本区击杀已满", "等待副本区切换（自动 · 每击杀 2 只）", tag: "切换中", tagCol: ColAmber);
        }
        if (InstanceController.PendingSwitchInstance > 0)
        {
            anyWarn = true;
            Row(IcAlert, ColAmber, $"待切换 {InstanceController.PendingSwitchInstance} 号副本区", "等待车头新坐标");
        }
        if (!Conductor.IsValid)
        {
            anyWarn = true;
            Row(IcUser, ColGray, "未设置车头", "右键聊天玩家名「设为车头」，或点击「自动获取车头」");
        }
        if (P.Config.CrossRegionAutoCancelConductor && P.Config.CrossRegionEndAetheryteId == 0)
        {
            anyWarn = true;
            Row(IcAlert, ColRed, "未选择结束地图", "「自动取消车头」已开启但结束地图为空，暂不生效", tag: "需配置", tagCol: ColRed);
        }
        if (!anyWarn)
            Row(IcAlert, ColGreen, "一切正常", "无待处理提醒", border: C(61, 220, 132, 90));

        // 地图扫描摘要
        Sect("地图狩猎怪扫描");
        if (Player.Available)
        {
            var entries = HuntScanService.GetSnapshot();
            var alive = entries.Count(e => !e.IsDead);
            Row(IcRadar, ColAccent, $"上次扫描 {HuntScanService.LastScanTime:HH:mm:ss}",
                $"存活 {alive} 只 / 共 {entries.Count} 只 · 每秒自动刷新",
                tag: "扫描中", tagCol: ColAccent);
        }
        else
        {
            Row(IcRadar, ColGray, "未登录角色", "请登录角色后扫描");
        }
    }

    private void MetricCard(float width, string label, bool? online, string value, uint valueCol)
    {
        // 纯 drawlist 绘制（不用子窗口），末尾注册等大占位项以支持 SameLine 横排
        var min = ImGui.GetCursorScreenPos();
        var max = min + new Vector2(width, 58f);
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(min, max, ColCard, 14f);

        var fs = ImGui.GetFontSize();
        var font = ImGui.GetFont();
        dl.AddText(font, fs, new(min.X + 14, min.Y + 9), ColSub, label);
        var vy = min.Y + 30;
        var vx = min.X + 14;
        if (online.HasValue)
        {
            var dot = online.Value ? "●" : "○";
            dl.AddText(font, fs, new(vx, vy), online.Value ? ColGreen : ColGray, dot);
            vx += ImGui.CalcTextSize(dot).X + 6;
        }
        dl.AddText(font, fs, new(vx, vy), valueCol, value);

        ImGui.SetCursorScreenPos(min);
        ImGui.Dummy(new Vector2(width, 58f));
    }

    /// <summary>胶囊主按钮：左半切换总开关，右侧 ▼ 弹出快捷操作菜单。</summary>
    private void DrawMainButton()
    {
        var dl = ImGui.GetWindowDrawList();
        var h = 38f;
        var dropW = 34f;
        var total = Math.Min(236f, ImGui.GetContentRegionAvail().X);
        var pos = ImGui.GetCursorScreenPos();

        dl.AddRectFilled(pos, pos + new Vector2(total, h), ColCard, h / 2);
        dl.AddRect(pos, pos + new Vector2(total, h), C(255, 255, 255, 90), h / 2, 0, 1f);

        ImGui.SetCursorScreenPos(pos);
        if (ImGui.InvisibleButton("##mainRun", new(total - dropW, h)))
        {
            P.Config.Enabled = !P.Config.Enabled;
            EzConfig.Save();
        }
        DrawIconAt(P.Config.Enabled ? IcStop : IcPlay, pos + new Vector2(16, 0), new(16, h),
            P.Config.Enabled ? ColAmber : ColGreen);
        var label = P.Config.Enabled ? "停止狩猎" : "启动狩猎";
        var fs = ImGui.GetFontSize();
        var lw = ImGui.CalcTextSize(label).X;
        dl.AddText(ImGui.GetFont(), fs, new(pos.X + 16 + 16 + 8, pos.Y + (h - fs) / 2), 0xFFFFFFFFu, label);

        // 分隔线
        dl.AddLine(new(pos.X + total - dropW, pos.Y + 6), new(pos.X + total - dropW, pos.Y + h - 6), C(255, 255, 255, 77));

        ImGui.SetCursorScreenPos(pos + new Vector2(total - dropW, 0));
        if (ImGui.InvisibleButton("##mainDrop", new(dropW, h)))
            ImGui.OpenPopup("##mainmenu");
        DrawIconCentered(IcChevD, pos + new Vector2(total - dropW, 0), new(dropW, h), C(207, 216, 226, 255), 0.72f);

        if (ImGui.BeginPopup("##mainmenu"))
        {
            if (ImGui.MenuItem("自动获取车头")) ConductorFetchService.Enqueue();
            if (ImGui.MenuItem("创建怪物狩猎招募"))
            {
                if (P.Config.PfinderEnable)
                    Tasks.TaskCreateHuntPF.Enqueue();
                else
                    Notify.Error("一键创建招募按钮未启用，请在「招募」页中开启。");
            }
            if (ImGui.MenuItem("全部取消车头")) Conductor.ClearAll();
            ImGui.EndPopup();
        }
    }

    // ===== 狩猎页 =====

    private string manualConductorName = "";

    private void DrawHuntPage()
    {
        // 车头列表
        Sect($"车头（{P.Config.Conductors.Count}）— 任一车头发送坐标都会触发狩猎");
        var list = P.Config.Conductors;
        for (var i = 0; i < list.Count; i++)
        {
            var c = list[i];
            ImGui.PushID(i);
            var pc = Conductor.FindByName(c.Name);
            var focusOn = Svc.Targets.FocusTarget != null && Svc.Targets.FocusTarget.Name.TextValue == c.Name;
            var world = WorldName(c.WorldId);
            var sub = (string.IsNullOrEmpty(world) ? "" : world + " · ") + (pc != null ? "在附近" : "未在附近");
            var removed = false;
            Row(IcUser, focusOn ? ColGreen : ColAccent, c.Name, sub,
                tag: focusOn ? "焦点保持" : null, tagCol: ColGreen,
                btn: "取消", onBtn: () => { Conductor.Remove(c.Name); removed = true; },
                border: focusOn ? C(61, 220, 132, 90) : null);
            ImGui.PopID();
            if (removed) break; // 列表被修改，跳过本帧剩余行
        }
        if (list.Count == 0)
            Row(IcUser, ColGray, "未设置车头", "右键聊天玩家名「设为车头」，或点击「自动获取车头」");

        // 手动添加
        ImGui.Dummy(new Vector2(0, 2));
        var name = manualConductorName ?? "";
        ImGui.SetNextItemWidth(Math.Min(220, ImGui.GetContentRegionAvail().X - 190));
        if (ImGui.InputTextWithHint("##manualconductor", "手动添加车头名", ref name, 32))
            manualConductorName = name;
        ImGui.SameLine(0, 8);
        if (GhostButton("##gbAdd", IcPlus, "添加") && !manualConductorName.IsNullOrEmpty())
        {
            Conductor.Add(manualConductorName);
            manualConductorName = "";
        }
        ImGui.SameLine(0, 8);
        if (GhostButton("##gbCancelAll", IcX, "全部取消", warn: true) && Conductor.IsValid) Conductor.ClearAll();

        // 流程开关
        Sect("流程开关");
        RowBegin();
        ToggleRow("插件总开关", "关闭时立即停止所有自动行为并清空操作队列", "##tEnabled", ref P.Config.Enabled);
        ToggleRow("自动输出（/rotation Manual）", "血量到达阈值后下坐骑并开始输出", "##tAutoAttack", ref P.Config.AutoAttack);
        ToggleRow("自动切换副本区", $"每击杀 {P.Config.KillsPerInstance} 只自动切换，共 {InstanceController.CachedInstanceCount} 个副本区", "##tAutoInstance", ref P.Config.AutoInstance);
        ToggleRow("包含 B 级狩猎怪", "默认仅锁定 A / S 级（游戏数据表判定，零误判）", "##tIncludeB", ref P.Config.IncludeBRank);
        ToggleRow("启用狩猎怪出生点辅助", "车头坐标命中数据库出生点时，前往出生点等待并监控", "##tSpawn", ref P.Config.UseSpawnPoints);
        if (P.Config.UseSpawnPoints)
        {
            ImGui.SetNextItemWidth(Math.Min(220, ImGui.GetContentRegionAvail().X - RightPad));
            if (ImGui.SliderFloat("出生点匹配半径 (米)", ref P.Config.SpawnMatchRadius, 30f, 300f)) EzConfig.Save();
        }
        RowEnd();

        // 流程参数
        Sect("流程参数");
        RowBegin();
        ImGui.SetNextItemWidth(Math.Min(220, ImGui.GetContentRegionAvail().X - RightPad));
        if (ImGui.SliderFloat("传送距离阈值 (米)", ref P.Config.TeleportDistanceThreshold, 10f, 300f)) EzConfig.Save();
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("自己与目标距离减去最近水晶到目标距离大于此值时传送");
        ImGui.SetNextItemWidth(Math.Min(220, ImGui.GetContentRegionAvail().X - RightPad));
        if (ImGui.SliderFloat("悬停高度偏移 (米)", ref P.Config.ZOffset, 0f, 100f)) EzConfig.Save();
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("接近车头坐标后，悬停点相对目标坐标地面高度的上移量；不可飞地图自动忽略");
        ImGui.SetNextItemWidth(Math.Min(220, ImGui.GetContentRegionAvail().X - RightPad));
        if (ImGui.SliderFloat("下坐骑血量 (%)", ref P.Config.DismountHpPercent, 10f, 100f)) EzConfig.Save();
        ImGui.SetNextItemWidth(Math.Min(220, ImGui.GetContentRegionAvail().X - RightPad));
        if (ImGui.SliderInt("每击杀几只怪切换副本区", ref P.Config.KillsPerInstance, 1, 10)) EzConfig.Save();
        ToggleRow("上坐骑寻路", null, "##tMount", ref P.Config.UseMount);
        ImGui.SetNextItemWidth(Math.Min(220, ImGui.GetContentRegionAvail().X - RightPad));
        if (ImGui.InputText("指定坐骑名称（留空随机）", ref P.Config.MountName, 64)) EzConfig.Save();
        RowEnd();

        // 地图狩猎怪扫描
        Sect("地图狩猎怪扫描");
        DrawHuntScanSection();
    }

    private static string WorldName(uint worldId)
    {
        if (worldId == 0) return "";
        try { return Svc.Data.GetExcelSheet<World>().GetRow(worldId).Name.ToString(); }
        catch { return ""; }
    }

    // ===== 招募页 =====

    private void DrawRecruitPage()
    {
        Sect("一键创建怪物狩猎招募");
        Row(IcFile, ColAccent, "招募文案", "自动填充狩猎目标与剩余数量",
            tag: P.Config.PfinderEnable ? "已配置" : "未启用", tagCol: P.Config.PfinderEnable ? ColAccent : ColGray);

        RowBegin();
        ToggleRow("启用一键创建队员招募", "启用后，主页「创建怪物狩猎招募」按钮可用", "##tPf", ref P.Config.PfinderEnable);
        ToggleRow("启用创建招募时设置青魔占位", "自动设置青魔职业占位并限定平均品级 531", "##tBlu", ref P.Config.BluPlaceholder);
        ImGui.Dummy(new Vector2(0, 4));
        BoldLabel("队员招募自由留言");
        ImGui.SetNextItemWidth(-RightPad);
        var comment = P.Config.PfinderString ?? "";
        if (ImGui.InputText("##pfindercomment", ref comment, 150))
        {
            P.Config.PfinderString = comment;
            EzConfig.Save();
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("创建招募时自动填入招募留言，最长 2 行（约 50 个汉字）");
        RowEnd();

        Sect("招募效果预览");
        Row(IcMega, ColGreen, "[怪物狩猎] 队员招募",
            string.IsNullOrWhiteSpace(P.Config.PfinderString) ? "（未设置留言）" : P.Config.PfinderString,
            tag: "预览", tagCol: ColAccent, border: C(61, 220, 132, 90));

        ImGui.Dummy(new Vector2(0, 2));
        if (GhostButton("##gbCreatePF", IcMega, "一键创建招募"))
        {
            if (P.Config.PfinderEnable)
                Tasks.TaskCreateHuntPF.Enqueue();
            else
                Notify.Error("请先开启「启用一键创建队员招募」开关。");
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("按上方文案与留言配置，立即在招募面板创建一条队员招募");
    }

    // ===== 跨区页 =====

    private static List<(uint Id, string Name)> cachedAetherytes;
    private static List<string> cachedDcWorlds;

    private static List<(uint Id, string Name)> GetAetherytesCached()
    {
        cachedAetherytes ??= CrossRegionController.GetTeleportableAetherytes();
        return cachedAetherytes;
    }

    private static List<string> GetDcWorldsCached()
    {
        cachedDcWorlds ??= CrossRegionController.GetCurrentDcWorlds();
        return cachedDcWorlds;
    }

    private void DrawCrossPage()
    {
        Sect("跨区流程");
        RowBegin();
        ToggleRow("启用跨区功能",
            "取消车头 → 解散小队 → 传送城市 → 跨区 → 传送水晶 →（可选）获取车头 →（可选）开招募",
            "##tCross", ref P.Config.CrossRegionEnable);
        ToggleRow("跨区完成后自动获取车头",
            "读取队员招募-怪物狩猎中的招募人并设为车头（已有车头时跳过）",
            "##tCrossFetch", ref P.Config.CrossRegionAutoFetchConductor);
        ToggleRow("自动取消车头（结束地图击杀满后）",
            "结束地图击杀满时全清并衔接跨区",
            "##tCrossCancel", ref P.Config.CrossRegionAutoCancelConductor);
        ToggleRow("自动开启招募",
            "跨区流程完成后，按「招募」页配置自动创建队员招募",
            "##tCrossPF", ref P.Config.CrossRegionAutoPF);

        ImGui.Dummy(new Vector2(0, 4));
        BoldLabel("跨区前传送到城市");
        ImGui.SetNextItemWidth(Math.Min(260, ImGui.GetContentRegionAvail().X - RightPad));
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

        ImGui.Dummy(new Vector2(0, 6));
        BoldLabel("跨区后传送到水晶");
        DrawAetheryteCombo("##crosspost", P.Config.CrossRegionPostAetheryteId,
            "跨区完成后的传送目的地；选择「不传送」则跨界后不进行传送操作",
            v => { P.Config.CrossRegionPostAetheryteId = v; EzConfig.Save(); });

        ImGui.Dummy(new Vector2(0, 6));
        BoldLabel("结束地图");
        DrawEndMapCombo("##crossend", P.Config.CrossRegionEndAetheryteId,
            P.Config.CrossRegionAutoCancelConductor
                ? "自动取消车头的触发地图；当前地图与所选地图相同且击杀满时触发"
                : "先开启「自动取消车头」后此地图才会生效；按地图名称选择",
            v => { P.Config.CrossRegionEndAetheryteId = v; EzConfig.Save(); });
        if (P.Config.CrossRegionAutoCancelConductor && P.Config.CrossRegionEndAetheryteId == 0)
            ImGui.TextColored(ColAmber, "未选择结束地图，自动取消车头不生效");
        RowEnd();

        // 狩猎时间表
        Sect("狩猎时间表 — 最近车次前 30 分钟内自动跨区，其余时间等待");
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("取消车头后：若本地时间处于某车次前 30 分钟内，立即跨往该车次对应的服务器；否则等待最近车次进入前 30 分钟窗口再跨区（例：本地 18:31，19:00 车次满足条件）。服务器列表为当前角色所在大区内的全部服务器");
        var schedule = P.Config.CrossRegionSchedule;
        var nextMinutes = GetNextScheduleMinutes();
        for (var i = 0; i < schedule.Count; i++)
        {
            var e = schedule[i];
            ImGui.PushID(i);
            var parsed = CrossRegionController.ParseHHMM(e.Time);
            var isNext = parsed != null && parsed.Value == nextMinutes;
            scheduleDeleted = false;
            ScheduleRow(e, isNext);
            ImGui.PopID();
            if (scheduleDeleted)
            {
                schedule.RemoveAt(i);
                EzConfig.Save();
                break;
            }
        }
        ImGui.Dummy(new Vector2(0, 2));
        if (GhostButton("##gbAddSched", IcPlus, "添加时间表行"))
            P.Config.CrossRegionSchedule.Add(new CrossRegionScheduleEntry());
    }

    private bool scheduleDeleted;

    private void ScheduleRow(CrossRegionScheduleEntry e, bool isNext)
    {
        RowBegin(50f, border: isNext ? C(61, 220, 132, 110) : null);
        var id = rowId; // 控件 ID 去重（无子窗口后同一父窗口内需唯一）
        var avail = rowRectMax.X - 14f - ImGui.GetCursorScreenPos().X - RightPad;
        var delW = ImGui.CalcTextSize("删除").X + 40f;

        if (isNext) ImGui.PushStyleColor(ImGuiCol.Text, ColGreen);
        ImGui.SetNextItemWidth(64);
        var time = e.Time ?? "";
        if (ImGui.InputText($"##time{id}", ref time, 4, ImGuiInputTextFlags.CharsDecimal))
        {
            e.Time = time;
            EzConfig.Save();
        }
        if (isNext) ImGui.PopStyleColor();
        if (ImGui.IsItemHovered())
        {
            var p2 = CrossRegionController.ParseHHMM(e.Time);
            ImGui.SetTooltip(p2 == null
                ? "本地时间，HHMM 格式（仅数字），如 0930 表示 09:30"
                : $"{p2.Value / 60:00}:{p2.Value % 60:00}（下一车次以绿色显示）");
        }

        ImGui.SameLine(0, 8);
        ImGui.TextColored(isNext ? ColGreen : ColSub, "→");
        ImGui.SameLine(0, 8);
        ImGui.SetNextItemWidth(Math.Max(80, avail - 64 - 16 - 16 - delW - 16));
        var dcWorlds = GetDcWorldsCached();
        var comboLabel = string.IsNullOrEmpty(e.World) ? "请选择服务器" : e.World;
        if (dcWorlds.Count == 0 && !string.IsNullOrEmpty(e.World)) comboLabel += "（当前大区列表不可用）";
        if (ImGui.BeginCombo($"##dc{id}", comboLabel))
        {
            if (dcWorlds.Count == 0)
            {
                ImGui.TextDisabled("无法读取当前大区的服务器列表");
                ImGui.TextDisabled("请登录角色后再编辑时间表");
            }
            else
            {
                foreach (var w2 in dcWorlds)
                {
                    if (ImGui.Selectable(w2, w2 == e.World))
                    {
                        e.World = w2;
                        EzConfig.Save();
                    }
                }
            }
            ImGui.EndCombo();
        }
        if (dcWorlds.Count > 0 && ImGui.IsItemHovered())
            ImGui.SetTooltip("服务器列表取自角色当前所在大区；切换大区后重新打开本页面，列表会随之更新");

        ImGui.SameLine(0, 8);
        if (GhostButton($"##del{id}", IcX, "删除", warn: true))
            scheduleDeleted = true;
        RowEnd();
    }

    /// <summary>水晶下拉框（含「不传送」选项，值为 0）。</summary>
    private static void DrawAetheryteCombo(string id, uint current, string tooltip, Action<uint> onChange)
    {
        ImGui.SetNextItemWidth(Math.Min(260, ImGui.GetContentRegionAvail().X - RightPad));
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

    private static List<(uint Id, string MapName)> cachedEndMaps;

    /// <summary>结束地图候选：按地图名去重的水晶（同一地图取首个水晶 RowId 作为存储值）。</summary>
    private static List<(uint Id, string MapName)> GetEndMapsCached()
    {
        if (cachedEndMaps == null)
        {
            cachedEndMaps = new List<(uint, string)>();
            try
            {
                var seen = new HashSet<string>();
                foreach (var a in Svc.Data.GetExcelSheet<Aetheryte>())
                {
                    if (!a.IsAetheryte) continue;
                    var mapName = MapManager.GetMapForTerritory(a.Territory.RowId)?.PlaceName.ValueNullable?.Name.ToString();
                    if (string.IsNullOrEmpty(mapName) || !seen.Add(mapName)) continue;
                    cachedEndMaps.Add((a.RowId, mapName));
                }
                cachedEndMaps.Sort((x, y) => string.CompareOrdinal(x.Item2, y.Item2));
            }
            catch (Exception e)
            {
                PluginLog.Warning($"[AutoHunt] 读取结束地图列表失败: {e.Message}");
            }
        }
        return cachedEndMaps;
    }

    /// <summary>结束地图下拉框：按地图名称选择（内部仍存水晶 RowId，判定逻辑不变）。</summary>
    private static void DrawEndMapCombo(string id, uint current, string tooltip, Action<uint> onChange)
    {
        ImGui.SetNextItemWidth(Math.Min(260, ImGui.GetContentRegionAvail().X - RightPad));
        var maps = GetEndMapsCached();
        string curMap = null;
        if (current != 0)
        {
            try
            {
                var a = Svc.Data.GetExcelSheet<Aetheryte>().GetRow(current);
                curMap = MapManager.GetMapForTerritory(a.Territory.RowId)?.PlaceName.ValueNullable?.Name.ToString();
            }
            catch { /* 未知水晶 */ }
        }
        var name = current == 0
            ? "不传送"
            : maps.FirstOrDefault(m => m.MapName == curMap).MapName ?? $"未知地图 ({current})";
        if (ImGui.BeginCombo(id, name))
        {
            if (ImGui.Selectable("不传送", current == 0))
                onChange(0);
            foreach (var m in maps)
            {
                if (ImGui.Selectable(m.MapName, m.MapName == curMap))
                    onChange(m.Id);
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

    private void DrawAdvancedPage()
    {
        Sect("界面");
        RowBegin();
        ToggleRow("显示悬浮窗", "屏幕上显示当前操作，点击打开设置，可拖动", "##tFloat", ref P.Config.FloatingWindowEnable);
        ToggleRow("收到坐标时自动打开地图插旗", null, "##tOpenMap", ref P.Config.AutoOpenMap);
        ToggleRow("调试模式", "聊天日志输出详细匹配信息", "##tDebug", ref P.Config.Debug);
        RowEnd();

        Sect("坐标与寻路");
        RowBegin();
        ToggleRow("解析纯文本坐标", "解析聊天中的纯文本坐标，如 12.3, 45.6", "##tParseText", ref P.Config.ParseTextCoordinates);
        ToggleRow("使用 /vnav flyflag 飞向旗标（推荐）",
            "开启后：插旗→上坐骑→执行飞旗命令；关闭则用 IPC 直接寻路到坐标+Z偏移",
            "##tFlyFlag", ref P.Config.UseFlyFlag);
        ImGui.SetNextItemWidth(Math.Min(300, ImGui.GetContentRegionAvail().X - RightPad));
        if (ImGui.InputText("飞旗命令", ref P.Config.FlyFlagCommand, 64)) EzConfig.Save();
        RowEnd();

        Sect("命令");
        RowBegin();
        ImGui.SetNextItemWidth(Math.Min(300, ImGui.GetContentRegionAvail().X - RightPad));
        if (ImGui.InputText("开始输出命令", ref P.Config.RotationStartCommand, 128)) EzConfig.Save();
        ImGui.SetNextItemWidth(Math.Min(300, ImGui.GetContentRegionAvail().X - RightPad));
        if (ImGui.InputText("停止输出命令", ref P.Config.RotationStopCommand, 128)) EzConfig.Save();
        RowEnd();

        Sect("数据");
        Row(IcDb, ColGreen, "出生点数据库", "219 只怪 · 3196 个出生点（内置于插件）", tag: "已加载", tagCol: ColGreen);

        Sect("依赖插件");
        RowBegin();
        foreach (var (label, feature, installed, required) in DependencyChecker.GetDependencyStatus())
        {
            var color = installed ? ColGreen : required ? ColRed : ColAmber;
            ImGui.TextColored(color, $"{(installed ? "√" : "×")} {label}{(required ? "（必需）" : "")}");
            ImGui.SameLine(0, 8);
            ImGui.TextColored(ColSub, feature);
        }
        RowEnd();
    }

    // ===== 地图狩猎怪扫描（狩猎页） =====

    private void DrawHuntScanSection()
    {
        if (!Player.Available)
        {
            Row(IcRadar, ColGray, "请登录角色后扫描", "");
            return;
        }

        var entries = HuntScanService.GetSnapshot();
        var alive = entries.Count(e => !e.IsDead);
        Row(IcRadar, ColAccent, $"上次扫描 {HuntScanService.LastScanTime:HH:mm:ss}",
            $"存活 {alive} 只 / 共 {entries.Count} 只 · 每秒自动刷新",
            tag: "扫描中", tagCol: ColAccent);

        ImGui.Dummy(new Vector2(0, 2));
        if (GhostButton("##gbCopyScan", IcFile, "复制全部信息"))
        {
            var lines = entries.Select(e =>
                $"{e.Name} [{e.Rank}] ({e.MapPos.X:0.0}, {e.MapPos.Y:0.0}) 血量{e.HpPercent:0}% {(e.IsDead ? "已死亡" : "存活")}");
            ImGui.SetClipboardText(string.Join("\n", lines));
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("按「名称 [等级] (X, Y) 血量 状态」格式复制到剪贴板");

        if (entries.Count == 0)
        {
            ImGui.Dummy(new Vector2(0, 2));
            ImGui.TextColored(ColSub, "周围未发现狩猎怪（对象加载范围内）");
            return;
        }

        ImGui.Dummy(new Vector2(0, 2));
        if (ImGui.BeginTable("huntScanTable", 5, ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders | ImGuiTableFlags.ScrollY, new Vector2(-1, ImGui.GetTextLineHeightWithSpacing() * Math.Min(entries.Count, 8) + 10)))
        {
            ImGui.TableSetupColumn("等级", ImGuiTableColumnFlags.WidthFixed, 40);
            ImGui.TableSetupColumn("名称");
            ImGui.TableSetupColumn("血量", ImGuiTableColumnFlags.WidthFixed, 60);
            ImGui.TableSetupColumn("距离", ImGuiTableColumnFlags.WidthFixed, 60);
            ImGui.TableSetupColumn("地图坐标", ImGuiTableColumnFlags.WidthFixed, 96);
            ImGui.TableHeadersRow();

            foreach (var e in entries)
            {
                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0);
                var rankColor = e.Rank switch
                {
                    "S" => ColPurple,
                    "A" => C(255, 153, 77, 255),
                    _ => ColGray,
                };
                ImGui.TextColored(rankColor, e.Rank);
                ImGui.TableSetColumnIndex(1);
                if (ImGui.Selectable(e.Name + "##" + e.GameObjectId))
                {
                    var mob = HuntScanService.FindById(e.GameObjectId);
                    if (mob != null && mob.IsTargetable)
                        Svc.Targets.Target = mob;
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip($"点击选中「{e.Name}」\n地图坐标 ({e.MapPos.X:0.0}, {e.MapPos.Y:0.0})\n对象ID: {e.GameObjectId:X}");
                ImGui.TableSetColumnIndex(2);
                ImGui.TextUnformatted(e.IsDead ? "死亡" : $"{e.HpPercent:0}%");
                ImGui.TableSetColumnIndex(3);
                ImGui.TextUnformatted($"{e.Distance:0}m");
                ImGui.TableSetColumnIndex(4);
                ImGui.TextUnformatted($"({e.MapPos.X:0.0}, {e.MapPos.Y:0.0})");
            }
            ImGui.EndTable();
        }
    }

    // ===== 绘制辅助：行式卡片 =====

    private uint? rowBorder;
    private bool rowAuto;
    private Vector2 rowStart;
    private float rowWidth;
    private Vector2 rowRectMin, rowRectMax; // 固定高度卡片的背景/描边统一矩形（父 drawlist 自绘）
    private int rowId;                      // 当前卡片编号（子窗口时代用于 ID 稳定，现在用于控件 ID 去重）

    /// <summary>
    /// 开始一张行式卡片。height &gt; 0：固定高度（子窗口实现）；
    /// height == 0：高度随内容自适应（Group + 双通道绘制；不能用子窗口的 y=0——那会填满父容器剩余高度）。
    /// </summary>
    private void RowBegin(float height = 0, uint? border = null)
    {
        rowBorder = border;
        if (height > 0)
        {
            rowAuto = false;
            rowId = ++rowCounter;
            // 不用子窗口：背景/描边/内容全部画在父 drawlist 上（绝对坐标），
            // 彻底规避子窗口裁剪/几何差异导致的"绿框不包围卡片/卡片显示不全"
            rowRectMin = ImGui.GetCursorScreenPos();
            rowRectMax = new Vector2(rowRectMin.X + ImGui.GetContentRegionAvail().X, rowRectMin.Y + height);
            ImGui.GetWindowDrawList().AddRectFilled(rowRectMin, rowRectMax, ColCard, 14f); // 背景最先画（最底层）
            ImGui.SetCursorScreenPos(rowRectMin + new Vector2(14, 8)); // 内容起点（等效内边距）
        }
        else
        {
            rowAuto = true;
            rowStart = ImGui.GetCursorScreenPos();
            rowWidth = ImGui.GetContentRegionAvail().X;
            var dl = ImGui.GetWindowDrawList();
            dl.ChannelsSplit(2);
            dl.ChannelsSetCurrent(0); // 内容层
            ImGui.BeginGroup();
            ImGui.Indent(14f);
            ImGui.Dummy(new Vector2(0, 8)); // 顶部内边距
        }
    }

    private static int rowCounter;

    /// <summary>结束当前行式卡片。描边在所有内容之后补画，保证始终可见。</summary>
    private void RowEnd()
    {
        if (rowAuto)
        {
            var dl = ImGui.GetWindowDrawList();
            ImGui.Dummy(new Vector2(0, 8)); // 底部内边距
            ImGui.Unindent(14f);
            ImGui.EndGroup();
            var min = rowStart;
            var max = new Vector2(rowStart.X + Math.Max(rowWidth, ImGui.GetItemRectMax().X - rowStart.X), ImGui.GetItemRectMax().Y);
            dl.ChannelsSetCurrent(1); // 背景层（画在内容之下）
            dl.AddRectFilled(min, max, ColCard, 14f);
            dl.ChannelsMerge();
            if (rowBorder.HasValue) // 描边最后画：位于内容之上，不会被卡片盖住
                dl.AddRect(min + new Vector2(0.5f, 0.5f), max - new Vector2(0.5f, 0.5f), rowBorder.Value, 14f, 0, 1.5f);
            ImGui.Dummy(new Vector2(0, 8));
        }
        else
        {
            if (rowBorder.HasValue) // 描边最后画：位于内容之上，与背景同一矩形
            {
                var dl = ImGui.GetWindowDrawList();
                dl.AddRect(rowRectMin + new Vector2(0.5f, 0.5f), rowRectMax - new Vector2(0.5f, 0.5f), rowBorder.Value, 14f, 0, 1.5f);
            }
            // 注册与卡片等大的占位项：恢复排版流，调用方 SameLine 也能正确横排
            ImGui.SetCursorScreenPos(rowRectMin);
            ImGui.Dummy(new Vector2(rowRectMax.X - rowRectMin.X, rowRectMax.Y - rowRectMin.Y));
            ImGui.Dummy(new Vector2(0, 6)); // 卡片间距
        }
        rowBorder = null;
        rowAuto = false;
    }

    /// <summary>简单信息行：图标 + 标题/副标题 + 可选右侧文字 / 标签 / 按钮 / 描边。</summary>
    private void Row(string glyph, uint glyphCol, string title, string sub,
        string rightText = null, uint rightCol = 0,
        string tag = null, uint tagCol = 0,
        string btn = null, System.Action onBtn = null,
        uint? border = null)
    {
        RowBegin(58f, border);
        var dl = ImGui.GetWindowDrawList();
        var p = rowRectMin;
        var s = rowRectMax - rowRectMin;

        // 裁剪到卡片圆角矩形内，防止长文本溢出边框（招募预览等）
        dl.PushClipRect(p + new Vector2(2, 2), p + s - new Vector2(2, 2), true);

        // 图标盒
        var icS = 34f;
        var icPos = new Vector2(p.X + 14, p.Y + (s.Y - icS) / 2);
        dl.AddRectFilled(icPos, icPos + new Vector2(icS, icS), C(255, 255, 255, 15), 10f);
        dl.AddRect(icPos, icPos + new Vector2(icS, icS), ColLine, 10f);
        DrawIconCentered(glyph, icPos, new(icS, icS), glyphCol, 0.92f);

        // 标题 + 副标题（限制宽度，避免压到右侧元素）
        var fs = ImGui.GetFontSize();
        var tx = p.X + 14 + icS + 13;
        var rightMost = RightExtent(p.X, s.X, btn, tag, rightText) - 8f;
        var textClip = Math.Max(rightMost, tx + 60f);
        dl.AddText(ImGui.GetFont(), fs, new(tx, p.Y + s.Y / 2 - fs - 1), 0xFFFFFFFFu, TruncateTo(title, textClip - tx));
        if (!string.IsNullOrEmpty(sub))
            dl.AddText(ImGui.GetFont(), fs * 0.88f, new(tx, p.Y + s.Y / 2 + 1), ColSub, TruncateTo(sub, textClip - tx));

        // 右侧区域：从右往左依次为 按钮 / 标签 / 右侧文字
        var x = p.X + s.X - 14f;
        var cy = p.Y + s.Y / 2;
        var font = ImGui.GetFont();

        if (!string.IsNullOrEmpty(btn) && onBtn != null)
        {
            var bw = ImGui.CalcTextSize(btn).X + 24f;
            var bh = 26f;
            var bpos = new Vector2(x - bw, cy - bh / 2);
            ImGui.SetCursorScreenPos(bpos);
            if (ImGui.InvisibleButton("##rowbtn" + rowId, new(bw, bh)))
                onBtn();
            if (ImGui.IsItemHovered())
                dl.AddRectFilled(bpos, bpos + new Vector2(bw, bh), C(255, 107, 107, 38), bh / 2);
            dl.AddRect(bpos, bpos + new Vector2(bw, bh), C(255, 107, 107, 115), bh / 2);
            var ts = ImGui.CalcTextSize(btn);
            dl.AddText(font, fs * 0.9f, bpos + new Vector2((bw - ts.X) / 2, (bh - ts.Y) / 2), C(255, 176, 176, 255), btn);
            x -= bw + 10f;
        }

        if (!string.IsNullOrEmpty(tag))
        {
            var tw = ImGui.CalcTextSize(tag).X + 18f;
            var th = 20f;
            var tpos = new Vector2(x - tw, cy - th / 2);
            dl.AddRectFilled(tpos, tpos + new Vector2(tw, th), Rgba(tagCol, 0.14f), th / 2);
            var ts2 = ImGui.CalcTextSize(tag);
            dl.AddText(font, fs * 0.85f, tpos + new Vector2((tw - ts2.X) / 2, (th - ts2.Y) / 2), tagCol, tag);
            x -= tw + 10f;
        }

        if (!string.IsNullOrEmpty(rightText))
        {
            var tw = ImGui.CalcTextSize(rightText).X;
            dl.AddText(font, fs * 0.92f, new(x - tw, cy - fs * 0.46f), rightCol != 0 ? rightCol : ColTxt, rightText);
        }

        dl.PopClipRect();

        // 占位推进布局（文本均由 drawlist 绘制，不参与排版）
        ImGui.Dummy(new Vector2(0, 0));
        RowEnd();
    }

    /// <summary>行卡右侧区域（按钮/标签/右侧文字）合计占用的像素宽度。</summary>
    private static float RightExtent(float cardLeft, float cardW, string btn, string tag, string rightText)
    {
        var x = cardLeft + cardW - 14f;
        if (!string.IsNullOrEmpty(btn)) x -= ImGui.CalcTextSize(btn).X + 24f + 10f;
        if (!string.IsNullOrEmpty(tag)) x -= ImGui.CalcTextSize(tag).X + 18f + 10f;
        if (!string.IsNullOrEmpty(rightText)) x -= ImGui.CalcTextSize(rightText).X + 10f;
        return x;
    }

    /// <summary>按像素宽度截断文本并追加省略号。</summary>
    private static string TruncateTo(string text, float maxWidth)
    {
        if (string.IsNullOrEmpty(text) || ImGui.CalcTextSize(text).X <= maxWidth) return text;
        var ell = "…";
        var w = ImGui.CalcTextSize(ell).X;
        for (var i = text.Length - 1; i > 0; i--)
        {
            if (ImGui.CalcTextSize(text[..i]).X + w <= maxWidth)
                return text[..i] + ell;
        }
        return ell;
    }

    // ===== 绘制辅助：图标 =====

    /// <summary>用图标字体在矩形内居中绘制一个图标，返回图标宽度（图标字体不可用时返回 0 且不绘制）。</summary>
    private static float DrawIconCentered(string glyph, Vector2 rectPos, Vector2 rectSize, uint color, float scale = 1f)
    {
        var f = IconFont();
        if (f.IsNull) return 0f;
        ImGui.PushFont(f);
        var nat = ImGui.CalcTextSize(glyph);
        ImGui.PopFont();
        var ts = nat * scale;
        ImGui.GetWindowDrawList().AddText(f, ImGui.GetFontSize() * scale, rectPos + (rectSize - ts) / 2, color, glyph);
        return nat.X * scale;
    }

    /// <summary>在指定左上角按垂直居中绘制图标（用于行内布局），返回图标宽度。</summary>
    private static float DrawIconAt(string glyph, Vector2 pos, Vector2 area, uint color)
    {
        var f = IconFont();
        if (f.IsNull) return 0f;
        ImGui.PushFont(f);
        var ts = ImGui.CalcTextSize(glyph);
        ImGui.PopFont();
        ImGui.GetWindowDrawList().AddText(f, ImGui.GetFontSize(), new(pos.X, pos.Y + (area.Y - ts.Y) / 2), color, glyph);
        return ts.X;
    }

    /// <summary>图标按钮（悬停圆角背景）。</summary>
    private static bool IconButton(string id, string glyph, Vector2 size)
    {
        var pos = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton(id, size);
        var hovered = ImGui.IsItemHovered();
        var active = ImGui.IsItemActive();
        var dl = ImGui.GetWindowDrawList();
        if (hovered || active)
            dl.AddRectFilled(pos, pos + size, active ? C(255, 255, 255, 31) : C(255, 255, 255, 18), 8f);
        DrawIconCentered(glyph, pos, size, hovered || active ? 0xFFFFFFFFu : C(174, 185, 198, 255), 0.88f);
        return clicked;
    }

    /// <summary>胶囊幽灵按钮（可选图标 + 文本，warn 红色调）。</summary>
    private static bool GhostButton(string id, string glyph, string label, bool warn = false)
    {
        var fs = ImGui.GetFontSize();
        var its = DrawIconSize(glyph);
        var iw = its.X > 0 ? its.X + 7f : 0f;
        var tw = ImGui.CalcTextSize(label).X;
        var h = ImGui.GetFrameHeight();
        var size = new Vector2(iw + tw + 26f, h);
        var pos = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton(id, size);
        var hovered = ImGui.IsItemHovered();
        var dl = ImGui.GetWindowDrawList();
        if (hovered)
            dl.AddRectFilled(pos, pos + size, warn ? C(255, 107, 107, 30) : C(255, 255, 255, 18), h / 2);
        dl.AddRect(pos, pos + size, warn ? C(255, 107, 107, 115) : ColLine2, h / 2);
        var txtCol = warn ? C(255, 176, 176, 255) : C(223, 231, 240, 255);
        var x = pos.X + 13f;
        if (its.X > 0)
        {
            dl.AddText(IconFont(), fs, new(x, pos.Y + (size.Y - its.Y) / 2), txtCol, glyph);
            x += iw;
        }
        var ts2 = ImGui.CalcTextSize(label);
        dl.AddText(ImGui.GetFont(), fs, new(x, pos.Y + (size.Y - ts2.Y) / 2), txtCol, label);
        return clicked;
    }

    private static Vector2 DrawIconSize(string glyph)
    {
        var f = IconFont();
        if (f.IsNull) return default;
        ImGui.PushFont(f);
        var s = ImGui.CalcTextSize(glyph);
        ImGui.PopFont();
        return s;
    }

    // ===== 绘制辅助：颜色 =====

    /// <summary>取颜色的 RGB、替换 alpha（0-1）。</summary>
    private static uint Rgba(uint col, float alpha)
    {
        var f = ImGui.ColorConvertU32ToFloat4(col);
        f.W = alpha;
        return ImGui.ColorConvertFloat4ToU32(f);
    }

    /// <summary>RGBA 字面量（0-255），等价 CSS rgba(r,g,b,a/255)。</summary>
    private static uint C(int r, int g, int b, int a) => (uint)((a << 24) | (b << 16) | (g << 8) | r);

    // ===== 绘制辅助：toggle 开关行 =====

    private void ToggleRow(string label, string sub, string id, ref bool value, string tooltip = null)
    {
        var x0 = ImGui.GetCursorPosX();
        var avail = ImGui.GetContentRegionAvail().X;
        var labelW = ImGui.CalcTextSize(label).X;
        var switchW = ImGui.GetFrameHeight() * 0.72f * 1.8f;
        ImGui.TextUnformatted(label);

        if (labelW + 16 + switchW + RightPad <= avail)
        {
            ImGui.SameLine(0, Math.Max(8, avail - labelW - switchW - RightPad));
        }
        else
        {
            ImGui.SetCursorPosX(x0 + avail - switchW - RightPad);
        }
        var v = DrawSwitch(id, value);
        if (v != value)
        {
            value = v;
            EzConfig.Save();
        }
        if (tooltip != null && ImGui.IsItemHovered())
            ImGui.SetTooltip(tooltip);
        if (sub != null)
        {
            ImGui.TextColored(ColSub, sub);
            ImGui.Dummy(new Vector2(0, 4)); // 开关行之间留出间隔，避免相互挨着
        }
    }

    /// <summary>伪加粗文本（同位置双重绘制；内置字体无粗体）。</summary>
    private void BoldLabel(string text)
    {
        var dl = ImGui.GetWindowDrawList();
        var p = ImGui.GetCursorScreenPos();
        dl.AddText(p, ColTxt, text);
        dl.AddText(p + new Vector2(0.7f, 0), ColTxt, text);
        ImGui.Dummy(new Vector2(ImGui.CalcTextSize(text).X, ImGui.GetTextLineHeight()));
    }

    private static bool DrawSwitch(string id, bool value)
    {
        var h = ImGui.GetFrameHeight() * 0.72f;
        var w = h * 1.8f;
        var pos = ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton(id, new Vector2(w, h));
        if (ImGui.IsItemClicked(ImGuiMouseButton.Left)) value = !value;
        var dl = ImGui.GetWindowDrawList();
        var bg = value ? C(46, 158, 99, 255) : C(255, 255, 255, 41);
        dl.AddRectFilled(pos, pos + new Vector2(w, h), bg, h * 0.5f);
        var cx = value ? pos.X + w - h / 2 : pos.X + h / 2;
        dl.AddCircleFilled(new Vector2(cx, pos.Y + h / 2), h / 2 - 2.5f, C(233, 238, 244, 255));
        return value;
    }
}
