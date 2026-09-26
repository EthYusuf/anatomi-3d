using System.Numerics;
using ImGuiNET;

namespace Anatomi3D.Desktop.UI;

/// <summary>Segoe MDL2 Assets / Segoe Fluent Icons simge kodları.</summary>
public static class Icons
{
    public const string Menu = "", Search = "", Close = "", Settings = "", Help = "", Info = "";
    public const string Eye = "", EyeOff = "", Camera = "", Home = "", Reset = "", Undo = "";
    public const string Layers = "", Cut = "", Zoom = "", Target = "", ChevronDown = "", ChevronRight = "";
    public const string ChevronUp = "", ChevronLeft = "", Add = "", Remove = "", Check = "";
    public const string FullScreen = "", BackToWindow = "", Education = "", Globe = "", Heart = "";
    public const string Bulb = "", Picture = "", Save = "", Filter = "", List = "", Tag = "";
    public const string Play = "", Pause = "", Warning = "", Library = "", Person = "", Rotate = "";
    public const string Move = "", Flag = "", Star = "", Lightning = "", Ghost = "", Pin = "";
    public const string Isolate = "", Fx = "", Contrast = "", Sync = "", Flip = "", Swap = "";
}

/// <summary>Özel çizilmiş arayüz bileşenleri (yuvarlak düğmeler, anahtar, bölümlü seçici, çip, bilgi satırı).</summary>
public static class Ui
{
    private static readonly Dictionary<uint, float> Anim = [];

    public static float Animate(uint id, float target, float speed = 14f)
    {
        float dt = ImGui.GetIO().DeltaTime;
        Anim.TryGetValue(id, out float v);
        v += (target - v) * (1 - MathF.Exp(-dt * speed));
        if (MathF.Abs(v - target) < 0.001f) v = target;
        Anim[id] = v;
        return v;
    }

    public static bool AnyAnimating()
    {
        foreach (var v in Anim.Values) if (v > 0.001f && v < 0.999f) return true;
        return false;
    }

    public static Vector4 Mix(Vector4 a, Vector4 b, float t) => Vector4.Lerp(a, b, Math.Clamp(t, 0, 1));

    /// <summary>Yuvarlak köşeli simge düğmesi (arka plan üzerine gelince belirir).</summary>
    public static bool IconButton(string icon, string id, string? tooltip = null, bool active = false, float size = 34, bool disabled = false, Vector4? color = null)
    {
        float s = Theme.Px(size);
        var pos = ImGui.GetCursorScreenPos();
        ImGui.PushID(id);
        bool pressed = ImGui.InvisibleButton(id, new Vector2(s, s)) && !disabled;
        bool hovered = ImGui.IsItemHovered() && !disabled;
        bool held = ImGui.IsItemActive() && !disabled;
        uint aid = ImGui.GetID("anim");
        float h = Animate(aid, hovered ? 1 : 0);
        var dl = ImGui.GetWindowDrawList();
        var bg = active ? Theme.AccentSoft : Mix(new Vector4(Theme.SurfaceHover.X, Theme.SurfaceHover.Y, Theme.SurfaceHover.Z, 0), Theme.SurfaceHover, h);
        if (held) bg = Theme.Rgb(0x323c50);
        if (bg.W > 0.01f) dl.AddRectFilled(pos, pos + new Vector2(s, s), Theme.U32(bg), Theme.Px(8));
        var fg = disabled ? Theme.TextMuted : active ? Theme.Accent : color ?? Mix(Theme.TextDim, Theme.Text, h);
        DrawIcon(dl, icon, pos + new Vector2(s, s) * 0.5f, Theme.U32(fg));
        if (tooltip != null && hovered) Tooltip(tooltip);
        ImGui.PopID();
        return pressed;
    }

    /// <summary>Simgeyi merkeze çizer; yazı tipinde olmayan "göz kapalı" simgesi göz + çapraz çizgiyle üretilir.</summary>
    public static void DrawIcon(ImDrawListPtr dl, string icon, Vector2 center, uint color)
    {
        bool off = icon == Icons.EyeOff;
        string g = off ? Icons.Eye : icon;
        var ts = ImGui.CalcTextSize(g);
        dl.AddText(center - ts * 0.5f, color, g);
        if (off)
        {
            float r = ts.Y * 0.42f;
            dl.AddLine(center + new Vector2(-r, -r * 0.75f), center + new Vector2(r, r * 0.75f), color, MathF.Max(1.5f, ts.Y * 0.08f));
        }
    }

    /// <summary>Simge + metin düğmesi.</summary>
    public static bool Button(string icon, string label, string id, bool active = false, bool primary = false, float width = 0, float height = 34, bool disabled = false)
    {
        float h = Theme.Px(height);
        string text = icon.Length > 0 ? icon + "  " + label : label;
        var ts = ImGui.CalcTextSize(text);
        float w = width > 0 ? Theme.Px(width) : ts.X + Theme.Px(24);
        var pos = ImGui.GetCursorScreenPos();
        ImGui.PushID(id);
        bool pressed = ImGui.InvisibleButton(id, new Vector2(w, h)) && !disabled;
        bool hovered = ImGui.IsItemHovered() && !disabled;
        bool held = ImGui.IsItemActive() && !disabled;
        float a = Animate(ImGui.GetID("anim"), hovered ? 1 : 0);
        var dl = ImGui.GetWindowDrawList();
        Vector4 bg, fg;
        if (primary)
        {
            bg = held ? Theme.AccentActive : Mix(Theme.Accent, Theme.AccentHover, a);
            fg = new Vector4(1, 1, 1, 1);
        }
        else if (active)
        {
            bg = Mix(Theme.AccentSoft, Theme.Rgb(0x3e8ef7, 0.24f), a);
            fg = Theme.Rgb(0x8cbcff);
        }
        else
        {
            bg = held ? Theme.Rgb(0x323c50) : Mix(Theme.Surface, Theme.SurfaceHover, a);
            fg = disabled ? Theme.TextMuted : Mix(Theme.TextDim, Theme.Text, a * 0.6f + 0.4f);
        }
        dl.AddRectFilled(pos, pos + new Vector2(w, h), Theme.U32(bg), Theme.Px(8));
        if (icon == Icons.EyeOff)
        {
            float iw = ImGui.CalcTextSize(Icons.Eye + "  ").X;
            var tp = pos + new Vector2((w - ts.X) * 0.5f, (h - ts.Y) * 0.5f);
            DrawIcon(dl, icon, tp + new Vector2(ImGui.CalcTextSize(Icons.Eye).X * 0.5f, ts.Y * 0.5f), Theme.U32(fg));
            dl.AddText(tp + new Vector2(iw, 0), Theme.U32(fg), label);
        }
        else dl.AddText(pos + new Vector2((w - ts.X) * 0.5f, (h - ts.Y) * 0.5f), Theme.U32(fg), text);
        ImGui.PopID();
        return pressed;
    }

    /// <summary>iOS tarzı anahtar.</summary>
    public static bool Toggle(string id, ref bool value, string? label = null)
    {
        float h = Theme.Px(20), w = Theme.Px(36);
        var pos = ImGui.GetCursorScreenPos();
        float lineH = ImGui.GetFrameHeight();
        var p = pos + new Vector2(0, (lineH - h) * 0.5f);
        ImGui.PushID(id);
        float labelW = label != null ? ImGui.CalcTextSize(label).X + Theme.Px(10) : 0;
        bool pressed = ImGui.InvisibleButton(id, new Vector2(w + labelW, lineH));
        if (pressed) value = !value;
        bool hovered = ImGui.IsItemHovered();
        float t = Animate(ImGui.GetID("knob"), value ? 1 : 0, 18);
        var dl = ImGui.GetWindowDrawList();
        var track = Mix(hovered ? Theme.SurfaceHover : Theme.SurfaceHi, Theme.Accent, t);
        dl.AddRectFilled(p, p + new Vector2(w, h), Theme.U32(track), h * 0.5f);
        float r = h * 0.5f - Theme.Px(3);
        var c = p + new Vector2(h * 0.5f + (w - h) * t, h * 0.5f);
        dl.AddCircleFilled(c, r, Theme.U32(0xffffff), 20);
        if (label != null)
            dl.AddText(pos + new Vector2(w + Theme.Px(10), (lineH - ImGui.GetTextLineHeight()) * 0.5f), Theme.U32(Theme.Text), label);
        ImGui.PopID();
        return pressed;
    }

    /// <summary>Bölümlü seçici (segmented control); seçim göstergesi kayarak hareket eder.</summary>
    public static bool Segmented(string id, string[] labels, ref int index, float width = 0)
    {
        float h = Theme.Px(32);
        float w = width > 0 ? width : ImGui.GetContentRegionAvail().X;
        var pos = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(pos, pos + new Vector2(w, h), Theme.U32(Theme.Surface), Theme.Px(8));
        float seg = w / labels.Length;
        ImGui.PushID(id);
        float x = Animate(ImGui.GetID("sel"), index, 16);
        var sp = pos + new Vector2(seg * x + Theme.Px(3), Theme.Px(3));
        dl.AddRectFilled(sp, sp + new Vector2(seg - Theme.Px(6), h - Theme.Px(6)), Theme.U32(Theme.SurfaceHover), Theme.Px(6));
        bool changed = false;
        for (int i = 0; i < labels.Length; i++)
        {
            ImGui.SetCursorScreenPos(pos + new Vector2(seg * i, 0));
            if (ImGui.InvisibleButton(labels[i] + i, new Vector2(seg, h)) && index != i)
            {
                index = i;
                changed = true;
            }
            bool hov = ImGui.IsItemHovered();
            var ts = ImGui.CalcTextSize(labels[i]);
            var col = i == index ? Theme.Text : hov ? Theme.Text : Theme.TextDim;
            dl.AddText(pos + new Vector2(seg * i + (seg - ts.X) * 0.5f, (h - ts.Y) * 0.5f), Theme.U32(col), labels[i]);
        }
        ImGui.PopID();
        ImGui.SetCursorScreenPos(pos + new Vector2(0, h + ImGui.GetStyle().ItemSpacing.Y));
        ImGui.Dummy(new Vector2(w, 0));
        return changed;
    }

    public static void Chip(string text, Vector4 color)
    {
        var pos = ImGui.GetCursorScreenPos();
        var ts = ImGui.CalcTextSize(text);
        float padX = Theme.Px(10), h = ts.Y + Theme.Px(8);
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(pos, pos + new Vector2(ts.X + padX * 2 + Theme.Px(12), h), Theme.U32(new Vector4(color.X, color.Y, color.Z, 0.16f)), h * 0.5f);
        dl.AddCircleFilled(pos + new Vector2(padX + Theme.Px(3), h * 0.5f), Theme.Px(4), Theme.U32(color));
        dl.AddText(pos + new Vector2(padX + Theme.Px(12), Theme.Px(4)), Theme.U32(Mix(color, Theme.Text, 0.55f)), text);
        ImGui.Dummy(new Vector2(ts.X + padX * 2 + Theme.Px(12), h));
    }

    /// <summary>Tıklanabilir yuvarlak etiket (ilgili yapılar).</summary>
    public static bool LinkChip(string text, string id, Vector4? dot = null)
    {
        var ts = ImGui.CalcTextSize(text);
        float padX = Theme.Px(10), h = ts.Y + Theme.Px(10);
        float dotW = dot.HasValue ? Theme.Px(14) : 0;
        float w = ts.X + padX * 2 + dotW;
        float avail = ImGui.GetContentRegionAvail().X;
        if (w > avail) w = avail;
        var pos = ImGui.GetCursorScreenPos();
        ImGui.PushID(id);
        bool pressed = ImGui.InvisibleButton(id, new Vector2(w, h));
        bool hov = ImGui.IsItemHovered();
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(pos, pos + new Vector2(w, h), Theme.U32(hov ? Theme.SurfaceHover : Theme.Surface), Theme.Px(7));
        if (dot.HasValue) dl.AddCircleFilled(pos + new Vector2(padX + Theme.Px(3), h * 0.5f), Theme.Px(4), Theme.U32(dot.Value));
        dl.PushClipRect(pos, pos + new Vector2(w - padX * 0.5f, h), true);
        dl.AddText(pos + new Vector2(padX + dotW, Theme.Px(5)), Theme.U32(hov ? Theme.Text : Theme.TextDim), text);
        dl.PopClipRect();
        if (hov) ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        ImGui.PopID();
        return pressed;
    }

    public static void SectionHeader(string text)
    {
        ImGui.Dummy(new Vector2(0, Theme.Px(4)));
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextMuted);
        ImGui.TextUnformatted(text.ToUpper(System.Globalization.CultureInfo.GetCultureInfo(L.Lang == Anatomi3D.Core.Content.Lang.Tr ? "tr-TR" : "en-US")));
        ImGui.PopStyleColor();
    }

    /// <summary>Başlık-değer satırı (bilgi tablosu).</summary>
    public static void KeyValue(string key, string value, float keyWidth)
    {
        var pos = ImGui.GetCursorScreenPos();
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextMuted);
        ImGui.TextUnformatted(key);
        ImGui.PopStyleColor();
        ImGui.SameLine(keyWidth);
        ImGui.PushTextWrapPos(0);
        ImGui.TextUnformatted(value);
        ImGui.PopTextWrapPos();
        _ = pos;
    }

    public static void Tooltip(string text)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(Theme.Px(10), Theme.Px(7)));
        ImGui.PushStyleColor(ImGuiCol.PopupBg, Theme.Rgb(0x0b0e13, 0.96f));
        ImGui.BeginTooltip();
        ImGui.TextUnformatted(text);
        ImGui.EndTooltip();
        ImGui.PopStyleColor();
        ImGui.PopStyleVar();
    }

    public static void Separator(float pad = 6)
    {
        ImGui.Dummy(new Vector2(0, Theme.Px(pad)));
        var p = ImGui.GetCursorScreenPos();
        float w = ImGui.GetContentRegionAvail().X;
        ImGui.GetWindowDrawList().AddLine(p, p + new Vector2(w, 0), Theme.U32(Theme.Border));
        ImGui.Dummy(new Vector2(0, Theme.Px(pad)));
    }

    public static void TextDim(string text, bool wrap = true)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextDim);
        if (wrap) ImGui.TextWrapped(text); else ImGui.TextUnformatted(text);
        ImGui.PopStyleColor();
    }

    public static void TextMuted(string text, bool wrap = true)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextMuted);
        if (wrap) ImGui.TextWrapped(text); else ImGui.TextUnformatted(text);
        ImGui.PopStyleColor();
    }

    /// <summary>Yuvarlak köşeli, gölgeli yüzen panel başlatır (NoDecoration pencere).</summary>
    public static bool BeginPanel(string id, Vector2 pos, Vector2 size, float alpha = 1f, ImGuiWindowFlags extra = 0, bool front = false)
    {
        ImGui.SetNextWindowPos(pos);
        ImGui.SetNextWindowSize(size);
        var dl = ImGui.GetBackgroundDrawList();
        // yumuşak gölge
        for (int i = 1; i <= 4; i++)
        {
            float g = Theme.Px(i * 3);
            dl.AddRectFilled(pos - new Vector2(g, g * 0.5f) + new Vector2(0, g), pos + size + new Vector2(g, g), Theme.U32(0x000000, 0.06f * alpha), Theme.Px(12) + g);
        }
        ImGui.PushStyleVar(ImGuiStyleVar.Alpha, alpha);
        var flags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoSavedSettings | extra;
        if (!front) flags |= ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoBringToFrontOnFocus;
        bool open = ImGui.Begin(id, flags);
        return open;
    }

    public static void EndPanel()
    {
        ImGui.End();
        ImGui.PopStyleVar();
    }

    /// <summary>Yazıyı verilen genişliğe sığdırır (sonuna … ekler).</summary>
    public static string Ellipsize(string text, float maxWidth)
    {
        if (ImGui.CalcTextSize(text).X <= maxWidth) return text;
        int lo = 0, hi = text.Length;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (ImGui.CalcTextSize(text[..mid] + "…").X <= maxWidth) lo = mid; else hi = mid - 1;
        }
        return text[..lo].TrimEnd() + "…";
    }
}
