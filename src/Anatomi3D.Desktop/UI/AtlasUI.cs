using System.Numerics;
using Anatomi3D.Core.Content;
using Anatomi3D.Core.Model;
using Anatomi3D.Core.Search;
using Anatomi3D.Desktop.App;
using ImGuiNET;

namespace Anatomi3D.Desktop.UI;

/// <summary>
/// Atlas arayüzü: üst çubuk ve küresel arama, sol panel (sistemler / yapılar / bölgeler), sağ bilgi paneli,
/// alt araç çubuğu, quiz, üzerine gelme ipucu, seçim etiketi, yön göstergesi, bildirimler ve iletişim kutuları.
/// </summary>
public sealed partial class AtlasUI : IDisposable
{
    private readonly AtlasApp app;
    private AnatomyModel Model => app.Model!;
    private AtlasState S => app.State!;
    private Fonts F => app.Gui.Fonts;

    public bool NeedsRedraw { get; private set; }

    // düzen
    private float M, TopH, LeftW, RightW;
    private Vector2 Display;
    private bool leftOpen = true, rightOpen = true;
    private float leftAnim = 1, rightAnim = 1;

    // arama
    private string search = "";
    private string lastSearch = "";
    private List<SearchIndex.Hit> hits = [];
    private int searchCursor;
    private bool focusSearch;
    private bool searchActive;

    // bildirim ve efektler
    private string? toast;
    private float toastTime = -10;
    private readonly List<(Vector3 world, float size, float start)> ripples = [];
    private int lastSelected = -1;
    private float selectedTime;
    private bool showSettings, showHelp, showWelcome;

    public AtlasUI(AtlasApp app)
    {
        this.app = app;
        showWelcome = app.Settings.FirstRun;
        L.Lang = app.State!.Lang;
        BuildTree();
    }

    public void Draw(float dt)
    {
        NeedsRedraw = false;
        L.Lang = S.Lang;
        var io = ImGui.GetIO();
        Display = io.DisplaySize;
        float sc = Theme.S;
        M = 12 * sc;
        TopH = 58 * sc;
        LeftW = MathF.Min(340 * sc, Display.X * 0.26f);
        RightW = MathF.Min(392 * sc, Display.X * 0.3f);
        leftAnim = Ui.Animate(ImGui.GetID("##leftAnim"), leftOpen ? 1 : 0, 12);
        rightAnim = Ui.Animate(ImGui.GetID("##rightAnim"), rightOpen && (S.Selected >= 0 || S.Quiz == null) ? 1 : 0, 12);
        if (S.Selected != lastSelected)
        {
            if (S.Selected >= 0) { rightOpen = true; selectedTime = app.Time; }
            lastSelected = S.Selected;
            infoScrollReset = true;
        }

        ImGui.PushFont(F.Body);
        DrawOverlays3D();
        DrawTopBar();
        if (leftAnim > 0.01f) DrawLeftPanel();
        if (rightAnim > 0.01f) DrawInfoPanel();
        DrawToolbar();
        DrawDigBar();
        DrawQuiz();
        DrawSearchResults();
        DrawHoverTooltip();
        DrawStatus();
        DrawToasts();
        if (showWelcome) DrawWelcome();
        if (showGlyphs) DrawGlyphTest();
        DrawDialogs();
        ImGui.PopFont();

        if (Ui.AnyAnimating() || ripples.Count > 0 || app.Time - toastTime < 3.5f) NeedsRedraw = true;
    }

    // ------------------------------------------------------------------------------------------------ üst çubuk
    private void DrawTopBar()
    {
        float sc = Theme.S;
        var pos = new Vector2(0, 0);
        var size = new Vector2(Display.X, TopH);
        ImGui.SetNextWindowPos(pos);
        ImGui.SetNextWindowSize(size);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(M, 0));
        ImGui.PushStyleColor(ImGuiCol.WindowBg, Theme.Rgb(0x10141b, 0.97f));
        ImGui.Begin("##topbar", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoBringToFrontOnFocus);
        var dl = ImGui.GetWindowDrawList();
        dl.AddLine(new Vector2(0, TopH - 1), new Vector2(Display.X, TopH - 1), Theme.U32(Theme.Border));

        float cy = TopH * 0.5f;
        // menü (sol panel) düğmesi
        ImGui.SetCursorPos(new Vector2(M, cy - Theme.Px(17)));
        if (Ui.IconButton(Icons.Menu, "menu", L.T("systems"), leftOpen)) leftOpen = !leftOpen;

        // logo + ad
        var lp = new Vector2(M + Theme.Px(46), cy - Theme.Px(15));
        DrawLogo(dl, lp, Theme.Px(30));
        ImGui.PushFont(F.Heading);
        dl.AddText(lp + new Vector2(Theme.Px(40), Theme.Px(-3)), Theme.U32(Theme.Text), L.T("appTitle"));
        ImGui.PopFont();
        ImGui.PushFont(F.Small);
        dl.AddText(lp + new Vector2(Theme.Px(41), Theme.Px(18)), Theme.U32(Theme.TextMuted), L.T("appSubtitle"));
        ImGui.PopFont();

        // arama kutusu (ortada)
        float sw = MathF.Min(Theme.Px(520), Display.X * 0.36f);
        float sx = MathF.Max(Theme.Px(300), (Display.X - sw) * 0.5f);
        DrawSearchBox(new Vector2(sx, cy - Theme.Px(18)), sw);

        // sağ taraf: mod, dil, ayarlar, yardım, tam ekran
        float x = Display.X - M;
        float bs = Theme.Px(34);
        x -= bs;
        ImGui.SetCursorPos(new Vector2(x, cy - bs / 2));
        if (Ui.IconButton(app.Window.Fullscreen ? Icons.BackToWindow : Icons.FullScreen, "fs", L.T("fullscreen"))) app.Window.ToggleFullscreen();
        x -= bs + Theme.Px(2);
        ImGui.SetCursorPos(new Vector2(x, cy - bs / 2));
        if (Ui.IconButton(Icons.Help, "help", L.T("help"), showHelp)) showHelp = !showHelp;
        x -= bs + Theme.Px(2);
        ImGui.SetCursorPos(new Vector2(x, cy - bs / 2));
        if (Ui.IconButton(Icons.Settings, "settings", L.T("settings"), showSettings)) showSettings = !showSettings;

        // dil
        x -= Theme.Px(92);
        ImGui.SetCursorPos(new Vector2(x, cy - Theme.Px(16)));
        int lang = S.Lang == Lang.Tr ? 0 : 1;
        if (Ui.Segmented("lang", ["TR", "EN"], ref lang, Theme.Px(84)))
        {
            S.Lang = lang == 0 ? Lang.Tr : Lang.En;
            app.Settings.Language = S.Lang;
            app.Settings.Save();
            L.Lang = S.Lang;
            BuildTree();
            S.Changed();
        }
        // mod: keşfet / quiz
        float qw = Theme.Px(190);
        x -= qw + Theme.Px(10);
        ImGui.SetCursorPos(new Vector2(x, cy - Theme.Px(16)));
        int mode = S.Quiz != null ? 1 : 0;
        if (Ui.Segmented("mode", [Icons.Search + "  " + L.T("explore"), Icons.Education + "  " + L.T("quiz")], ref mode, qw))
        {
            if (mode == 1) StartQuiz(); else S.Quiz = null;
            S.Changed();
        }
        ImGui.End();
        ImGui.PopStyleColor();
        ImGui.PopStyleVar(3);
    }

    /// <summary>Uygulama logosu: gradyanlı yuvarlak kare içinde stilize insan figürü.</summary>
    public static void DrawLogo(ImDrawListPtr dl, Vector2 p, float s)
    {
        uint c0 = Theme.U32(0xff7a6b), c1 = Theme.U32(0x3e8ef7);
        dl.AddRectFilledMultiColor(p, p + new Vector2(s, s), c0, Theme.U32(0xb07cc0), c1, Theme.U32(0x9b83d6));
        // köşeleri yuvarlat: arka plan rengiyle maske
        float r = s * 0.24f;
        var bg = Theme.U32(0x10141b);
        for (int k = 0; k < 4; k++)
        {
            var corner = p + new Vector2(k % 2 == 0 ? 0 : s, k < 2 ? 0 : s);
            var dir = new Vector2(k % 2 == 0 ? 1 : -1, k < 2 ? 1 : -1);
            dl.PathLineTo(corner);
            for (int i = 0; i <= 8; i++)
            {
                float a = i / 8f * MathF.PI / 2;
                dl.PathLineTo(corner + dir * r - new Vector2(dir.X * r * MathF.Cos(a), dir.Y * r * MathF.Sin(a)));
            }
            dl.PathFillConvex(bg);
        }
        uint w = Theme.U32(0xffffff);
        float u = s / 32f;
        dl.AddCircleFilled(p + new Vector2(16, 7.5f) * u, 3.4f * u, w, 16);
        dl.AddRectFilled(p + new Vector2(12.2f, 11.5f) * u, p + new Vector2(19.8f, 20) * u, w, 2.5f * u);
        dl.AddTriangleFilled(p + new Vector2(12.4f, 12.2f) * u, p + new Vector2(7.5f, 19) * u, p + new Vector2(9.2f, 19.8f) * u, w);
        dl.AddTriangleFilled(p + new Vector2(19.6f, 12.2f) * u, p + new Vector2(24.5f, 19) * u, p + new Vector2(22.8f, 19.8f) * u, w);
        dl.AddRectFilled(p + new Vector2(12.6f, 19) * u, p + new Vector2(15.4f, 27) * u, w, 1.2f * u);
        dl.AddRectFilled(p + new Vector2(16.6f, 19) * u, p + new Vector2(19.4f, 27) * u, w, 1.2f * u);
    }

    // ------------------------------------------------------------------------------------------------ arama
    private Vector2 searchPos;
    private float searchW;

    private void DrawSearchBox(Vector2 pos, float width)
    {
        searchPos = pos;
        searchW = width;
        float h = Theme.Px(36);
        var dl = ImGui.GetWindowDrawList();
        var sp = ImGui.GetWindowPos() + pos;
        bool active = searchActive;
        dl.AddRectFilled(sp, sp + new Vector2(width, h), Theme.U32(active ? Theme.SurfaceHi : Theme.Surface), Theme.Px(10));
        if (active) dl.AddRect(sp, sp + new Vector2(width, h), Theme.U32(Theme.Rgb(0x3e8ef7, 0.7f)), Theme.Px(10), ImDrawFlags.None, 1.5f);
        dl.AddText(sp + new Vector2(Theme.Px(12), (h - ImGui.GetTextLineHeight()) * 0.5f), Theme.U32(Theme.TextMuted), Icons.Search);
        // kısayol ipucu
        string kbd = "Ctrl K";
        ImGui.PushFont(F.Small);
        var ks = ImGui.CalcTextSize(kbd);
        var kp = sp + new Vector2(width - ks.X - Theme.Px(20), (h - ks.Y) * 0.5f);
        if (search.Length == 0)
        {
            dl.AddRectFilled(kp - new Vector2(Theme.Px(6), Theme.Px(2)), kp + ks + new Vector2(Theme.Px(6), Theme.Px(2)), Theme.U32(Theme.SurfaceHover), Theme.Px(5));
            dl.AddText(kp, Theme.U32(Theme.TextMuted), kbd);
        }
        ImGui.PopFont();

        ImGui.SetCursorPos(pos + new Vector2(Theme.Px(34), (h - ImGui.GetFrameHeight()) * 0.5f));
        ImGui.PushItemWidth(width - Theme.Px(34) - (search.Length > 0 ? Theme.Px(36) : Theme.Px(70)));
        ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0, 0, 0, 0));
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(0, 0, 0, 0));
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, new Vector4(0, 0, 0, 0));
        if (focusSearch)
        {
            ImGui.SetKeyboardFocusHere();
            focusSearch = false;
        }
        ImGui.InputTextWithHint("##search", L.T("searchHint"), ref search, 128);
        searchActive = ImGui.IsItemActive();
        bool enter = searchActive && ImGui.IsKeyPressed(ImGuiKey.Enter);
        if (searchActive && ImGui.IsKeyPressed(ImGuiKey.DownArrow)) searchCursor = Math.Min(hits.Count - 1, searchCursor + 1);
        if (searchActive && ImGui.IsKeyPressed(ImGuiKey.UpArrow)) searchCursor = Math.Max(0, searchCursor - 1);
        if (ImGui.IsItemDeactivated() && ImGui.IsKeyPressed(ImGuiKey.Enter) && hits.Count > 0) enter = true;
        if (searchActive && ImGui.IsKeyPressed(ImGuiKey.Escape)) search = "";
        ImGui.PopStyleColor(3);
        ImGui.PopItemWidth();
        if (search.Length > 0)
        {
            ImGui.SetCursorPos(pos + new Vector2(width - Theme.Px(34), (h - Theme.Px(28)) * 0.5f));
            if (Ui.IconButton(Icons.Close, "clearSearch", null, false, 28)) search = "";
        }
        if (search != lastSearch)
        {
            lastSearch = search;
            hits = search.Trim().Length > 0 ? SearchIndex.Query(Model.Parts, search, 60) : [];
            searchCursor = 0;
        }
        if (enter && hits.Count > 0) PickSearchResult(hits[Math.Clamp(searchCursor, 0, hits.Count - 1)].Part);
    }

    private bool searchHover;

    private void DrawSearchResults()
    {
        if (search.Trim().Length == 0 || (!searchActive && !searchHover)) { searchHover = false; return; }
        float sc = Theme.S;
        float rowH = Theme.Px(44);
        int shown = Math.Min(hits.Count, 9);
        float h = hits.Count == 0 ? Theme.Px(56) : shown * rowH + Theme.Px(38);
        var pos = new Vector2(searchPos.X, TopH - Theme.Px(4));
        ImGui.SetNextWindowPos(pos);
        ImGui.SetNextWindowSize(new Vector2(searchW, h));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(Theme.Px(6), Theme.Px(6)));
        ImGui.PushStyleColor(ImGuiCol.WindowBg, Theme.Rgb(0x171c26, 0.99f));
        ImGui.Begin("##searchResults", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoSavedSettings |
                                       ImGuiWindowFlags.NoFocusOnAppearing | (shown < hits.Count ? 0 : ImGuiWindowFlags.NoScrollbar));
        searchHover = ImGui.IsWindowHovered(ImGuiHoveredFlags.AllowWhenBlockedByActiveItem);
        if (hits.Count == 0)
        {
            ImGui.SetCursorPos(new Vector2(Theme.Px(14), Theme.Px(16)));
            Ui.TextMuted(L.T("noResults"));
        }
        else
        {
            ImGui.PushFont(F.Small);
            ImGui.SetCursorPos(new Vector2(Theme.Px(12), Theme.Px(8)));
            Ui.TextMuted($"{hits.Count}{(hits.Count >= 60 ? "+" : "")} {L.T("results")}  ·  ↑↓ Enter", false);
            ImGui.PopFont();
            ImGui.BeginChild("##hits", new Vector2(0, 0));
            var dl = ImGui.GetWindowDrawList();
            for (int i = 0; i < hits.Count; i++)
            {
                var p = hits[i].Part;
                var rp = ImGui.GetCursorScreenPos();
                float w = ImGui.GetContentRegionAvail().X;
                bool clicked = ImGui.InvisibleButton("h" + i, new Vector2(w, rowH));
                bool hov = ImGui.IsItemHovered();
                if (hov) searchCursor = i;
                bool cur = i == searchCursor;
                if (cur) dl.AddRectFilled(rp, rp + new Vector2(w, rowH), Theme.U32(Theme.SurfaceHover), Theme.Px(8));
                var info = p.Info;
                dl.AddCircleFilled(rp + new Vector2(Theme.Px(16), rowH * 0.5f), Theme.Px(5), Theme.U32(new Vector4(info.Color, 1)));
                string main = p.DisplayName(S.Lang);
                string? sub = p.SecondaryName(S.Lang);
                dl.AddText(rp + new Vector2(Theme.Px(32), Theme.Px(5)), Theme.U32(Theme.Text), Ui.Ellipsize(main, w - Theme.Px(150)));
                ImGui.PushFont(F.Small);
                string cat = S.Lang == Lang.Tr ? info.Tr : info.En;
                var cs = ImGui.CalcTextSize(cat);
                dl.AddText(rp + new Vector2(w - cs.X - Theme.Px(12), Theme.Px(6)), Theme.U32(Theme.TextMuted), cat);
                if (sub != null) dl.AddText(rp + new Vector2(Theme.Px(32), Theme.Px(24)), Theme.U32(Theme.TextMuted), Ui.Ellipsize(sub, w - Theme.Px(50)));
                ImGui.PopFont();
                if (cur && searchActive) ImGui.SetScrollHereY();
                if (clicked) PickSearchResult(p);
            }
            ImGui.EndChild();
        }
        ImGui.End();
        ImGui.PopStyleColor();
        ImGui.PopStyleVar();
    }

    private void PickSearchResult(Part p)
    {
        // gizli kategorideyse görünür yap, soyulmuş katmandaysa geri getir
        S.SetCategories([p.Category], false);
        S.SetPartsHidden([p.Index], false);
        if (Categories.IsPeeled(p.Category, S.PeelStep))
        {
            var info = p.Info;
            if (info.PeelStep.HasValue) S.SetPeel(info.PeelStep.Value - 1);
        }
        S.Select(p.Index, focus: true);
        search = "";
        searchHover = false;
        ImGui.SetWindowFocus(null);
    }

    public void FocusSearch() => focusSearch = true;
    public void SetSearch(string text) { search = text; searchActive = true; focusSearch = true; }

    // ------------------------------------------------------------------------------------------------ 3B katman: etiket, dalga efekti, yön göstergesi
    private Vector2? Project(Vector3 world)
    {
        var st = app.Camera.State(Display.X / MathF.Max(1, Display.Y));
        var h = Vector4.Transform(new Vector4(world, 1), st.View * st.Proj);
        if (h.W <= 0.001f) return null;
        var ndc = new Vector2(h.X, h.Y) / h.W;
        return new Vector2((ndc.X * 0.5f + 0.5f) * Display.X, (0.5f - ndc.Y * 0.5f) * Display.Y);
    }

    private void DrawOverlays3D()
    {
        var dl = ImGui.GetBackgroundDrawList();
        // tıklama dalgaları
        for (int i = ripples.Count - 1; i >= 0; i--)
        {
            var (w, size, start) = ripples[i];
            float t = (app.Time - start) / 0.75f;
            if (t > 1.6f) { ripples.RemoveAt(i); continue; }
            var sp = Project(w);
            if (sp == null) continue;
            for (int k = 0; k < 2; k++)
            {
                float tk = t - k * 0.19f;
                if (tk < 0 || tk > 1) continue;
                float e = 1 - MathF.Pow(1 - tk, 3);
                float r = Theme.Px(8) + Theme.Px(70) * e * (k == 1 ? 0.75f : 1f);
                dl.AddCircle(sp.Value, r, Theme.U32(0x8fdcff, (1 - tk) * (k == 1 ? 0.5f : 0.85f)), 48, Theme.Px(2.2f));
            }
        }
        // seçili yapının etiketi (işaret çizgisiyle)
        if (S.Selected >= 0 && S.Quiz == null)
        {
            var p = Model.Parts[S.Selected];
            var anchor = Project(p.Center);
            if (anchor is { } a && a.X > 0 && a.Y > TopH && a.X < Display.X && a.Y < Display.Y)
            {
                float appear = Math.Clamp((app.Time - selectedTime) / 0.35f, 0, 1);
                string name = p.DisplayName(S.Lang);
                ImGui.PushFont(F.BodyBold);
                var ts = ImGui.CalcTextSize(name);
                ImGui.PopFont();
                var off = new Vector2(Theme.Px(46), -Theme.Px(46));
                var box0 = a + off;
                if (box0.X + ts.X + Theme.Px(28) > Display.X - (rightAnim * (RightW + M))) box0.X = a.X - off.X - ts.X - Theme.Px(28);
                var box1 = box0 + ts + new Vector2(Theme.Px(24), Theme.Px(14));
                uint lineCol = Theme.U32(Theme.Rgb(0xffffff, 0.75f * appear));
                dl.AddCircleFilled(a, Theme.Px(4), Theme.U32(Theme.Rgb(0xffffff, appear)));
                dl.AddCircle(a, Theme.Px(7), Theme.U32(Theme.Rgb(0x3e8ef7, appear)), 20, Theme.Px(2));
                var elbow = new Vector2(box0.X < a.X ? box1.X : box0.X, (box0.Y + box1.Y) * 0.5f);
                dl.AddLine(a, elbow, lineCol, Theme.Px(1.5f));
                dl.AddRectFilled(box0, box1, Theme.U32(Theme.Rgb(0x10141b, 0.9f * appear)), Theme.Px(8));
                dl.AddRect(box0, box1, Theme.U32(Theme.Rgb(0x3e8ef7, 0.6f * appear)), Theme.Px(8));
                ImGui.PushFont(F.BodyBold);
                dl.AddText(box0 + new Vector2(Theme.Px(12), Theme.Px(7)), Theme.U32(Theme.Rgb(0xffffff, appear)), name);
                ImGui.PopFont();
            }
        }
        DrawGizmo(dl);
    }

    /// <summary>Yön göstergesi: anatomik eksenler (S/I üst-alt, A/P ön-arka, L/R sol-sağ); tıklayınca o yöne bakar.</summary>
    private void DrawGizmo(ImDrawListPtr dl)
    {
        float r = Theme.Px(38);
        var c = new Vector2(Display.X - (rightAnim * (RightW + M)) - M - r - Theme.Px(14), Display.Y - Theme.Px(150) - r);
        var st = app.Camera.State(1);
        var axes = new (Vector3 dir, string label, uint col, CameraPreset preset)[]
        {
            (Vector3.UnitY, "S", Theme.U32(0x46a758), CameraPreset.Top), (-Vector3.UnitY, "I", Theme.U32(0x46a758, 0.55f), CameraPreset.Bottom),
            (Vector3.UnitZ, "A", Theme.U32(0x3e8ef7), CameraPreset.Front), (-Vector3.UnitZ, "P", Theme.U32(0x3e8ef7, 0.55f), CameraPreset.Back),
            (Vector3.UnitX, "L", Theme.U32(0xe5484d), CameraPreset.Left), (-Vector3.UnitX, "R", Theme.U32(0xe5484d, 0.55f), CameraPreset.Right),
        };
        dl.AddCircleFilled(c, r + Theme.Px(12), Theme.U32(0x10141b, 0.55f), 40);
        var io = ImGui.GetIO();
        var list = axes.Select(a =>
        {
            var v = Vector3.TransformNormal(a.dir, st.View);
            return (a, v);
        }).OrderBy(x => x.v.Z).ToList();
        foreach (var (a, v) in list)
        {
            var end = c + new Vector2(v.X, -v.Y) * r;
            bool pos = a.label is "S" or "A" or "L";
            if (pos) dl.AddLine(c, end, a.col, Theme.Px(2));
            float br = Theme.Px(pos ? 9 : 7);
            bool hov = Vector2.Distance(io.MousePos, end) < br + 2 && !io.WantCaptureMouse;
            dl.AddCircleFilled(end, br + (hov ? Theme.Px(2) : 0), a.col, 20);
            ImGui.PushFont(F.SmallBold);
            var ts = ImGui.CalcTextSize(a.label);
            dl.AddText(end - ts * 0.5f, Theme.U32(0xffffff, pos ? 1f : 0.8f), a.label);
            ImGui.PopFont();
            if (hov)
            {
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                string tip = a.label switch
                {
                    "S" => S.Lang == Lang.Tr ? "Üst (superior)" : "Superior", "I" => S.Lang == Lang.Tr ? "Alt (inferior)" : "Inferior",
                    "A" => S.Lang == Lang.Tr ? "Ön (anterior)" : "Anterior", "P" => S.Lang == Lang.Tr ? "Arka (posterior)" : "Posterior",
                    "L" => S.Lang == Lang.Tr ? "Sol (sinister)" : "Left", _ => S.Lang == Lang.Tr ? "Sağ (dexter)" : "Right",
                };
                Ui.Tooltip(tip);
                if (ImGui.IsMouseClicked(ImGuiMouseButton.Left)) S.RequestCamera(a.preset);
            }
        }
    }

    public void Ripple(Vector3 world, float radius) => ripples.Add((world, radius, app.Time));

    // ------------------------------------------------------------------------------------------------ üzerine gelme ipucu
    private void DrawHoverTooltip()
    {
        var io = ImGui.GetIO();
        if (S.Hovered < 0 || io.WantCaptureMouse || S.Quiz != null || ImGui.IsMouseDown(ImGuiMouseButton.Left) || ImGui.IsMouseDown(ImGuiMouseButton.Right)) return;
        var p = Model.Parts[S.Hovered];
        string main = p.DisplayName(S.Lang);
        string? sub = p.SecondaryName(S.Lang);
        var mp = io.MousePos + new Vector2(Theme.Px(16), Theme.Px(18));
        ImGui.PushFont(F.BodyBold);
        var ms = ImGui.CalcTextSize(main);
        ImGui.PopFont();
        ImGui.PushFont(F.Small);
        var ss = sub != null ? ImGui.CalcTextSize(sub) : Vector2.Zero;
        ImGui.PopFont();
        float w = MathF.Max(ms.X, ss.X) + Theme.Px(36), h = ms.Y + (sub != null ? ss.Y + Theme.Px(2) : 0) + Theme.Px(16);
        if (mp.X + w > Display.X) mp.X = io.MousePos.X - w - Theme.Px(8);
        if (mp.Y + h > Display.Y) mp.Y = io.MousePos.Y - h - Theme.Px(8);
        var dl = ImGui.GetForegroundDrawList();
        dl.AddRectFilled(mp + new Vector2(0, Theme.Px(3)), mp + new Vector2(w, h + Theme.Px(3)), Theme.U32(0x000000, 0.25f), Theme.Px(9));
        dl.AddRectFilled(mp, mp + new Vector2(w, h), Theme.U32(0x0f131a, 0.96f), Theme.Px(9));
        dl.AddRect(mp, mp + new Vector2(w, h), Theme.U32(Theme.Border), Theme.Px(9));
        dl.AddCircleFilled(mp + new Vector2(Theme.Px(14), Theme.Px(8) + ms.Y * 0.5f), Theme.Px(4.5f), Theme.U32(new Vector4(p.Info.Color, 1)));
        ImGui.PushFont(F.BodyBold);
        dl.AddText(mp + new Vector2(Theme.Px(26), Theme.Px(8)), Theme.U32(Theme.Text), main);
        ImGui.PopFont();
        if (sub != null)
        {
            ImGui.PushFont(F.Small);
            dl.AddText(mp + new Vector2(Theme.Px(26), Theme.Px(10) + ms.Y), Theme.U32(Theme.TextMuted), sub);
            ImGui.PopFont();
        }
    }

    // ------------------------------------------------------------------------------------------------ kazı çubuğu
    private void DrawDigBar()
    {
        if (S.Dug.Count == 0 || S.Quiz != null) return;
        int n = S.Dug.Sum(d => d.Parts.Length);
        var last = Model.Parts[S.Dug[^1].Part];
        string text = $"{n} {L.T("digRemoved")}  ·  {Ui.Ellipsize(last.DisplayName(S.Lang), Theme.Px(260))}";
        var ts = ImGui.CalcTextSize(text);
        float bw1 = ImGui.CalcTextSize(Icons.Undo + "  " + L.T("undo")).X + Theme.Px(24);
        float bw2 = ImGui.CalcTextSize(L.T("restoreAll")).X + Theme.Px(24);
        float w = ts.X + bw1 + bw2 + Theme.Px(56), h = Theme.Px(48);
        var pos = new Vector2((Display.X - w) * 0.5f, TopH + M);
        Ui.BeginPanel("##digbar", pos, new Vector2(w, h), 1, ImGuiWindowFlags.NoScrollbar);
        ImGui.SetCursorPos(new Vector2(Theme.Px(16), (h - ts.Y) * 0.5f));
        ImGui.TextUnformatted(text);
        ImGui.SameLine(0, Theme.Px(14));
        ImGui.SetCursorPosY((h - Theme.Px(32)) * 0.5f);
        if (Ui.Button(Icons.Undo, L.T("undo"), "undo", height: 32)) S.UndoDig();
        ImGui.SameLine(0, Theme.Px(6));
        ImGui.SetCursorPosY((h - Theme.Px(32)) * 0.5f);
        if (Ui.Button("", L.T("restoreAll"), "restore", height: 32)) S.RestoreDigs();
        Ui.EndPanel();
    }

    // ------------------------------------------------------------------------------------------------ durum çubuğu ve bildirimler
    private void DrawStatus()
    {
        var dl = ImGui.GetBackgroundDrawList();
        ImGui.PushFont(F.Small);
        var r = app.Renderer!;
        string txt = $"{Model.Parts.Count(p => !p.IsAttachment):N0} {L.T("structuresCount")}  ·  {r.Stats.VisibleParts:N0} {L.T("visible")}";
        if (app.Settings.ShowFps)
            txt += $"  ·  {app.Fps:F0} FPS  ·  GPU {r.Profiler.TotalMs:F1} ms  ·  {r.Stats.Triangles / 1e6:F2}M {L.T("triangles")}  ·  {app.Device.AdapterName}  ·  MSAA {r.Stats.Samples}x";
        float x = (leftAnim > 0.5f ? LeftW + 2 * M : M);
        dl.AddText(new Vector2(x + Theme.Px(2), Display.Y - Theme.Px(24)), Theme.U32(Theme.TextMuted), txt);
        string credit = "3D model: Z-Anatomy (CC BY-SA 4.0)";
        var cs = ImGui.CalcTextSize(credit);
        float rx = Display.X - (rightAnim > 0.5f ? RightW + 2 * M : M) - cs.X - Theme.Px(2);
        dl.AddText(new Vector2(rx, Display.Y - Theme.Px(24)), Theme.U32(Theme.Rgb(0x6f7888, 0.8f)), credit);
        ImGui.PopFont();
    }

    public void Toast(string msg)
    {
        toast = msg;
        toastTime = app.Time;
        NeedsRedraw = true;
    }

    private void DrawToasts()
    {
        if (toast == null) return;
        float age = app.Time - toastTime;
        if (age > 3.2f) { toast = null; return; }
        float a = Math.Clamp(MathF.Min(age / 0.2f, (3.2f - age) / 0.4f), 0, 1);
        var ts = ImGui.CalcTextSize(Icons.Check + "  " + toast);
        var size = ts + new Vector2(Theme.Px(32), Theme.Px(20));
        var pos = new Vector2((Display.X - size.X) * 0.5f, Display.Y - Theme.Px(150) - size.Y + (1 - a) * Theme.Px(12));
        var dl = ImGui.GetForegroundDrawList();
        dl.AddRectFilled(pos, pos + size, Theme.U32(Theme.Rgb(0x1b2330, 0.97f * a)), Theme.Px(10));
        dl.AddRect(pos, pos + size, Theme.U32(Theme.Rgb(0x30a46c, 0.7f * a)), Theme.Px(10));
        dl.AddText(pos + new Vector2(Theme.Px(16), Theme.Px(10)), Theme.U32(Theme.Rgb(0xffffff, a)), Icons.Check + "  " + toast);
    }

    // ------------------------------------------------------------------------------------------------ hoş geldin
    private void DrawWelcome()
    {
        float w = Theme.Px(460), h = Theme.Px(250);
        var pos = new Vector2((Display.X - w) * 0.5f, (Display.Y - h) * 0.5f + Theme.Px(40));
        Ui.BeginPanel("##welcome", pos, new Vector2(w, h), 1, ImGuiWindowFlags.NoScrollbar);
        ImGui.SetCursorPos(new Vector2(Theme.Px(24), Theme.Px(22)));
        ImGui.PushFont(F.Heading);
        ImGui.TextUnformatted(L.T("welcomeTitle"));
        ImGui.PopFont();
        ImGui.SetCursorPosX(Theme.Px(24));
        ImGui.PushTextWrapPos(w - Theme.Px(24));
        Ui.TextDim(L.T("welcomeText"));
        ImGui.Dummy(new Vector2(0, Theme.Px(4)));
        ImGui.SetCursorPosX(Theme.Px(24));
        Ui.TextMuted(L.T("digHint"));
        ImGui.PopTextWrapPos();
        ImGui.SetCursorPos(new Vector2(w - Theme.Px(24) - Theme.Px(120), h - Theme.Px(56)));
        if (Ui.Button("", L.T("start"), "start", primary: true, width: 120, height: 36))
        {
            showWelcome = false;
            app.Settings.FirstRun = false;
            app.Settings.Save();
        }
        Ui.EndPanel();
    }

    private bool showGlyphs;

    /// <summary>Tanılama: kullanılan simge kodlarının yazı tipinde bulunup bulunmadığını gösterir.</summary>
    private void DrawGlyphTest()
    {
        ImGui.SetNextWindowPos(new Vector2(Display.X * 0.3f, TopH + M));
        ImGui.SetNextWindowSize(new Vector2(Theme.Px(580), Display.Y * 0.85f));
        ImGui.Begin("##glyphs", ImGuiWindowFlags.NoDecoration);
        int k = 0;
        // kullanılan simgeler + aday kodlar
        var items = typeof(Icons).GetFields().Select(f => (f.Name, (string)f.GetValue(null)!)).ToList();
        foreach (var code in new[] { 0xE7B3, 0xE8F8, 0xE9A8, 0xED1A, 0xE890, 0xE7C2, 0xE7AD, 0xE71D, 0xE8AB, 0xE9E9, 0xE793, 0xE7B8, 0xE9D9, 0xE945,
                     0xE8F1, 0xEB51, 0xE77B, 0xE7BE, 0xE774, 0xEA80, 0xE8B9, 0xE718, 0xE81D, 0xE707, 0xE1D2, 0xE8A7, 0xE7B7, 0xF0E2, 0xE8FD, 0xEA37, 0xE9CE,
                     0xE72D, 0xE7F4, 0xE958, 0xE7F8, 0xEC4A, 0xE8B3, 0xE14C, 0xE895, 0xE72A, 0xE72B, 0xE7A6, 0xE8C8, 0xE70F, 0xE81C, 0xE9F5, 0xEC7A })
            items.Add(("cand", ((char)code).ToString()));
        foreach (var (name, v) in items)
        {
            if (k % 3 != 0) ImGui.SameLine(Theme.Px(185) * (k % 3));
            ImGui.PushFont(F.IconLarge);
            ImGui.TextUnformatted(v);
            ImGui.PopFont();
            ImGui.SameLine(0, Theme.Px(6));
            ImGui.PushFont(F.Small);
            ImGui.TextUnformatted($"{name} {(int)v[0]:X4}");
            ImGui.PopFont();
            k++;
        }
        ImGui.End();
    }

    public void ScriptPanel(string a, string b)
    {
        switch (a)
        {
            case "glyphs": showGlyphs = b != "off"; break;
            case "left": leftOpen = b != "off"; break;
            case "right": rightOpen = b != "off"; break;
            case "settings": showSettings = b != "off"; break;
            case "help": showHelp = b != "off"; break;
            case "welcome": showWelcome = b != "off"; break;
            case "tab": leftTab = int.TryParse(b, out var t) ? t : 0; break;
            case "expand": expanded.Add(b); break;
            case "popover": popover = b; break;
        }
    }

    public void Dispose() { }
}
