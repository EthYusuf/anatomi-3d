import * as THREE from 'three';

/**
 * Anatomi materyal eklentisi (onBeforeCompile ile standart/fiziksel/Lambert materyale eklenir).
 *
 * 1) Çözülme: yapı kaldırılırken gürültülü bir cephe ilerler, cephenin gerisindeki pikseller atılır,
 *    cephe boyunca parlak bir kenar çizilir.
 *    - uMode 0: tıklanan noktadan (uOrigin) dışa yayılır (çift tıklama ile kazı)
 *    - uMode 1: bir düzlem boyunca tarar (uDir yönünde; katman soyma, göster/gizle, açılış)
 * 2) Pencere: deri kapalıyken iç yapılar yalnız deride açılan kürelerin (HOLES) içinde çizilir.
 * 3) Prosedürel yüzey: doku dosyası olmadan dünya koordinatlı gürültüyle kas lifi, kemik gözeneği,
 *    deri, ıslak mukoza, iris ve sklera ayrıntısı; ekran uzayı türevleriyle kabartma (bump).
 *    Ayrıntı yakından görünür, uzaklaştıkça titreşmesin diye söner.
 */

export const MAX_HOLES = 16;

/** Tüm materyallerin paylaştığı pencere uniform'ları */
export const HOLES = {
  uHoles: { value: Array.from({ length: MAX_HOLES }, () => new THREE.Vector4()) },
  uHoleCount: { value: 0 },
};

export interface DissolveUniforms {
  uDissolve: { value: number };
  uMode: { value: number };
  uOrigin: { value: THREE.Vector3 };
  uDir: { value: THREE.Vector3 };
  uRadius: { value: number };
  uEdgeColor: { value: THREE.Color };
  uWindowed: { value: number };
  uDetail: { value: number };
  uCenter: { value: THREE.Vector3 };
  uScale: { value: number };
  uBump: { value: number };
  uFreq: { value: number };
  /** Kadife kenar ışığı rengi × şiddeti (fiziksel sheen'in ucuz karşılığı) */
  uSheen: { value: THREE.Color };
}

export function createDissolveUniforms(): DissolveUniforms {
  return {
    uDissolve: { value: 0 },
    uMode: { value: 0 },
    uOrigin: { value: new THREE.Vector3() },
    uDir: { value: new THREE.Vector3(0, -1, 0) },
    uRadius: { value: 0.1 },
    uEdgeColor: { value: new THREE.Color('#6fd0ff') },
    uWindowed: { value: 0 },
    uDetail: { value: 0 },
    uCenter: { value: new THREE.Vector3() },
    uScale: { value: 0.01 },
    uBump: { value: 0 },
    uFreq: { value: 1 },
    uSheen: { value: new THREE.Color(0, 0, 0) },
  };
}

const VERT_HEAD = /* glsl */ `
attribute vec3 aAxis;
varying vec3 vDPos;
varying vec3 vAxis;
`;

const VERT_BODY = /* glsl */ `
#include <project_vertex>
vec4 dWorld = vec4(transformed, 1.0);
#ifdef USE_BATCHING
dWorld = batchingMatrix * dWorld;
#endif
vDPos = (modelMatrix * dWorld).xyz;
vAxis = aAxis;
`;

const FRAG_HEAD = /* glsl */ `
uniform float uDissolve;
uniform float uMode;
uniform vec3 uOrigin;
uniform vec3 uDir;
uniform float uRadius;
uniform vec3 uEdgeColor;
uniform float uWindowed;
uniform vec4 uHoles[${MAX_HOLES}];
uniform int uHoleCount;
uniform float uDetail;
uniform vec3 uCenter;
uniform float uScale;
uniform float uBump;
uniform float uFreq;
uniform vec3 uSheen;
varying vec3 vDPos;
varying vec3 vAxis;

float dHash(vec3 p) {
  p = fract(p * 0.3183099 + 0.1);
  p *= 17.0;
  return fract(p.x * p.y * p.z * (p.x + p.y + p.z));
}
float dNoise(vec3 x) {
  vec3 i = floor(x);
  vec3 f = fract(x);
  f = f * f * (3.0 - 2.0 * f);
  return mix(
    mix(mix(dHash(i), dHash(i + vec3(1, 0, 0)), f.x), mix(dHash(i + vec3(0, 1, 0)), dHash(i + vec3(1, 1, 0)), f.x), f.y),
    mix(mix(dHash(i + vec3(0, 0, 1)), dHash(i + vec3(1, 0, 1)), f.x), mix(dHash(i + vec3(0, 1, 1)), dHash(i + vec3(1, 1, 1)), f.x), f.y),
    f.z);
}
vec3 dPerturb(vec3 surfPos, vec3 surfNorm, vec2 dHdxy, float faceDir) {
  vec3 sx = normalize(dFdx(surfPos));
  vec3 sy = normalize(dFdy(surfPos));
  vec3 r1 = cross(sy, surfNorm);
  vec3 r2 = cross(surfNorm, sx);
  float det = dot(sx, r1) * faceDir;
  vec3 grad = sign(det) * (dHdxy.x * r1 + dHdxy.y * r2);
  return normalize(abs(det) * surfNorm - grad);
}
`;

const FRAG_CLIP = /* glsl */ `
#include <clipping_planes_fragment>
float dEdge = 0.0;
if (uWindowed > 0.5) {
  // Dalsız döngü (break/discard yok): D3D derleyicisinin kodu şişirmesini önler
  float inside = 0.0;
  for (int i = 0; i < ${MAX_HOLES}; i++) {
    float holeOn = i < uHoleCount ? 1.0 : 0.0;
    inside = max(inside, holeOn * step(distance(vDPos, uHoles[i].xyz), uHoles[i].w));
  }
  if (inside < 0.5) discard;
}
if (uDissolve > 0.0) {
  float n = dNoise(vDPos * 70.0) * 0.65 + dNoise(vDPos * 240.0) * 0.35;
  float d = uMode > 0.5 ? dot(vDPos - uOrigin, uDir) / uRadius : distance(vDPos, uOrigin) / uRadius;
  float v = d + (n - 0.5) * (uMode > 0.5 ? 0.12 : 0.5);
  float front = uDissolve * 1.45 - 0.2;
  if (v < front) discard;
  dEdge = 1.0 - smoothstep(front, front + (uMode > 0.5 ? 0.025 : 0.08), v);
}

// ---- prosedürel yüzey ayrıntısı
// Her materyal yalnız kendi ailesinin kodunu derler (ANATOMY_FAMILY): dallanma yok, program küçük.
float dH = 0.0;
vec3 dTint = vec3(1.0);
float dRough = 1.0;
float dOverride = 0.0;
vec3 dOverColor = vec3(0.0);
float dFade = 1.0 - smoothstep(0.45, 2.2, length(vViewPosition));
#if defined(ANATOMY_FAMILY) && ANATOMY_FAMILY > 0
vec3 p = vDPos * uFreq;
vec3 ax = normalize(vAxis);
#endif

#if defined(ANATOMY_FAMILY) && ANATOMY_FAMILY == 1
// LİF ailesi: kas (1), tendon (2), sinir (9), damar (10) — yapının ana ekseni boyunca uzatılmış gürültü
{
  float kind = uDetail;
  float fineF = kind < 1.5 ? 820.0 : kind < 2.5 ? 1500.0 : kind < 9.5 ? 1100.0 : 820.0;
  float squash = kind > 9.5 ? 0.97 : 0.93;
  vec3 sp = p - ax * dot(p, ax) * squash;
  float f1 = dNoise(sp * fineF);
  float f2 = dNoise(sp * 230.0 + 7.0);
  dH = f1 * 0.6 + f2 * 0.4;
  vec3 lo = kind < 1.5 ? vec3(0.7, 0.64, 0.64) : kind < 2.5 ? vec3(0.88) : kind < 9.5 ? vec3(0.82, 0.78, 0.7) : vec3(0.9);
  vec3 hi = kind < 1.5 ? vec3(1.14, 1.07, 1.05) : kind < 2.5 ? vec3(1.06) : kind < 9.5 ? vec3(1.08) : vec3(1.07);
  dTint = mix(lo, hi, kind > 9.5 ? f2 : dH);
  dRough = kind < 2.5 ? mix(1.2, 0.78, f1) : 1.0;
}
#elif defined(ANATOMY_FAMILY) && ANATOMY_FAMILY == 2
// KEMİK: gözenekler, lameller doku, fildişi renk dalgalanması
{
  float pore = dNoise(p * 900.0);
  float grain = dNoise(p * 1700.0);
  float low = dNoise(p * 22.0);
  float mid = dNoise(p * 300.0);
  float pit = smoothstep(0.6, 0.85, pore);
  dH = mid * 0.5 + grain * 0.3 - pit * 0.45;
  dTint = mix(vec3(1.03, 1.01, 0.98), vec3(0.92, 0.84, 0.68), low * 0.85) * (1.0 - pit * 0.22);
  dRough = mix(0.85, 1.18, grain);
}
#elif defined(ANATOMY_FAMILY) && ANATOMY_FAMILY == 3
// DERİ: gözenekler, ince kırışıklık, kılcal damar kızarıklığı
{
  float pores = dNoise(p * 2600.0);
  float fine = dNoise(p * 700.0);
  float mott = dNoise(p * 16.0);
  float red = dNoise(p * 45.0 + 3.0);
  dH = fine * 0.5 + (1.0 - smoothstep(0.55, 0.75, pores)) * 0.5;
  dTint = mix(vec3(1.0), vec3(1.06, 0.88, 0.84), red * 0.55) * mix(0.95, 1.04, mott);
  dRough = mix(0.88, 1.12, pores);
}
#elif defined(ANATOMY_FAMILY) && ANATOMY_FAMILY == 4
// ISLAK DOKU: mukoza / organ yüzeyi (5), beyin yüzeyi ve pia damarları (8)
{
  float a = dNoise(p * 220.0);
  float b = dNoise(p * 800.0);
  float c = dNoise(p * 30.0);
  dH = a * 0.65 + b * 0.35;
  dTint = vec3(mix(0.88, 1.08, a * 0.6 + c * 0.4));
  dRough = mix(0.7, 1.25, b);
  if (uDetail > 7.5) {
    float ves = 1.0 - smoothstep(0.0, 0.025, abs(c - 0.5));
    dTint *= mix(vec3(1.0), vec3(1.0, 0.55, 0.5), ves * 0.35);
  }
}
#elif defined(ANATOMY_FAMILY) && ANATOMY_FAMILY == 5
// GÖZ: iris (6) ve sklera (7)
if (uDetail < 6.5) {
  // iris: göz bebeği, radyal stroma lifleri, kriptler, kollaret ve limbal halka
  vec3 rel = vDPos - uCenter;
  vec3 t = normalize(cross(ax, abs(ax.y) < 0.9 ? vec3(0.0, 1.0, 0.0) : vec3(1.0, 0.0, 0.0)));
  vec3 b = cross(ax, t);
  vec2 q = vec2(dot(rel, t), dot(rel, b));
  float r = length(q) / uScale;
  float ang = atan(q.y, q.x);
  vec2 ap = vec2(cos(ang), sin(ang));
  float fib = dNoise(vec3(ap * 9.0, r * 7.0));
  float streak = dNoise(vec3(ap * 28.0, r * 2.5 + 11.0));
  float crypt = smoothstep(0.62, 0.8, dNoise(vec3(ap * 7.0, r * 5.0 + 3.0)));
  vec3 dark = vec3(0.09, 0.04, 0.015);
  vec3 midc = vec3(0.33, 0.16, 0.05);
  vec3 light = vec3(0.62, 0.40, 0.13);
  vec3 col = mix(dark, midc, fib);
  col = mix(col, light, streak * 0.6 * smoothstep(0.35, 0.6, r) * (1.0 - smoothstep(0.75, 0.95, r)));
  col *= 1.0 - crypt * 0.35;
  col = mix(col, light * 1.1, (1.0 - smoothstep(0.0, 0.06, abs(r - 0.52))) * 0.45);
  col *= mix(1.0, 0.22, smoothstep(0.78, 1.0, r));
  float pupil = smoothstep(0.30, 0.345, r);
  dOverride = 1.0;
  dOverColor = mix(vec3(0.004), col, pupil);
  dH = (fib * 0.6 + streak * 0.4) * pupil;
  dRough = mix(0.3, 1.0, pupil);
  dFade = 1.0;
} else {
  // sklera: kenarlarda belirginleşen ince kılcal damarlar; uCenter göz küresi merkezi, uScale limbus (cos)
  float fwd = dot(normalize(vDPos - uCenter), ax);
  if (fwd > uScale) discard;
  float v1 = abs(dNoise(p * 380.0) - 0.5);
  float v2 = abs(dNoise(p * 950.0 + 5.0) - 0.5);
  float veins = (1.0 - smoothstep(0.0, 0.03, v1)) * 0.75 + (1.0 - smoothstep(0.0, 0.018, v2)) * 0.45;
  veins *= 1.0 - smoothstep(0.1, 0.75, fwd);
  dTint = mix(vec3(1.0, 0.985, 0.96), vec3(0.8, 0.25, 0.24), clamp(veins, 0.0, 1.0) * 0.6);
  dTint *= mix(vec3(1.0), vec3(0.55, 0.58, 0.66), smoothstep(uScale - 0.08, uScale, fwd));
  dH = veins * 0.25;
  dFade = max(dFade, 0.6);
}
#endif
#if defined(ANATOMY_FAMILY) && ANATOMY_FAMILY > 0
if (dOverride < 0.5) dTint = mix(vec3(1.0), dTint, 0.4 + 0.6 * dFade);
#endif
`;

const FRAG_COLOR = /* glsl */ `
#include <color_fragment>
diffuseColor.rgb = mix(diffuseColor.rgb * dTint, dOverColor, dOverride);
diffuseColor.rgb = mix(diffuseColor.rgb, diffuseColor.rgb * 0.5, dEdge * 0.6);
`;

const FRAG_ROUGH = /* glsl */ `
#include <roughnessmap_fragment>
roughnessFactor = clamp(roughnessFactor * dRough, 0.03, 1.0);
`;

const FRAG_NORMAL = /* glsl */ `
#include <normal_fragment_maps>
#if defined(ANATOMY_FAMILY) && ANATOMY_FAMILY > 0
normal = dPerturb(-vViewPosition, normal, vec2(dFdx(dH), dFdy(dH)) * uBump * dFade * 0.5, faceDirection);
#endif
`;

const FRAG_EMISSIVE = /* glsl */ `
#include <emissivemap_fragment>
totalEmissiveRadiance += uEdgeColor * dEdge * 2.6;
// kenar ışığı: bakış açısı yüzeye yataylaştıkça hafif, renkli parlama (kas lifi / deri altı saçılımı hissi)
float dRim = 1.0 - clamp(dot(normal, normalize(vViewPosition)), 0.0, 1.0);
totalEmissiveRadiance += uSheen * (dRim * dRim * dRim);
`;

/** Prosedürel doku ailesi: her aile ayrı ve küçük bir shader programı olarak derlenir */
export const FAMILY = { none: 0, fiber: 1, bone: 2, skin: 3, wet: 4, eye: 5 } as const;

/**
 * Materyale anatomi eklentisini uygular. Aynı aile + materyal türündeki tüm materyaller
 * tek derlenmiş programı paylaşır; uniform değerleri materyal başınadır.
 */
export function applyDissolve(mat: THREE.Material, u: DissolveUniforms, family: number = FAMILY.none) {
  mat.defines = { ...(mat.defines ?? {}), ANATOMY_FAMILY: family };
  mat.onBeforeCompile = (shader) => {
    Object.assign(shader.uniforms, u, HOLES);
    shader.vertexShader = VERT_HEAD + shader.vertexShader.replace('#include <project_vertex>', VERT_BODY);
    shader.fragmentShader =
      FRAG_HEAD +
      shader.fragmentShader
        .replace('#include <clipping_planes_fragment>', FRAG_CLIP)
        .replace('#include <color_fragment>', FRAG_COLOR)
        .replace('#include <roughnessmap_fragment>', FRAG_ROUGH)
        .replace('#include <normal_fragment_maps>', FRAG_NORMAL)
        .replace('#include <emissivemap_fragment>', FRAG_EMISSIVE);
  };
  mat.customProgramCacheKey = () => 'anatomy-v4-' + family;
}
