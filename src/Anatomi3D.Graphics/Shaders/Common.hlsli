// Anatomi 3D — ortak shader tanımları
// Matrisler satır-öncelikli (C# System.Numerics ile aynı): mul(float4(p, 1), M)

#ifndef COMMON_HLSLI
#define COMMON_HLSLI

#define PI 3.14159265359
#define MAX_HOLES 16

// PartState.flags
#define PF_SWEEP      1u    // çözülme bir düzlem boyunca (katman soyma / göster-gizle / açılış)
#define PF_WINDOWED   2u    // deri penceresi: yalnız deliklerin içinde çiz
#define PF_SELECTED   4u
#define PF_HOVERED    8u
#define PF_QUIZ       16u
#define PF_ATTACH_O   32u   // origo alanı
#define PF_ATTACH_E   64u   // insersiyo alanı

// FrameFlags
#define FF_CLIP       1u
#define FF_SHADOWS    2u
#define FF_SSAO       4u
#define FF_DETAIL     8u
#define FF_WIRE       16u

cbuffer FrameCB : register(b0)
{
    float4x4 View;
    float4x4 Proj;
    float4x4 ViewProj;
    float4x4 InvViewProj;
    float4x4 ShadowViewProj;
    float4x4 EnvRotation;       // dünya -> ortam (kamera ile dönen stüdyo ışığı)
    float3 CameraPos;       float Time;
    float2 ViewSize;        float2 InvViewSize;
    float3 QuantMin;        float TessTargetPx;
    float3 QuantScale;      float TessMaxFactor;
    float4 ClipPlane;       // n·p + d >= 0 tarafı tutulur
    float3 KeyDir;          float KeyIntensity;     // ışığa doğru yön (dünya)
    float3 KeyColor;        float ShadowStrength;
    float3 FillDir;         float FillIntensity;
    float3 FillColor;       float RimIntensity;
    float3 RimDir;          float EnvIntensity;
    float3 RimColor;        float DetailStrength;
    float4 Holes[MAX_HOLES];
    uint HoleCount;         uint FrameFlags;        float ShadowTexel;      float ShadowNormalOffset;
    float4 EnvSH[9];
    float3 SelectColor;     float SelectPulse;
    float3 HoverColor;      float Exposure;
    float3 QuizColor;       float GhostOpacityScale;
    float3 EdgeColor;       float SpecularAA;
};

struct PartState
{
    float3 pivot;       float scale;
    float3 digOrigin;   float dissolve;
    float3 digDir;      float digRadius;
    float3 tint;        float tintAmount;
    float opacity;      uint flags;     float emissive;     float capBias;
};

struct PartMaterial
{
    float3 albedo;      float roughness;
    float3 sheen;       float clearcoat;
    float3 center;      float param;        // göz merkezi, iris yarıçapı / limbus kosinüsü
    float3 axis;        uint family;
    uint kind;          float bump;         float freq;     float subsurface;
};

StructuredBuffer<PartState> PartStates : register(t0);
StructuredBuffer<PartMaterial> PartMaterials : register(t1);

// ------------------------------------------------------------------ yardımcılar
float3 OctDecode(float2 e)
{
    float3 v = float3(e.x, e.y, 1.0 - abs(e.x) - abs(e.y));
    float t = saturate(-v.z);
    v.xy += (v.xy >= 0.0) ? -t : t;
    return normalize(v);
}

float2 OctEncode(float3 n)
{
    n /= (abs(n.x) + abs(n.y) + abs(n.z));
    float2 p = n.xy;
    if (n.z < 0.0) p = (1.0 - abs(p.yx)) * ((p >= 0.0) ? 1.0 : -1.0);
    return p;
}

float Luma(float3 c) { return dot(c, float3(0.2126, 0.7152, 0.0722)); }

// Değer gürültüsü (sin kullanmayan karma — GPU'lar arası kararlı)
float Hash3(float3 p)
{
    p = frac(p * 0.3183099 + 0.1);
    p *= 17.0;
    return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
}

float Noise3(float3 x)
{
    float3 i = floor(x);
    float3 f = frac(x);
    f = f * f * (3.0 - 2.0 * f);
    return lerp(
        lerp(lerp(Hash3(i), Hash3(i + float3(1, 0, 0)), f.x), lerp(Hash3(i + float3(0, 1, 0)), Hash3(i + float3(1, 1, 0)), f.x), f.y),
        lerp(lerp(Hash3(i + float3(0, 0, 1)), Hash3(i + float3(1, 0, 1)), f.x), lerp(Hash3(i + float3(0, 1, 1)), Hash3(i + float3(1, 1, 1)), f.x), f.y),
        f.z);
}

// Ekran uzayı titreşimi (bantlanmayı kırmak için)
float InterleavedGradient(float2 p)
{
    return frac(52.9829189 * frac(dot(p, float2(0.06711056, 0.00583715))));
}

// ------------------------------------------------------------------ BRDF
float D_GGX(float NoH, float a)
{
    float a2 = a * a;
    float d = NoH * NoH * (a2 - 1.0) + 1.0;
    return a2 / (PI * d * d + 1e-7);
}

float V_SmithGGXCorrelated(float NoV, float NoL, float a)
{
    float a2 = a * a;
    float gv = NoL * sqrt(NoV * NoV * (1.0 - a2) + a2);
    float gl = NoV * sqrt(NoL * NoL * (1.0 - a2) + a2);
    return 0.5 / (gv + gl + 1e-7);
}

float3 F_Schlick(float3 f0, float VoH)
{
    float f = pow(1.0 - VoH, 5.0);
    return f0 + (1.0 - f0) * f;
}

// Kadife (Charlie) parlaklığı: lifli dokularda kenar parıltısı
float D_Charlie(float NoH, float rough)
{
    float inv = 1.0 / max(rough, 0.05);
    float sin2 = max(1.0 - NoH * NoH, 0.0078125);
    return (2.0 + inv) * pow(sin2, inv * 0.5) / (2.0 * PI);
}

float3 EvalSH(float3 n)
{
    // L2 küresel harmonik ışınım (Ramamoorthi & Hanrahan)
    float3 r = EnvSH[0].rgb * 0.282095
             + EnvSH[1].rgb * 0.488603 * n.y
             + EnvSH[2].rgb * 0.488603 * n.z
             + EnvSH[3].rgb * 0.488603 * n.x
             + EnvSH[4].rgb * 1.092548 * n.x * n.y
             + EnvSH[5].rgb * 1.092548 * n.y * n.z
             + EnvSH[6].rgb * 0.315392 * (3.0 * n.z * n.z - 1.0)
             + EnvSH[7].rgb * 1.092548 * n.x * n.z
             + EnvSH[8].rgb * 0.546274 * (n.x * n.x - n.y * n.y);
    return max(r, 0.0);
}

float3 ToEnv(float3 dirWorld) { return mul(float4(dirWorld, 0.0), EnvRotation).xyz; }

#endif
