using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Anatomi3D.Core.Pack;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace Anatomi3D.Graphics;

/// <summary>Kalite ayarları (Ayarlar penceresinden ve otomatik GPU algılamasından).</summary>
public sealed class RenderSettings
{
    public uint Msaa { get; set; } = 4;
    public bool Shadows { get; set; } = true;
    public int ShadowMapSize { get; set; } = 2048;
    public bool Ssao { get; set; } = true;
    public bool Bloom { get; set; } = true;
    public bool Tessellation { get; set; } = true;
    /// <summary>Tesselasyon hedef kenar uzunluğu (piksel)</summary>
    public float TessTargetPx { get; set; } = 6f;
    public float TessMaxFactor { get; set; } = 12f;
    /// <summary>LOD seçimi için izin verilen ekran uzayı hatası (piksel)</summary>
    public float LodPixelError { get; set; } = 0.6f;
    public bool ProceduralDetail { get; set; } = true;

    public RenderSettings Clone() => (RenderSettings)MemberwiseClone();
}

public struct CameraState
{
    public Matrix4x4 View;
    public Matrix4x4 Proj;
    public Vector3 Position;
    public float FovY;
    public float Near;
    public float Far;
}

/// <summary>Bir karenin çizim girdisi — uygulama durumu buraya yazılır.</summary>
public sealed class FrameInput
{
    public CameraState Camera;
    public float Time;
    public PartStateGpu[] States = [];
    public PartRender[] Render = [];
    /// <summary>Kesit düzlemi (n·p + d ≥ 0 tarafı tutulur); null = kapalı</summary>
    public Vector4? Clip;
    public readonly List<Vector4> Holes = [];
    public readonly List<int> Selected = [];
    public int Hovered = -1;
    public bool Wireframe;
    public float Exposure = 1f;
    public float GhostOpacity = 1f;
    public Vector3 SelectColor = new(0.18f, 0.55f, 1.0f);
    public Vector3 HoverColor = new(0.45f, 0.75f, 1.0f);
    public Vector3 BackgroundTop = new(0.075f, 0.09f, 0.115f);
    public Vector3 BackgroundBottom = new(0.018f, 0.021f, 0.028f);
}

/// <summary>Seçim okuması: ekran noktasındaki yapı ve dünya konumu.</summary>
public readonly record struct PickResult(int Token, int Part, Vector3 World, float Depth, int X, int Y);

/// <summary>
/// Anatomi sahnesinin Direct3D 11 çizicisi.
///
/// Geçiş sırası: gölge haritası → derinlik/normal/kimlik ön geçişi (MSAA) → doğrusal derinlik + SSAO →
/// opak aydınlatma (ön geçiş derinliğine eşit, sıfır aşırı çizim) → X-ray/cam için ağırlıklı karışımlı saydamlık →
/// seçim maskeleri → ton eşlemeli MSAA çözme + arka plan → bloom → ACES ton eşleme, dış çizgi, vinyet.
///
/// Tüm yapılar tek köşe/indeks tamponundadır; her karede görünürlük, bakış konisi ayıklaması, ekran uzayı hatasına göre
/// LOD ve tesselasyon kararı verilir, aynı LOD'daki ardışık yapılar tek çizim komutunda birleştirilir.
/// </summary>
public sealed unsafe class AnatomyRenderer : IDisposable
{
    private readonly GraphicsDevice gd;
    private ID3D11Device1 Dev => gd.Device;
    private ID3D11DeviceContext1 Ctx => gd.Context;
    private readonly ShaderCompiler sc;

    public RenderSettings Settings { get; private set; }
    public int PartCount { get; }
    public PartRecord[] Parts { get; }
    public Vector3 QuantMin { get; }
    public Vector3 QuantScale { get; }

    // geometri
    private readonly ID3D11Buffer vb, ib;
    private readonly uint[] lodBase = new uint[PackFormat.MaxLods];
    private readonly float[] avgEdge;
    private readonly bool[] isSkin;
    private readonly bool[] isClosed;
    private readonly ID3D11InputLayout layout;
    private readonly ID3D11Buffer stateBuf, materialBuf;
    private readonly ID3D11ShaderResourceView stateSrv, materialSrv;
    private readonly ID3D11Buffer frameCb, postCb, envCb;

    // shader'lar
    private ID3D11VertexShader vsMain = null!, vsShadow = null!, vsControl = null!, vsFull = null!;
    private ID3D11HullShader hs = null!;
    private ID3D11DomainShader ds = null!;
    private readonly ID3D11PixelShader[] psPrepass = new ID3D11PixelShader[3];
    private readonly ID3D11PixelShader[] psMain = new ID3D11PixelShader[3];
    private ID3D11PixelShader psShadowCut = null!, psGhost = null!, psMask = null!;
    private ID3D11PixelShader psResolveDepthMs = null!, psResolveDepthSs = null!, psResolveMs = null!, psResolveSs = null!;
    private ID3D11PixelShader psSsao = null!, psBlurAo = null!, psDown = null!, psUp = null!, psFinalMs = null!, psFinalSs = null!;

    // durumlar
    private ID3D11RasterizerState rsSolid = null!, rsWire = null!, rsShadow = null!, rsFull = null!;
    private ID3D11DepthStencilState dsWrite = null!, dsEqualRead = null!, dsRead = null!, dsNone = null!, dsAlways = null!;
    private ID3D11BlendState bsOpaque = null!, bsOit = null!, bsAdd = null!, bsMaskOnly = null!;
    private ID3D11SamplerState smpShadow = null!, smpLinear = null!, smpPoint = null!;

    // hedefler
    private int width, height;
    private Target2D? depthMs, normalMs, idMs, colorMs, accumMs, revealMs, maskVisMs, maskAll;
    private Target2D? linZ, viewNormal, id1, ao, aoTmp, hdr, depth1;
    private readonly List<Target2D> bloomChain = [];
    private Target2D? shadowMap;

    // ortam
    private CubeTarget? envCube, specCube;
    private Target2D? brdfLut;
    private Float4x9 envSh;

    // çizim listeleri
    private readonly Bucket[,] opaque = new Bucket[3, 2];
    private readonly Bucket ghost = new(), glass = new(), mask = new();
    /// <summary>Gölge haritası için bir kaba LOD ile ayrı listeler: [0] kesmesiz, [1] kesmeli</summary>
    private readonly Bucket[] shadowLists = [new(), new()];
    private readonly PartStateGpu[] statesScratch;
    private int lastSkinLod;

    // seçim okuma halkası
    private readonly Queue<(ID3D11Texture2D idTex, ID3D11Texture2D zTex, int token, int x, int y, int frame)> pickQueue = new();
    private readonly Stack<(ID3D11Texture2D, ID3D11Texture2D)> pickPool = new();
    private readonly List<PickResult> pickResults = [];
    private int frameIndex;
    private Matrix4x4 lastInvViewProj;
    private CameraState lastCamera;

    public RenderStats Stats { get; } = new();
    public GpuProfiler Profiler { get; }

    public AnatomyRenderer(GraphicsDevice gd, PackData pack, RenderSettings settings)
    {
        this.gd = gd;
        Settings = settings.Clone();
        sc = new ShaderCompiler(gd.Device);
        Profiler = new GpuProfiler(gd.Device, gd.Context);
        Parts = pack.Parts;
        PartCount = pack.Parts.Length;
        QuantMin = pack.Header.QuantMin;
        QuantScale = (pack.Header.QuantMax - pack.Header.QuantMin) / 65535f;

        // köşe ve indeks tamponları (tüm LOD'lar tek tamponda art arda)
        vb = Dev.CreateBuffer(pack.Vertices.AsSpan(), BindFlags.VertexBuffer, ResourceUsage.Immutable);
        long total = 0;
        for (int l = 0; l < PackFormat.MaxLods; l++) { lodBase[l] = (uint)total; total += pack.Indices[l].Length; }
        var all = new uint[total];
        for (int l = 0; l < PackFormat.MaxLods; l++) pack.Indices[l].CopyTo(all, lodBase[l]);
        ib = Dev.CreateBuffer(all.AsSpan(), BindFlags.IndexBuffer, ResourceUsage.Immutable);

        isSkin = new bool[PartCount];
        isClosed = new bool[PartCount];
        for (int i = 0; i < PartCount; i++)
        {
            isSkin[i] = (Parts[i].Flags & (uint)PartFlags.Subdivided) != 0;
            isClosed[i] = (Parts[i].Flags & (uint)PartFlags.Closed) != 0;
        }
        avgEdge = ComputeAverageEdges(pack);

        int stateSize = Marshal.SizeOf<PartStateGpu>();
        int matSize = Marshal.SizeOf<PartMaterialGpu>();
        stateBuf = Dev.CreateBuffer(new BufferDescription((uint)(stateSize * PartCount), BindFlags.ShaderResource, ResourceUsage.Dynamic,
            CpuAccessFlags.Write, ResourceOptionFlags.BufferStructured, (uint)stateSize));
        materialBuf = Dev.CreateBuffer(new BufferDescription((uint)(matSize * PartCount), BindFlags.ShaderResource, ResourceUsage.Default,
            CpuAccessFlags.None, ResourceOptionFlags.BufferStructured, (uint)matSize));
        stateSrv = Dev.CreateShaderResourceView(stateBuf, null);
        materialSrv = Dev.CreateShaderResourceView(materialBuf, null);
        statesScratch = new PartStateGpu[PartCount];

        frameCb = Dev.CreateBuffer(new BufferDescription((uint)Unsafe.SizeOf<FrameConstants>(), BindFlags.ConstantBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write));
        postCb = Dev.CreateBuffer(new BufferDescription((uint)Unsafe.SizeOf<PostConstants>(), BindFlags.ConstantBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write));
        envCb = Dev.CreateBuffer(new BufferDescription((uint)Unsafe.SizeOf<EnvConstants>(), BindFlags.ConstantBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write));

        for (int v = 0; v < 3; v++) for (int t = 0; t < 2; t++) opaque[v, t] = new Bucket();

        CreateShaders(out var vsBytecode);
        layout = Dev.CreateInputLayout(
        [
            new InputElementDescription("POSITION", 0, Format.R16G16B16A16_UInt, 0, 0),
            new InputElementDescription("NORMAL", 0, Format.R16G16_SNorm, 8, 0),
            new InputElementDescription("TANGENT", 0, Format.R8G8_SNorm, 12, 0),
            new InputElementDescription("TEXCOORD", 0, Format.R8G8_UNorm, 14, 0),
        ], vsBytecode);
        CreateStates();
        BuildEnvironment();
    }

    /// <summary>Her yapının LOD0 ortalama kenar uzunluğu (tesselasyon kararı için).</summary>
    private float[] ComputeAverageEdges(PackData pack)
    {
        var r = new float[PartCount];
        var verts = pack.Vertices;
        var idx = pack.Indices[0];
        var qs = QuantScale;
        Parallel.For(0, PartCount, i =>
        {
            var l = Parts[i].Lod0;
            if (l.IndexCount == 0) return;
            double sum = 0;
            int n = 0;
            int step = Math.Max(1, (int)(l.IndexCount / 3 / 400)) * 3;
            for (uint t = 0; t < l.IndexCount; t += (uint)step)
            {
                var a = verts[idx[l.IndexOffset + t]];
                var b = verts[idx[l.IndexOffset + t + 1]];
                var pa = new Vector3(a.X, a.Y, a.Z) * qs;
                var pb = new Vector3(b.X, b.Y, b.Z) * qs;
                sum += Vector3.Distance(pa, pb);
                n++;
            }
            r[i] = n > 0 ? (float)(sum / n) : 0;
        });
        return r;
    }

    private void CreateShaders(out byte[] vsBytecode)
    {
        const string G = "Geometry.hlsl", P = "Post.hlsl";
        vsMain = sc.VS(G, "VSMain", out vsBytecode);
        vsShadow = sc.VS(G, "VSShadow");
        vsControl = sc.VS(G, "VSControl");
        hs = sc.HS(G, "HSMain");
        ds = sc.DS(G, "DSMain");
        (string, string)[][] variants = [[("CUTOUT", "0"), ("CAPS", "0")], [("CUTOUT", "1"), ("CAPS", "0")], [("CUTOUT", "1"), ("CAPS", "1")]];
        for (int v = 0; v < 3; v++)
        {
            psPrepass[v] = sc.PS(G, "PSPrepass", variants[v]);
            psMain[v] = sc.PS(G, "PSMain", variants[v]);
        }
        psShadowCut = sc.PS(G, "PSShadow", ("CUTOUT", "1"));
        psGhost = sc.PS(G, "PSGhost", ("CUTOUT", "1"));
        psMask = sc.PS(G, "PSMask", ("CUTOUT", "1"));
        vsFull = sc.VS(P, "VSFullscreen");
        psResolveDepthMs = sc.PS(P, "PSResolveDepth", ("MSAA", "1"));
        psResolveDepthSs = sc.PS(P, "PSResolveDepth", ("MSAA", "0"));
        psResolveMs = sc.PS(P, "PSResolve", ("MSAA", "1"));
        psResolveSs = sc.PS(P, "PSResolve", ("MSAA", "0"));
        psSsao = sc.PS(P, "PSSsao");
        psBlurAo = sc.PS(P, "PSBlurAo");
        psDown = sc.PS(P, "PSDownsample");
        psUp = sc.PS(P, "PSUpsample");
        psFinalMs = sc.PS(P, "PSFinal", ("MSAA", "1"));
        psFinalSs = sc.PS(P, "PSFinal", ("MSAA", "0"));
    }

    private void CreateStates()
    {
        // glTF üçgenleri saat yönü tersine (sağ el sistemi): ön yüz = CCW
        var solid = RasterizerDescription.CullNone;
        solid.DepthClipEnable = true;
        solid.FrontCounterClockwise = true;
        rsSolid = Dev.CreateRasterizerState(solid);
        var wire = solid;
        wire.FillMode = FillMode.Wireframe;
        wire.AntialiasedLineEnable = true;
        rsWire = Dev.CreateRasterizerState(wire);
        var sh = solid;
        sh.DepthBias = 800;
        sh.SlopeScaledDepthBias = 2.0f;
        sh.DepthBiasClamp = 0.01f;
        rsShadow = Dev.CreateRasterizerState(sh);
        rsFull = Dev.CreateRasterizerState(RasterizerDescription.CullNone);

        dsWrite = Dev.CreateDepthStencilState(new DepthStencilDescription(true, DepthWriteMask.All, ComparisonFunction.LessEqual));
        dsEqualRead = Dev.CreateDepthStencilState(new DepthStencilDescription(true, DepthWriteMask.Zero, ComparisonFunction.LessEqual));
        dsRead = Dev.CreateDepthStencilState(new DepthStencilDescription(true, DepthWriteMask.Zero, ComparisonFunction.LessEqual));
        dsNone = Dev.CreateDepthStencilState(DepthStencilDescription.None);
        dsAlways = Dev.CreateDepthStencilState(new DepthStencilDescription(true, DepthWriteMask.All, ComparisonFunction.Always));

        bsOpaque = Dev.CreateBlendState(BlendDescription.Opaque);
        var oit = new BlendDescription { IndependentBlendEnable = true };
        oit.RenderTarget[0] = new RenderTargetBlendDescription
        {
            BlendEnable = true, SourceBlend = Blend.One, DestinationBlend = Blend.One, BlendOperation = BlendOperation.Add,
            SourceBlendAlpha = Blend.One, DestinationBlendAlpha = Blend.One, BlendOperationAlpha = BlendOperation.Add,
            RenderTargetWriteMask = ColorWriteEnable.All,
        };
        oit.RenderTarget[1] = new RenderTargetBlendDescription
        {
            BlendEnable = true, SourceBlend = Blend.Zero, DestinationBlend = Blend.InverseSourceColor, BlendOperation = BlendOperation.Add,
            SourceBlendAlpha = Blend.Zero, DestinationBlendAlpha = Blend.InverseSourceAlpha, BlendOperationAlpha = BlendOperation.Add,
            RenderTargetWriteMask = ColorWriteEnable.All,
        };
        bsOit = Dev.CreateBlendState(oit);
        bsAdd = Dev.CreateBlendState(BlendDescription.Additive);
        bsMaskOnly = Dev.CreateBlendState(BlendDescription.Opaque);

        smpShadow = Dev.CreateSamplerState(new SamplerDescription(Filter.ComparisonMinMagLinearMipPoint, TextureAddressMode.Border,
            TextureAddressMode.Border, TextureAddressMode.Border, 0, 1, ComparisonFunction.LessEqual, new Color4(1, 1, 1, 1), 0, float.MaxValue));
        smpLinear = Dev.CreateSamplerState(SamplerDescription.LinearClamp);
        smpPoint = Dev.CreateSamplerState(SamplerDescription.PointClamp);
    }

    // ------------------------------------------------------------------------------------------------ ortam ışığı
    private void BuildEnvironment()
    {
        const int size = 128;
        envCube = new CubeTarget(Dev, size, 8, Format.R16G16B16A16_Float, generateMips: true);
        specCube = new CubeTarget(Dev, size, 7, Format.R16G16B16A16_Float, generateMips: false);
        var vs = sc.VS("Env.hlsl", "VSFullscreen");
        var psStudio = sc.PS("Env.hlsl", "PSStudio");
        var psPre = sc.PS("Env.hlsl", "PSPrefilter");
        var psLut = sc.PS("Env.hlsl", "PSBrdfLut");
        var csSh = sc.CS("Env.hlsl", "CSProjectSH");

        Ctx.ClearState();
        Ctx.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        Ctx.RSSetState(rsFull);
        Ctx.OMSetBlendState(bsOpaque);
        Ctx.OMSetDepthStencilState(dsNone, 0);
        Ctx.VSSetShader(vs);
        Ctx.PSSetConstantBuffer(0, envCb);
        Ctx.PSSetSampler(0, smpLinear);

        // stüdyo küpü
        Ctx.PSSetShader(psStudio);
        for (int f = 0; f < 6; f++)
        {
            WriteCb(envCb, new EnvConstants { Face = (uint)f });
            Ctx.OMSetRenderTargets(envCube.Rtv(f, 0), null);
            Ctx.RSSetViewport(new Viewport(0, 0, size, size));
            Ctx.Draw(3, 0);
        }
        Ctx.OMSetRenderTargets((ID3D11RenderTargetView)null!, null);
        Ctx.GenerateMips(envCube.Srv);

        // GGX ön süzme
        Ctx.PSSetShader(psPre);
        Ctx.PSSetShaderResource(0, envCube.Srv);
        for (int m = 0; m < specCube.Mips; m++)
        {
            int s = Math.Max(1, size >> m);
            for (int f = 0; f < 6; f++)
            {
                WriteCb(envCb, new EnvConstants { Face = (uint)f, Roughness = m / (float)(specCube.Mips - 1), SrcSize = size });
                Ctx.OMSetRenderTargets(specCube.Rtv(f, m), null);
                Ctx.RSSetViewport(new Viewport(0, 0, s, s));
                Ctx.Draw(3, 0);
            }
        }
        Ctx.OMSetRenderTargets((ID3D11RenderTargetView)null!, null);
        Ctx.PSUnsetShaderResource(0);

        // BRDF tablosu
        brdfLut = Target2D.Color(Dev, 128, 128, Format.R16G16_Float);
        Ctx.PSSetShader(psLut);
        Ctx.OMSetRenderTargets(brdfLut.Rtv!, null);
        Ctx.RSSetViewport(new Viewport(0, 0, 128, 128));
        Ctx.Draw(3, 0);
        Ctx.OMSetRenderTargets((ID3D11RenderTargetView)null!, null);

        // SH9 ışınım: GPU'da izdüşür, bir kez geri oku
        using var shBuf = Dev.CreateBuffer(new BufferDescription(16 * 9, BindFlags.UnorderedAccess, ResourceUsage.Default,
            CpuAccessFlags.None, ResourceOptionFlags.BufferStructured, 16));
        using var shUav = Dev.CreateUnorderedAccessView(shBuf, null);
        using var shStage = Dev.CreateBuffer(new BufferDescription(16 * 9, BindFlags.None, ResourceUsage.Staging, CpuAccessFlags.Read));
        Ctx.CSSetShader(csSh);
        Ctx.CSSetShaderResource(0, envCube.Srv);
        Ctx.CSSetSampler(0, smpLinear);
        Ctx.CSSetUnorderedAccessView(0, shUav, 0);
        Ctx.Dispatch(1, 1, 1);
        Ctx.CSUnsetUnorderedAccessView(0, 0);
        Ctx.CSUnsetShaderResource(0);
        Ctx.CopyResource(shStage, shBuf);
        var map = Ctx.Map(shStage, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        var sh = new ReadOnlySpan<Vector4>((void*)map.DataPointer, 9);
        for (int i = 0; i < 9; i++) envSh[i] = sh[i];
        Ctx.Unmap(shStage, 0);

        vs.Dispose(); psStudio.Dispose(); psPre.Dispose(); psLut.Dispose(); csSh.Dispose();
    }

    // ------------------------------------------------------------------------------------------------ yardımcılar
    private void WriteCb<T>(ID3D11Buffer buf, in T value) where T : unmanaged
    {
        var m = Ctx.Map(buf, MapMode.WriteDiscard, Vortice.Direct3D11.MapFlags.None);
        Unsafe.Write((void*)m.DataPointer, value);
        Ctx.Unmap(buf, 0);
    }

    public void UploadMaterials(PartMaterialGpu[] materials)
    {
        if (materials.Length != PartCount) throw new ArgumentException("Malzeme sayısı yapı sayısıyla aynı olmalı");
        Ctx.UpdateSubresource(materials.AsSpan(), materialBuf, 0, 0, 0, null);
    }

    public void ApplySettings(RenderSettings s)
    {
        bool rebuild = s.Msaa != Settings.Msaa || s.ShadowMapSize != Settings.ShadowMapSize;
        Settings = s.Clone();
        if (rebuild) DestroyTargets();
    }

    public void Resize(int w, int h)
    {
        if (w == width && h == height && depthMs != null) return;
        DestroyTargets();
        width = Math.Max(1, w);
        height = Math.Max(1, h);
    }

    private void DestroyTargets()
    {
        foreach (var t in new[] { depthMs, normalMs, idMs, colorMs, accumMs, revealMs, maskVisMs, maskAll, linZ, viewNormal, id1, ao, aoTmp, hdr, shadowMap, depth1 })
            t?.Dispose();
        depthMs = normalMs = idMs = colorMs = accumMs = revealMs = maskVisMs = maskAll = linZ = viewNormal = id1 = ao = aoTmp = hdr = shadowMap = depth1 = null;
        foreach (var t in bloomChain) t.Dispose();
        bloomChain.Clear();
    }

    private void EnsureTargets()
    {
        if (depthMs != null) return;
        uint s = gd.SupportedSampleCount(Format.R16G16B16A16_Float, Math.Max(1u, Settings.Msaa));
        s = Math.Min(s, gd.SupportedSampleCount(Format.R32_UInt, s));
        s = Math.Min(s, gd.SupportedSampleCount(Format.R32_Typeless, s));
        int w = width, h = height;
        depthMs = Target2D.Depth(Dev, w, h, s);
        normalMs = Target2D.Color(Dev, w, h, Format.R16G16_SNorm, s);
        idMs = Target2D.Color(Dev, w, h, Format.R32_UInt, s);
        colorMs = Target2D.Color(Dev, w, h, Format.R16G16B16A16_Float, s);
        // saydamlık (X-ray) tek örnekli: yoğun üst üste çizimde bant genişliğini ~4 kat azaltır
        accumMs = Target2D.Color(Dev, w, h, Format.R16G16B16A16_Float);
        revealMs = Target2D.Color(Dev, w, h, Format.R16_Float);
        depth1 = Target2D.Depth(Dev, w, h);
        maskVisMs = Target2D.Color(Dev, w, h, Format.R8_UNorm, s);
        maskAll = Target2D.Color(Dev, w, h, Format.R8_UNorm);
        linZ = Target2D.Color(Dev, w, h, Format.R32_Float);
        viewNormal = Target2D.Color(Dev, w, h, Format.R16G16_SNorm);
        id1 = Target2D.Color(Dev, w, h, Format.R32_UInt);
        ao = Target2D.Color(Dev, (w + 1) / 2, (h + 1) / 2, Format.R8_UNorm);
        aoTmp = Target2D.Color(Dev, (w + 1) / 2, (h + 1) / 2, Format.R8_UNorm);
        hdr = Target2D.Color(Dev, w, h, Format.R16G16B16A16_Float);
        int bw = w, bh = h;
        for (int i = 0; i < 6 && bw > 8 && bh > 8; i++)
        {
            bw = (bw + 1) / 2;
            bh = (bh + 1) / 2;
            bloomChain.Add(Target2D.Color(Dev, bw, bh, Format.R11G11B10_Float));
        }
        shadowMap = Target2D.Depth(Dev, Settings.ShadowMapSize, Settings.ShadowMapSize);
        Stats.Samples = s;
    }

    public uint ActiveSamples => depthMs?.Samples ?? Settings.Msaa;

    // ------------------------------------------------------------------------------------------------ çizim listeleri
    private sealed class Bucket
    {
        public readonly List<(uint start, uint count)> Runs = new(256);
        public long Triangles;

        public void Clear() { Runs.Clear(); Triangles = 0; }

        public void Add(uint start, uint count)
        {
            Triangles += count / 3;
            if (Runs.Count > 0)
            {
                var (s, c) = Runs[^1];
                if (s + c == start) { Runs[^1] = (s, c + count); return; }
            }
            Runs.Add((start, count));
        }
    }

    private static Vector4[] FrustumPlanes(Matrix4x4 m)
    {
        // satır vektörü kuralı (v * M): düzlemler sütunlardan
        var c1 = new Vector4(m.M11, m.M21, m.M31, m.M41);
        var c2 = new Vector4(m.M12, m.M22, m.M32, m.M42);
        var c3 = new Vector4(m.M13, m.M23, m.M33, m.M43);
        var c4 = new Vector4(m.M14, m.M24, m.M34, m.M44);
        Vector4[] p = [c4 + c1, c4 - c1, c4 + c2, c4 - c2, c3, c4 - c3];
        for (int i = 0; i < p.Length; i++)
        {
            float l = new Vector3(p[i].X, p[i].Y, p[i].Z).Length();
            p[i] /= l;
        }
        return p;
    }

    private void BuildLists(FrameInput f, Matrix4x4 viewProj, out Vector3 bCenter, out float bRadius)
    {
        for (int v = 0; v < 3; v++) for (int t = 0; t < 2; t++) opaque[v, t].Clear();
        ghost.Clear(); glass.Clear(); mask.Clear();
        shadowLists[0].Clear(); shadowLists[1].Clear();
        var planes = FrustumPlanes(viewProj);
        var cam = f.Camera.Position;
        float pxPerRad = height / (2f * MathF.Tan(f.Camera.FovY * 0.5f));
        float lodPx = Settings.LodPixelError;
        bool clipOn = f.Clip.HasValue;
        var clip = f.Clip ?? default;

        // deri: tüm deri yamaları aynı alt bölümleme düzeyini kullanmalı (aksi halde komşu yamalar arasında çatlak)
        float skinMinDist = float.MaxValue;
        for (int i = 0; i < PartCount; i++)
        {
            if (!isSkin[i] || f.Render[i] == PartRender.Hidden) continue;
            ref readonly var p = ref Parts[i];
            if (!InFrustum(planes, p.Center, p.Radius)) continue;
            skinMinDist = MathF.Min(skinMinDist, MathF.Max(f.Camera.Near, Vector3.Distance(cam, p.Center) - p.Radius));
        }
        int skinLod = 0;
        bool skinTess = false;
        if (skinMinDist < float.MaxValue)
        {
            float ppm = pxPerRad / skinMinDist;
            var rec = Parts[FirstSkin()];
            for (int l = PackFormat.MaxLods - 1; l >= 0; l--)
                if (rec.Lod(l).Error * ppm <= lodPx * 3f) { skinLod = l; break; }
            // deri yamaları kenar paylaşır: tesselasyon ya hepsine ya hiçbirine (aksi halde sınırlarda çatlak)
            skinTess = Settings.Tessellation && skinLod == 0 && !f.Wireframe && avgEdge[FirstSkin()] * ppm > Settings.TessTargetPx * 1.25f;
        }
        lastSkinLod = skinLod;

        var bmin = new Vector3(float.MaxValue);
        var bmax = new Vector3(float.MinValue);
        long tris = 0;
        int visible = 0;
        for (int i = 0; i < PartCount; i++)
        {
            var r = f.Render[i];
            if (r == PartRender.Hidden) continue;
            ref readonly var p = ref Parts[i];
            ref readonly var st = ref f.States[i];
            float rad = p.Radius * MathF.Max(1f, st.Scale);
            if (!InFrustum(planes, p.Center, rad)) continue;
            bool caps = false;
            if (clipOn)
            {
                float d = Vector3.Dot(new Vector3(clip.X, clip.Y, clip.Z), p.Center) + clip.W;
                if (d < -rad) continue;
                // kesit kapağı yalnız su geçirmez yüzeylerde (deri: birleşik yüzey olarak kapalı)
                caps = d < rad && r == PartRender.Opaque && (isClosed[i] || isSkin[i]);
            }
            float dist = MathF.Max(f.Camera.Near, Vector3.Distance(cam, p.Center) - rad);
            float ppm = pxPerRad / dist;
            int lod;
            if (isSkin[i]) lod = skinLod;
            else
            {
                lod = 0;
                for (int l = PackFormat.MaxLods - 1; l > 0; l--)
                    if (p.Lod(l).Error * ppm <= lodPx) { lod = l; break; }
            }
            var range = p.Lod(lod);
            if (range.IndexCount == 0) continue;
            uint start = lodBase[lod] + range.IndexOffset;
            visible++;

            if (f.Selected.Count > 0 && f.Selected.Contains(i)) mask.Add(start, range.IndexCount);

            switch (r)
            {
                case PartRender.Opaque:
                {
                    bool cut = st.Dissolve > 0 || (st.Flags & (uint)PartStateFlags.Windowed) != 0;
                    int variant = caps ? 2 : cut ? 1 : 0;
                    bool tess = isSkin[i] ? skinTess : Settings.Tessellation && lod == 0 && !f.Wireframe && avgEdge[i] * ppm > Settings.TessTargetPx * 1.25f;
                    opaque[variant, tess ? 1 : 0].Add(start, range.IndexCount);
                    int slod = Math.Min(PackFormat.MaxLods - 1, lod + 1);
                    var sr = p.Lod(slod);
                    shadowLists[cut || caps ? 1 : 0].Add(lodBase[slod] + sr.IndexOffset, sr.IndexCount);
                    bmin = Vector3.Min(bmin, p.Center - new Vector3(rad));
                    bmax = Vector3.Max(bmax, p.Center + new Vector3(rad));
                    break;
                }
                case PartRender.Ghost:
                {
                    // saydam yapılarda ayrıntı daha az algılanır: bir kaba LOD
                    int gl = Math.Min(PackFormat.MaxLods - 1, lod + 1);
                    var gr = p.Lod(gl);
                    ghost.Add(lodBase[gl] + gr.IndexOffset, gr.IndexCount);
                    break;
                }
                case PartRender.Glass:
                    glass.Add(start, range.IndexCount);
                    break;
            }
            tris += range.IndexCount / 3;
        }
        Stats.VisibleParts = visible;
        Stats.Triangles = tris;
        Stats.SkinLod = skinLod;
        if (bmin.X <= bmax.X)
        {
            bCenter = (bmin + bmax) * 0.5f;
            bRadius = (bmax - bmin).Length() * 0.5f;
        }
        else
        {
            bCenter = Vector3.Zero;
            bRadius = 0;
        }
    }

    private int firstSkin = -2;
    private int FirstSkin()
    {
        if (firstSkin == -2)
        {
            firstSkin = 0;
            for (int i = 0; i < PartCount; i++) if (isSkin[i]) { firstSkin = i; break; }
        }
        return firstSkin;
    }

    private static bool InFrustum(Vector4[] planes, Vector3 c, float r)
    {
        foreach (var p in planes)
            if (p.X * c.X + p.Y * c.Y + p.Z * c.Z + p.W < -r) return false;
        return true;
    }

    private void DrawBucket(Bucket b)
    {
        foreach (var (start, count) in b.Runs)
        {
            Ctx.DrawIndexed(count, start, 0);
            Stats.DrawCalls++;
        }
    }

    // ------------------------------------------------------------------------------------------------ kare
    public void Render(FrameInput f, ID3D11RenderTargetView output)
    {
        EnsureTargets();
        Profiler.BeginFrame();
        Stats.DrawCalls = 0;
        frameIndex++;
        var cam = f.Camera;
        var viewProj = cam.View * cam.Proj;
        Matrix4x4.Invert(viewProj, out var invViewProj);
        lastInvViewProj = invViewProj;
        lastCamera = cam;

        BuildLists(f, viewProj, out var bc, out var br);

        // yapı durumları
        {
            var m = Ctx.Map(stateBuf, MapMode.WriteDiscard, Vortice.Direct3D11.MapFlags.None);
            fixed (PartStateGpu* src = f.States)
                Buffer.MemoryCopy(src, (void*)m.DataPointer, (long)PartCount * sizeof(PartStateGpu), (long)PartCount * sizeof(PartStateGpu));
            Ctx.Unmap(stateBuf, 0);
        }

        // kamerayla dönen ışıklar: görünüm uzayında tanımlı yönleri dünyaya çevir
        Matrix4x4.Invert(cam.View, out var invView);
        Vector3 ToWorld(Vector3 v) => Vector3.Normalize(Vector3.TransformNormal(Vector3.Normalize(v), invView));
        var keyDir = ToWorld(new Vector3(-0.55f, 0.62f, 0.56f));
        var fc = new FrameConstants
        {
            View = cam.View, Proj = cam.Proj, ViewProj = viewProj, InvViewProj = invViewProj,
            EnvRotation = new Matrix4x4(cam.View.M11, cam.View.M12, cam.View.M13, 0, cam.View.M21, cam.View.M22, cam.View.M23, 0,
                cam.View.M31, cam.View.M32, cam.View.M33, 0, 0, 0, 0, 1),
            CameraPos = cam.Position, Time = f.Time,
            ViewSize = new Vector2(width, height), InvViewSize = new Vector2(1f / width, 1f / height),
            QuantMin = QuantMin, QuantScale = QuantScale,
            TessTargetPx = Settings.TessTargetPx, TessMaxFactor = Settings.TessMaxFactor,
            ClipPlane = f.Clip ?? Vector4.Zero,
            KeyDir = keyDir, KeyIntensity = 3.0f, KeyColor = new Vector3(1.0f, 0.96f, 0.9f), ShadowStrength = 0.85f,
            FillDir = ToWorld(new Vector3(0.8f, 0.1f, 0.55f)), FillIntensity = 0.7f, FillColor = new Vector3(0.75f, 0.85f, 1.0f),
            RimDir = ToWorld(new Vector3(0.2f, 0.45f, -0.85f)), RimIntensity = 1.3f, RimColor = new Vector3(0.9f, 0.95f, 1.0f),
            EnvIntensity = 1.0f, DetailStrength = Settings.ProceduralDetail ? 1f : 0f,
            HoleCount = (uint)Math.Min(16, f.Holes.Count),
            FrameFlags = (uint)((f.Clip.HasValue ? FrameFlags.Clip : 0) | (Settings.Shadows ? FrameFlags.Shadows : 0) |
                                (Settings.Ssao ? FrameFlags.Ssao : 0) | (Settings.ProceduralDetail ? FrameFlags.Detail : 0) |
                                (f.Wireframe ? FrameFlags.Wire : 0)),
            EnvSH = envSh,
            SelectColor = f.SelectColor, SelectPulse = 0.5f + 0.5f * MathF.Sin(f.Time * 4f),
            HoverColor = f.HoverColor, Exposure = f.Exposure,
            QuizColor = new Vector3(0.19f, 0.64f, 0.42f), GhostOpacityScale = f.GhostOpacity,
            EdgeColor = new Vector3(0.44f, 0.82f, 1.0f), SpecularAA = 0.35f,
        };
        for (int i = 0; i < fc.HoleCount; i++) fc.Holes[i] = f.Holes[i];

        // gölge haritası: görünür opak yapıların sınır küresine oturtulmuş dik izdüşüm
        bool shadows = Settings.Shadows && br > 0;
        if (shadows)
        {
            var eye = bc + keyDir * br * 2f;
            var up = MathF.Abs(keyDir.Y) > 0.95f ? Vector3.UnitZ : Vector3.UnitY;
            var lv = Matrix4x4.CreateLookAt(eye, bc, up);
            var lp = Matrix4x4.CreateOrthographic(br * 2f, br * 2f, br * 0.5f, br * 3.5f);
            fc.ShadowViewProj = lv * lp;
            fc.ShadowTexel = 1f / Settings.ShadowMapSize;
            fc.ShadowNormalOffset = br * 2f / Settings.ShadowMapSize * 1.5f;
        }
        else fc.FrameFlags &= ~(uint)FrameFlags.Shadows;
        WriteCb(frameCb, fc);

        // ortak bağlamalar
        Ctx.ClearState();
        Ctx.IASetInputLayout(layout);
        Ctx.IASetVertexBuffer(0, vb, (uint)PackFormat.VertexSize, 0);
        Ctx.IASetIndexBuffer(ib, Format.R32_UInt, 0);
        foreach (var stage in new Action<uint, ID3D11Buffer>[] { Ctx.VSSetConstantBuffer, Ctx.HSSetConstantBuffer, Ctx.DSSetConstantBuffer, Ctx.PSSetConstantBuffer })
            stage(0, frameCb);
        Ctx.VSSetShaderResource(0, stateSrv);
        Ctx.DSSetShaderResource(0, stateSrv);
        Ctx.HSSetShaderResource(0, stateSrv);
        Ctx.PSSetShaderResource(0, stateSrv);
        Ctx.PSSetShaderResource(1, materialSrv);
        Ctx.PSSetSampler(0, smpShadow);
        Ctx.PSSetSampler(1, smpLinear);

        // ---- gölge
        if (shadows)
        {
            Ctx.ClearDepthStencilView(shadowMap!.Dsv!, DepthStencilClearFlags.Depth, 1f, 0);
            Ctx.OMSetRenderTargets((ID3D11RenderTargetView)null!, shadowMap.Dsv);
            Ctx.RSSetViewport(new Viewport(0, 0, Settings.ShadowMapSize, Settings.ShadowMapSize));
            Ctx.RSSetState(rsShadow);
            Ctx.OMSetDepthStencilState(dsWrite, 0);
            Ctx.OMSetBlendState(bsOpaque);
            Ctx.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
            Ctx.VSSetShader(vsShadow);
            Ctx.PSSetShader(null);
            DrawBucket(shadowLists[0]);
            Ctx.PSSetShader(psShadowCut);
            DrawBucket(shadowLists[1]);
            Ctx.OMSetRenderTargets((ID3D11RenderTargetView)null!, null);
        }
        Profiler.Mark("Gölge");

        var vp = new Viewport(0, 0, width, height);
        Ctx.RSSetViewport(vp);
        var geomRs = f.Wireframe ? rsWire : rsSolid;

        // ---- ön geçiş: derinlik + normal + kimlik
        Ctx.ClearDepthStencilView(depthMs!.Dsv!, DepthStencilClearFlags.Depth, 1f, 0);
        Ctx.ClearRenderTargetView(normalMs!.Rtv!, new Color4(0, 0, 0, 0));
        Ctx.ClearRenderTargetView(idMs!.Rtv!, new Color4(0, 0, 0, 0));
        Ctx.OMSetRenderTargets([normalMs.Rtv!, idMs.Rtv!], depthMs.Dsv);
        Ctx.RSSetState(geomRs);
        Ctx.OMSetDepthStencilState(dsWrite, 0);
        DrawOpaque(psPrepass);
        Profiler.Mark("Ön geçiş");

        // ---- doğrusal derinlik, normal ve kimliği tek örneğe indir
        FullscreenBegin();
        var pc = new PostConstants
        {
            TexelSize = new Vector4(1f / width, 1f / height, 1f / width, 1f / height),
            ProjInfo = new Vector4(1f / cam.Proj.M11, 1f / cam.Proj.M22, cam.Near, cam.Far),
            AoRadius = 0.03f, AoIntensity = 0.8f, SampleCount = depthMs.Samples,
            BloomIntensity = Settings.Bloom ? 0.06f : 0f, BloomThreshold = 0f, Vignette = 0.32f,
            HoveredId = f.Hovered >= 0 ? (uint)f.Hovered + 1 : 0u,
            SelectedId = f.Selected.Count > 0 ? (uint)f.Selected[0] + 1 : 0u,
            QuizId = f.Selected.Count > 1 ? (uint)f.Selected[1] + 1 : 0xFFFFFFFFu,
            OutlineFlags = f.Selected.Count > 0 ? 1u : 0u,
            BgTop = f.BackgroundTop, BgBottom = f.BackgroundBottom, Contrast = 1.04f, Saturation = 1.06f,
            UpsampleRadius = 1f,
        };
        bool ms = depthMs.Samples > 1;
        WriteCb(postCb, pc);
        Ctx.OMSetRenderTargets([linZ!.Rtv!, viewNormal!.Rtv!, id1!.Rtv!], depth1!.Dsv);
        Ctx.OMSetDepthStencilState(dsAlways, 0);
        Ctx.PSSetShaderResources(0, [depthMs.Srv!, normalMs.Srv!, idMs.Srv!]);
        Ctx.PSSetShader(ms ? psResolveDepthMs : psResolveDepthSs);
        Ctx.Draw(3, 0);
        Ctx.OMSetDepthStencilState(dsNone, 0);
        Ctx.OMSetRenderTargets((ID3D11RenderTargetView)null!, null);
        Ctx.PSUnsetShaderResources(0, 3);
        QueuePickCopies();
        Profiler.Mark("Derinlik çözme");

        // ---- SSAO (yarım çözünürlük) + çift yönlü bulanıklaştırma
        if (Settings.Ssao)
        {
            int aw = ao!.Width, ah = ao.Height;
            pc.TexelSize = new Vector4(1f / width, 1f / height, 1f / aw, 1f / ah);
            pc.AoProjScale = ah / (2f * MathF.Tan(cam.FovY * 0.5f));
            WriteCb(postCb, pc);
            Ctx.RSSetViewport(new Viewport(0, 0, aw, ah));
            Ctx.OMSetRenderTargets(ao.Rtv!, null);
            Ctx.PSSetShaderResources(0, [linZ.Srv!, viewNormal.Srv!]);
            Ctx.PSSetShader(psSsao);
            Ctx.Draw(3, 0);
            for (int pass = 0; pass < 2; pass++)
            {
                var src = pass == 0 ? ao : aoTmp!;
                var dst = pass == 0 ? aoTmp! : ao;
                pc.TexelSize = new Vector4(1f / aw, 1f / ah, 1f / aw, 1f / ah);
                pc.BlurDir = pass == 0 ? new Vector2(1, 0) : new Vector2(0, 1);
                WriteCb(postCb, pc);
                Ctx.OMSetRenderTargets(dst.Rtv!, null);
                Ctx.PSSetShaderResource(2, src.Srv!);
                Ctx.PSSetShader(psBlurAo);
                Ctx.Draw(3, 0);
                Ctx.OMSetRenderTargets((ID3D11RenderTargetView)null!, null);
                Ctx.PSUnsetShaderResource(2);
            }
            Ctx.PSUnsetShaderResources(0, 2);
            Ctx.RSSetViewport(vp);
        }
        Profiler.Mark("SSAO");

        // ---- opak aydınlatma
        GeometryBegin();
        Ctx.ClearRenderTargetView(colorMs!.Rtv!, new Color4(0, 0, 0, 0));
        Ctx.OMSetRenderTargets(colorMs.Rtv!, depthMs.Dsv);
        Ctx.RSSetState(geomRs);
        Ctx.OMSetDepthStencilState(dsEqualRead, 0);
        Ctx.PSSetShaderResource(2, shadows ? shadowMap!.Srv! : null!);
        Ctx.PSSetShaderResource(3, Settings.Ssao ? ao!.Srv! : null!);
        Ctx.PSSetShaderResource(4, specCube!.Srv);
        Ctx.PSSetShaderResource(5, brdfLut!.Srv!);
        DrawOpaque(psMain);
        Profiler.Mark("Aydınlatma");

        // ---- X-ray / cam: sıra bağımsız saydamlık
        Ctx.ClearRenderTargetView(accumMs!.Rtv!, new Color4(0, 0, 0, 0));
        Ctx.ClearRenderTargetView(revealMs!.Rtv!, new Color4(1, 1, 1, 1));
        if (ghost.Runs.Count + glass.Runs.Count > 0)
        {
            Ctx.OMSetRenderTargets([accumMs.Rtv!, revealMs.Rtv!], depth1!.Dsv);
            Ctx.OMSetBlendState(bsOit);
            Ctx.OMSetDepthStencilState(dsRead, 0);
            Ctx.RSSetState(rsSolid);
            Ctx.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
            Ctx.VSSetShader(vsMain);
            Ctx.HSSetShader(null);
            Ctx.DSSetShader(null);
            Ctx.PSSetShader(psGhost);
            DrawBucket(ghost);
            DrawBucket(glass);
            Ctx.OMSetBlendState(bsOpaque);
        }

        Profiler.Mark("Saydamlık");
        // ---- seçim maskesi (arkada kalan kısmın soluk dış çizgisi için, derinliksiz); görünen kısım kimlik tamponundan
        if (mask.Runs.Count > 0)
        {
            Ctx.ClearRenderTargetView(maskAll!.Rtv!, new Color4(0, 0, 0, 0));
            Ctx.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
            Ctx.VSSetShader(vsMain);
            Ctx.HSSetShader(null);
            Ctx.DSSetShader(null);
            Ctx.PSSetShader(psMask);
            Ctx.RSSetState(rsSolid);
            Ctx.OMSetRenderTargets(maskAll.Rtv!, null);
            Ctx.OMSetDepthStencilState(dsNone, 0);
            DrawBucket(mask);
        }
        Ctx.OMSetRenderTargets((ID3D11RenderTargetView)null!, null);
        for (uint t = 2; t < 6; t++) Ctx.PSUnsetShaderResource(t);

        // ---- çözme + saydamlık birleştirme + arka plan
        FullscreenBegin();
        Ctx.OMSetRenderTargets(hdr!.Rtv!, null);
        Ctx.PSSetShaderResources(0, [colorMs.Srv!, accumMs.Srv!, revealMs.Srv!, depthMs.Srv!]);
        Ctx.PSSetShader(ms ? psResolveMs : psResolveSs);
        Ctx.Draw(3, 0);
        Ctx.OMSetRenderTargets((ID3D11RenderTargetView)null!, null);
        Ctx.PSUnsetShaderResources(0, 4);
        Profiler.Mark("Çözme");

        // ---- bloom
        if (Settings.Bloom && bloomChain.Count > 0)
        {
            Target2D src = hdr;
            for (int i = 0; i < bloomChain.Count; i++)
            {
                var dst = bloomChain[i];
                pc.TexelSize = new Vector4(1f / src.Width, 1f / src.Height, 1f / dst.Width, 1f / dst.Height);
                pc.BloomThreshold = i == 0 ? 1.0f : 0f;
                WriteCb(postCb, pc);
                Ctx.RSSetViewport(new Viewport(0, 0, dst.Width, dst.Height));
                Ctx.OMSetRenderTargets(dst.Rtv!, null);
                Ctx.PSSetShaderResource(0, src.Srv!);
                Ctx.PSSetShader(psDown);
                Ctx.Draw(3, 0);
                Ctx.OMSetRenderTargets((ID3D11RenderTargetView)null!, null);
                Ctx.PSUnsetShaderResource(0);
                src = dst;
            }
            Ctx.OMSetBlendState(bsAdd);
            for (int i = bloomChain.Count - 1; i > 0; i--)
            {
                var s = bloomChain[i];
                var d = bloomChain[i - 1];
                pc.TexelSize = new Vector4(1f / s.Width, 1f / s.Height, 1f / d.Width, 1f / d.Height);
                WriteCb(postCb, pc);
                Ctx.RSSetViewport(new Viewport(0, 0, d.Width, d.Height));
                Ctx.OMSetRenderTargets(d.Rtv!, null);
                Ctx.PSSetShaderResource(0, s.Srv!);
                Ctx.PSSetShader(psUp);
                Ctx.Draw(3, 0);
                Ctx.OMSetRenderTargets((ID3D11RenderTargetView)null!, null);
                Ctx.PSUnsetShaderResource(0);
            }
            Ctx.OMSetBlendState(bsOpaque);
            Ctx.RSSetViewport(vp);
        }

        Profiler.Mark("Bloom");
        // ---- son: ton eşleme, bloom, dış çizgi, vinyet
        pc.TexelSize = new Vector4(1f / width, 1f / height, 1f / width, 1f / height);
        WriteCb(postCb, pc);
        Ctx.OMSetRenderTargets(output, null);
        Ctx.PSSetShaderResources(0, [hdr.Srv!, Settings.Bloom && bloomChain.Count > 0 ? bloomChain[0].Srv! : hdr.Srv!, id1.Srv!,
            null!, f.Selected.Count > 0 ? maskAll!.Srv! : null!]);
        Ctx.PSSetShader(ms ? psFinalMs : psFinalSs);
        Ctx.Draw(3, 0);
        Ctx.PSUnsetShaderResources(0, 5);
        Ctx.OMSetRenderTargets((ID3D11RenderTargetView)null!, null);
        Profiler.Mark("Ton eşleme");
        Profiler.EndFrame();
    }

    private void GeometryBegin()
    {
        Ctx.IASetInputLayout(layout);
        Ctx.IASetVertexBuffer(0, vb, (uint)PackFormat.VertexSize, 0);
        Ctx.IASetIndexBuffer(ib, Format.R32_UInt, 0);
        Ctx.VSSetConstantBuffer(0, frameCb);
        Ctx.HSSetConstantBuffer(0, frameCb);
        Ctx.DSSetConstantBuffer(0, frameCb);
        Ctx.PSSetConstantBuffer(0, frameCb);
        Ctx.VSSetShaderResource(0, stateSrv);
        Ctx.HSSetShaderResource(0, stateSrv);
        Ctx.DSSetShaderResource(0, stateSrv);
        Ctx.PSSetShaderResource(0, stateSrv);
        Ctx.PSSetShaderResource(1, materialSrv);
        Ctx.PSSetSampler(0, smpShadow);
        Ctx.PSSetSampler(1, smpLinear);
        Ctx.OMSetBlendState(bsOpaque);
    }

    private void FullscreenBegin()
    {
        Ctx.IASetInputLayout(null);
        Ctx.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        Ctx.VSSetShader(vsFull);
        Ctx.HSSetShader(null);
        Ctx.DSSetShader(null);
        Ctx.RSSetState(rsFull);
        Ctx.OMSetDepthStencilState(dsNone, 0);
        Ctx.OMSetBlendState(bsOpaque);
        Ctx.PSSetConstantBuffer(0, frameCb);
        Ctx.PSSetConstantBuffer(1, postCb);
        Ctx.PSSetSampler(0, smpPoint);
        Ctx.PSSetSampler(1, smpLinear);
        Ctx.RSSetViewport(new Viewport(0, 0, width, height));
    }

    private void DrawOpaque(ID3D11PixelShader[] ps)
    {
        for (int v = 0; v < 3; v++)
        {
            Ctx.PSSetShader(ps[v]);
            if (opaque[v, 0].Runs.Count > 0)
            {
                Ctx.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
                Ctx.VSSetShader(vsMain);
                Ctx.HSSetShader(null);
                Ctx.DSSetShader(null);
                DrawBucket(opaque[v, 0]);
            }
            if (opaque[v, 1].Runs.Count > 0)
            {
                Ctx.IASetPrimitiveTopology(PrimitiveTopology.PatchListWith3ControlPoints);
                Ctx.VSSetShader(vsControl);
                Ctx.HSSetShader(hs);
                Ctx.DSSetShader(ds);
                DrawBucket(opaque[v, 1]);
            }
        }
        Ctx.HSSetShader(null);
        Ctx.DSSetShader(null);
        Ctx.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        Ctx.VSSetShader(vsMain);
    }

    // ------------------------------------------------------------------------------------------------ seçim (picking)
    private readonly List<(int token, int x, int y)> pickRequests = [];

    /// <summary>Ekran noktasındaki yapıyı sorgular; sonuç 1-2 kare sonra <see cref="DrainPickResults"/> ile alınır.</summary>
    public void RequestPick(int token, int x, int y) => pickRequests.Add((token, x, y));

    private void QueuePickCopies()
    {
        foreach (var (token, x, y) in pickRequests)
        {
            if (x < 0 || y < 0 || x >= width || y >= height) { pickResults.Add(new PickResult(token, -1, default, 1, x, y)); continue; }
            var (idTex, zTex) = pickPool.Count > 0 ? pickPool.Pop() : CreatePickPair();
            var box = new Box(x, y, 0, x + 1, y + 1, 1);
            Ctx.CopySubresourceRegion(idTex, 0, 0, 0, 0, id1!.Texture, 0, box);
            Ctx.CopySubresourceRegion(zTex, 0, 0, 0, 0, linZ!.Texture, 0, box);
            pickQueue.Enqueue((idTex, zTex, token, x, y, frameIndex));
        }
        pickRequests.Clear();
    }

    private (ID3D11Texture2D, ID3D11Texture2D) CreatePickPair()
    {
        var d = new Texture2DDescription
        {
            Width = 1, Height = 1, MipLevels = 1, ArraySize = 1, Format = Format.R32_UInt, SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Staging, CPUAccessFlags = CpuAccessFlags.Read,
        };
        var a = Dev.CreateTexture2D(ref d);
        d.Format = Format.R32_Float;
        var b = Dev.CreateTexture2D(ref d);
        return (a, b);
    }

    /// <summary>Hazır olan seçim sonuçları (en az bir kare önce istenenler — GPU beklenmez).</summary>
    public List<PickResult> DrainPickResults(bool wait = false)
    {
        var outList = new List<PickResult>(pickResults);
        pickResults.Clear();
        while (pickQueue.Count > 0)
        {
            var q = pickQueue.Peek();
            if (!wait && frameIndex - q.frame < 1) break;
            pickQueue.Dequeue();
            var m1 = Ctx.Map(q.idTex, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
            uint id = *(uint*)m1.DataPointer;
            Ctx.Unmap(q.idTex, 0);
            var m2 = Ctx.Map(q.zTex, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
            float z = *(float*)m2.DataPointer;
            Ctx.Unmap(q.zTex, 0);
            pickPool.Push((q.idTex, q.zTex));
            Vector3 world = default;
            if (id != 0 && z < 1e5f)
            {
                // doğrusal derinlikten dünya konumu: bakış ışını boyunca
                float nx = (q.x + 0.5f) / width * 2f - 1f;
                float ny = 1f - (q.y + 0.5f) / height * 2f;
                var viewPos = new Vector3(nx / lastCamera.Proj.M11 * z, ny / lastCamera.Proj.M22 * z, -z);
                Matrix4x4.Invert(lastCamera.View, out var iv);
                world = Vector3.Transform(viewPos, iv);
            }
            outList.Add(new PickResult(q.token, id == 0 ? -1 : (int)id - 1, world, z, q.x, q.y));
        }
        return outList;
    }

    public void Dispose()
    {
        DestroyTargets();
        Profiler.Dispose();
        while (pickQueue.Count > 0) { var q = pickQueue.Dequeue(); q.idTex.Dispose(); q.zTex.Dispose(); }
        while (pickPool.Count > 0) { var (a, b) = pickPool.Pop(); a.Dispose(); b.Dispose(); }
        envCube?.Dispose(); specCube?.Dispose(); brdfLut?.Dispose();
        foreach (var d in new IDisposable?[] { vb, ib, layout, stateBuf, materialBuf, stateSrv, materialSrv, frameCb, postCb, envCb,
                     vsMain, vsShadow, vsControl, vsFull, hs, ds, psShadowCut, psGhost, psMask, psResolveDepthMs, psResolveDepthSs,
                     psResolveMs, psResolveSs, psSsao, psBlurAo, psDown, psUp, psFinalMs, psFinalSs,
                     rsSolid, rsWire, rsShadow, rsFull, dsWrite, dsEqualRead, dsRead, dsNone, dsAlways, bsOpaque, bsOit, bsAdd, bsMaskOnly,
                     smpShadow, smpLinear, smpPoint })
            d?.Dispose();
        foreach (var p in psPrepass) p?.Dispose();
        foreach (var p in psMain) p?.Dispose();
    }
}

public sealed class RenderStats
{
    public int VisibleParts;
    public long Triangles;
    public int DrawCalls;
    public int SkinLod;
    public uint Samples;
}
