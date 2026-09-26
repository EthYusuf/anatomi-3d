// Anatomi 3D — tam ekran geçişler: derinlik çözme, SSAO, bulanıklaştırma, MSAA çözme + saydamlık birleştirme,
// arka plan, bloom, ton eşleme ve seçim dış çizgisi
//
// MSAA = 1: çok örnekli kaynaklar (Texture2DMS), 0: tek örnekli

#include "Common.hlsli"

#ifndef MSAA
#define MSAA 1
#endif

#if MSAA
#define TEXMS(T) Texture2DMS<T>
#define LOADMS(tex, p, s) tex.Load(p, s)
#else
#define TEXMS(T) Texture2D<T>
#define LOADMS(tex, p, s) tex.Load(int3(p, 0))
#endif

cbuffer PostCB : register(b1)
{
    float4 TexelSize;       // xy: kaynak doku 1/boyut, zw: hedef doku 1/boyut
    float4 ProjInfo;        // x = 1/P00, y = 1/P11, z = yakın düzlem, w = uzak düzlem
    float AoRadius;         float AoIntensity;     float AoProjScale;     uint SampleCount;
    float BloomIntensity;   float BloomThreshold;  float Vignette;        float Grain;
    uint SelectedId;        uint HoveredId;        uint QuizId;           uint OutlineFlags;
    float3 BgTop;           float Contrast;
    float3 BgBottom;        float Saturation;
    float2 BlurDir;         float UpsampleRadius;  float PostPad;
};

SamplerState PointClamp : register(s0);
SamplerState LinearClampS : register(s1);

struct FSOut
{
    float4 pos : SV_Position;
    float2 uv : TEXCOORD0;
};

// tek üçgenle tam ekran
FSOut VSFullscreen(uint id : SV_VertexID)
{
    FSOut o;
    o.uv = float2((id << 1) & 2, id & 2);
    o.pos = float4(o.uv * float2(2, -2) + float2(-1, 1), 0, 1);
    return o;
}

float LinearDepth(float d)
{
    float n = ProjInfo.z, f = ProjInfo.w;
    return n * f / (f - d * (f - n));
}

float3 ViewPos(float2 uv, float linZ)
{
    float2 ndc = uv * float2(2, -2) + float2(-1, 1);
    return float3(ndc.x * ProjInfo.x * linZ, ndc.y * ProjInfo.y * linZ, -linZ);
}

// ------------------------------------------------------------------ derinlik, normal ve kimliği tek örneğe indir (örnek 0)
TEXMS(float) SceneDepth : register(t0);
TEXMS(float2) SceneNormal : register(t1);
TEXMS(uint) SceneId : register(t2);

struct DepthNormalOut
{
    float depth : SV_Target0;       // doğrusal derinlik (metre); arka plan 1e6
    float2 normal : SV_Target1;
    uint id : SV_Target2;
    float raw : SV_Depth;           // tek örnekli derinlik tamponu (saydamlık geçişi için)
};

DepthNormalOut PSResolveDepth(FSOut i)
{
    int2 p = int2(i.pos.xy);
    DepthNormalOut o;
    float d = LOADMS(SceneDepth, p, 0);
    // saydamlık için en uzak örnek: kenarlarda opak yapının arkasındaki hayaletler kesilmesin
    float dmax = d;
#if MSAA
    [loop] for (uint s = 1; s < SampleCount; s++) dmax = max(dmax, SceneDepth.Load(p, s));
#endif
    o.raw = dmax;
    o.depth = d >= 1.0 ? 1e6 : LinearDepth(d);
    o.normal = LOADMS(SceneNormal, p, 0);
    o.id = LOADMS(SceneId, p, 0);
    return o;
}

// ------------------------------------------------------------------ SSAO (ölçeklenebilir ortam kapatması, McGuire 2012)
Texture2D<float> LinZ : register(t0);
Texture2D<float2> ViewNormal : register(t1);

float PSSsao(FSOut i) : SV_Target0
{
    float2 uv = i.uv;
    float z = LinZ.SampleLevel(PointClamp, uv, 0);
    if (z > 1e5) return 1.0;
    float3 P = ViewPos(uv, z);
    float3 N = OctDecode(ViewNormal.SampleLevel(PointClamp, uv, 0));
    float radiusPx = AoProjScale * AoRadius / z;
    if (radiusPx < 1.0) return 1.0;
    float rot = InterleavedGradient(i.pos.xy) * 6.2831853;
    const int NS = 14;
    float r2 = AoRadius * AoRadius;
    float sum = 0.0;
    [unroll] for (int k = 0; k < NS; k++)
    {
        float alpha = (k + 0.5) / NS;
        float ang = alpha * 6.2831853 * 3.0 + rot;
        float2 suv = uv + float2(cos(ang), sin(ang)) * alpha * radiusPx * TexelSize.zw;
        float sz = LinZ.SampleLevel(PointClamp, suv, 0);
        float3 v = ViewPos(suv, sz) - P;
        float vv = dot(v, v);
        float vn = dot(v, N);
        float f = max(r2 - vv, 0.0);
        sum += f * f * f * max((vn - 0.002 * z) / (vv + 1e-6), 0.0);
    }
    return max(0.0, 1.0 - sum * AoIntensity * 5.0 / (r2 * r2 * r2) / NS);
}

// derinlik duyarlı ayrılabilir bulanıklaştırma (çift yönlü)
Texture2D<float> AoIn : register(t2);

float PSBlurAo(FSOut i) : SV_Target0
{
    float z0 = LinZ.SampleLevel(PointClamp, i.uv, 0);
    float sum = 0.0, wsum = 0.0;
    [unroll] for (int k = -4; k <= 4; k++)
    {
        float2 uv = i.uv + BlurDir * TexelSize.xy * k;
        float a = AoIn.SampleLevel(LinearClampS, uv, 0);
        float z = LinZ.SampleLevel(PointClamp, uv, 0);
        float w = exp(-k * k / 10.0) * max(0.0, 1.0 - abs(z - z0) / (0.03 * z0 + 0.002));
        sum += a * w;
        wsum += w;
    }
    return wsum > 1e-4 ? sum / wsum : AoIn.SampleLevel(LinearClampS, i.uv, 0);
}

// ------------------------------------------------------------------ çözme + saydamlık + arka plan
TEXMS(float4) SceneColor : register(t0);
Texture2D<float4> OitAccum : register(t1);
Texture2D<float> OitReveal : register(t2);
TEXMS(float) SceneDepth2 : register(t3);

float3 Background(float2 uv)
{
    float3 c = lerp(BgTop, BgBottom, saturate(uv.y * 1.05));
    float2 d = (uv - float2(0.5, 0.45)) * float2(ViewSize.x * InvViewSize.y, 1.0);
    c += float3(0.03, 0.038, 0.05) * exp(-dot(d, d) * 2.6);
    return c;
}

// Tersinir ton eşleme ile çözme: parlak kenarlarda merdiven etkisini azaltır (Karis)
float3 TM(float3 c) { return c / (1.0 + Luma(c)); }
float3 TMInv(float3 c) { return c / max(1e-4, 1.0 - Luma(c)); }

float4 PSResolve(FSOut i) : SV_Target0
{
    int2 p = int2(i.pos.xy);
    float3 sum = 0.0;
    float cov = 0.0;
    [loop] for (uint s = 0; s < SampleCount; s++)
    {
        float hit = LOADMS(SceneDepth2, p, s) < 1.0 ? 1.0 : 0.0;
        sum += TM(LOADMS(SceneColor, p, s).rgb) * hit;
        cov += hit;
    }
    float3 opaque = cov > 0 ? TMInv(sum / cov) : 0.0;
    float3 c = lerp(Background(i.uv), opaque, cov / SampleCount);
    // ağırlıklı karışımlı sıra bağımsız saydamlık (McGuire & Bavoil 2013), tek örnekli hedefte
    float4 acc = OitAccum.Load(int3(p, 0));
    float a = 1.0 - OitReveal.Load(int3(p, 0));
    c = lerp(c, acc.rgb / clamp(acc.a, 1e-4, 5e4), a);
    return float4(c, 1.0);
}

// ------------------------------------------------------------------ bloom (enerji korumalı indirme/yükseltme zinciri)
Texture2D<float4> Src : register(t0);

float4 PSDownsample(FSOut i) : SV_Target0
{
    float2 t = TexelSize.xy;
    float2 uv = i.uv;
    float3 a = Src.SampleLevel(LinearClampS, uv + t * float2(-2, -2), 0).rgb;
    float3 b = Src.SampleLevel(LinearClampS, uv + t * float2(0, -2), 0).rgb;
    float3 c = Src.SampleLevel(LinearClampS, uv + t * float2(2, -2), 0).rgb;
    float3 d = Src.SampleLevel(LinearClampS, uv + t * float2(-2, 0), 0).rgb;
    float3 e = Src.SampleLevel(LinearClampS, uv, 0).rgb;
    float3 f = Src.SampleLevel(LinearClampS, uv + t * float2(2, 0), 0).rgb;
    float3 g = Src.SampleLevel(LinearClampS, uv + t * float2(-2, 2), 0).rgb;
    float3 h = Src.SampleLevel(LinearClampS, uv + t * float2(0, 2), 0).rgb;
    float3 k = Src.SampleLevel(LinearClampS, uv + t * float2(2, 2), 0).rgb;
    float3 l = Src.SampleLevel(LinearClampS, uv + t * float2(-1, -1), 0).rgb;
    float3 m = Src.SampleLevel(LinearClampS, uv + t * float2(1, -1), 0).rgb;
    float3 n = Src.SampleLevel(LinearClampS, uv + t * float2(-1, 1), 0).rgb;
    float3 o = Src.SampleLevel(LinearClampS, uv + t * float2(1, 1), 0).rgb;
    float3 r = e * 0.125 + (a + c + g + k) * 0.03125 + (b + d + f + h) * 0.0625 + (l + m + n + o) * 0.125;
    if (BloomThreshold > 0.0)
    {
        // yumuşak dizli eşik: yalnız parlak alanlar taşar
        const float knee = 0.6;
        float br = max(r.r, max(r.g, r.b));
        float rq = clamp(br - BloomThreshold + knee, 0.0, 2.0 * knee);
        rq = rq * rq / (4.0 * knee + 1e-4);
        r *= max(rq, br - BloomThreshold) / max(br, 1e-4);
        r /= 1.0 + Luma(r) * 0.2; // ateş böceği önleme
    }
    return float4(r, 1.0);
}

float4 PSUpsample(FSOut i) : SV_Target0
{
    float2 t = TexelSize.xy * UpsampleRadius;
    float2 uv = i.uv;
    float3 s = Src.SampleLevel(LinearClampS, uv + t * float2(-1, -1), 0).rgb;
    s += Src.SampleLevel(LinearClampS, uv + t * float2(0, -1), 0).rgb * 2.0;
    s += Src.SampleLevel(LinearClampS, uv + t * float2(1, -1), 0).rgb;
    s += Src.SampleLevel(LinearClampS, uv + t * float2(-1, 0), 0).rgb * 2.0;
    s += Src.SampleLevel(LinearClampS, uv, 0).rgb * 4.0;
    s += Src.SampleLevel(LinearClampS, uv + t * float2(1, 0), 0).rgb * 2.0;
    s += Src.SampleLevel(LinearClampS, uv + t * float2(-1, 1), 0).rgb;
    s += Src.SampleLevel(LinearClampS, uv + t * float2(0, 1), 0).rgb * 2.0;
    s += Src.SampleLevel(LinearClampS, uv + t * float2(1, 1), 0).rgb;
    return float4(s / 16.0, 1.0);
}

// ------------------------------------------------------------------ son: ton eşleme, bloom, dış çizgi, vinyet
Texture2D<float4> Hdr : register(t0);
Texture2D<float4> Bloom : register(t1);
Texture2D<uint> IdTex : register(t2);
TEXMS(float) MaskVisible : register(t3);
Texture2D<float> MaskAll : register(t4);

// ACES (Stephen Hill uyarlaması)
float3 RRTAndODTFit(float3 v)
{
    float3 a = v * (v + 0.0245786) - 0.000090537;
    float3 b = v * (0.983729 * v + 0.4329510) + 0.238081;
    return a / b;
}

float3 ACES(float3 c)
{
    const float3x3 inM = { 0.59719, 0.35458, 0.04823, 0.07600, 0.90834, 0.01566, 0.02840, 0.13383, 0.83777 };
    const float3x3 outM = { 1.60475, -0.53108, -0.07367, -0.10208, 1.10813, -0.00605, -0.00327, -0.07276, 1.07602 };
    c = mul(inM, c);
    c = RRTAndODTFit(c);
    return saturate(mul(outM, c));
}

float3 LinearToSrgb(float3 c)
{
    return c <= 0.0031308 ? c * 12.92 : 1.055 * pow(abs(c), 1.0 / 2.4) - 0.055;
}

float4 PSFinal(FSOut i) : SV_Target0
{
    int2 p = int2(i.pos.xy);
    float3 c = Hdr.Load(int3(p, 0)).rgb;
    c += Bloom.SampleLevel(LinearClampS, i.uv, 0).rgb * BloomIntensity;

    float3 m = ACES(c);
    float l = Luma(m);
    m = lerp(l.xxx, m, Saturation);
    m = saturate((m - 0.5) * Contrast + 0.5);
    m = LinearToSrgb(m);

    // seçili yapıların dış çizgisi: görünen kenar net, arkada kalan kısım soluk
    if (OutlineFlags & 1u)
    {
        // görünen kısım: kimlik tamponundan (tesselasyon/kesme ile birebir aynı piksel), tamamı: derinliksiz maske
        uint ic = IdTex.Load(int3(p, 0));
        float vc = (ic == SelectedId || ic == QuizId) ? 1.0 : 0.0, vmn = vc, vmx = vc;
        float ac = MaskAll.Load(int3(p, 0)), amn = ac, amx = ac;
        [unroll] for (int y = -2; y <= 2; y++)
            [unroll] for (int x = -2; x <= 2; x++)
            {
                if (abs(x) + abs(y) > 3) continue;
                int2 q = p + int2(x, y);
                uint iq = IdTex.Load(int3(q, 0));
                float v = (iq == SelectedId || iq == QuizId) ? 1.0 : 0.0;
                float a = MaskAll.Load(int3(q, 0));
                vmn = min(vmn, v); vmx = max(vmx, v);
                amn = min(amn, a); amx = max(amx, a);
            }
        float3 sc = LinearToSrgb(SelectColor);
        float visEdge = vmx - vmn;
        float allEdge = (amx - amn) * (1.0 - visEdge);
        m = lerp(m, sc, saturate(visEdge) * 0.9);
        m = lerp(m, sc, saturate(allEdge) * 0.35);
    }
    if (HoveredId != 0u)
    {
        bool inside = IdTex.Load(int3(p, 0)) == HoveredId;
        bool edge = inside && (IdTex.Load(int3(p + int2(1, 0), 0)) != HoveredId || IdTex.Load(int3(p + int2(0, 1), 0)) != HoveredId
                            || IdTex.Load(int3(p - int2(1, 0), 0)) != HoveredId || IdTex.Load(int3(p - int2(0, 1), 0)) != HoveredId);
        if (edge) m = lerp(m, LinearToSrgb(HoverColor), 0.7);
    }

    float2 d = i.uv - 0.5;
    m *= 1.0 - Vignette * smoothstep(0.35, 0.95, length(d * float2(1.0, 0.8)));
    m += (InterleavedGradient(i.pos.xy + frac(Time) * 97.0) - 0.5) / 255.0;
    return float4(m, 1.0);
}
