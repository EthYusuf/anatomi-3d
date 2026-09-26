using System.Numerics;
using Anatomi3D.Core.Content;
using Anatomi3D.Core.Model;
using Anatomi3D.Desktop.App;
using ImGuiNET;

namespace Anatomi3D.Desktop.UI;

public sealed partial class AtlasUI
{
    private string? popover;
    private Vector2 toolbarPos, toolbarSize;

    /// <summary>Alt araç çubuğu düğmesi: simge üstte, küçük etiket altta.</summary>
    private bool ToolButton(string icon, string label, string id, bool active)
    {
        float w = Theme.Px(70), h = Theme.Px(52);
        var pos = ImGui.GetCursorScreenPos();
        bool pressed = ImGui.InvisibleButton(id, new Vector2(w, h));
        bool hov = ImGui.IsItemHovered();
        float a = Ui.Animate(ImGui.GetID(id + "a"), hov ? 1 : 0);
        var dl = ImGui.GetWindowDrawList();
        var bg = active ? Theme.AccentSoft : new Vector4(Theme.SurfaceHover.X, Theme.SurfaceHover.Y, Theme.SurfaceHover.Z, a);
        if (bg.W > 0.01f) dl.AddRectFilled(pos, pos + new Vector2(w, h), Theme.U32(bg), Theme.Px(10));
        var col = active ? Theme.Rgb(0x8cbcff) : Ui.Mix(Theme.TextDim, Theme.Text, a);
        ImGui.PushFont(F.IconLarge);
        var isz = ImGui.CalcTextSize(icon);
        dl.AddText(pos + new Vector2((w - isz.X) * 0.5f, Theme.Px(7)), Theme.U32(col), icon);
        ImGui.PopFont();
        ImGui.PushFont(F.Small);
        var ls = ImGui.CalcTextSize(label);
        string lbl = ls.X > w - Theme.Px(4) ? Ui.Ellipsize(label, w - Theme.Px(4)) : label;
        ls = ImGui.CalcTextSize(lbl);
        dl.AddText(pos + new Vector2((w - ls.X) * 0.5f, h - ls.Y - Theme.Px(6)), Theme.U32(col), lbl);
        ImGui.PopFont();
        return pressed;
    }

    private void DrawToolbar()
    {
        if (S.Quiz != null && S.Quiz.Last == null) { }
        float h = Theme.Px(68);
        float peelW = Theme.Px(250);
        float w = peelW + Theme.Px(70) * 6 + Theme.Px(52);
        float left = leftAnim * (LeftW + M), right = rightAnim * (RightW + M);
        float cx = left + (Display.X - left - right) * 0.5f;
        var pos = new Vector2(cx - w * 0.5f, Display.Y - h - M - Theme.Px(28));
        toolbarPos = pos;
        toolbarSize = new Vector2(w, h);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(Theme.Px(8), Theme.Px(8)));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(Theme.Px(2), 0));
        Ui.BeginPanel("##toolbar", pos, new Vector2(w, h), 1, ImGuiWindowFlags.NoScrollbar);
        var dl = ImGui.GetWindowDrawList();

        // katman soyma
        var p0 = ImGui.GetCursorScreenPos();
        ImGui.SetCursorPosY(Theme.Px(15));
        if (Ui.IconButton(Icons.Remove, "unpeel", L.T("unpeel"), false, 38, S.PeelStep == 0)) S.Unpeel();
        ImGui.SameLine(0, Theme.Px(4));
        var lp = ImGui.GetCursorScreenPos();
        float lw = peelW - Theme.Px(92);
        int steps = Categories.PeelSteps.Length;
        float dotW = (lw - Theme.Px(6) * (steps - 1)) / steps;
        for (int i = 0; i < steps; i++)
        {
            var dp = new Vector2(lp.X + i * (dotW + Theme.Px(6)), p0.Y + Theme.Px(12));
            dl.AddRectFilled(dp, dp + new Vector2(dotW, Theme.Px(4)), Theme.U32(i < S.PeelStep ? Theme.Accent : Theme.SurfaceHover), Theme.Px(2));
        }
        string cur = S.PeelStep == 0 ? L.T("fullBody") :
            "− " + string.Join(", ", Categories.PeelSteps[S.PeelStep - 1].Take(2).Select(c => S.Lang == Lang.Tr ? Categories.Get(c).Tr : Categories.Get(c).En));
        string next = S.PeelStep < steps ? string.Join(", ", Categories.PeelSteps[S.PeelStep].Take(2).Select(c => S.Lang == Lang.Tr ? Categories.Get(c).Tr : Categories.Get(c).En)) : "";
        ImGui.PushFont(F.SmallBold);
        string curL = Ui.Ellipsize(cur, lw);
        var cs = ImGui.CalcTextSize(curL);
        dl.AddText(new Vector2(lp.X + (lw - cs.X) * 0.5f, p0.Y + Theme.Px(24)), Theme.U32(Theme.Text), curL);
        ImGui.PopFont();
        ImGui.PushFont(F.Small);
        string lay = L.T("layer") + $" {S.PeelStep}/{steps}";
        var ls = ImGui.CalcTextSize(lay);
        dl.AddText(new Vector2(lp.X + (lw - ls.X) * 0.5f, p0.Y + Theme.Px(38)), Theme.U32(Theme.TextMuted), lay);
        ImGui.PopFont();
        ImGui.SetCursorScreenPos(new Vector2(lp.X + lw + Theme.Px(4), p0.Y + Theme.Px(7)));
        if (Ui.IconButton(Icons.Add, "peel", next.Length > 0 ? L.T("peel") + ": " + next : L.T("peel"), false, 38, S.PeelStep >= steps)) S.Peel();

        // ayraç
        var sp = new Vector2(p0.X + peelW + Theme.Px(12), p0.Y + Theme.Px(6));
        dl.AddLine(sp, sp + new Vector2(0, Theme.Px(40)), Theme.U32(Theme.Border));
        ImGui.SetCursorScreenPos(new Vector2(sp.X + Theme.Px(10), p0.Y - Theme.Px(1)));

        bool dispActive = popover == "display" || S.ViewMode != ViewMode.Solid || S.ColorMode != ColorMode.Anatomic;
        if (ToolButton(Icons.Layers, L.T("display"), "tbDisplay", dispActive)) popover = popover == "display" ? null : "display";
        ImGui.SameLine();
        if (ToolButton(Icons.Cut, L.T("section"), "tbSection", popover == "section" || S.ClipAxis != ClipAxis.None)) popover = popover == "section" ? null : "section";
        ImGui.SameLine();
        if (ToolButton(Icons.Rotate, L.T("camera"), "tbCamera", popover == "camera")) popover = popover == "camera" ? null : "camera";
        ImGui.SameLine();
        if (ToolButton(Icons.Person, L.T("home"), "tbHome", false)) { S.RequestCamera(CameraPreset.Home); popover = null; }
        ImGui.SameLine();
        if (ToolButton(Icons.Reset, L.T("reset"), "tbReset", false)) { S.ResetAll(); popover = null; activeRegion = -1; }
        ImGui.SameLine();
        if (ToolButton(Icons.Camera, L.T("shot"), "tbShot", false)) { popover = null; app.TakeScreenshot(); }
        Ui.EndPanel();
        ImGui.PopStyleVar(2);

        if (popover != null) DrawPopover();
    }

    private void DrawPopover()
    {
        float w = Theme.Px(popover == "camera" ? 300 : 360);
        float h = popover switch { "display" => Theme.Px(290), "section" => S.ClipAxis == ClipAxis.None ? Theme.Px(96) : Theme.Px(150), _ => Theme.Px(170) };
        var pos = new Vector2(toolbarPos.X + (toolbarSize.X - w) * 0.5f, toolbarPos.Y - h - Theme.Px(10));
        Ui.BeginPanel("##popover", pos, new Vector2(w, h), 1, ImGuiWindowFlags.NoScrollbar);
        bool hovered = ImGui.IsWindowHovered(ImGuiHoveredFlags.ChildWindows);
        switch (popover)
        {
            case "display":
            {
                Ui.SectionHeader(L.T("display"));
                int vm = (int)S.ViewMode;
                if (Ui.Segmented("vm", [L.T("solid"), L.T("xray"), L.T("wire")], ref vm)) S.SetViewMode((ViewMode)vm);
                Ui.SectionHeader(L.T("colors"));
                int cm = (int)S.ColorMode;
                if (Ui.Segmented("cm", [L.T("colorAnatomic"), L.T("colorFunction")], ref cm)) S.SetColorMode((ColorMode)cm);
                ImGui.Dummy(new Vector2(0, Theme.Px(4)));
                bool alive = S.Alive;
                if (Ui.Toggle("alive", ref alive, L.T("physiology"))) { S.Alive = alive; app.Settings.Physiology = alive; S.Changed(); }
                bool att = S.ShowAttachments;
                if (Ui.Toggle("attT", ref att, L.T("showAttachments"))) { S.ShowAttachments = att; app.Settings.ShowAttachments = att; S.Changed(); }
                break;
            }
            case "section":
            {
                Ui.SectionHeader(L.T("section"));
                int ax = (int)S.ClipAxis;
                if (Ui.Segmented("clip", [L.T("off"), L.T("sagittal"), L.T("coronal"), L.T("axial")], ref ax)) S.SetClip((ClipAxis)ax);
                if (S.ClipAxis != ClipAxis.None)
                {
                    float v = S.ClipPos;
                    ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X - Theme.Px(44));
                    if (ImGui.SliderFloat("##clipPos", ref v, -1, 1, "")) S.SetClipPos(v);
                    ImGui.PopItemWidth();
                    ImGui.SameLine(0, Theme.Px(8));
                    if (Ui.IconButton(Icons.Swap, "flip", L.T("flip"), S.ClipFlip, 34)) S.ToggleClipFlip();
                }
                break;
            }
            case "camera":
            {
                Ui.SectionHeader(L.T("camera"));
                var presets = new (CameraPreset p, string key)[]
                {
                    (CameraPreset.Front, "front"), (CameraPreset.Back, "back"), (CameraPreset.Iso, "iso"),
                    (CameraPreset.Left, "leftSide"), (CameraPreset.Right, "rightSide"), (CameraPreset.Home, "home"),
                    (CameraPreset.Top, "top"), (CameraPreset.Bottom, "bottom"),
                };
                float bw = (ImGui.GetContentRegionAvail().X - Theme.Px(12)) / 3;
                for (int i = 0; i < presets.Length; i++)
                {
                    if (i % 3 != 0) ImGui.SameLine(0, Theme.Px(6));
                    if (Ui.Button("", L.T(presets[i].key), "cam" + i, width: bw / Theme.S, height: 32)) S.RequestCamera(presets[i].p);
                }
                break;
            }
        }
        Ui.EndPanel();
        // dışarı tıklayınca kapan
        var io = ImGui.GetIO();
        bool inToolbar = io.MousePos.X >= toolbarPos.X && io.MousePos.X <= toolbarPos.X + toolbarSize.X && io.MousePos.Y >= toolbarPos.Y && io.MousePos.Y <= toolbarPos.Y + toolbarSize.Y;
        if (ImGui.IsMouseClicked(ImGuiMouseButton.Left) && !hovered && !inToolbar) popover = null;
    }
}
