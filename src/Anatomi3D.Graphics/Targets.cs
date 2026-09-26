using Vortice.Direct3D11;
using Vortice.DXGI;

namespace Anatomi3D.Graphics;

/// <summary>Render hedefi / derinlik dokusu ve görünümleri.</summary>
public sealed class Target2D : IDisposable
{
    public ID3D11Texture2D Texture { get; }
    public ID3D11RenderTargetView? Rtv { get; }
    public ID3D11ShaderResourceView? Srv { get; }
    public ID3D11DepthStencilView? Dsv { get; }
    public ID3D11UnorderedAccessView? Uav { get; }
    public int Width { get; }
    public int Height { get; }
    public uint Samples { get; }
    public Format Format { get; }

    private Target2D(ID3D11Texture2D tex, ID3D11RenderTargetView? rtv, ID3D11ShaderResourceView? srv, ID3D11DepthStencilView? dsv,
        ID3D11UnorderedAccessView? uav, int w, int h, uint samples, Format f)
    {
        Texture = tex; Rtv = rtv; Srv = srv; Dsv = dsv; Uav = uav; Width = w; Height = h; Samples = samples; Format = f;
    }

    public static Target2D Color(ID3D11Device dev, int w, int h, Format format, uint samples = 1, bool uav = false, int mips = 1)
    {
        var bind = BindFlags.RenderTarget | BindFlags.ShaderResource | (uav ? BindFlags.UnorderedAccess : 0);
        var desc = new Texture2DDescription
        {
            Width = (uint)Math.Max(1, w), Height = (uint)Math.Max(1, h), MipLevels = (uint)mips, ArraySize = 1, Format = format,
            SampleDescription = new SampleDescription(samples, 0), Usage = ResourceUsage.Default, BindFlags = bind,
        };
        var tex = dev.CreateTexture2D(ref desc);
        var rtv = dev.CreateRenderTargetView(tex);
        var srv = dev.CreateShaderResourceView(tex);
        var u = uav ? dev.CreateUnorderedAccessView(tex) : null;
        return new Target2D(tex, rtv, srv, null, u, w, h, samples, format);
    }

    /// <summary>Okunabilir derinlik: R32 tipsiz doku, D32 görünüm + R32F kaynak görünümü.</summary>
    public static Target2D Depth(ID3D11Device dev, int w, int h, uint samples = 1)
    {
        var desc = new Texture2DDescription
        {
            Width = (uint)Math.Max(1, w), Height = (uint)Math.Max(1, h), MipLevels = 1, ArraySize = 1, Format = Format.R32_Typeless,
            SampleDescription = new SampleDescription(samples, 0), Usage = ResourceUsage.Default,
            BindFlags = BindFlags.DepthStencil | BindFlags.ShaderResource,
        };
        var tex = dev.CreateTexture2D(ref desc);
        var dsv = dev.CreateDepthStencilView(tex, new DepthStencilViewDescription(
            samples > 1 ? DepthStencilViewDimension.Texture2DMultisampled : DepthStencilViewDimension.Texture2D, Format.D32_Float));
        var srv = dev.CreateShaderResourceView(tex, new ShaderResourceViewDescription(tex,
            samples > 1 ? Vortice.Direct3D.ShaderResourceViewDimension.Texture2DMultisampled : Vortice.Direct3D.ShaderResourceViewDimension.Texture2D,
            Format.R32_Float));
        return new Target2D(tex, null, srv, dsv, null, w, h, samples, Format.R32_Float);
    }

    public void Dispose()
    {
        Uav?.Dispose();
        Dsv?.Dispose();
        Srv?.Dispose();
        Rtv?.Dispose();
        Texture.Dispose();
    }
}

/// <summary>Önceden hesaplanmış ortam küp dokusu (mip zinciriyle).</summary>
public sealed class CubeTarget : IDisposable
{
    public ID3D11Texture2D Texture { get; }
    public ID3D11ShaderResourceView Srv { get; }
    public int Size { get; }
    public int Mips { get; }
    private readonly ID3D11RenderTargetView[,] rtvs;

    public CubeTarget(ID3D11Device dev, int size, int mips, Format format, bool generateMips)
    {
        Size = size;
        Mips = mips;
        var desc = new Texture2DDescription
        {
            Width = (uint)size, Height = (uint)size, MipLevels = (uint)mips, ArraySize = 6, Format = format,
            SampleDescription = new SampleDescription(1, 0), Usage = ResourceUsage.Default,
            BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
            MiscFlags = ResourceOptionFlags.TextureCube | (generateMips ? ResourceOptionFlags.GenerateMips : 0),
        };
        Texture = dev.CreateTexture2D(ref desc);
        Srv = dev.CreateShaderResourceView(Texture);
        rtvs = new ID3D11RenderTargetView[6, mips];
        for (int f = 0; f < 6; f++)
            for (int m = 0; m < mips; m++)
                rtvs[f, m] = dev.CreateRenderTargetView(Texture, new RenderTargetViewDescription(Texture,
                    RenderTargetViewDimension.Texture2DArray, format, (uint)m, (uint)f, 1));
    }

    public ID3D11RenderTargetView Rtv(int face, int mip) => rtvs[face, mip];

    public void Dispose()
    {
        foreach (var r in rtvs) r.Dispose();
        Srv.Dispose();
        Texture.Dispose();
    }
}
