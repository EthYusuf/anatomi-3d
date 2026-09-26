using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Anatomi3D.Desktop.Platform;
using Anatomi3D.Graphics;
using ImGuiNET;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace Anatomi3D.Desktop.UI;

/// <summary>Uygulamanın yazı tipleri (DPI ölçeğine göre kurulur).</summary>
public sealed class Fonts
{
    public ImFontPtr Body;
    public ImFontPtr BodyBold;
    public ImFontPtr Small;
    public ImFontPtr SmallBold;
    public ImFontPtr Heading;
    public ImFontPtr Title;
    public ImFontPtr Display;
    public ImFontPtr IconLarge;
}

/// <summary>
/// Dear ImGui için Direct3D 11 çizici ve Win32 giriş köprüsü.
/// Yazı tipi: Segoe UI (Türkçe karakterler dahil Latin Genişletilmiş-A), simgeler: Segoe MDL2 / Fluent Icons.
/// </summary>
public sealed unsafe class ImGuiBackend : IDisposable
{
    private readonly GraphicsDevice gd;
    private readonly Window window;
    private readonly IntPtr context;
    private ID3D11VertexShader vs = null!;
    private ID3D11PixelShader ps = null!;
    private ID3D11InputLayout layout = null!;
    private ID3D11Buffer cb = null!;
    private ID3D11Buffer? vb, ib;
    private int vbSize = 5000, ibSize = 10000;
    private ID3D11SamplerState sampler = null!;
    private ID3D11BlendState blend = null!;
    private ID3D11RasterizerState raster = null!;
    private ID3D11DepthStencilState depth = null!;
    private ID3D11ShaderResourceView? fontSrv;
    private readonly Dictionary<IntPtr, ID3D11ShaderResourceView> textures = [];
    private int nextTexId = 100;
    private readonly List<IntPtr> glyphRangeAllocs = [];
    private readonly IntPtr[] cursors = new IntPtr[(int)ImGuiMouseCursor.COUNT];
    public Fonts Fonts { get; } = new();
    public float Scale { get; private set; } = 1f;

    public ImGuiBackend(GraphicsDevice gd, Window window)
    {
        this.gd = gd;
        this.window = window;
        context = ImGui.CreateContext();
        ImGui.SetCurrentContext(context);
        var io = ImGui.GetIO();
        io.BackendFlags |= ImGuiBackendFlags.HasMouseCursors | ImGuiBackendFlags.RendererHasVtxOffset;
        io.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard;
        io.ConfigWindowsMoveFromTitleBarOnly = true;
        // arayüz durumu kaydı uygulama ayarlarında tutulur; imgui.ini yazılmasın
        io.NativePtr->IniFilename = null;
        io.NativePtr->LogFilename = null;

        cursors[(int)ImGuiMouseCursor.Arrow] = Win32.LoadCursorW(IntPtr.Zero, Win32.IDC_ARROW);
        cursors[(int)ImGuiMouseCursor.TextInput] = Win32.LoadCursorW(IntPtr.Zero, Win32.IDC_IBEAM);
        cursors[(int)ImGuiMouseCursor.ResizeAll] = Win32.LoadCursorW(IntPtr.Zero, Win32.IDC_SIZEALL);
        cursors[(int)ImGuiMouseCursor.ResizeEW] = Win32.LoadCursorW(IntPtr.Zero, Win32.IDC_SIZEWE);
        cursors[(int)ImGuiMouseCursor.ResizeNS] = Win32.LoadCursorW(IntPtr.Zero, Win32.IDC_SIZENS);
        cursors[(int)ImGuiMouseCursor.ResizeNESW] = Win32.LoadCursorW(IntPtr.Zero, Win32.IDC_SIZENESW);
        cursors[(int)ImGuiMouseCursor.ResizeNWSE] = Win32.LoadCursorW(IntPtr.Zero, Win32.IDC_SIZENWSE);
        cursors[(int)ImGuiMouseCursor.Hand] = Win32.LoadCursorW(IntPtr.Zero, Win32.IDC_HAND);
        cursors[(int)ImGuiMouseCursor.NotAllowed] = Win32.LoadCursorW(IntPtr.Zero, Win32.IDC_NO);

        CreateDeviceObjects();
        SetScale(window.DpiScale);
    }

    // ------------------------------------------------------------------------------------------------ yazı tipleri
    private IntPtr Ranges(params ushort[] pairs)
    {
        var mem = Marshal.AllocHGlobal((pairs.Length + 1) * 2);
        var p = (ushort*)mem;
        for (int i = 0; i < pairs.Length; i++) p[i] = pairs[i];
        p[pairs.Length] = 0;
        glyphRangeAllocs.Add(mem);
        return mem;
    }

    private static string FontPath(params string[] names)
    {
        string dir = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        foreach (var n in names)
        {
            var p = Path.Combine(dir, n);
            if (File.Exists(p)) return p;
        }
        return "";
    }

    /// <summary>DPI değişince yazı tipi atlası yeni ölçekte yeniden kurulur (bulanık ölçekleme yok).</summary>
    public void SetScale(float scale)
    {
        Scale = scale;
        var io = ImGui.GetIO();
        io.Fonts.Clear();
        foreach (var m in glyphRangeAllocs) Marshal.FreeHGlobal(m);
        glyphRangeAllocs.Clear();

        var text = Ranges(0x0020, 0x00FF, 0x0100, 0x017F, 0x0300, 0x036F, 0x0370, 0x03FF, 0x2000, 0x206F, 0x2070, 0x209F,
            0x2190, 0x21FF, 0x2200, 0x22FF, 0x25A0, 0x25FF, 0x2600, 0x26FF, 0x2700, 0x27BF);
        var icons = Ranges(0xE700, 0xF8FF);
        string regular = FontPath("segoeui.ttf"), semibold = FontPath("seguisb.ttf", "segoeuib.ttf"), bold = FontPath("segoeuib.ttf");
        string light = FontPath("segoeuisl.ttf", "segoeuil.ttf", "segoeui.ttf");
        string iconFont = FontPath("SegoeIcons.ttf", "segmdl2.ttf");
        string symbol = FontPath("seguisym.ttf");

        ImFontPtr Add(string file, float size, bool withIcons, float iconScale = 1f)
        {
            var cfg = ImGuiNative.ImFontConfig_ImFontConfig();
            cfg->OversampleH = 3;
            cfg->OversampleV = 2;
            cfg->PixelSnapH = 1;
            cfg->RasterizerMultiply = 1.08f;
            ImFontPtr f = file.Length > 0 ? io.Fonts.AddFontFromFileTTF(file, size * scale, cfg, text) : io.Fonts.AddFontDefault(cfg);
            if (symbol.Length > 0)
            {
                var sc = ImGuiNative.ImFontConfig_ImFontConfig();
                sc->MergeMode = 1;
                sc->OversampleH = 2;
                sc->PixelSnapH = 1;
                io.Fonts.AddFontFromFileTTF(symbol, size * scale, sc, Ranges(0x2190, 0x21FF, 0x2600, 0x26FF, 0x2700, 0x27BF, 0x25A0, 0x25FF));
                ImGuiNative.ImFontConfig_destroy(sc);
            }
            if (withIcons && iconFont.Length > 0)
            {
                var ic = ImGuiNative.ImFontConfig_ImFontConfig();
                ic->MergeMode = 1;
                ic->OversampleH = 2;
                ic->PixelSnapH = 1;
                ic->GlyphOffset = new Vector2(0, MathF.Round(size * scale * 0.14f));
                ic->GlyphMinAdvanceX = size * scale * iconScale;
                io.Fonts.AddFontFromFileTTF(iconFont, size * scale * iconScale * 0.92f, ic, icons);
                ImGuiNative.ImFontConfig_destroy(ic);
            }
            ImGuiNative.ImFontConfig_destroy(cfg);
            return f;
        }

        Fonts.Body = Add(regular, 15.5f, true);
        Fonts.BodyBold = Add(semibold, 15.5f, true);
        Fonts.Small = Add(regular, 13f, true);
        Fonts.SmallBold = Add(semibold, 12.5f, true);
        Fonts.Heading = Add(semibold, 19f, true);
        Fonts.Title = Add(semibold, 25f, true);
        Fonts.Display = Add(light, 34f, false);
        // büyük simgeler: yalnız simge yazı tipinden, simge kod aralığıyla
        {
            var ic = ImGuiNative.ImFontConfig_ImFontConfig();
            ic->OversampleH = 2;
            ic->PixelSnapH = 1;
            Fonts.IconLarge = iconFont.Length > 0 ? io.Fonts.AddFontFromFileTTF(iconFont, 20f * scale, ic, icons) : io.Fonts.AddFontDefault(ic);
            ImGuiNative.ImFontConfig_destroy(ic);
        }
        io.Fonts.Build();
        CreateFontTexture();
        Theme.Apply(scale);
    }

    private void CreateFontTexture()
    {
        var io = ImGui.GetIO();
        io.Fonts.GetTexDataAsRGBA32(out byte* pixels, out int w, out int h, out int bpp);
        var desc = new Texture2DDescription
        {
            Width = (uint)w, Height = (uint)h, MipLevels = 1, ArraySize = 1, Format = Format.R8G8B8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0), Usage = ResourceUsage.Immutable, BindFlags = BindFlags.ShaderResource,
        };
        using var tex = gd.Device.CreateTexture2D(ref desc, new SubresourceData(pixels, (uint)(w * bpp)));
        fontSrv?.Dispose();
        fontSrv = gd.Device.CreateShaderResourceView(tex);
        io.Fonts.SetTexID((IntPtr)1);
        textures[(IntPtr)1] = fontSrv;
        io.Fonts.ClearTexData();
    }

    /// <summary>Arayüzde gösterilecek bir doku kaydeder (ör. logo, önizleme).</summary>
    public IntPtr RegisterTexture(ID3D11ShaderResourceView srv)
    {
        var id = (IntPtr)nextTexId++;
        textures[id] = srv;
        return id;
    }

    // ------------------------------------------------------------------------------------------------ cihaz nesneleri
    private void CreateDeviceObjects()
    {
        var sc = new ShaderCompiler(gd.Device);
        vs = sc.VS("ImGui.hlsl", "VSMain", out var bytecode);
        ps = sc.PS("ImGui.hlsl", "PSMain");
        layout = gd.Device.CreateInputLayout(
        [
            new InputElementDescription("POSITION", 0, Format.R32G32_Float, 0, 0),
            new InputElementDescription("TEXCOORD", 0, Format.R32G32_Float, 8, 0),
            new InputElementDescription("COLOR", 0, Format.R8G8B8A8_UNorm, 16, 0),
        ], bytecode);
        cb = gd.Device.CreateBuffer(new BufferDescription(64, BindFlags.ConstantBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write));
        var bd = new BlendDescription();
        bd.RenderTarget[0] = new RenderTargetBlendDescription
        {
            BlendEnable = true, SourceBlend = Blend.SourceAlpha, DestinationBlend = Blend.InverseSourceAlpha, BlendOperation = BlendOperation.Add,
            SourceBlendAlpha = Blend.One, DestinationBlendAlpha = Blend.InverseSourceAlpha, BlendOperationAlpha = BlendOperation.Add,
            RenderTargetWriteMask = ColorWriteEnable.All,
        };
        blend = gd.Device.CreateBlendState(bd);
        var rd = RasterizerDescription.CullNone;
        rd.ScissorEnable = true;
        rd.DepthClipEnable = true;
        raster = gd.Device.CreateRasterizerState(rd);
        depth = gd.Device.CreateDepthStencilState(DepthStencilDescription.None);
        sampler = gd.Device.CreateSamplerState(new SamplerDescription(Filter.MinMagMipLinear, TextureAddressMode.Clamp, 0, 1, ComparisonFunction.Always, 0, 0));
    }

    // ------------------------------------------------------------------------------------------------ giriş
    public void NewFrame(float dt, bool consumeEvents = true)
    {
        var io = ImGui.GetIO();
        io.DisplaySize = new Vector2(window.Width, window.Height);
        io.DisplayFramebufferScale = Vector2.One;
        io.DeltaTime = Math.Max(dt, 1e-4f);
        if (consumeEvents) ProcessEvents(window.Events);
        UpdateModifiers(io);
        var cur = ImGui.GetMouseCursor();
        window.Cursor = io.MouseDrawCursor || cur == ImGuiMouseCursor.None ? IntPtr.Zero : cursors[(int)cur == -1 ? 0 : (int)cur];
        ImGui.NewFrame();
    }

    private static void UpdateModifiers(ImGuiIOPtr io)
    {
        io.AddKeyEvent(ImGuiKey.ModCtrl, (Win32.GetKeyState(0x11) & 0x8000) != 0);
        io.AddKeyEvent(ImGuiKey.ModShift, (Win32.GetKeyState(0x10) & 0x8000) != 0);
        io.AddKeyEvent(ImGuiKey.ModAlt, (Win32.GetKeyState(0x12) & 0x8000) != 0);
    }

    public void ProcessEvents(List<InputEvent> events)
    {
        var io = ImGui.GetIO();
        foreach (var e in events)
        {
            switch (e.Kind)
            {
                case InputKind.MouseMove: io.AddMousePosEvent(e.X, e.Y); break;
                case InputKind.MouseLeave: io.AddMousePosEvent(-float.MaxValue, -float.MaxValue); break;
                case InputKind.MouseDown: io.AddMousePosEvent(e.X, e.Y); io.AddMouseButtonEvent((int)e.Button, true); break;
                case InputKind.MouseUp: io.AddMousePosEvent(e.X, e.Y); io.AddMouseButtonEvent((int)e.Button, false); break;
                case InputKind.Wheel: io.AddMouseWheelEvent(e.Horizontal ? e.Delta : 0, e.Horizontal ? 0 : e.Delta); break;
                case InputKind.KeyDown or InputKind.KeyUp:
                {
                    bool down = e.Kind == InputKind.KeyDown;
                    var k = MapKey(e.Key);
                    if (k != ImGuiKey.None) io.AddKeyEvent(k, down);
                    // sol/sağ ayrımı olmayan değiştiriciler
                    if (e.Key == 0x10) { io.AddKeyEvent(ImGuiKey.LeftShift, down); }
                    if (e.Key == 0x11) { io.AddKeyEvent(ImGuiKey.LeftCtrl, down); }
                    if (e.Key == 0x12) { io.AddKeyEvent(ImGuiKey.LeftAlt, down); }
                    break;
                }
                case InputKind.Char: io.AddInputCharacterUTF16(e.Char); break;
                case InputKind.Focus: io.AddFocusEvent(e.Flag); break;
            }
        }
    }

    private static ImGuiKey MapKey(int vk)
    {
        if (vk >= 0x30 && vk <= 0x39) return ImGuiKey._0 + (vk - 0x30);
        if (vk >= 0x41 && vk <= 0x5A) return ImGuiKey.A + (vk - 0x41);
        if (vk >= 0x70 && vk <= 0x7B) return ImGuiKey.F1 + (vk - 0x70);
        if (vk >= 0x60 && vk <= 0x69) return ImGuiKey.Keypad0 + (vk - 0x60);
        return vk switch
        {
            0x09 => ImGuiKey.Tab, 0x25 => ImGuiKey.LeftArrow, 0x27 => ImGuiKey.RightArrow, 0x26 => ImGuiKey.UpArrow, 0x28 => ImGuiKey.DownArrow,
            0x21 => ImGuiKey.PageUp, 0x22 => ImGuiKey.PageDown, 0x24 => ImGuiKey.Home, 0x23 => ImGuiKey.End, 0x2D => ImGuiKey.Insert,
            0x2E => ImGuiKey.Delete, 0x08 => ImGuiKey.Backspace, 0x20 => ImGuiKey.Space, 0x0D => ImGuiKey.Enter, 0x1B => ImGuiKey.Escape,
            0xDE => ImGuiKey.Apostrophe, 0xBC => ImGuiKey.Comma, 0xBD => ImGuiKey.Minus, 0xBE => ImGuiKey.Period, 0xBF => ImGuiKey.Slash,
            0xBA => ImGuiKey.Semicolon, 0xBB => ImGuiKey.Equal, 0xDB => ImGuiKey.LeftBracket, 0xDC => ImGuiKey.Backslash,
            0xDD => ImGuiKey.RightBracket, 0xC0 => ImGuiKey.GraveAccent, 0x14 => ImGuiKey.CapsLock, 0x91 => ImGuiKey.ScrollLock,
            0x90 => ImGuiKey.NumLock, 0x2C => ImGuiKey.PrintScreen, 0x13 => ImGuiKey.Pause, 0x6E => ImGuiKey.KeypadDecimal,
            0x6F => ImGuiKey.KeypadDivide, 0x6A => ImGuiKey.KeypadMultiply, 0x6D => ImGuiKey.KeypadSubtract, 0x6B => ImGuiKey.KeypadAdd,
            0xA0 => ImGuiKey.LeftShift, 0xA2 => ImGuiKey.LeftCtrl, 0xA4 => ImGuiKey.LeftAlt, 0x5B => ImGuiKey.LeftSuper,
            0xA1 => ImGuiKey.RightShift, 0xA3 => ImGuiKey.RightCtrl, 0xA5 => ImGuiKey.RightAlt, 0x5C => ImGuiKey.RightSuper, 0x5D => ImGuiKey.Menu,
            _ => ImGuiKey.None,
        };
    }

    // ------------------------------------------------------------------------------------------------ çizim
    public void Render(ID3D11RenderTargetView target)
    {
        ImGui.Render();
        var dd = ImGui.GetDrawData();
        if (dd.DisplaySize.X <= 0 || dd.DisplaySize.Y <= 0 || dd.TotalVtxCount == 0) return;
        var ctx = gd.Context;

        if (vb == null || vbSize < dd.TotalVtxCount)
        {
            vb?.Dispose();
            vbSize = dd.TotalVtxCount + 5000;
            vb = gd.Device.CreateBuffer(new BufferDescription((uint)(vbSize * sizeof(ImDrawVert)), BindFlags.VertexBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write));
        }
        if (ib == null || ibSize < dd.TotalIdxCount)
        {
            ib?.Dispose();
            ibSize = dd.TotalIdxCount + 10000;
            ib = gd.Device.CreateBuffer(new BufferDescription((uint)(ibSize * sizeof(ushort)), BindFlags.IndexBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write));
        }
        var vm = ctx.Map(vb, MapMode.WriteDiscard, Vortice.Direct3D11.MapFlags.None);
        var im = ctx.Map(ib, MapMode.WriteDiscard, Vortice.Direct3D11.MapFlags.None);
        var vdst = (ImDrawVert*)vm.DataPointer;
        var idst = (ushort*)im.DataPointer;
        for (int n = 0; n < dd.CmdListsCount; n++)
        {
            var cl = dd.CmdLists[n];
            Unsafe.CopyBlock(vdst, (void*)cl.VtxBuffer.Data, (uint)(cl.VtxBuffer.Size * sizeof(ImDrawVert)));
            Unsafe.CopyBlock(idst, (void*)cl.IdxBuffer.Data, (uint)(cl.IdxBuffer.Size * sizeof(ushort)));
            vdst += cl.VtxBuffer.Size;
            idst += cl.IdxBuffer.Size;
        }
        ctx.Unmap(vb, 0);
        ctx.Unmap(ib, 0);

        float L = dd.DisplayPos.X, R = dd.DisplayPos.X + dd.DisplaySize.X, T = dd.DisplayPos.Y, B = dd.DisplayPos.Y + dd.DisplaySize.Y;
        var mvp = new Matrix4x4(
            2f / (R - L), 0, 0, 0,
            0, 2f / (T - B), 0, 0,
            0, 0, 0.5f, 0,
            (R + L) / (L - R), (T + B) / (B - T), 0.5f, 1f);
        var cm = ctx.Map(cb, MapMode.WriteDiscard, Vortice.Direct3D11.MapFlags.None);
        Unsafe.Write((void*)cm.DataPointer, mvp);
        ctx.Unmap(cb, 0);

        ctx.OMSetRenderTargets(target, null);
        ctx.RSSetViewport(new Viewport(0, 0, dd.DisplaySize.X, dd.DisplaySize.Y));
        ctx.IASetInputLayout(layout);
        ctx.IASetVertexBuffer(0, vb, (uint)sizeof(ImDrawVert), 0);
        ctx.IASetIndexBuffer(ib, Format.R16_UInt, 0);
        ctx.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        ctx.VSSetShader(vs);
        ctx.VSSetConstantBuffer(0, cb);
        ctx.HSSetShader(null);
        ctx.DSSetShader(null);
        ctx.GSSetShader(null);
        ctx.PSSetShader(ps);
        ctx.PSSetSampler(0, sampler);
        ctx.OMSetBlendState(blend);
        ctx.OMSetDepthStencilState(depth, 0);
        ctx.RSSetState(raster);

        int vtxOff = 0, idxOff = 0;
        var clipOff = dd.DisplayPos;
        for (int n = 0; n < dd.CmdListsCount; n++)
        {
            var cl = dd.CmdLists[n];
            for (int c = 0; c < cl.CmdBuffer.Size; c++)
            {
                var cmd = cl.CmdBuffer[c];
                if (cmd.UserCallback != IntPtr.Zero) continue;
                var clip = cmd.ClipRect;
                int x0 = (int)(clip.X - clipOff.X), y0 = (int)(clip.Y - clipOff.Y), x1 = (int)(clip.Z - clipOff.X), y1 = (int)(clip.W - clipOff.Y);
                if (x1 <= x0 || y1 <= y0) continue;
                ctx.RSSetScissorRect(x0, y0, x1 - x0, y1 - y0);
                ctx.PSSetShaderResource(0, textures.TryGetValue(cmd.TextureId, out var srv) ? srv : fontSrv!);
                ctx.DrawIndexed(cmd.ElemCount, (uint)(cmd.IdxOffset + idxOff), (int)(cmd.VtxOffset + vtxOff));
            }
            vtxOff += cl.VtxBuffer.Size;
            idxOff += cl.IdxBuffer.Size;
        }
        ctx.PSUnsetShaderResource(0);
    }

    public void Dispose()
    {
        vb?.Dispose(); ib?.Dispose(); cb.Dispose(); layout.Dispose(); vs.Dispose(); ps.Dispose();
        sampler.Dispose(); blend.Dispose(); raster.Dispose(); depth.Dispose(); fontSrv?.Dispose();
        foreach (var m in glyphRangeAllocs) Marshal.FreeHGlobal(m);
        ImGui.DestroyContext(context);
    }
}
