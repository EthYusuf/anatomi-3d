// Anatomi 3D — görüntü tabanlı aydınlatma: prosedürel stüdyo ortamı, GGX ön süzme, BRDF tablosu, SH ışınımı
// Ortam yönleri görünüm uzayındadır (+Z kameraya doğru); stüdyo ışıkları kamerayla birlikte döner.

cbuffer EnvCB : register(b0)
{
    uint Face;
    float Roughness;
    float SrcSize;
    float EnvPad;
};

TextureCube<float4> SrcCube : register(t0);
SamplerState LinearClamp : register(s0);

#define PI 3.14159265359

struct FSOut
{
    float4 pos : SV_Position;
    float2 uv : TEXCOORD0;
};

FSOut VSFullscreen(uint id : SV_VertexID)
{
    FSOut o;
    o.uv = float2((id << 1) & 2, id & 2);
    o.pos = float4(o.uv * float2(2, -2) + float2(-1, 1), 0, 1);
    return o;
}

float3 FaceDir(uint face, float2 uv)
{
    float2 t = uv * 2.0 - 1.0;
    float3 d;
    if (face == 0) d = float3(1, -t.y, -t.x);
    else if (face == 1) d = float3(-1, -t.y, t.x);
    else if (face == 2) d = float3(t.x, 1, t.y);
    else if (face == 3) d = float3(t.x, -1, -t.y);
    else if (face == 4) d = float3(t.x, -t.y, 1);
    else d = float3(-t.x, -t.y, -1);
    return normalize(d);
}

// Yumuşak kenarlı dikdörtgen ışık kutusu
float Softbox(float3 d, float3 center, float3 up, float2 halfAngle, float soft)
{
    float3 c = normalize(center);
    float3 r = normalize(cross(up, c));
    float3 u = cross(c, r);
    float z = dot(d, c);
    if (z <= 0.0) return 0.0;
    float2 q = float2(dot(d, r), dot(d, u)) / z;          // gnomonik izdüşüm
    float2 h = tan(halfAngle);
    float2 e = smoothstep(h, h * (1.0 - soft), abs(q));
    return e.x * e.y;
}

float3 Studio(float3 d)
{
    // koyu mavi-gri stüdyo; üst yarı biraz daha aydınlık, yer koyu
    float y = d.y;
    float3 col = lerp(float3(0.018, 0.02, 0.024), float3(0.07, 0.08, 0.095), smoothstep(-0.4, 0.8, y));
    col += float3(0.05, 0.045, 0.04) * pow(saturate(1.0 - abs(y)), 6.0);    // ufuk bandı
    // ana ışık: sol üst ön, sıcak
    col += float3(1.0, 0.95, 0.88) * 3.2 * Softbox(d, float3(-0.55, 0.62, 0.56), float3(0, 1, 0), float2(0.42, 0.30), 0.35);
    // dolgu: sağ, soğuk
    col += float3(0.72, 0.82, 1.0) * 0.9 * Softbox(d, float3(0.85, 0.12, 0.5), float3(0, 1, 0), float2(0.35, 0.5), 0.5);
    // kenar ışıkları: arkadan iki yan
    col += float3(0.85, 0.92, 1.0) * 2.4 * Softbox(d, float3(-0.7, 0.35, -0.62), float3(0, 1, 0), float2(0.12, 0.55), 0.4);
    col += float3(1.0, 0.9, 0.82) * 1.6 * Softbox(d, float3(0.75, 0.25, -0.6), float3(0, 1, 0), float2(0.12, 0.5), 0.4);
    // tepe halka ışığı
    col += float3(1.0, 1.0, 1.0) * 0.8 * smoothstep(0.9, 0.97, y);
    // yerden sıcak yansıma
    col += float3(0.09, 0.07, 0.06) * smoothstep(-0.2, -0.9, y);
    return col;
}

float4 PSStudio(FSOut i) : SV_Target0
{
    return float4(Studio(FaceDir(Face, i.uv)), 1.0);
}

// ------------------------------------------------------------------ GGX ön süzme (süzülmüş önem örneklemesi)
float RadicalInverse(uint b)
{
    b = (b << 16u) | (b >> 16u);
    b = ((b & 0x55555555u) << 1u) | ((b & 0xAAAAAAAAu) >> 1u);
    b = ((b & 0x33333333u) << 2u) | ((b & 0xCCCCCCCCu) >> 2u);
    b = ((b & 0x0F0F0F0Fu) << 4u) | ((b & 0xF0F0F0F0u) >> 4u);
    b = ((b & 0x00FF00FFu) << 8u) | ((b & 0xFF00FF00u) >> 8u);
    return float(b) * 2.3283064365386963e-10;
}

float2 Hammersley(uint i, uint n) { return float2(float(i) / float(n), RadicalInverse(i)); }

float3 ImportanceSampleGGX(float2 xi, float a, float3 N)
{
    float phi = 2.0 * PI * xi.x;
    float cosT = sqrt((1.0 - xi.y) / (1.0 + (a * a - 1.0) * xi.y));
    float sinT = sqrt(1.0 - cosT * cosT);
    float3 h = float3(sinT * cos(phi), sinT * sin(phi), cosT);
    float3 up = abs(N.z) < 0.999 ? float3(0, 0, 1) : float3(1, 0, 0);
    float3 t = normalize(cross(up, N));
    float3 b = cross(N, t);
    return normalize(t * h.x + b * h.y + N * h.z);
}

float4 PSPrefilter(FSOut i) : SV_Target0
{
    float3 N = FaceDir(Face, i.uv);
    float3 V = N;
    float a = max(Roughness * Roughness, 0.002);
    const uint COUNT = 256u;
    float3 sum = 0.0;
    float wsum = 0.0;
    [loop] for (uint k = 0; k < COUNT; k++)
    {
        float3 H = ImportanceSampleGGX(Hammersley(k, COUNT), a, N);
        float3 L = normalize(2.0 * dot(V, H) * H - V);
        float NoL = dot(N, L);
        if (NoL > 0.0)
        {
            float NoH = saturate(dot(N, H));
            float a2 = a * a;
            float dd = NoH * NoH * (a2 - 1.0) + 1.0;
            float D = a2 / (PI * dd * dd);
            float pdf = D / 4.0 + 1e-5;
            float saTexel = 4.0 * PI / (6.0 * SrcSize * SrcSize);
            float saSample = 1.0 / (COUNT * pdf + 1e-5);
            float mip = Roughness < 0.01 ? 0.0 : 0.5 * log2(saSample / saTexel) + 1.0;
            sum += SrcCube.SampleLevel(LinearClamp, L, mip).rgb * NoL;
            wsum += NoL;
        }
    }
    return float4(sum / max(wsum, 1e-4), 1.0);
}

// ------------------------------------------------------------------ bölünmüş toplam BRDF tablosu (Karis 2013)
float2 PSBrdfLut(FSOut i) : SV_Target0
{
    float NoV = max(i.uv.x, 1e-3);
    float rough = i.uv.y;
    float a = max(rough * rough, 1e-3);
    float3 V = float3(sqrt(1.0 - NoV * NoV), 0.0, NoV);
    float3 N = float3(0, 0, 1);
    float A = 0.0, B = 0.0;
    const uint COUNT = 512u;
    [loop] for (uint k = 0; k < COUNT; k++)
    {
        float3 H = ImportanceSampleGGX(Hammersley(k, COUNT), a, N);
        float3 L = normalize(2.0 * dot(V, H) * H - V);
        float NoL = saturate(L.z);
        float NoH = saturate(H.z);
        float VoH = saturate(dot(V, H));
        if (NoL > 0.0)
        {
            float k2 = a / 2.0;
            float gv = NoV / (NoV * (1.0 - k2) + k2);
            float gl = NoL / (NoL * (1.0 - k2) + k2);
            float G = gv * gl;
            float Gv = G * VoH / (NoH * NoV + 1e-5);
            float Fc = pow(1.0 - VoH, 5.0);
            A += (1.0 - Fc) * Gv;
            B += Fc * Gv;
        }
    }
    return float2(A, B) / COUNT;
}

// ------------------------------------------------------------------ SH9 ışınım (hesaplama gölgelendiricisi, tek grup)
RWStructuredBuffer<float4> ShOut : register(u0);
groupshared float3 Acc[256][9];

[numthreads(256, 1, 1)]
void CSProjectSH(uint tid : SV_GroupIndex)
{
    float3 c[9];
    [unroll] for (int j = 0; j < 9; j++) c[j] = 0.0;
    const uint TOTAL = 256u * 64u;
    [loop] for (uint k = tid; k < TOTAL; k += 256u)
    {
        // Fibonacci küresi üzerinde eşit dağılımlı yönler
        float z = 1.0 - 2.0 * (k + 0.5) / TOTAL;
        float r = sqrt(1.0 - z * z);
        float phi = k * 2.39996323;
        float3 d = float3(r * cos(phi), z, r * sin(phi));
        float3 L = SrcCube.SampleLevel(LinearClamp, d, 2.0).rgb;
        c[0] += L * 0.282095;
        c[1] += L * 0.488603 * d.y;
        c[2] += L * 0.488603 * d.z;
        c[3] += L * 0.488603 * d.x;
        c[4] += L * 1.092548 * d.x * d.y;
        c[5] += L * 1.092548 * d.y * d.z;
        c[6] += L * 0.315392 * (3.0 * d.z * d.z - 1.0);
        c[7] += L * 1.092548 * d.x * d.z;
        c[8] += L * 0.546274 * (d.x * d.x - d.y * d.y);
    }
    [unroll] for (int j2 = 0; j2 < 9; j2++) Acc[tid][j2] = c[j2];
    GroupMemoryBarrierWithGroupSync();
    for (uint s = 128u; s > 0u; s >>= 1u)
    {
        if (tid < s)
        {
            [unroll] for (int j3 = 0; j3 < 9; j3++) Acc[tid][j3] += Acc[tid + s][j3];
        }
        GroupMemoryBarrierWithGroupSync();
    }
    if (tid == 0)
    {
        // ışınım konvolüsyonu (cosinüs lobu) ve 1/π: EvalSH doğrudan albedo ile çarpılabilir
        const float band[9] = { 3.141593, 2.094395, 2.094395, 2.094395, 0.785398, 0.785398, 0.785398, 0.785398, 0.785398 };
        float norm = 4.0 * PI / TOTAL;
        [unroll] for (int j4 = 0; j4 < 9; j4++) ShOut[j4] = float4(Acc[0][j4] * norm * band[j4] / PI, 0.0);
    }
}
