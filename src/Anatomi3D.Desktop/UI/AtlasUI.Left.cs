using System.Numerics;
using Anatomi3D.Core.Content;
using Anatomi3D.Core.Model;
using Anatomi3D.Core.Search;
using Anatomi3D.Desktop.App;
using ImGuiNET;

namespace Anatomi3D.Desktop.UI;

public sealed partial class AtlasUI
{
    private int leftTab;
    private readonly HashSet<string> expanded = [];
    private string treeFilter = "";
    private List<TreeNode> tree = [];
    private readonly List<(int depth, TreeNode? node, Part? part)> treeRows = [];
    private int activeRegion = -1;

    private sealed class TreeNode
    {
        public required string Key;
        public required string Label;
        public string? Sub;
        public Vector4 Color;
        public readonly List<TreeNode> Children = [];
        public readonly List<Part> Parts = [];
        public int[] All = [];
    }

    private void BuildTree()
    {
        var lang = S.Lang;
        var roots = new Dictionary<BodySystem, TreeNode>();
        foreach (var s in Categories.Systems)
            roots[s.Id] = new TreeNode { Key = s.Id.ToString(), Label = lang == Lang.Tr ? s.Tr : s.En, Color = SystemColor(s.Id) };
        var index = new Dictionary<string, TreeNode>();
        foreach (var p in Model.Parts)
        {
            if (p.IsAttachment) continue;
            var sys = roots[p.Info.System];
            var node = sys;
            string key = sys.Key;
            foreach (var g in Model.Groups[p.Group])
            {
                key += "/" + g[0];
                if (!index.TryGetValue(key, out var child))
                {
                    string en = g[0], la = g.Length > 1 ? g[1] : en;
                    string? tr = Model.Names.Lookup(en) ?? ContentLibrary.TurkishName(en);
                    child = new TreeNode
                    {
                        Key = key,
                        Label = lang == Lang.Tr ? tr ?? la : en,
                        Sub = lang == Lang.Tr ? (tr != null ? la : en != la ? en : null) : la != en ? la : null,
                        Color = sys.Color,
                    };
                    index[key] = child;
                    node.Children.Add(child);
                }
                node = child;
            }
            node.Parts.Add(p);
        }
        int[] Fill(TreeNode n)
        {
            var all = new List<int>(n.Parts.Select(p => p.Index));
            foreach (var c in n.Children) all.AddRange(Fill(c));
            n.Parts.Sort((a, b) => string.Compare(a.DisplayName(lang), b.DisplayName(lang), StringComparison.CurrentCulture));
            n.All = all.ToArray();
            return n.All;
        }
        tree = roots.Values.Where(r => { Fill(r); return r.All.Length > 0; }).ToList();
    }

    private static Vector4 SystemColor(BodySystem s) => s switch
    {
        BodySystem.Integument => Theme.Rgb(0xd8a88e),
        BodySystem.Muscular => Theme.Rgb(0xd0564c),
        BodySystem.Skeletal => Theme.Rgb(0xe6dcc3),
        BodySystem.Cardiovascular => Theme.Rgb(0xe5484d),
        BodySystem.Nervous => Theme.Rgb(0xf0cf3c),
        _ => Theme.Rgb(0xe79a9e),
    };

    private void DrawLeftPanel()
    {
        float x = M - (1 - leftAnim) * (LeftW + M * 2);
        var pos = new Vector2(x, TopH + M);
        var size = new Vector2(LeftW, Display.Y - TopH - M * 2 - Theme.Px(28));
        Ui.BeginPanel("##left", pos, size, leftAnim, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        int tab = leftTab;
        if (Ui.Segmented("leftTabs", [L.T("systems"), L.T("structures"), L.T("regions")], ref tab)) leftTab = tab;
        ImGui.Dummy(new Vector2(0, Theme.Px(2)));
        ImGui.BeginChild("##leftBody", new Vector2(0, 0));
        switch (leftTab)
        {
            case 0: DrawSystemsTab(); break;
            case 1: DrawTreeTab(); break;
            default: DrawRegionsTab(); break;
        }
        ImGui.EndChild();
        Ui.EndPanel();
    }

    // ------------------------------------------------------------------------------------------------ sistemler
    private void DrawSystemsTab()
    {
        var counts = new int[Categories.All.Length];
        foreach (var p in Model.Parts) counts[(int)p.Category]++;
        var dl = ImGui.GetWindowDrawList();
        foreach (var sys in Categories.Systems)
        {
            var cats = Categories.All.Where(c => c.System == sys.Id && c.Id != Category.Attachment && counts[(int)c.Id] > 0).ToList();
            if (cats.Count == 0) continue;
            bool allHidden = cats.All(c => S.HiddenCats.Contains(c.Id));
            ImGui.Dummy(new Vector2(0, Theme.Px(2)));
            // sistem başlığı
            var hp = ImGui.GetCursorScreenPos();
            float w = ImGui.GetContentRegionAvail().X;
            float hh = Theme.Px(30);
            var sc = SystemColor(sys.Id);
            dl.AddRectFilled(hp + new Vector2(0, Theme.Px(7)), hp + new Vector2(Theme.Px(3), hh - Theme.Px(7)), Theme.U32(sc), Theme.Px(2));
            ImGui.PushFont(F.SmallBold);
            string title = (S.Lang == Lang.Tr ? sys.Tr : sys.En).ToUpper(System.Globalization.CultureInfo.GetCultureInfo(S.Lang == Lang.Tr ? "tr-TR" : "en-US"));
            dl.AddText(hp + new Vector2(Theme.Px(12), (hh - ImGui.GetTextLineHeight()) * 0.5f), Theme.U32(Theme.TextDim), title);
            ImGui.PopFont();
            ImGui.SetCursorScreenPos(hp + new Vector2(w - Theme.Px(30), (hh - Theme.Px(28)) * 0.5f));
            if (Ui.IconButton(allHidden ? Icons.EyeOff : Icons.Eye, "sys" + sys.Id, allHidden ? L.T("show") : L.T("hide"), false, 28))
                S.SetCategories(cats.Select(c => c.Id), !allHidden);
            ImGui.SetCursorScreenPos(hp + new Vector2(w - Theme.Px(30) - Theme.Px(30), (hh - Theme.Px(28)) * 0.5f));
            if (Ui.IconButton(Icons.Target, "only" + sys.Id, L.T("showOnly"), false, 28)) S.ShowOnlyCategories(cats.Select(c => c.Id));
            ImGui.SetCursorScreenPos(hp);
            ImGui.Dummy(new Vector2(w, hh));
            foreach (var c in cats)
            {
                bool on = !S.HiddenCats.Contains(c.Id);
                bool peeled = Categories.IsPeeled(c.Id, S.PeelStep);
                var rp = ImGui.GetCursorScreenPos();
                float rh = Theme.Px(34);
                ImGui.PushID((int)c.Id);
                bool clicked = ImGui.InvisibleButton("cat", new Vector2(w - Theme.Px(40), rh));
                bool hov = ImGui.IsItemHovered();
                if (hov) dl.AddRectFilled(rp, rp + new Vector2(w, rh), Theme.U32(Theme.Rgb(0xffffff, 0.035f)), Theme.Px(7));
                dl.AddCircleFilled(rp + new Vector2(Theme.Px(18), rh * 0.5f), Theme.Px(6), Theme.U32(new Vector4(c.Color, on ? 1 : 0.35f)));
                string name = S.Lang == Lang.Tr ? c.Tr : c.En;
                var tc = on && !peeled ? Theme.Text : Theme.TextMuted;
                dl.AddText(rp + new Vector2(Theme.Px(34), (rh - ImGui.GetTextLineHeight()) * 0.5f), Theme.U32(tc), name + (peeled && on ? "  ·  " + L.T("peel").ToLower(System.Globalization.CultureInfo.GetCultureInfo(S.Lang == Lang.Tr ? "tr-TR" : "en-US")) : ""));
                ImGui.PushFont(F.Small);
                string cnt = counts[(int)c.Id].ToString("N0");
                var cs = ImGui.CalcTextSize(cnt);
                dl.AddText(rp + new Vector2(w - Theme.Px(52) - cs.X, (rh - cs.Y) * 0.5f), Theme.U32(Theme.TextMuted), cnt);
                ImGui.PopFont();
                ImGui.SetCursorScreenPos(rp + new Vector2(w - Theme.Px(38), (rh - ImGui.GetFrameHeight()) * 0.5f));
                bool v = on;
                if (Ui.Toggle("t", ref v)) S.ToggleCategory(c.Id);
                if (clicked) S.ToggleCategory(c.Id);
                ImGui.SetCursorScreenPos(rp);
                ImGui.Dummy(new Vector2(w, rh));
                ImGui.PopID();
            }
            ImGui.Dummy(new Vector2(0, Theme.Px(6)));
        }
        Ui.Separator(4);
        if (Ui.Button(Icons.Eye, L.T("show") + " — " + (S.Lang == Lang.Tr ? "tümü" : "all"), "unhideAll", width: 0)) S.UnhideAll();
    }

    // ------------------------------------------------------------------------------------------------ yapı ağacı
    private void DrawTreeTab()
    {
        float w = ImGui.GetContentRegionAvail().X;
        ImGui.PushItemWidth(w - Theme.Px(76));
        ImGui.InputTextWithHint("##treeFilter", Icons.Filter + "  " + L.T("filterTree"), ref treeFilter, 64);
        ImGui.PopItemWidth();
        ImGui.SameLine(0, Theme.Px(4));
        if (Ui.IconButton(Icons.Add, "expandAll", L.T("expandAll"), false, 32))
            foreach (var n in tree) ExpandAll(n);
        ImGui.SameLine(0, Theme.Px(2));
        if (Ui.IconButton(Icons.Remove, "collapseAll", L.T("collapseAll"), false, 32)) expanded.Clear();

        // görünür satırlar
        treeRows.Clear();
        string f = SearchIndex.Normalize(treeFilter.Trim());
        foreach (var n in tree) CollectRows(n, 0, f);

        ImGui.BeginChild("##treeRows", new Vector2(0, 0));
        float rowH = Theme.Px(30);
        var dl = ImGui.GetWindowDrawList();
        unsafe
        {
            var clipper = new ImGuiListClipperPtr(ImGuiNative.ImGuiListClipper_ImGuiListClipper());
            clipper.Begin(treeRows.Count, rowH);
            while (clipper.Step())
            {
                for (int i = clipper.DisplayStart; i < clipper.DisplayEnd; i++)
                {
                    var (depth, node, part) = treeRows[i];
                    var rp = ImGui.GetCursorScreenPos();
                    float rw = ImGui.GetContentRegionAvail().X;
                    float indent = Theme.Px(6) + depth * Theme.Px(14);
                    ImGui.PushID(i);
                    bool clicked = ImGui.InvisibleButton("row", new Vector2(rw - Theme.Px(62), rowH));
                    bool hov = ImGui.IsItemHovered();
                    bool rowHov = ImGui.IsMouseHoveringRect(rp, rp + new Vector2(rw, rowH));
                    if (node != null)
                    {
                        bool open = expanded.Contains(node.Key) || f.Length > 0;
                        int hidden = node.All.Count(S.HiddenParts.Contains);
                        bool allHidden = hidden == node.All.Length;
                        if (hov) dl.AddRectFilled(rp, rp + new Vector2(rw, rowH), Theme.U32(Theme.Rgb(0xffffff, 0.035f)), Theme.Px(6));
                        dl.AddText(rp + new Vector2(indent, (rowH - ImGui.GetTextLineHeight()) * 0.5f), Theme.U32(Theme.TextMuted), open ? Icons.ChevronDown : Icons.ChevronRight);
                        string label = Ui.Ellipsize(node.Label, rw - indent - Theme.Px(110));
                        dl.AddText(rp + new Vector2(indent + Theme.Px(20), (rowH - ImGui.GetTextLineHeight()) * 0.5f), Theme.U32(allHidden ? Theme.TextMuted : depth == 0 ? Theme.Text : Theme.TextDim), label);
                        ImGui.PushFont(F.Small);
                        string cnt = node.All.Length.ToString();
                        var cs = ImGui.CalcTextSize(cnt);
                        dl.AddText(rp + new Vector2(rw - Theme.Px(66) - cs.X, (rowH - cs.Y) * 0.5f), Theme.U32(Theme.TextMuted), cnt);
                        ImGui.PopFont();
                        if (hov && node.Sub != null) Ui.Tooltip(node.Sub);
                        if (clicked) { if (!expanded.Remove(node.Key)) expanded.Add(node.Key); }
                        if (rowHov || hidden > 0)
                        {
                            ImGui.SetCursorScreenPos(rp + new Vector2(rw - Theme.Px(58), Theme.Px(2)));
                            if (Ui.IconButton(Icons.Target, "iso", L.T("isolate"), false, 26)) S.Isolate(node.All);
                            ImGui.SetCursorScreenPos(rp + new Vector2(rw - Theme.Px(30), Theme.Px(2)));
                            if (Ui.IconButton(allHidden ? Icons.EyeOff : Icons.Eye, "vis", allHidden ? L.T("show") : L.T("hide"), false, 26,
                                    color: hidden > 0 && !allHidden ? Theme.Warning : null))
                                S.SetPartsHidden(node.All, !allHidden);
                        }
                    }
                    else if (part != null)
                    {
                        bool hidden = S.HiddenParts.Contains(part.Index);
                        bool sel = S.Selected == part.Index;
                        if (sel) dl.AddRectFilled(rp, rp + new Vector2(rw, rowH), Theme.U32(Theme.AccentSoft), Theme.Px(6));
                        else if (hov) dl.AddRectFilled(rp, rp + new Vector2(rw, rowH), Theme.U32(Theme.Rgb(0xffffff, 0.035f)), Theme.Px(6));
                        dl.AddCircleFilled(rp + new Vector2(indent + Theme.Px(8), rowH * 0.5f), Theme.Px(3.5f), Theme.U32(new Vector4(part.Info.Color, hidden ? 0.35f : 1)));
                        string label = Ui.Ellipsize(part.DisplayName(S.Lang), rw - indent - Theme.Px(90));
                        dl.AddText(rp + new Vector2(indent + Theme.Px(20), (rowH - ImGui.GetTextLineHeight()) * 0.5f),
                            Theme.U32(hidden ? Theme.TextMuted : sel ? Theme.Rgb(0x9cc6ff) : Theme.Text), label);
                        if (hov)
                        {
                            var sub = part.SecondaryName(S.Lang);
                            if (sub != null) Ui.Tooltip(sub);
                        }
                        if (clicked)
                        {
                            S.SetPartsHidden([part.Index], false);
                            S.SetCategories([part.Category], false);
                            S.Select(part.Index, focus: true);
                        }
                        if (rowHov || hidden)
                        {
                            ImGui.SetCursorScreenPos(rp + new Vector2(rw - Theme.Px(30), Theme.Px(2)));
                            if (Ui.IconButton(hidden ? Icons.EyeOff : Icons.Eye, "vis", hidden ? L.T("show") : L.T("hide"), false, 26)) S.TogglePart(part.Index);
                        }
                    }
                    // satırı bir öğeyle kapat (imleci elle taşımak pencere sınırını büyütmez)
                    ImGui.SetCursorScreenPos(rp);
                    ImGui.Dummy(new Vector2(rw, rowH));
                    ImGui.PopID();
                }
            }
            clipper.End();
            clipper.Destroy();
        }
        ImGui.EndChild();
    }

    private void ExpandAll(TreeNode n)
    {
        expanded.Add(n.Key);
        foreach (var c in n.Children) ExpandAll(c);
    }

    /// <summary>Ağaç satırları; filtre varsa yalnız eşleşen dallar (açık halde) gösterilir.</summary>
    private bool CollectRows(TreeNode n, int depth, string filter)
    {
        if (filter.Length > 0)
        {
            int start = treeRows.Count;
            treeRows.Add((depth, n, null));
            bool any = false;
            foreach (var c in n.Children) any |= CollectRows(c, depth + 1, filter);
            foreach (var p in n.Parts)
                if (p.SearchText.Contains(filter)) { treeRows.Add((depth + 1, null, p)); any = true; }
            if (!any && !SearchIndex.Normalize(n.Label).Contains(filter)) treeRows.RemoveRange(start, treeRows.Count - start);
            return any;
        }
        treeRows.Add((depth, n, null));
        if (!expanded.Contains(n.Key)) return true;
        foreach (var c in n.Children) CollectRows(c, depth + 1, filter);
        foreach (var p in n.Parts) treeRows.Add((depth + 1, null, p));
        return true;
    }

    // ------------------------------------------------------------------------------------------------ bölgeler
    private void DrawRegionsTab()
    {
        ImGui.PushTextWrapPos(0);
        Ui.TextMuted(L.T("regionsHint"));
        ImGui.PopTextWrapPos();
        ImGui.Dummy(new Vector2(0, Theme.Px(4)));
        string? lastGroup = null;
        var dl = ImGui.GetWindowDrawList();
        for (int i = 0; i < Regions.Presets.Length; i++)
        {
            var r = Regions.Presets[i];
            string group = S.Lang == Lang.Tr ? r.GroupTr : r.GroupEn;
            if (group != lastGroup)
            {
                Ui.SectionHeader(group);
                lastGroup = group;
            }
            var rp = ImGui.GetCursorScreenPos();
            float w = ImGui.GetContentRegionAvail().X, h = Theme.Px(38);
            bool clicked = ImGui.InvisibleButton("reg" + i, new Vector2(w, h));
            bool hov = ImGui.IsItemHovered();
            bool act = activeRegion == i && S.Isolated != null;
            dl.AddRectFilled(rp, rp + new Vector2(w, h), Theme.U32(act ? Theme.AccentSoft : hov ? Theme.SurfaceHover : Theme.Surface), Theme.Px(8));
            dl.AddText(rp + new Vector2(Theme.Px(12), (h - ImGui.GetTextLineHeight()) * 0.5f), Theme.U32(act ? Theme.Accent : Theme.TextDim), r.Icon);
            dl.AddText(rp + new Vector2(Theme.Px(38), (h - ImGui.GetTextLineHeight()) * 0.5f), Theme.U32(act ? Theme.Rgb(0x9cc6ff) : Theme.Text), S.Lang == Lang.Tr ? r.Tr : r.En);
            dl.AddText(rp + new Vector2(w - Theme.Px(24), (h - ImGui.GetTextLineHeight()) * 0.5f), Theme.U32(Theme.TextMuted), act ? Icons.Close : Icons.ChevronRight);
            if (clicked)
            {
                if (act) { S.Isolate(null); activeRegion = -1; }
                else ApplyRegion(i);
            }
            ImGui.Dummy(new Vector2(0, Theme.Px(2)));
        }
    }

    public void ApplyRegion(int i)
    {
        var r = Regions.Presets[i];
        var (main, ctx) = Regions.Resolve(Model, r);
        if (main.Count == 0) return;
        activeRegion = i;
        S.SetPeel(0);
        S.Isolate(main.Concat(ctx));
        S.Select(-1);
        var (c, rad) = Regions.Bounds(Model, main);
        float aspect = Display.X / MathF.Max(1, Display.Y);
        float fov = app.Camera.FovY;
        float fit = rad / MathF.Sin(MathF.Min(fov, fov * aspect) / 2);
        S.CameraRequest = null;
        app.Camera.FlyTo(c, Math.Clamp(fit * 1.15f, 0.15f, 4.5f), r.Yaw, r.Pitch, 0.9f);
    }
}
