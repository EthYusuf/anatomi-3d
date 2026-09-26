using System.Numerics;
using Anatomi3D.Core.Content;
using Anatomi3D.Desktop.App;
using ImGuiNET;

namespace Anatomi3D.Desktop.UI;

public sealed partial class AtlasUI
{
    private static (string, string)[] HelpMouse() => L.Lang == Lang.Tr
        ? [("Sol tık + sürükle", "Döndür"), ("Sağ tık + sürükle", "Kaydır"), ("Tekerlek", "İmlece doğru yakınlaştır"), ("Tıkla", "Yapıyı seç"), ("Çift tıkla", "Yapıyı kaldır (kazı)")]
        : [("Left drag", "Rotate"), ("Right drag", "Pan"), ("Wheel", "Zoom to cursor"), ("Click", "Select structure"), ("Double-click", "Remove structure (dissect)")];

    private static (string, string)[] HelpKeys() => L.Lang == Lang.Tr
        ? [("Ctrl + K", "Ara"), ("F", "Seçili yapıya odaklan"), ("H", "Seçili yapıyı gizle"), ("I", "İzole et / çık"), ("X", "X-Ray"),
           ("W", "Tel kafes"), ("[ ve ]", "Katman geri koy / soy"), ("Ctrl + Z", "Kazıyı geri al"), ("Ctrl + Shift + Z", "Tüm kazıları geri getir"),
           ("U", "Tümünü göster"), ("Esc", "Seçimi / izolasyonu kaldır"), ("1 – 6", "Ön, arka, sol, sağ, üst, 3/4 görünüm"), ("Home", "Tüm vücut"),
           ("F11", "Tam ekran"), ("F12", "Ekran görüntüsü")]
        : [("Ctrl + K", "Search"), ("F", "Focus selection"), ("H", "Hide selection"), ("I", "Isolate / exit"), ("X", "X-Ray"),
           ("W", "Wireframe"), ("[ and ]", "Restore / peel layer"), ("Ctrl + Z", "Undo removal"), ("Ctrl + Shift + Z", "Restore all removals"),
           ("U", "Show all"), ("Esc", "Clear selection / isolation"), ("1 – 6", "Front, back, left, right, top, 3/4 view"), ("Home", "Full body"),
           ("F11", "Full screen"), ("F12", "Screenshot")];

    private void DrawDialogs()
    {
        if (showSettings) DrawSettings();
        if (showHelp) DrawHelp();
    }

    private bool BeginDialog(string id, string title, float w, float h, ref bool open)
    {
        var pos = new Vector2((Display.X - Theme.Px(w)) * 0.5f, (Display.Y - Theme.Px(h)) * 0.5f);
        var fg = ImGui.GetBackgroundDrawList();
        fg.AddRectFilled(Vector2.Zero, Display, Theme.U32(0x05070a, 0.5f));
        ImGui.SetNextWindowFocus();
        Ui.BeginPanel(id, pos, new Vector2(Theme.Px(w), Theme.Px(h)), 1, front: true);
        ImGui.PushFont(F.Heading);
        ImGui.TextUnformatted(title);
        ImGui.PopFont();
        ImGui.SameLine(Theme.Px(w) - Theme.Px(50));
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - Theme.Px(4));
        if (Ui.IconButton(Icons.Close, id + "close", L.T("close"), false, 32)) open = false;
        if (ImGui.IsKeyPressed(ImGuiKey.Escape)) open = false;
        Ui.Separator(4);
        return true;
    }

    private void DrawSettings()
    {
        var st = app.Settings;
        BeginDialog("##settings", Icons.Settings + "  " + L.T("settings"), 560, 700, ref showSettings);
        ImGui.BeginChild("##settingsBody", new Vector2(0, 0));
        bool changed = false;

        Ui.SectionHeader(L.T("language"));
        int lang = S.Lang == Lang.Tr ? 0 : 1;
        if (Ui.Segmented("setLang", ["Türkçe", "English"], ref lang))
        {
            S.Lang = lang == 0 ? Lang.Tr : Lang.En;
            st.Language = S.Lang;
            L.Lang = S.Lang;
            BuildTree();
            S.Changed();
            st.Save();
        }

        Ui.SectionHeader(L.T("quality"));
        var rec = Settings.Recommended(app.Device);
        string recName = rec switch { QualityPreset.Ultra => L.T("qUltra"), QualityPreset.High => L.T("qHigh"), QualityPreset.Medium => L.T("qMedium"), _ => L.T("qLow") };
        int q = st.Quality switch { QualityPreset.Auto => 0, QualityPreset.Ultra => 1, QualityPreset.High => 2, QualityPreset.Medium => 3, QualityPreset.Low => 4, _ => -1 };
        int qq = q;
        if (Ui.Segmented("quality", [L.T("qAuto"), L.T("qUltra"), L.T("qHigh"), L.T("qMedium"), L.T("qLow")], ref qq))
        {
            st.ApplyPreset(qq switch { 0 => QualityPreset.Auto, 1 => QualityPreset.Ultra, 2 => QualityPreset.High, 3 => QualityPreset.Medium, _ => QualityPreset.Low }, app.Device);
            changed = true;
        }
        ImGui.PushFont(F.Small);
        Ui.TextMuted($"{L.T("recommended")}: {recName}  ·  {app.Device.AdapterName}" + (st.Quality == QualityPreset.Custom ? $"  ·  {L.T("qCustom")}" : ""), false);
        ImGui.PopFont();

        ImGui.Dummy(new Vector2(0, Theme.Px(4)));
        ImGui.TextUnformatted(L.T("msaa"));
        int msaa = st.Msaa switch { 1 => 0, 2 => 1, 4 => 2, _ => 3 };
        if (Ui.Segmented("msaa", [L.T("off"), "2x", "4x", "8x"], ref msaa)) { st.Msaa = msaa switch { 0 => 1u, 1 => 2u, 2 => 4u, _ => 8u }; st.Quality = QualityPreset.Custom; changed = true; }
        bool b;
        b = st.Shadows; if (Ui.Toggle("sh", ref b, L.T("shadows"))) { st.Shadows = b; st.Quality = QualityPreset.Custom; changed = true; }
        b = st.Ssao; if (Ui.Toggle("ao", ref b, L.T("ssao"))) { st.Ssao = b; st.Quality = QualityPreset.Custom; changed = true; }
        b = st.Bloom; if (Ui.Toggle("bl", ref b, L.T("bloom"))) { st.Bloom = b; st.Quality = QualityPreset.Custom; changed = true; }
        b = st.Tessellation; if (Ui.Toggle("ts", ref b, L.T("tess"))) { st.Tessellation = b; st.Quality = QualityPreset.Custom; changed = true; }
        b = st.ProceduralDetail; if (Ui.Toggle("dt", ref b, L.T("detail"))) { st.ProceduralDetail = b; st.Quality = QualityPreset.Custom; changed = true; }
        b = st.VSync; if (Ui.Toggle("vs", ref b, L.T("vsync"))) { st.VSync = b; changed = true; }

        Ui.SectionHeader(L.T("display"));
        float ui = st.UiScale * 100;
        ImGui.TextUnformatted(L.T("uiScale"));
        ImGui.PushItemWidth(-1);
        if (ImGui.SliderFloat("##uiScale", ref ui, 80, 150, "%.0f%%") ) { }
        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            st.UiScale = MathF.Round(ui / 5) * 5 / 100f;
            app.Gui.SetScale(app.Window.DpiScale * st.UiScale);
            changed = true;
        }
        float ex = st.Exposure;
        ImGui.TextUnformatted(L.T("exposure"));
        if (ImGui.SliderFloat("##exposure", ref ex, 0.6f, 1.6f, "%.2f")) { st.Exposure = ex; changed = true; }
        ImGui.PopItemWidth();
        b = st.ShowFps; if (Ui.Toggle("fps", ref b, L.T("showFps"))) { st.ShowFps = b; changed = true; }

        Ui.SectionHeader(L.T("gpu"));
        var gd = app.Device;
        Ui.KeyValue(L.T("gpu"), gd.AdapterName, Theme.Px(140));
        Ui.KeyValue("VRAM", $"{gd.DedicatedVideoMemory / (1024.0 * 1024 * 1024):F1} GB", Theme.Px(140));
        Ui.KeyValue("Direct3D", gd.FeatureLevel.ToString().Replace("Level_", "").Replace('_', '.'), Theme.Px(140));
        b = st.PreferIntegrated; if (Ui.Toggle("igpu", ref b, L.T("preferIntegrated"))) { st.PreferIntegrated = b; changed = true; }

        Ui.SectionHeader(L.T("about"));
        ImGui.PushTextWrapPos(0);
        Ui.TextDim($"Anatomi 3D {typeof(AtlasUI).Assembly.GetName().Version?.ToString(3)}  ·  C# / .NET 8 / Direct3D 11");
        Ui.TextDim($"{Model.Parts.Count(p => !p.IsAttachment):N0} {L.T("structuresCount")}  ·  {ContentLibrary.Count} {L.T("contentInfo")}");
        ImGui.PushFont(F.Small);
        Ui.TextMuted(S.Lang == Lang.Tr
            ? "3D model, yapı adları ve İngilizce açıklamalar: Z-Anatomy (Lluís Vinent Juanico ve katkıda bulunanlar), CC BY-SA 4.0; açıklama metinleri Wikipedia (CC BY-SA). Türkçe içerik eğitim amaçlıdır, klinik karar için kullanılmamalıdır."
            : "3D model, structure names and English descriptions: Z-Anatomy (Lluís Vinent Juanico et al.), CC BY-SA 4.0; description texts from Wikipedia (CC BY-SA). For education only; not for clinical decisions.");
        Ui.TextMuted("Dear ImGui, ImGui.NET, Vortice.Windows, meshoptimizer, SharpGLTF — MIT");
        ImGui.PopFont();
        ImGui.PopTextWrapPos();
        ImGui.EndChild();
        Ui.EndPanel();
        if (changed)
        {
            app.ApplyRenderSettings();
            st.Save();
        }
    }

    private void DrawHelp()
    {
        BeginDialog("##help", Icons.Help + "  " + L.T("help"), 560, 640, ref showHelp);
        ImGui.BeginChild("##helpBody", new Vector2(0, 0));
        Ui.SectionHeader(L.T("mouse"));
        foreach (var (k, v) in HelpMouse()) Ui.KeyValue(k, v, Theme.Px(190));
        Ui.SectionHeader(L.T("shortcutKeys"));
        foreach (var (k, v) in HelpKeys()) Ui.KeyValue(k, v, Theme.Px(190));
        ImGui.Dummy(new Vector2(0, Theme.Px(6)));
        ImGui.PushTextWrapPos(0);
        Ui.TextMuted(L.T("digHint"));
        ImGui.PopTextWrapPos();
        ImGui.EndChild();
        Ui.EndPanel();
    }

    // ------------------------------------------------------------------------------------------------ yükleme ekranı
    public static void DrawSplash(AtlasApp app, float progress, string? error)
    {
        var io = ImGui.GetIO();
        var size = io.DisplaySize;
        var dl = ImGui.GetBackgroundDrawList();
        dl.AddRectFilledMultiColor(Vector2.Zero, size, Theme.U32(0x151b24), Theme.U32(0x151b24), Theme.U32(0x0b0e13), Theme.U32(0x0b0e13));
        var f = app.Gui.Fonts;
        float cx = size.X / 2, cy = size.Y / 2;
        float ls = Theme.Px(64);
        var lp = new Vector2(cx - ls / 2, cy - Theme.Px(150));
        dl.AddRectFilled(lp - new Vector2(4, 4), lp + new Vector2(ls + 4, ls + 4), Theme.U32(0x151b24), Theme.Px(16));
        DrawLogo(dl, lp, ls);
        ImGui.PushFont(f.Display);
        string title = L.T("appTitle");
        var ts = ImGui.CalcTextSize(title);
        dl.AddText(new Vector2(cx - ts.X / 2, cy - Theme.Px(70)), Theme.U32(Theme.Text), title);
        ImGui.PopFont();
        ImGui.PushFont(f.Body);
        if (error == null)
        {
            string sub = L.T("loading");
            var ss = ImGui.CalcTextSize(sub);
            dl.AddText(new Vector2(cx - ss.X / 2, cy - Theme.Px(18)), Theme.U32(Theme.TextDim), sub);
        }
        else
        {
            // hata iletisi birden çok satır olabilir: her satırı ortala
            float y = cy - Theme.Px(18);
            foreach (var line in error.Replace("\r", "").Split('\n'))
            {
                var ls2 = ImGui.CalcTextSize(line);
                dl.AddText(new Vector2(cx - ls2.X / 2, y), Theme.U32(line.Length > 0 && y == cy - Theme.Px(18) ? Theme.Danger : Theme.TextDim), line);
                y += ImGui.GetTextLineHeightWithSpacing();
            }
        }
        ImGui.PopFont();
        if (error == null)
        {
            float w = Theme.Px(320), h = Theme.Px(4);
            var p0 = new Vector2(cx - w / 2, cy + Theme.Px(22));
            dl.AddRectFilled(p0, p0 + new Vector2(w, h), Theme.U32(Theme.Surface), h);
            dl.AddRectFilled(p0, p0 + new Vector2(w * Math.Clamp(progress, 0.02f, 1f), h), Theme.U32(Theme.Accent), h);
        }
        ImGui.PushFont(f.Small);
        string foot = "Z-Anatomy · CC BY-SA 4.0";
        var fs = ImGui.CalcTextSize(foot);
        dl.AddText(new Vector2(cx - fs.X / 2, size.Y - Theme.Px(40)), Theme.U32(Theme.TextMuted), foot);
        ImGui.PopFont();
    }
}
