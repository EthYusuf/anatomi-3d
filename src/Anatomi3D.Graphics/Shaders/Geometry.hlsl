// Anatomi 3D — sahne geometrisi: köşe çözme, Phong tesselasyonu, ön geçiş, gölge, aydınlatma, saydamlık
//
// Tanımlar:
//   CUTOUT = 1  çözülme (kazı / katman soyma) ve deri penceresi için piksel atma
//   CAPS   = 1  kesit düzleminin açtığı iç yüzeyleri düz bir kesit kapağı olarak çiz (SV_Depth)

#include "Common.hlsli"

#ifndef CUTOUT
#define CUTOUT 0
#endif
#ifndef CAPS
#define CAPS 0
#endif

Texture2D<float> ShadowMap : register(t2);
Texture2D<float> AOTex : register(t3);
TextureCube<float4> EnvSpecular : register(t4);
Texture2D<float2> BrdfLut : register(t5);
SamplerComparisonState ShadowSampler : register(s0);
SamplerState LinearClamp : register(s1);

#define ENV_MIPS 7.0

struct VSIn
{
    uint4 pos : POSITION;     // xyz: 16 bit nicemlenmiş konum, w: yapı indeksi
    float2 nrm : NORMAL;      // oktahedral normal
    float2 dir : TANGENT;     // oktahedral lif yönü
    float2 occ : TEXCOORD0;   // x: köşe AO, y: kavite
};

struct Surface
{
    float3 posW : WORLDPOS;
    float3 nrmW : NORMAL;
    float3 dirW : TANGENT;
    float2 occ : TEXCOORD0;
    nointerpolation uint part : PART;
};

struct PSIn
{
    float4 posH : SV_Position;
    float3 posW : WORLDPOS;
    float3 nrmW : NORMAL;
    float3 dirW : TANGENT;
    float2 occ : TEXCOORD0;
    nointerpolation uint part : PART;
    float clip : SV_ClipDistance0;
};

Surface Decode(VSIn v)
{
    Surface s;
    uint part = v.pos.w;
    PartState st = PartStates[part];
    float3 p = QuantMin + float3(v.pos.xyz) * QuantScale;
    s.nrmW = OctDecode(v.nrm);
    // kalp atışı / solunum: yapı pivotuna göre ölçek
    p = st.pivot + (p - st.pivot) * st.scale;
    // kas yapışma alanları kemik yüzeyinin hemen üstünde
    if (st.flags & (PF_ATTACH_O | PF_ATTACH_E)) p += s.nrmW * 0.0004;
    s.posW = p;
    s.dirW = OctDecode(v.dir);
    s.occ = v.occ;
    s.part = part;
    return s;
}

PSIn Project(Surface s, float4x4 vp)
{
    PSIn o;
    o.posH = mul(float4(s.posW, 1.0), vp);
    o.posW = s.posW;
    o.nrmW = s.nrmW;
    o.dirW = s.dirW;
    o.occ = s.occ;
    o.part = s.part;
    o.clip = (FrameFlags & FF_CLIP) ? dot(ClipPlane.xyz, s.posW) + ClipPlane.w : 1.0;
    return o;
}

PSIn VSMain(VSIn v) { return Project(Decode(v), ViewProj); }
PSIn VSShadow(VSIn v) { return Project(Decode(v), ShadowViewProj); }

// ------------------------------------------------------------------ Phong tesselasyonu
// Kenar başına tesselasyon katsayısı yalnız kenarın iki uç noktasının ekran uzunluğundan hesaplanır;
// böylece kenarı paylaşan iki üçgen aynı katsayıyı bulur ve çatlak oluşmaz.

Surface VSControl(VSIn v) { return Decode(v); }

struct PatchConst
{
    float edges[3] : SV_TessFactor;
    float inside : SV_InsideTessFactor;
};

float EdgeFactor(float4 a, float4 b)
{
    float2 sa = a.xy / max(a.w, 1e-4) * 0.5 * ViewSize;
    float2 sb = b.xy / max(b.w, 1e-4) * 0.5 * ViewSize;
    return clamp(length(sa - sb) / TessTargetPx, 1.0, TessMaxFactor);
}

bool Outside(float4 a, float4 b, float4 c)
{
    // tümü aynı kırpma düzleminin dışındaysa (tesselasyonla şişme payı %10)
    float m = 1.1;
    if (a.x > a.w * m && b.x > b.w * m && c.x > c.w * m) return true;
    if (a.x < -a.w * m && b.x < -b.w * m && c.x < -c.w * m) return true;
    if (a.y > a.w * m && b.y > b.w * m && c.y > c.w * m) return true;
    if (a.y < -a.w * m && b.y < -b.w * m && c.y < -c.w * m) return true;
    if (a.w < 0 && b.w < 0 && c.w < 0) return true;
    return false;
}

PatchConst HSConst(InputPatch<Surface, 3> p)
{
    PatchConst o;
    float4 h0 = mul(float4(p[0].posW, 1.0), ViewProj);
    float4 h1 = mul(float4(p[1].posW, 1.0), ViewProj);
    float4 h2 = mul(float4(p[2].posW, 1.0), ViewProj);
    if (Outside(h0, h1, h2))
    {
        o.edges[0] = o.edges[1] = o.edges[2] = o.inside = 0.0;
        return o;
    }
    o.edges[0] = EdgeFactor(h1, h2);
    o.edges[1] = EdgeFactor(h2, h0);
    o.edges[2] = EdgeFactor(h0, h1);
    o.inside = (o.edges[0] + o.edges[1] + o.edges[2]) / 3.0;
    return o;
}

[domain("tri")]
[partitioning("fractional_odd")]
[outputtopology("triangle_cw")]
[outputcontrolpoints(3)]
[patchconstantfunc("HSConst")]
[maxtessfactor(16.0)]
Surface HSMain(InputPatch<Surface, 3> p, uint i : SV_OutputControlPointID)
{
    return p[i];
}

float3 ProjectToPlane(float3 q, float3 p, float3 n) { return q - dot(q - p, n) * n; }

[domain("tri")]
PSIn DSMain(PatchConst pc, float3 b : SV_DomainLocation, const OutputPatch<Surface, 3> p)
{
    Surface s;
    float3 q = b.x * p[0].posW + b.y * p[1].posW + b.z * p[2].posW;
    float3 phong = b.x * ProjectToPlane(q, p[0].posW, p[0].nrmW)
                 + b.y * ProjectToPlane(q, p[1].posW, p[1].nrmW)
                 + b.z * ProjectToPlane(q, p[2].posW, p[2].nrmW);
    s.posW = lerp(q, phong, 0.65);
    s.nrmW = normalize(b.x * p[0].nrmW + b.y * p[1].nrmW + b.z * p[2].nrmW);
    s.dirW = b.x * p[0].dirW + b.y * p[1].dirW + b.z * p[2].dirW;
    s.occ = b.x * p[0].occ + b.y * p[1].occ + b.z * p[2].occ;
    s.part = p[0].part;
    return Project(s, ViewProj);
}

// ------------------------------------------------------------------ kesme (çözülme / pencere / kesit kapağı)
float Cutout(PSIn i, PartState st)
{
    float edge = 0.0;
#if CUTOUT
    if (st.flags & PF_WINDOWED)
    {
        float inside = 0.0;
        [loop] for (uint h = 0; h < HoleCount; h++)
            inside = max(inside, step(distance(i.posW, Holes[h].xyz), Holes[h].w));
        if (inside < 0.5) discard;
    }
    if (st.dissolve > 0.0)
    {
        bool sweep = (st.flags & PF_SWEEP) != 0;
        float n = Noise3(i.posW * 70.0) * 0.65 + Noise3(i.posW * 240.0) * 0.35;
        float d = sweep ? dot(i.posW - st.digOrigin, st.digDir) / st.digRadius : distance(i.posW, st.digOrigin) / st.digRadius;
        float v = d + (n - 0.5) * (sweep ? 0.12 : 0.5);
        float front = st.dissolve * 1.45 - 0.2;
        if (v < front) discard;
        edge = 1.0 - smoothstep(front, front + (sweep ? 0.025 : 0.08), v);
    }
#endif
    return edge;
}

// Arka yüz kesit düzlemi ardında görünüyorsa: bakış ışınının düzlemi kestiği nokta (kapak)
bool CapPoint(float3 posW, out float3 capPos)
{
    float3 rd = posW - CameraPos;
    float tBack = length(rd);
    rd /= tBack;
    float denom = dot(ClipPlane.xyz, rd);
    float t = -(dot(ClipPlane.xyz, CameraPos) + ClipPlane.w) / (abs(denom) < 1e-6 ? 1e-6 : denom);
    capPos = CameraPos + rd * t;
    return t > 0.0 && t < tBack;
}

float DepthOf(float3 posW)
{
    float4 h = mul(float4(posW, 1.0), ViewProj);
    return h.z / h.w;
}

// ------------------------------------------------------------------ prosedürel yüzey ayrıntısı
// Doku dosyası kullanılmaz; dünya koordinatlı gürültü + ekran türevleriyle kabartma. Frekans piksel başına
// yarım döngüyü aşınca o oktav söndürülür (prosedürel örtüşme önleme).

struct Detail
{
    float3 tint;
    float rough;
    float height;       // metre cinsinden kabartma yüksekliği
    float override;
    float3 overColor;
};

float Octave(float freq, float footprint) { return 1.0 - smoothstep(0.2, 0.55, freq * footprint); }

Detail ComputeDetail(PartMaterial m, float3 posW, float3 N, float3 dirW)
{
    Detail d;
    d.tint = 1.0;
    d.rough = 1.0;
    d.height = 0.0;
    d.override = 0.0;
    d.overColor = 0.0;
    if (!(FrameFlags & FF_DETAIL) || m.family == 0) return d;

    // bir pikselin p uzayındaki boyu (dünya ayak izi × frekans çarpanı)
    float footprint = max(length(ddx(posW)), length(ddy(posW))) * m.freq;
    float3 p = (posW - m.center) * m.freq;
    float amp = m.bump * DetailStrength;

    [branch] if (m.family == 1)
    {
        // LİF: kas (1), tendon (2), sinir (3), damar (4), kalp kası (5)
        float3 ax = normalize(dirW + 1e-5);
        float squash = m.kind == 4 ? 0.97 : 0.93;
        float3 sp = p - ax * dot(p, ax) * squash;
        float fineF = m.kind == 1 ? 820.0 : m.kind == 2 ? 1500.0 : m.kind == 3 ? 1100.0 : m.kind == 5 ? 650.0 : 820.0;
        float o1 = Octave(fineF, footprint);
        float o2 = Octave(230.0, footprint);
        float f1 = lerp(0.5, Noise3(sp * fineF), o1);
        float f2 = lerp(0.5, Noise3(sp * 230.0 + 7.0), o2);
        float h = f1 * 0.6 + f2 * 0.4;
        float3 lo = m.kind == 1 || m.kind == 5 ? float3(0.72, 0.66, 0.66) : m.kind == 2 ? float3(0.9, 0.9, 0.88) : m.kind == 3 ? float3(0.84, 0.8, 0.72) : float3(0.9, 0.9, 0.9);
        float3 hi = m.kind == 1 || m.kind == 5 ? float3(1.12, 1.06, 1.04) : m.kind == 2 ? float3(1.05, 1.05, 1.04) : float3(1.07, 1.06, 1.05);
        d.tint = lerp(lo, hi, m.kind == 4 ? f2 : h);
        d.rough = m.kind <= 2 ? lerp(1.18, 0.8, f1) : 1.0;
        d.height = h * 0.00006 * amp;
    }
    else if (m.family == 2)
    {
        // KEMİK: gözenekler, lamel doku, fildişi renk dalgalanması
        float o1 = Octave(900.0, footprint), o2 = Octave(1700.0, footprint), o3 = Octave(300.0, footprint);
        float pore = lerp(0.4, Noise3(p * 900.0), o1);
        float grain = lerp(0.5, Noise3(p * 1700.0), o2);
        float low = Noise3(p * 22.0);
        float mid = lerp(0.5, Noise3(p * 300.0), o3);
        float pit = smoothstep(0.7, 0.9, pore) * o1;
        d.height = (mid * 0.5 + grain * 0.3 - pit * 0.35) * 0.00006 * amp;
        d.tint = lerp(float3(1.03, 1.01, 0.98), float3(0.92, 0.85, 0.72), low * 0.8) * (1.0 - pit * 0.12);
        d.rough = lerp(0.86, 1.16, grain);
    }
    else if (m.family == 3)
    {
        // DERİ: gözenekler, ince kırışıklık, kılcal kızarıklık, melanin dalgalanması
        float o1 = Octave(2600.0, footprint), o2 = Octave(700.0, footprint);
        float pores = lerp(0.3, Noise3(p * 2600.0), o1);
        float fine = lerp(0.5, Noise3(p * 700.0), o2);
        float mott = Noise3(p * 16.0);
        float red = Noise3(p * 45.0 + 3.0);
        d.height = (fine * 0.5 + (1.0 - smoothstep(0.55, 0.75, pores)) * 0.5) * 0.00003 * amp;
        d.tint = lerp(1.0, float3(1.05, 0.9, 0.86), red * 0.5) * lerp(0.95, 1.04, mott);
        d.rough = lerp(0.9, 1.12, pores);
    }
    else if (m.family == 4)
    {
        // ISLAK DOKU: mukoza / organ yüzeyi (1), beyin yüzeyi ve pia damarları (2)
        float o1 = Octave(220.0, footprint), o2 = Octave(800.0, footprint);
        float a = lerp(0.5, Noise3(p * 220.0), o1);
        float b = lerp(0.5, Noise3(p * 800.0), o2);
        float c = Noise3(p * 30.0);
        d.height = (a * 0.65 + b * 0.35) * 0.00005 * amp;
        d.tint = lerp(0.9, 1.07, a * 0.6 + c * 0.4);
        d.rough = lerp(0.72, 1.22, b);
        if (m.kind == 2)
        {
            float ves = 1.0 - smoothstep(0.0, 0.025, abs(c - 0.5));
            d.tint *= lerp(1.0, float3(1.0, 0.55, 0.5), ves * 0.35);
        }
    }
    else if (m.family == 5)
    {
        float3 ax = normalize(m.axis);
        if (m.kind == 1)
        {
            // İRİS: göz bebeği, radyal stroma lifleri, kriptler, kollaret, limbal halka
            float3 rel = posW - m.center;
            float3 t = normalize(cross(ax, abs(ax.y) < 0.9 ? float3(0, 1, 0) : float3(1, 0, 0)));
            float3 b = cross(ax, t);
            float2 q = float2(dot(rel, t), dot(rel, b));
            float r = length(q) / m.param;
            float ang = atan2(q.y, q.x);
            float2 apv = float2(cos(ang), sin(ang));
            float fib = Noise3(float3(apv * 9.0, r * 7.0));
            float streak = Noise3(float3(apv * 28.0, r * 2.5 + 11.0));
            float crypt = smoothstep(0.62, 0.8, Noise3(float3(apv * 7.0, r * 5.0 + 3.0)));
            float3 dark = float3(0.09, 0.04, 0.015), midc = float3(0.33, 0.16, 0.05), light = float3(0.62, 0.40, 0.13);
            float3 col = lerp(dark, midc, fib);
            col = lerp(col, light, streak * 0.6 * smoothstep(0.35, 0.6, r) * (1.0 - smoothstep(0.75, 0.95, r)));
            col *= 1.0 - crypt * 0.35;
            col = lerp(col, light * 1.1, (1.0 - smoothstep(0.0, 0.06, abs(r - 0.52))) * 0.45);
            col *= lerp(1.0, 0.22, smoothstep(0.78, 1.0, r));
            float pupil = smoothstep(0.30, 0.345, r);
            d.override = 1.0;
            d.overColor = lerp(float3(0.004, 0.004, 0.004), col, pupil);
            d.height = (fib * 0.6 + streak * 0.4) * pupil * 0.00004;
            d.rough = lerp(0.3, 1.0, pupil);
        }
        else
        {
            // SKLERA: kenarlarda belirginleşen ince kılcal damarlar; param = limbus kosinüsü
            float fwd = dot(normalize(posW - m.center), ax);
            if (fwd > m.param) discard;
            float o1 = Octave(380.0, footprint), o2 = Octave(950.0, footprint);
            float v1 = abs(Noise3(p * 380.0) - 0.5);
            float v2 = abs(Noise3(p * 950.0 + 5.0) - 0.5);
            float veins = (1.0 - smoothstep(0.0, 0.03, v1)) * 0.75 * o1 + (1.0 - smoothstep(0.0, 0.018, v2)) * 0.45 * o2;
            veins *= 1.0 - smoothstep(0.1, 0.75, fwd);
            d.tint = lerp(float3(1.0, 0.985, 0.96), float3(0.8, 0.25, 0.24), saturate(veins) * 0.6);
            d.tint *= lerp(1.0, float3(0.55, 0.58, 0.66), smoothstep(m.param - 0.08, m.param, fwd));
            d.height = veins * 0.00002;
        }
    }
    return d;
}

// Mikkelsen (2010): parametrelenmemiş yüzeyde kabartma — yükseklik dünya birimindedir
float3 PerturbNormal(float3 posW, float3 N, float height)
{
    float3 sx = ddx(posW);
    float3 sy = ddy(posW);
    float3 r1 = cross(sy, N);
    float3 r2 = cross(N, sx);
    float det = dot(sx, r1);
    float2 dh = float2(ddx(height), ddy(height));
    float3 grad = sign(det) * (dh.x * r1 + dh.y * r2);
    float3 n = abs(det) * N - grad;
    float l = length(n);
    return l > 1e-12 ? n / l : N;
}

// ------------------------------------------------------------------ gölge
static const float2 Poisson[12] =
{
    float2(-0.326, -0.406), float2(-0.840, -0.074), float2(-0.696, 0.457), float2(-0.203, 0.621),
    float2(0.962, -0.195), float2(0.473, -0.480), float2(0.519, 0.767), float2(0.185, -0.893),
    float2(0.507, 0.064), float2(0.896, 0.412), float2(-0.322, -0.933), float2(-0.792, -0.598),
};

float Shadow(float3 posW, float3 N, float2 pix)
{
    if (!(FrameFlags & FF_SHADOWS)) return 1.0;
    // normal yönünde kaydırma (gölge sivilcesini önler) + döndürülmüş Poisson PCF (yumuşak kenar)
    float3 p = posW + N * ShadowNormalOffset;
    float4 s = mul(float4(p, 1.0), ShadowViewProj);
    float2 uv = s.xy * float2(0.5, -0.5) + 0.5;
    if (any(uv < 0.0) || any(uv > 1.0)) return 1.0;
    float z = s.z - 0.0004;
    float a = InterleavedGradient(pix) * 6.2831853;
    float2x2 rot = float2x2(cos(a), -sin(a), sin(a), cos(a));
    float sum = 0.0;
    [unroll] for (int k = 0; k < 12; k++)
    {
        float2 o = mul(Poisson[k], rot) * ShadowTexel * 2.5;
        sum += ShadowMap.SampleCmpLevelZero(ShadowSampler, uv + o, z);
    }
    return lerp(1.0, sum / 12.0, ShadowStrength);
}

// ------------------------------------------------------------------ aydınlatma
struct Shade
{
    float3 albedo;
    float rough;
    float3 N;
    float clearcoat;
    float3 sheen;
    float subsurface;
    float ao;
    float specOcc;
};

float3 DirectLight(Shade m, float3 V, float3 L, float3 radiance, float shadow)
{
    float3 N = m.N;
    float NoL = dot(N, L);
    float NoV = saturate(dot(N, V)) + 1e-4;
    float3 H = normalize(L + V);
    float NoH = saturate(dot(N, H));
    float VoH = saturate(dot(V, H));
    float cl = saturate(NoL);

    // yaygın: sarma ile deri altı saçılım yaklaşımı (sonlandırıcıda kırmızıya kayan ışık)
    float w = m.subsurface;
    float wrapped = saturate((NoL + w) / (1.0 + w));
    float3 scatter = saturate(wrapped - cl) * float3(0.95, 0.42, 0.32) * (w > 0 ? 1.0 : 0.0);
    float3 diffuse = m.albedo / PI * (cl + scatter * 1.1) * shadow;

    // tabaka: GGX (doku F0 ~ 0.028)
    float a = m.rough * m.rough;
    float3 F = F_Schlick(0.028, VoH);
    float3 spec = D_GGX(NoH, a) * V_SmithGGXCorrelated(NoV, cl, a) * F * cl * shadow;

    // ıslak katman: ikinci, keskin GGX
    float ac = 0.2 * 0.2;
    float Fc = F_Schlick(0.04, VoH).x * m.clearcoat;
    float coat = D_GGX(NoH, ac) * V_SmithGGXCorrelated(NoV, cl, ac) * Fc * cl * shadow;

    // kadife parıltı
    float3 sheen = m.sheen * D_Charlie(NoH, 0.45) * 0.25 * cl * shadow;

    return ((diffuse + spec) * (1.0 - Fc) + coat + sheen) * radiance;
}

float3 AmbientLight(Shade m, float3 V)
{
    float3 N = m.N;
    float NoV = saturate(dot(N, V));
    float2 brdf = BrdfLut.SampleLevel(LinearClamp, float2(NoV, m.rough), 0);
    float3 R = reflect(-V, N);
    float3 pre = EnvSpecular.SampleLevel(LinearClamp, ToEnv(R), m.rough * (ENV_MIPS - 1.0)).rgb;
    float3 spec = pre * (0.028 * brdf.x + brdf.y) * m.specOcc;
    float3 preC = EnvSpecular.SampleLevel(LinearClamp, ToEnv(R), 1.6).rgb;
    float Fc = (0.04 + 0.96 * pow(1.0 - NoV, 5.0)) * m.clearcoat;
    float3 diffuse = EvalSH(ToEnv(N)) * m.albedo * m.ao;
    // deri altı saçılımı ortam ışığında da biraz renk verir
    diffuse *= lerp(1.0, float3(1.08, 0.94, 0.9), m.subsurface);
    return ((diffuse + spec) * (1.0 - Fc) + preC * Fc * m.specOcc + m.sheen * 0.08 * m.ao) * EnvIntensity;
}

struct LitResult
{
    float3 color;
    float3 N;
};

LitResult Light(PSIn i, bool front, float3 posW, float3 Ngeo, bool isCap, float edge)
{
    PartState st = PartStates[i.part];
    PartMaterial m = PartMaterials[i.part];
    float3 V = normalize(CameraPos - posW);
    float3 N = Ngeo;

    Detail d = ComputeDetail(m, posW, N, i.dirW);
    if (!isCap) N = PerturbNormal(posW, N, d.height);
    else N = Ngeo;

    // speküler örtüşme önleme: normal değişimine göre pürüzlülüğü artır (Kaplanyan)
    float3 dn = fwidth(N);
    float variance = saturate(dot(dn, dn) * SpecularAA);
    float rough = saturate(m.roughness * d.rough);
    rough = sqrt(saturate(rough * rough + variance));
    rough = clamp(rough, 0.04, 1.0);

    Shade s;
    s.albedo = lerp(m.albedo * d.tint, d.overColor, d.override);
    if (isCap)
    {
        // kesit yüzeyi: derinin kapağı vücut içindeki yağ / bağ dokusunu temsil eder
        if (m.family == 3) s.albedo = float3(0.74, 0.5, 0.16) * lerp(0.9, 1.05, Noise3(posW * 180.0));
        else s.albedo *= 0.85;
    }
    s.rough = isCap ? 0.75 : rough;
    s.N = N;
    s.clearcoat = isCap ? 0.0 : m.clearcoat;
    s.sheen = m.sheen;
    s.subsurface = m.subsurface;

    // ortam kapatması: ekran uzayı AO × köşe AO × kavite
    float ssao = (FrameFlags & FF_SSAO) ? AOTex.SampleLevel(LinearClamp, i.posH.xy * InvViewSize, 0) : 1.0;
    float cav = i.occ.y * 2.0 - 1.0;
    float vao = i.occ.x * (1.0 - saturate(cav) * 0.45);
    float ao = ssao * vao;
    float NoV = saturate(dot(N, V));
    s.ao = ao;
    s.specOcc = saturate(pow(NoV + ao, exp2(-16.0 * s.rough - 1.0)) - 1.0 + ao);

    float shadow = Shadow(posW, Ngeo, i.posH.xy);
    float3 c = DirectLight(s, V, KeyDir, KeyColor * KeyIntensity, shadow);
    c += DirectLight(s, V, FillDir, FillColor * FillIntensity, 1.0) * lerp(1.0, ao, 0.5);
    c += DirectLight(s, V, RimDir, RimColor * RimIntensity, 1.0) * lerp(1.0, ao, 0.5);
    c += AmbientLight(s, V);

    // sırt vurgusu: dışbükey kenarlar hafifçe aydınlanır (kavite haritası)
    c *= 1.0 + saturate(-cav) * 0.12;

    // seçim / üzerine gelme / quiz: Fresnel kenar parıltısı + hafif ton
    float fres = pow(1.0 - NoV, 2.5);
    if (st.tintAmount > 0.0)
    {
        c = lerp(c, c * 0.6 + st.tint * 0.25 * Luma(c + 0.2), st.tintAmount * 0.35);
        c += st.tint * (fres * 1.6 + 0.12) * st.tintAmount;
    }
    // kas yapışma alanları: origo kırmızı, insersiyo mavi
    if (st.flags & PF_ATTACH_O) c = lerp(c, float3(0.95, 0.12, 0.1) * (0.35 + Luma(c)), 0.8);
    if (st.flags & PF_ATTACH_E) c = lerp(c, float3(0.1, 0.35, 1.0) * (0.35 + Luma(c)), 0.8);

    // çözülme kenarı: parlak (bloom'a taşan) kenar
    c = lerp(c, c * 0.5, edge * 0.6) + EdgeColor * edge * 4.0;
    c += m.albedo * st.emissive;

    LitResult r;
    r.color = c * Exposure;
    r.N = N;
    return r;
}

// ------------------------------------------------------------------ ön geçiş: derinlik + görünüm normali + yapı kimliği
struct PrepassOut
{
    float2 normal : SV_Target0;
    uint id : SV_Target1;
#if CAPS
    float depth : SV_Depth;
#endif
};

PrepassOut PSPrepass(PSIn i, bool front : SV_IsFrontFace)
{
    PrepassOut o;
    PartState st = PartStates[i.part];
    Cutout(i, st);
    float3 N = normalize(i.nrmW);
#if CAPS
    o.depth = i.posH.z;
    float3 cap;
    if (!front && (FrameFlags & FF_CLIP) && CapPoint(i.posW, cap))
    {
        N = -ClipPlane.xyz;
        // deri kapağı (vücut içi dolgu) düzlemde değil arka yüzeyde kalır: kapalı olmayan iç yapılar önde görünür
        o.depth = PartMaterials[i.part].family == 3 ? i.posH.z : DepthOf(cap + normalize(cap - CameraPos) * st.capBias);
    }
    else if (!front) N = -N;
#else
    if (!front) N = -N;
#endif
    o.normal = OctEncode(normalize(mul(float4(N, 0.0), View).xyz));
    o.id = i.part + 1;
    return o;
}

void PSShadow(PSIn i)
{
    Cutout(i, PartStates[i.part]);
}

// ------------------------------------------------------------------ ana (opak) geçiş
struct MainOut
{
    float4 color : SV_Target0;
#if CAPS
    float depth : SV_Depth;
#endif
};

MainOut PSMain(PSIn i, bool front : SV_IsFrontFace)
{
    MainOut o;
    PartState st = PartStates[i.part];
    float edge = Cutout(i, st);
    float3 N = normalize(i.nrmW);
    float3 posW = i.posW;
    bool isCap = false;
#if CAPS
    o.depth = i.posH.z;
    float3 cap;
    if (!front && (FrameFlags & FF_CLIP) && CapPoint(i.posW, cap))
    {
        isCap = true;
        posW = cap;
        N = -ClipPlane.xyz;
        o.depth = PartMaterials[i.part].family == 3 ? i.posH.z : DepthOf(cap + normalize(cap - CameraPos) * st.capBias);
    }
    else if (!front) N = -N;
#else
    if (!front) N = -N;
#endif
    LitResult r = Light(i, front, posW, N, isCap, edge);
    o.color = float4(r.color, 1.0);
    return o;
}

// ------------------------------------------------------------------ hayalet (X-ray) ve cam: ağırlıklı karışımlı sıra bağımsız saydamlık
struct OITOut
{
    float4 accum : SV_Target0;
    float reveal : SV_Target1;
};

OITOut PSGhost(PSIn i, bool front : SV_IsFrontFace)
{
    PartState st = PartStates[i.part];
    PartMaterial m = PartMaterials[i.part];
    float edge = Cutout(i, st);
    float3 N = normalize(front ? i.nrmW : -i.nrmW);
    float3 V = normalize(CameraPos - i.posW);
    float NoV = saturate(dot(N, V));
    float fres = pow(1.0 - NoV, 2.0);
    float3 color;
    float a;
    if (m.family == 6)
    {
        // cam (kornea, lens): yansıma ağırlıklı
        float3 R = reflect(-V, N);
        float3 env = EnvSpecular.SampleLevel(LinearClamp, ToEnv(R), 0.3).rgb * EnvIntensity;
        float F = 0.04 + 0.96 * pow(1.0 - NoV, 5.0);
        color = env * 1.6 + m.albedo * 0.05;
        a = saturate(st.opacity * 0.35 + F * 0.9) * (st.opacity > 0.0 ? 1.0 : 0.0);
    }
    else
    {
        float key = saturate(dot(N, KeyDir)) * 0.55 + 0.45;
        color = m.albedo * key * (0.55 + 0.9 * fres) * Exposure;
        color += st.tint * st.tintAmount * (0.4 + fres);
        a = st.opacity * GhostOpacityScale * (0.25 + 0.75 * fres);
    }
    color += EdgeColor * edge * 3.0;
    a = saturate(a + edge * 0.5);
    float z = distance(i.posW, CameraPos);
    float w = clamp(pow(min(1.0, a * 10.0) + 0.01, 3.0) * 1e3 * pow(1.0 - saturate(z / 12.0) * 0.9, 3.0), 1e-2, 3e3);
    OITOut o;
    o.accum = float4(color * a, a) * w;
    o.reveal = a;
    return o;
}

// seçim maskesi (dış çizgi için)
float PSMask(PSIn i) : SV_Target0
{
    Cutout(i, PartStates[i.part]);
    return 1.0;
}
