using System.Numerics;
using ImGuiNET;

namespace Anatomi3D.Desktop.UI;

/// <summary>Uygulamanın görsel dili: koyu, sakin yüzeyler, mavi vurgu, yuvarlak köşeler, geniş boşluklar.</summary>
public static class Theme
{
    public static Vector4 Rgb(uint hex, float a = 1f) =>
        new(((hex >> 16) & 0xff) / 255f, ((hex >> 8) & 0xff) / 255f, (hex & 0xff) / 255f, a);

    public static uint U32(Vector4 c) => ImGui.ColorConvertFloat4ToU32(c);
    public static uint U32(uint hex, float a = 1f) => ImGui.ColorConvertFloat4ToU32(Rgb(hex, a));

    // renk belirteçleri
    public static readonly Vector4 Bg = Rgb(0x0e1117);
    public static readonly Vector4 Panel = Rgb(0x141922, 0.96f);
    public static readonly Vector4 Surface = Rgb(0x1b212c);
    public static readonly Vector4 SurfaceHi = Rgb(0x232a37);
    public static readonly Vector4 SurfaceHover = Rgb(0x2a3242);
    public static readonly Vector4 Border = Rgb(0x2a3140);
    public static readonly Vector4 Text = Rgb(0xe8ebf1);
    public static readonly Vector4 TextDim = Rgb(0xa3adbd);
    public static readonly Vector4 TextMuted = Rgb(0x6f7888);
    public static readonly Vector4 Accent = Rgb(0x3e8ef7);
    public static readonly Vector4 AccentHover = Rgb(0x5aa0ff);
    public static readonly Vector4 AccentActive = Rgb(0x2f78de);
    public static readonly Vector4 AccentSoft = Rgb(0x3e8ef7, 0.16f);
    public static readonly Vector4 Coral = Rgb(0xff7a6b);
    public static readonly Vector4 Success = Rgb(0x30a46c);
    public static readonly Vector4 Danger = Rgb(0xe5484d);
    public static readonly Vector4 Warning = Rgb(0xf5a524);

    public static float S = 1f;
    public static float Px(float v) => MathF.Round(v * S);

    public static void Apply(float scale)
    {
        S = scale;
        var st = ImGui.GetStyle();
        st.WindowPadding = new Vector2(16, 16) * scale;
        st.FramePadding = new Vector2(10, 7) * scale;
        st.CellPadding = new Vector2(8, 6) * scale;
        st.ItemSpacing = new Vector2(8, 8) * scale;
        st.ItemInnerSpacing = new Vector2(8, 6) * scale;
        st.TouchExtraPadding = Vector2.Zero;
        st.IndentSpacing = 16 * scale;
        st.ScrollbarSize = 10 * scale;
        st.GrabMinSize = 10 * scale;
        st.WindowBorderSize = 1;
        st.ChildBorderSize = 0;
        st.PopupBorderSize = 1;
        st.FrameBorderSize = 0;
        st.TabBorderSize = 0;
        st.WindowRounding = 12 * scale;
        st.ChildRounding = 10 * scale;
        st.FrameRounding = 8 * scale;
        st.PopupRounding = 10 * scale;
        st.ScrollbarRounding = 8 * scale;
        st.GrabRounding = 8 * scale;
        st.TabRounding = 8 * scale;
        st.WindowTitleAlign = new Vector2(0, 0.5f);
        st.ButtonTextAlign = new Vector2(0.5f, 0.5f);
        st.SelectableTextAlign = new Vector2(0, 0.5f);
        st.DisplaySafeAreaPadding = Vector2.Zero;
        st.AntiAliasedLines = true;
        st.AntiAliasedFill = true;
        st.CurveTessellationTol = 1.0f;

        var c = st.Colors;
        c[(int)ImGuiCol.Text] = Text;
        c[(int)ImGuiCol.TextDisabled] = TextMuted;
        c[(int)ImGuiCol.WindowBg] = Panel;
        c[(int)ImGuiCol.ChildBg] = new Vector4(0, 0, 0, 0);
        c[(int)ImGuiCol.PopupBg] = Rgb(0x171c26, 0.98f);
        c[(int)ImGuiCol.Border] = Border;
        c[(int)ImGuiCol.BorderShadow] = new Vector4(0, 0, 0, 0);
        c[(int)ImGuiCol.FrameBg] = Surface;
        c[(int)ImGuiCol.FrameBgHovered] = SurfaceHi;
        c[(int)ImGuiCol.FrameBgActive] = SurfaceHover;
        c[(int)ImGuiCol.TitleBg] = Panel;
        c[(int)ImGuiCol.TitleBgActive] = Panel;
        c[(int)ImGuiCol.TitleBgCollapsed] = Panel;
        c[(int)ImGuiCol.MenuBarBg] = Surface;
        c[(int)ImGuiCol.ScrollbarBg] = new Vector4(0, 0, 0, 0);
        c[(int)ImGuiCol.ScrollbarGrab] = Rgb(0x2c3444);
        c[(int)ImGuiCol.ScrollbarGrabHovered] = Rgb(0x394357);
        c[(int)ImGuiCol.ScrollbarGrabActive] = Rgb(0x455068);
        c[(int)ImGuiCol.CheckMark] = Accent;
        c[(int)ImGuiCol.SliderGrab] = Accent;
        c[(int)ImGuiCol.SliderGrabActive] = AccentHover;
        c[(int)ImGuiCol.Button] = Surface;
        c[(int)ImGuiCol.ButtonHovered] = SurfaceHover;
        c[(int)ImGuiCol.ButtonActive] = Rgb(0x323c50);
        c[(int)ImGuiCol.Header] = AccentSoft;
        c[(int)ImGuiCol.HeaderHovered] = Rgb(0x2a3242);
        c[(int)ImGuiCol.HeaderActive] = Rgb(0x3e8ef7, 0.28f);
        c[(int)ImGuiCol.Separator] = Border;
        c[(int)ImGuiCol.SeparatorHovered] = Accent;
        c[(int)ImGuiCol.SeparatorActive] = AccentActive;
        c[(int)ImGuiCol.ResizeGrip] = new Vector4(0, 0, 0, 0);
        c[(int)ImGuiCol.ResizeGripHovered] = AccentSoft;
        c[(int)ImGuiCol.ResizeGripActive] = Accent;
        c[(int)ImGuiCol.Tab] = new Vector4(0, 0, 0, 0);
        c[(int)ImGuiCol.TabHovered] = SurfaceHover;
        c[(int)ImGuiCol.TabSelected] = SurfaceHi;
        c[(int)ImGuiCol.TabSelectedOverline] = Accent;
        c[(int)ImGuiCol.TabDimmed] = new Vector4(0, 0, 0, 0);
        c[(int)ImGuiCol.TabDimmedSelected] = Surface;
        c[(int)ImGuiCol.PlotLines] = Accent;
        c[(int)ImGuiCol.PlotHistogram] = Accent;
        c[(int)ImGuiCol.TableHeaderBg] = Surface;
        c[(int)ImGuiCol.TableBorderStrong] = Border;
        c[(int)ImGuiCol.TableBorderLight] = Rgb(0x222936);
        c[(int)ImGuiCol.TableRowBg] = new Vector4(0, 0, 0, 0);
        c[(int)ImGuiCol.TableRowBgAlt] = Rgb(0xffffff, 0.02f);
        c[(int)ImGuiCol.TextSelectedBg] = Rgb(0x3e8ef7, 0.35f);
        c[(int)ImGuiCol.DragDropTarget] = Accent;
        c[(int)ImGuiCol.NavCursor] = Accent;
        c[(int)ImGuiCol.NavWindowingHighlight] = Accent;
        c[(int)ImGuiCol.NavWindowingDimBg] = Rgb(0x000000, 0.4f);
        c[(int)ImGuiCol.ModalWindowDimBg] = Rgb(0x05070a, 0.62f);
    }
}
