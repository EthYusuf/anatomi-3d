using System.Numerics;
using Anatomi3D.Core.Content;
using Anatomi3D.Core.Model;
using Anatomi3D.Desktop.App;
using ImGuiNET;

namespace Anatomi3D.Desktop.UI;

public sealed partial class AtlasUI
{
    private bool infoScrollReset;
    private bool wikiMore;
    private string? descKey;
    private List<DescriptionParser.Section> descSections = [];
    private string? descUrl;

    private void DrawInfoPanel()
    {
        float x = Display.X - RightW - M + (1 - rightAnim) * (RightW + M * 2);
        var pos = new Vector2(x, TopH + M);
        var size = new Vector2(RightW, Display.Y - TopH - M * 2 - Theme.Px(28));
        Ui.BeginPanel("##info", pos, size, rightAnim);
        if (infoScrollReset) { ImGui.SetScrollY(0); infoScrollReset = false; wikiMore = false; }
        if (S.Selected < 0) DrawEmptyInfo(size);
        else DrawPartInfo(Model.Parts[S.Selected]);
        Ui.EndPanel();
    }

    private void DrawEmptyInfo(Vector2 size)
    {
        var dl = ImGui.GetWindowDrawList();
        var wp = ImGui.GetWindowPos();
        var c = wp + new Vector2(size.X * 0.5f, Theme.Px(150));
        dl.AddCircle(c, Theme.Px(34), Theme.U32(Theme.Border), 48, Theme.Px(2));
        dl.AddCircle(c, Theme.Px(18), Theme.U32(Theme.Rgb(0x3e8ef7, 0.5f)), 36, Theme.Px(2));
        dl.AddCircleFilled(c, Theme.Px(4), Theme.U32(Theme.Accent));
        foreach (var d in new[] { new Vector2(1, 0), new Vector2(-1, 0), new Vector2(0, 1), new Vector2(0, -1) })
            dl.AddLine(c + d * Theme.Px(24), c + d * Theme.Px(44), Theme.U32(Theme.Border), Theme.Px(2));
        ImGui.SetCursorPosY(Theme.Px(215));
        ImGui.PushTextWrapPos(size.X - Theme.Px(24));
        CenteredWrapped(L.T("noSelection"), Theme.TextDim, size.X);
        ImGui.Dummy(new Vector2(0, Theme.Px(16)));
        Ui.SectionHeader(L.T("mouse"));
        foreach (var (k, v) in HelpMouse()) Ui.KeyValue(k, v, Theme.Px(150));
        ImGui.Dummy(new Vector2(0, Theme.Px(8)));
        Ui.SectionHeader(L.T("shortcutKeys"));
        foreach (var (k, v) in HelpKeys().Take(8)) Ui.KeyValue(k, v, Theme.Px(150));
        ImGui.PopTextWrapPos();
    }

    private static void CenteredWrapped(string text, Vector4 color, float width)
    {
        // basit ortalama: satırları kendimiz kır
        var words = text.Split(' ');
        var line = "";
        float max = width - Theme.Px(56);
        var lines = new List<string>();
        foreach (var w in words)
        {
            var t = line.Length == 0 ? w : line + " " + w;
            if (ImGui.CalcTextSize(t).X > max && line.Length > 0) { lines.Add(line); line = w; }
            else line = t;
        }
        if (line.Length > 0) lines.Add(line);
        ImGui.PushStyleColor(ImGuiCol.Text, color);
        foreach (var l in lines)
        {
            ImGui.SetCursorPosX((width - ImGui.CalcTextSize(l).X) * 0.5f);
            ImGui.TextUnformatted(l);
        }
        ImGui.PopStyleColor();
    }

    private void DrawPartInfo(Part p)
    {
        var info = p.Info;
        var sys = Categories.System(info.System);
        float w = ImGui.GetContentRegionAvail().X;

        // başlık satırı: kategori çipi + kapat
        Ui.Chip(S.Lang == Lang.Tr ? info.Tr : info.En, new Vector4(info.Color, 1));
        ImGui.SameLine(w - Theme.Px(26));
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - Theme.Px(3));
        if (Ui.IconButton(Icons.Close, "closeInfo", L.T("close"), false, 30)) S.Select(-1);

        var (entry, inherited) = ContentLibrary.For(p, Model.Groups[p.Group]);
        string name = p.DisplayName(S.Lang);
        ImGui.Dummy(new Vector2(0, Theme.Px(2)));
        ImGui.PushFont(F.Title);
        ImGui.PushTextWrapPos(0);
        ImGui.TextUnformatted(name);
        ImGui.PopTextWrapPos();
        ImGui.PopFont();
        // ikincil adlar
        if (S.Lang == Lang.Tr)
        {
            if (p.La != null && p.Tr != null) { ImGui.PushTextWrapPos(0); Ui.TextDim(p.La + SideLatin(p)); ImGui.PopTextWrapPos(); }
            ImGui.PushFont(F.Small);
            Ui.TextMuted(p.En + (p.Side == Side.None ? "" : p.Side == Side.Left ? " (left)" : " (right)"));
            ImGui.PopFont();
        }
        else if (p.La != null) Ui.TextDim(p.La + SideLatin(p));

        // eylemler
        ImGui.Dummy(new Vector2(0, Theme.Px(6)));
        float bw = (w - Theme.Px(8) * 1) / 2;
        if (Ui.Button(Icons.Zoom, L.T("focus"), "focus", width: bw / Theme.S)) S.RequestCamera(CameraPreset.Focus, p.Index);
        ImGui.SameLine(0, Theme.Px(8));
        if (Ui.Button(Icons.EyeOff, L.T("hide"), "hideSel", width: bw / Theme.S)) S.HideSelected();
        bool iso = S.Isolated != null;
        if (Ui.Button(Icons.Target, iso ? L.T("exitIsolate") : L.T("isolate"), "iso", iso, width: bw / Theme.S))
            S.Isolate(iso ? null : Model.WithChildren(p).Select(x => x.Index).Concat(p.Attachments.Select(a => a.Index)));
        ImGui.SameLine(0, Theme.Px(8));
        if (Ui.Button(Icons.Ghost, L.T("ghostOthers"), "ghost", S.Reveal, width: bw / Theme.S)) S.SetReveal(!S.Reveal);
        if (p.Attachments.Count > 0 || p.Parent?.Attachments.Count > 0)
        {
            bool on = S.ShowAttachments;
            if (Ui.Button(Icons.Pin, L.T("attachments"), "att", on, width: w / Theme.S)) { S.ShowAttachments = !on; app.Settings.ShowAttachments = !on; S.Changed(); }
            if (on)
            {
                var dl = ImGui.GetWindowDrawList();
                var lp = ImGui.GetCursorScreenPos();
                dl.AddCircleFilled(lp + new Vector2(Theme.Px(8), Theme.Px(9)), Theme.Px(5), Theme.U32(0xe5484d));
                dl.AddText(lp + new Vector2(Theme.Px(18), 0), Theme.U32(Theme.TextDim), L.T("origin"));
                float ox = ImGui.CalcTextSize(L.T("origin")).X + Theme.Px(40);
                dl.AddCircleFilled(lp + new Vector2(ox + Theme.Px(8), Theme.Px(9)), Theme.Px(5), Theme.U32(0x3e8ef7));
                dl.AddText(lp + new Vector2(ox + Theme.Px(18), 0), Theme.U32(Theme.TextDim), L.T("insertion"));
                ImGui.Dummy(new Vector2(0, ImGui.GetTextLineHeight()));
            }
        }

        // Türkçe bilgi girdisi
        if (entry != null && S.Lang == Lang.Tr)
        {
            Ui.Separator(8);
            if (inherited)
            {
                ImGui.PushFont(F.Small);
                Ui.TextMuted(Icons.Info + "  " + L.T("inherited") + ": " + (entry.Tr ?? entry.Key));
                ImGui.PopFont();
            }
            if (entry.Summary != null)
            {
                ImGui.PushTextWrapPos(0);
                ImGui.TextUnformatted(entry.Summary);
                ImGui.PopTextWrapPos();
            }
            if (entry.Facts is { Count: > 0 })
            {
                ImGui.Dummy(new Vector2(0, Theme.Px(6)));
                DrawFacts(entry.Facts);
            }
            if (entry.Sections != null)
                foreach (var sec in entry.Sections)
                {
                    Ui.SectionHeader(sec.Title);
                    ImGui.PushTextWrapPos(0);
                    Ui.TextDim(sec.Text);
                    ImGui.PopTextWrapPos();
                }
            if (entry.Clinical != null)
            {
                ImGui.Dummy(new Vector2(0, Theme.Px(8)));
                DrawCallout(Icons.Bulb, L.T("clinical"), entry.Clinical, Theme.Warning);
            }
            if (entry.Status is null or "taslak")
            {
                ImGui.Dummy(new Vector2(0, Theme.Px(4)));
                ImGui.PushFont(F.Small);
                Ui.TextMuted(Icons.Warning + "  " + L.T("draftNote"));
                ImGui.PopFont();
            }
        }

        // temel bilgiler
        Ui.Separator(8);
        float kw = Theme.Px(110);
        Ui.KeyValue(L.T("system"), S.Lang == Lang.Tr ? sys.Tr : sys.En, kw);
        Ui.KeyValue(L.T("category"), S.Lang == Lang.Tr ? info.Tr : info.En, kw);
        if (p.Side != Side.None) Ui.KeyValue(L.T("side"), p.Side == Side.Left ? L.T("left") : L.T("right"), kw);
        if (p.Category == Category.Muscle && p.Material != null && Categories.MuscleFunction.TryGetValue(p.Material, out var fn))
            Ui.KeyValue(L.T("fnGroup"), S.Lang == Lang.Tr ? fn.Tr : fn.En, kw);

        // hiyerarşi
        var path = Model.Groups[p.Group];
        if (path.Length > 0)
        {
            Ui.SectionHeader(L.T("hierarchy"));
            ImGui.PushFont(F.Small);
            for (int i = 0; i < path.Length; i++)
            {
                var g = path[i];
                string label = Model.GroupLabel(g, S.Lang);
                Ui.TextDim(new string(' ', i * 3) + (i > 0 ? "› " : "") + label, false);
            }
            ImGui.PopFont();
        }

        // ilgili yapılar
        var other = Model.Counterpart(p);
        var children = p.Children.Where(c => !c.IsAttachment).ToList();
        if (p.Parent != null || other != null || children.Count > 0)
        {
            if (p.Parent != null)
            {
                Ui.SectionHeader(L.T("partOf"));
                if (Ui.LinkChip(p.Parent.DisplayName(S.Lang), "parent", new Vector4(p.Parent.Info.Color, 1))) S.Select(p.Parent.Index);
            }
            if (other != null)
            {
                Ui.SectionHeader(L.T("counterpart"));
                if (Ui.LinkChip(other.DisplayName(S.Lang), "other", new Vector4(other.Info.Color, 1))) S.Select(other.Index, focus: true);
            }
            if (children.Count > 0)
            {
                Ui.SectionHeader(L.T("branches") + $" ({children.Count})");
                foreach (var c in children.Take(40))
                    if (Ui.LinkChip(c.DisplayName(S.Lang), "c" + c.Index, new Vector4(c.Info.Color, 1))) S.Select(c.Index);
            }
        }

        // İngilizce ayrıntılı açıklama (Wikipedia)
        if (p.DescriptionKey != null && Model.Descriptions.TryGetValue(p.DescriptionKey, out var raw))
        {
            if (descKey != p.DescriptionKey)
            {
                descKey = p.DescriptionKey;
                (descSections, descUrl) = DescriptionParser.Parse(raw);
            }
            if (descSections.Count > 0)
            {
                Ui.Separator(8);
                Ui.SectionHeader(S.Lang == Lang.Tr ? L.T("wikiDesc") : L.T("overview"));
                ImGui.PushTextWrapPos(0);
                var secs = wikiMore ? descSections : descSections.Take(1).ToList();
                foreach (var sec in secs)
                {
                    if (sec.Title.Length > 0)
                    {
                        ImGui.PushFont(F.BodyBold);
                        ImGui.TextUnformatted(sec.Title);
                        ImGui.PopFont();
                    }
                    foreach (var para in wikiMore ? sec.Paragraphs : sec.Paragraphs.Take(2))
                    {
                        Ui.TextDim(para);
                        ImGui.Dummy(new Vector2(0, Theme.Px(2)));
                    }
                }
                ImGui.PopTextWrapPos();
                bool hasMore = descSections.Count > 1 || descSections[0].Paragraphs.Count > 2;
                if (hasMore && Ui.Button("", wikiMore ? L.T("readLess") : L.T("readMore"), "more")) wikiMore = !wikiMore;
                ImGui.PushFont(F.Small);
                Ui.TextMuted(L.T("wikiNote"));
                ImGui.PopFont();
            }
        }
        ImGui.Dummy(new Vector2(0, Theme.Px(12)));
    }

    private static string SideLatin(Part p) => p.Side switch { Side.Left => " sinister", Side.Right => " dexter", _ => "" };

    private void DrawFacts(List<string[]> facts)
    {
        var dl = ImGui.GetWindowDrawList();
        float w = ImGui.GetContentRegionAvail().X;
        foreach (var f in facts)
        {
            if (f.Length < 2) continue;
            var start = ImGui.GetCursorScreenPos();
            ImGui.PushFont(F.SmallBold);
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.Rgb(0x8cbcff));
            ImGui.TextUnformatted(f[0].ToUpper(System.Globalization.CultureInfo.GetCultureInfo("tr-TR")));
            ImGui.PopStyleColor();
            ImGui.PopFont();
            ImGui.PushTextWrapPos(0);
            ImGui.TextUnformatted(f[1]);
            ImGui.PopTextWrapPos();
            var end = ImGui.GetCursorScreenPos();
            dl.AddLine(start - new Vector2(Theme.Px(8), 0), new Vector2(start.X - Theme.Px(8), end.Y - Theme.Px(6)), Theme.U32(Theme.Rgb(0x3e8ef7, 0.5f)), Theme.Px(2));
            ImGui.Dummy(new Vector2(w, Theme.Px(2)));
        }
    }

    private void DrawCallout(string icon, string title, string text, Vector4 color)
    {
        var dl = ImGui.GetWindowDrawList();
        var start = ImGui.GetCursorScreenPos();
        float w = ImGui.GetContentRegionAvail().X;
        dl.ChannelsSplit(2);
        dl.ChannelsSetCurrent(1);
        ImGui.Indent(Theme.Px(12));
        ImGui.Dummy(new Vector2(0, Theme.Px(4)));
        ImGui.PushFont(F.BodyBold);
        ImGui.PushStyleColor(ImGuiCol.Text, color);
        ImGui.TextUnformatted(icon + "  " + title);
        ImGui.PopStyleColor();
        ImGui.PopFont();
        ImGui.PushTextWrapPos(start.X - ImGui.GetWindowPos().X + w - Theme.Px(12));
        Ui.TextDim(text);
        ImGui.PopTextWrapPos();
        ImGui.Dummy(new Vector2(0, Theme.Px(4)));
        ImGui.Unindent(Theme.Px(12));
        var end = ImGui.GetCursorScreenPos();
        dl.ChannelsSetCurrent(0);
        dl.AddRectFilled(start, new Vector2(start.X + w, end.Y), Theme.U32(new Vector4(color.X, color.Y, color.Z, 0.08f)), Theme.Px(8));
        dl.AddRectFilled(start, new Vector2(start.X + Theme.Px(3), end.Y), Theme.U32(color), Theme.Px(2));
        dl.ChannelsMerge();
    }
}
