using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace Anatomi3D.Graphics;

public enum GpuPreference
{
    /// <summary>Harici (yüksek performanslı) GPU; dizüstü bilgisayarlarda NVIDIA/AMD kartı seçer</summary>
    HighPerformance,
    /// <summary>Tümleşik (düşük güç) GPU</summary>
    LowPower,
}

/// <summary>
/// Direct3D 11 cihazı. Optimus/çift GPU'lu dizüstülerde bağdaştırıcı DXGI 1.6 ile açıkça seçilir;
/// OpenGL/WebGL'in aksine uygulama, sürücü profili gerekmeden harici GPU'da çalışır.
/// </summary>
public sealed class GraphicsDevice : IDisposable
{
    public ID3D11Device1 Device { get; }
    public ID3D11DeviceContext1 Context { get; }
    public IDXGIFactory2 Factory { get; }
    public IDXGIAdapter1 Adapter { get; }
    public string AdapterName { get; }
    public ulong DedicatedVideoMemory { get; }
    public FeatureLevel FeatureLevel { get; }
    public bool TearingSupported { get; }
    public bool IsIntegrated { get; }

    private GraphicsDevice(ID3D11Device1 device, ID3D11DeviceContext1 context, IDXGIFactory2 factory, IDXGIAdapter1 adapter, FeatureLevel level)
    {
        Device = device;
        Context = context;
        Factory = factory;
        Adapter = adapter;
        FeatureLevel = level;
        var d = adapter.Description1;
        AdapterName = d.Description.Trim();
        DedicatedVideoMemory = (ulong)d.DedicatedVideoMemory;
        // Intel tümleşik GPU'ları (vendor 0x8086) ya da 512 MB altı ayrılmış belleği olanlar
        IsIntegrated = d.VendorId == 0x8086 || DedicatedVideoMemory < 512UL * 1024 * 1024;
        using var f5 = factory.QueryInterfaceOrNull<IDXGIFactory5>();
        TearingSupported = f5 != null && f5.PresentAllowTearing;
    }

    public static GraphicsDevice Create(GpuPreference preference, bool debug = false)
    {
        var factory = DXGI.CreateDXGIFactory2<IDXGIFactory2>(debug);
        IDXGIAdapter1? adapter = null;
        using (var f6 = factory.QueryInterfaceOrNull<IDXGIFactory6>())
        {
            if (f6 != null)
            {
                var pref = preference == GpuPreference.HighPerformance ? Vortice.DXGI.GpuPreference.HighPerformance : Vortice.DXGI.GpuPreference.MinimumPower;
                for (uint i = 0; f6.EnumAdapterByGpuPreference(i, pref, out IDXGIAdapter1? a).Success; i++)
                {
                    if ((a!.Description1.Flags & AdapterFlags.Software) != 0) { a.Dispose(); continue; }
                    adapter = a;
                    break;
                }
            }
        }
        if (adapter == null)
        {
            var f1 = factory.QueryInterface<IDXGIFactory1>();
            f1.EnumAdapters1(0, out adapter).CheckError();
            f1.Dispose();
        }

        FeatureLevel[] levels = [FeatureLevel.Level_11_1, FeatureLevel.Level_11_0];
        var flags = DeviceCreationFlags.BgraSupport;
        if (debug && D3D11.SdkLayersAvailable()) flags |= DeviceCreationFlags.Debug;
        D3D11.D3D11CreateDevice(adapter, DriverType.Unknown, flags, levels, out ID3D11Device? dev, out FeatureLevel fl, out ID3D11DeviceContext? ctx).CheckError();
        var dev1 = dev!.QueryInterface<ID3D11Device1>();
        var ctx1 = ctx!.QueryInterface<ID3D11DeviceContext1>();
        dev.Dispose();
        ctx.Dispose();
        return new GraphicsDevice(dev1, ctx1, factory, adapter!, fl);
    }

    /// <summary>Biçim için desteklenen en yüksek MSAA örnek sayısı (≤ <paramref name="wanted"/>).</summary>
    public uint SupportedSampleCount(Format format, uint wanted)
    {
        for (uint s = wanted; s > 1; s /= 2)
            if (Device.CheckMultisampleQualityLevels(format, s) > 0) return s;
        return 1;
    }

    public void Dispose()
    {
        Context.ClearState();
        Context.Flush();
        Context.Dispose();
        Device.Dispose();
        Adapter.Dispose();
        Factory.Dispose();
    }
}

/// <summary>Bir pencere tanıtıcısına bağlı çevirme (flip) modeli takas zinciri.</summary>
public sealed class SwapChainTarget : IDisposable
{
    private readonly GraphicsDevice gd;
    private readonly IDXGISwapChain1 swapChain;
    private readonly bool allowTearing;
    public ID3D11Texture2D? BackBuffer { get; private set; }
    public ID3D11RenderTargetView? BackBufferView { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }
    public const Format BackBufferFormat = Format.R8G8B8A8_UNorm;

    public SwapChainTarget(GraphicsDevice gd, IntPtr hwnd, int width, int height)
    {
        this.gd = gd;
        allowTearing = gd.TearingSupported;
        var desc = new SwapChainDescription1
        {
            Width = (uint)Math.Max(1, width),
            Height = (uint)Math.Max(1, height),
            Format = BackBufferFormat,
            BufferCount = 2,
            BufferUsage = Usage.RenderTargetOutput,
            SampleDescription = new SampleDescription(1, 0),
            Scaling = Scaling.Stretch,
            SwapEffect = SwapEffect.FlipDiscard,
            AlphaMode = AlphaMode.Ignore,
            Flags = allowTearing ? SwapChainFlags.AllowTearing : SwapChainFlags.None,
        };
        swapChain = gd.Factory.CreateSwapChainForHwnd(gd.Device, hwnd, desc, null, null);
        gd.Factory.MakeWindowAssociation(hwnd, WindowAssociationFlags.IgnoreAltEnter);
        Width = (int)desc.Width;
        Height = (int)desc.Height;
        CreateViews();
    }

    private void CreateViews()
    {
        BackBuffer = swapChain.GetBuffer<ID3D11Texture2D>(0);
        BackBufferView = gd.Device.CreateRenderTargetView(BackBuffer);
    }

    public void Resize(int width, int height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        if (width == Width && height == Height) return;
        gd.Context.OMSetRenderTargets((ID3D11RenderTargetView)null!, null);
        BackBufferView?.Dispose();
        BackBuffer?.Dispose();
        gd.Context.Flush();
        swapChain.ResizeBuffers(2, (uint)width, (uint)height, BackBufferFormat, allowTearing ? SwapChainFlags.AllowTearing : SwapChainFlags.None).CheckError();
        Width = width;
        Height = height;
        CreateViews();
    }

    /// <returns>false: cihaz kayboldu</returns>
    public bool Present(bool vsync)
    {
        var r = vsync ? swapChain.Present(1, PresentFlags.None) : swapChain.Present(0, allowTearing ? PresentFlags.AllowTearing : PresentFlags.None);
        // DXGI_STATUS_OCCLUDED bir başarı kodudur; yalnız cihaz kaybı hatadır
        return r.Success || (r != Vortice.DXGI.ResultCode.DeviceRemoved && r != Vortice.DXGI.ResultCode.DeviceReset && r != Vortice.DXGI.ResultCode.DeviceHung);
    }

    public void Dispose()
    {
        BackBufferView?.Dispose();
        BackBuffer?.Dispose();
        swapChain.Dispose();
    }
}
