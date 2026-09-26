import { useEffect, useMemo, useRef } from 'react';
import * as THREE from 'three';
import { Canvas, useFrame, useThree } from '@react-three/fiber';
import { Environment, GizmoHelper, GizmoViewport, Lightformer, OrbitControls } from '@react-three/drei';
import { Bloom, EffectComposer, N8AO, SMAA, ToneMapping, Vignette } from '@react-three/postprocessing';
import { ToneMappingMode } from 'postprocessing';
import type { OrbitControls as OrbitControlsImpl } from 'three-stdlib';
import { CAT, MATERIAL_TINT, MUSCLE_FUNCTION, type Category } from '../data/categories';
import { appearanceOf, DETAIL, type Appearance } from '../data/appearance';
import type { ModelData, Part } from '../model';
import { isPeeled, useStore } from '../store';
import { applyDissolve, createDissolveUniforms, FAMILY, HOLES, MAX_HOLES, type DissolveUniforms } from './dissolve';

const clipPlane = new THREE.Plane(new THREE.Vector3(-1, 0, 0), 0);
const NO_RAYCAST = () => {};
const RAYCAST = THREE.Mesh.prototype.raycast;

function hash(s: string) {
  let h = 2166136261;
  for (let i = 0; i < s.length; i++) h = Math.imul(h ^ s.charCodeAt(i), 16777619);
  return ((h >>> 0) % 1000) / 1000;
}

const easeInOut = (t: number) => (t < 0.5 ? 4 * t * t * t : 1 - Math.pow(-2 * t + 2, 3) / 2);
const easeOut = (t: number) => 1 - Math.pow(1 - t, 3);

/** Kazı animasyonu süresi (sn) */
const DIG_TIME = 1.15;

function insideHoles(p: THREE.Vector3) {
  const n = HOLES.uHoleCount.value;
  for (let i = 0; i < n; i++) {
    const h = HOLES.uHoles.value[i];
    if (p.distanceToSquared(new THREE.Vector3(h.x, h.y, h.z)) < h.w * h.w) return true;
  }
  return false;
}

// ------------------------------------------------------------ tıklama halkası efekti
interface Ripple {
  point: THREE.Vector3;
  normal: THREE.Vector3;
  size: number;
  start: number;
}
const ripples: Ripple[] = [];
function spawnRipple(point: THREE.Vector3, normal: THREE.Vector3, size: number) {
  ripples.push({ point: point.clone(), normal: normal.clone().normalize(), size, start: performance.now() });
  if (ripples.length > 4) ripples.shift();
}

function Ripples() {
  const pool = useMemo(
    () =>
      Array.from({ length: 8 }, (_, i) => {
        const m = new THREE.Mesh(
          new THREE.RingGeometry(i % 2 ? 0.9 : 0.78, 1, 64),
          new THREE.MeshBasicMaterial({
            color: '#8fdcff',
            transparent: true,
            opacity: 0,
            blending: THREE.AdditiveBlending,
            depthTest: false,
            depthWrite: false,
            side: THREE.DoubleSide,
            toneMapped: false,
          }),
        );
        m.renderOrder = 20;
        m.visible = false;
        return m;
      }),
    [],
  );
  const group = useMemo(() => {
    const g = new THREE.Group();
    pool.forEach((m) => g.add(m));
    return g;
  }, [pool]);
  const look = useMemo(() => new THREE.Vector3(), []);

  useFrame(() => {
    const now = performance.now();
    pool.forEach((m) => (m.visible = false));
    ripples.forEach((r, i) => {
      // her tıklamaya iki halka: biri hemen, biri kısa gecikmeyle
      for (let k = 0; k < 2; k++) {
        const m = pool[(i * 2 + k) % pool.length];
        const t = (now - r.start - k * 140) / 750;
        if (t < 0 || t > 1) continue;
        m.visible = true;
        m.position.copy(r.point).addScaledVector(r.normal, 0.002);
        m.lookAt(look.copy(m.position).add(r.normal));
        m.scale.setScalar(0.004 + r.size * easeOut(t) * (k ? 0.75 : 1));
        (m.material as THREE.MeshBasicMaterial).opacity = (1 - t) * (k ? 0.5 : 0.85);
      }
    });
    while (ripples.length && now - ripples[0].start > 1200) ripples.shift();
  });

  return <primitive object={group} />;
}

interface Entry {
  part: Part;
  mesh: THREE.Mesh;
  /** Opak hal: standart PBR materyal + prosedürel doku, ıslaklık ve kenar ışığı */
  mat: THREE.MeshStandardMaterial;
  /** Saydam haller için ucuz materyal (X-ray): Lambert, yalnız ön yüz, derinlik yazmaz */
  ghostMat: THREE.MeshLambertMaterial;
  /** Kornea, lens gibi saydam yapılar */
  glass: boolean;
  anatomic: THREE.Color;
  func: THREE.Color;
  /** Yüzey ve çözülme uniform'ları (iki materyal ortak kullanır) */
  u: DissolveUniforms;
  /** Kazı (çift tıklama ile kaldırma) ilerlemesi: 0 yerinde, 1 tamamen kalktı */
  dig: number;
  /** Katman soyma ilerlemesi (0..1) */
  peel: number;
  /** Gizlenme ilerlemesi (0 görünür, 1 gizli) — göster/gizle, izole ve açılış animasyonu */
  hide: number;
  /** Açılış animasyonunda bu yapının belirme gecikmesi (ms) */
  introDelay: number;
  lastOpacity: number;
  /** Toplu çizim: yapı sabitken bu gruptaki örnek olarak çizilir */
  batch: Batch | null;
  instance: number;
  inBatch: boolean;
  appearance: Appearance;
}

/** Aynı görünüm profilindeki sabit yapıları tek çizim komutunda toplayan grup */
interface Batch {
  mesh: THREE.BatchedMesh;
  mat: THREE.MeshStandardMaterial;
  u: DissolveUniforms;
}

/** Açılışta katmanların sırayla belirmesi (sn): önce iskelet, en son deri */
const INTRO_DELAY: Partial<Record<Category, number>> = {
  bone: 0, teeth: 0.1, cartilage: 0.1, ligament: 0.35,
  cns: 0.55, sense: 0.55, meninges: 0.55,
  heart: 0.7, respiratory: 0.7, digestive: 0.75, urinary: 0.75, genital: 0.75, endocrine: 0.75, serosa: 0.75,
  artery: 0.95, vein: 1.0, nerve: 1.1,
  muscle: 1.35, fascia: 1.35,
  skin: 2.1, hair: 2.3,
};

/** Soyma / göster-gizle taramasının süresi (sn) */
const SWEEP_TIME = 1.0;
/** Vücut yüksekliği boyunca tarama düzlemi */
const SWEEP_TOP = 1.76;

/** Görünüm profilinden doku ailesi */
function familyOf(detail: number) {
  switch (detail) {
    case DETAIL.muscle:
    case DETAIL.tendon:
    case DETAIL.nerve:
    case DETAIL.vessel:
      return FAMILY.fiber;
    case DETAIL.bone:
      return FAMILY.bone;
    case DETAIL.skin:
      return FAMILY.skin;
    case DETAIL.wet:
    case DETAIL.brain:
      return FAMILY.wet;
    case DETAIL.iris:
    case DETAIL.sclera:
      return FAMILY.eye;
  }
  return FAMILY.none;
}

/** Islak yüzey: ağır clearcoat katmanı yerine pürüzlülüğü parlak katmana doğru çek */
const wetRoughness = (a: Appearance) => THREE.MathUtils.lerp(a.roughness, a.clearcoatRoughness ?? a.roughness, (a.clearcoat ?? 0) * 0.55);

/** Kadife kenar ışığı (fiziksel sheen yerine ucuz bir kenar parlaması) */
const sheenOf = (a: Appearance) => new THREE.Color(a.sheenColor ?? '#ffffff').multiplyScalar((a.sheen ?? 0) * 0.22);

function baseColor(part: Part, ap: Appearance) {
  const info = CAT[part.cat];
  const tint = part.m ? MATERIAL_TINT[part.m] : undefined;
  const c = new THREE.Color(ap.color ?? (tint && part.cat !== 'muscle' && part.cat !== 'bone' ? tint : info.color));
  // Komşu yapıları ayırt edebilmek için hafif ton farkı
  const hsl = { h: 0, s: 0, l: 0 };
  c.getHSL(hsl);
  const j = hash(part.en) - 0.5;
  const k = part.cat === 'skin' || ap.glass || part.en === 'Iris' || part.en === 'Sclera' ? 0.1 : 0.8;
  c.setHSL(hsl.h + j * 0.02 * k, hsl.s, THREE.MathUtils.clamp(hsl.l + j * 0.07 * k, 0, 1));
  return c;
}

function buildEntries(model: ModelData): Entry[] {
  return model.parts.map((part) => {
    const ap = appearanceOf(part);
    const anatomic = baseColor(part, ap);
    const f = part.cat === 'muscle' && part.m ? MUSCLE_FUNCTION[part.m] : undefined;
    const func = f ? new THREE.Color(f.color) : anatomic.clone().lerp(new THREE.Color('#9aa0a6'), 0.75);
    const glass = !!ap.glass;
    const mat = new THREE.MeshStandardMaterial({
      color: glass ? new THREE.Color('#ffffff') : anatomic,
      roughness: glass ? 0.02 : wetRoughness(ap),
      metalness: 0,
      transparent: glass,
      opacity: glass ? ap.glassOpacity ?? 0.1 : 1,
      depthWrite: !glass,
      envMapIntensity: glass ? 1.8 : 1,
      side: part.cat === 'serosa' || part.cat === 'meninges' || glass ? THREE.FrontSide : THREE.DoubleSide,
      clippingPlanes: [],
    });
    const ghostMat = new THREE.MeshLambertMaterial({
      color: anatomic,
      transparent: true,
      depthWrite: false,
      side: THREE.FrontSide,
      clippingPlanes: [],
    });
    const u = createDissolveUniforms();
    u.uDetail.value = ap.detail;
    u.uBump.value = ap.bump;
    u.uFreq.value = ap.freq ?? 1;
    u.uSheen.value.copy(sheenOf(ap));
    u.uCenter.value.copy(part.center);
    u.uScale.value = part.r;
    if (ap.detail === DETAIL.sclera) {
      // Göz küresi merkezi ve limbus açısı: aynı taraftaki arka segment ve iris yarıçapından
      const ball = model.parts.find((q) => q.en === 'Posterior segment of eyeball' && q.side === part.side);
      const iris = model.parts.find((q) => q.en === 'Iris' && q.side === part.side);
      if (ball) u.uCenter.value.copy(ball.center);
      const R = ball?.r ?? part.r;
      const ri = (iris?.r ?? R * 0.5) * 0.98;
      u.uScale.value = Math.cos(Math.asin(Math.min(0.95, ri / R)));
    }
    // Lif / disk yönü köşe özniteliği olarak (toplu çizimde de parça başına farklı olabilsin)
    if (!part.geometry.getAttribute('aAxis')) {
      const ax = ap.detail === DETAIL.iris ? part.minor : ap.detail === DETAIL.sclera ? new THREE.Vector3(0, 0, 1) : part.axis;
      const n = part.geometry.getAttribute('position').count;
      const arr = new Float32Array(n * 3);
      for (let i = 0; i < n; i++) {
        arr[i * 3] = ax.x;
        arr[i * 3 + 1] = ax.y;
        arr[i * 3 + 2] = ax.z;
      }
      part.geometry.setAttribute('aAxis', new THREE.BufferAttribute(arr, 3));
    }
    applyDissolve(mat, u, glass ? FAMILY.none : familyOf(ap.detail));
    applyDissolve(ghostMat, u);
    const mesh = new THREE.Mesh(part.geometry, mat);
    mesh.name = part.id;
    mesh.userData.id = part.id;
    mesh.matrixAutoUpdate = false;
    if (glass) mesh.renderOrder = 4;
    return {
      part, mesh, mat, ghostMat, glass, anatomic, func, u,
      dig: 0, peel: 0, hide: 1,
      introDelay: (INTRO_DELAY[part.cat] ?? 1) * 1000,
      lastOpacity: 1,
      batch: null,
      instance: -1,
      inBatch: false,
      appearance: ap,
    };
  });
}

/** Özel efektli (saydam göz, iris, sklera) ve sürekli hareket eden (kalp, akciğer) yapılar gruplanmaz */
function batchable(e: Entry) {
  const d = e.appearance.detail;
  if (e.glass || d === DETAIL.iris || d === DETAIL.sclera) return false;
  if (e.part.cat === 'heart' || /lobe of (right|left) lung/i.test(e.part.en)) return false;
  return true;
}

function buildBatches(entries: Entry[]): Batch[] {
  const groups = new Map<string, Entry[]>();
  for (const e of entries) {
    if (!batchable(e)) continue;
    const a = e.appearance;
    const side = e.mat.side;
    const key = JSON.stringify([a.roughness, a.clearcoat, a.clearcoatRoughness, a.sheen, a.sheenColor, a.sheenRoughness, a.detail, a.bump, a.freq, side]);
    groups.set(key, [...(groups.get(key) ?? []), e]);
  }
  const out: Batch[] = [];
  for (const list of groups.values()) {
    const a = list[0].appearance;
    let nv = 0, ni = 0;
    for (const e of list) {
      nv += e.part.geometry.getAttribute('position').count;
      ni += e.part.geometry.getIndex()!.count;
    }
    const mat = new THREE.MeshStandardMaterial({
      color: '#ffffff',
      roughness: wetRoughness(a),
      metalness: 0,
      side: list[0].mat.side,
      clippingPlanes: [],
    });
    const u = createDissolveUniforms();
    u.uDetail.value = a.detail;
    u.uBump.value = a.bump;
    u.uFreq.value = a.freq ?? 1;
    u.uSheen.value.copy(sheenOf(a));
    applyDissolve(mat, u, familyOf(a.detail));
    const mesh = new THREE.BatchedMesh(list.length, nv, ni, mat);
    mesh.sortObjects = false;
    mesh.frustumCulled = false;
    mesh.raycast = NO_RAYCAST;
    const batch: Batch = { mesh, mat, u };
    for (const e of list) {
      const gid = mesh.addGeometry(e.part.geometry);
      const iid = mesh.addInstance(gid);
      mesh.setColorAt(iid, e.anatomic);
      mesh.setVisibleAt(iid, false);
      e.batch = batch;
      e.instance = iid;
    }
    out.push(batch);
  }
  return out;
}

function Anatomy({ model, onHover }: { model: ModelData; onHover: ViewerProps['onHover'] }) {
  // Grup ve girdiler aynı memo'da: StrictMode çift çağrısında mesh'ler başka gruba taşınmasın
  const { entries, group, batchGroup, batches, byMesh, skinEntries } = useMemo(() => {
    const entries = buildEntries(model);
    const group = new THREE.Group();
    for (const e of entries) group.add(e.mesh);
    const batchGroup = new THREE.Group();
    const batches = buildBatches(entries);
    for (const b of batches) batchGroup.add(b.mesh);
    return {
      entries,
      group,
      batchGroup,
      batches,
      byMesh: new Map(entries.map((e) => [e.mesh, e])),
      skinEntries: entries.filter((e) => e.part.cat === 'skin'),
    };
  }, [model]);
  const { camera, gl, scene } = useThree();
  const raycaster = useMemo(() => new THREE.Raycaster(), []);

  // ------------------------------------------------------------ seçim
  useEffect(() => {
    const el = gl.domElement;
    const ndc = new THREE.Vector2();
    let pending: PointerEvent | null = null;
    let raf = 0;
    let down: { x: number; y: number; t: number } | null = null;

    const pick = (ev: { clientX: number; clientY: number }): { e: Entry; hit: THREE.Intersection } | null => {
      const rect = el.getBoundingClientRect();
      ndc.set(((ev.clientX - rect.left) / rect.width) * 2 - 1, -((ev.clientY - rect.top) / rect.height) * 2 + 1);
      raycaster.setFromCamera(ndc, camera);
      const s = useStore.getState();
      const hits = raycaster.intersectObject(group, true);
      for (const h of hits) {
        if (s.clipAxis !== 'none' && clipPlane.distanceToPoint(h.point) < 0) continue;
        const e = byMesh.get(h.object as THREE.Mesh);
        if (!e) continue;
        // X-ray modunda yarı saydam katmanların arkasındakini seçebilmek için çok saydamları atla
        if (e.lastOpacity < 0.3 && s.viewMode === 'xray' && s.selected !== e.part.id) continue;
        // Deri penceresi açıkken pencere dışında kalan (çizilmeyen) iç yüzeyleri atla
        if (e.u.uWindowed.value > 0.5 && !insideHoles(h.point)) continue;
        return { e, hit: h };
      }
      return null;
    };

    const flushHover = () => {
      raf = 0;
      if (!pending) return;
      const ev = pending;
      pending = null;
      const e = pick(ev)?.e;
      const s = useStore.getState();
      const id = e ? e.part.id : null;
      if (s.hovered !== id) s.hover(id);
      onHover(e ? e.part : null, ev.clientX, ev.clientY);
      el.style.cursor = e ? 'pointer' : '';
    };

    const onMove = (ev: PointerEvent) => {
      if (ev.pointerType !== 'mouse') return;
      if (ev.buttons) {
        if (useStore.getState().hovered) {
          useStore.getState().hover(null);
          onHover(null);
        }
        return;
      }
      pending = ev;
      if (!raf) raf = requestAnimationFrame(flushHover);
    };
    const onLeave = () => {
      pending = null;
      useStore.getState().hover(null);
      onHover(null);
    };
    const onDown = (ev: PointerEvent) => {
      down = { x: ev.clientX, y: ev.clientY, t: performance.now() };
    };
    // Yapı + aynı kategorideki alt yapıları (ör. göğüs bölgesi derisi ile meme bölgesi derisi)
    const children = new Map<string, Part[]>();
    for (const p of model.parts) if (p.parent) children.set(p.parent, [...(children.get(p.parent) ?? []), p]);
    const withChildren = (p: Part): string[] => [
      p.id,
      ...(children.get(p.id) ?? []).filter((c) => c.cat === p.cat).flatMap(withChildren),
    ];

    // Çift tıklama / çift dokunma: tıklanan yapıyı çözülme animasyonuyla kaldır
    let lastUp: { x: number; y: number; t: number } | null = null;
    const onUp = (ev: PointerEvent) => {
      if (!down || ev.button !== 0) return;
      const moved = Math.hypot(ev.clientX - down.x, ev.clientY - down.y);
      down = null;
      if (moved > 6) return;
      const now = performance.now();
      const isDouble = !!lastUp && now - lastUp.t < 340 && Math.hypot(ev.clientX - lastUp.x, ev.clientY - lastUp.y) < 12;
      lastUp = isDouble ? null : { x: ev.clientX, y: ev.clientY, t: now };
      const r = pick(ev);
      const s = useStore.getState();
      if (s.quiz) {
        if (!isDouble) window.dispatchEvent(new CustomEvent('quiz-pick', { detail: r ? r.e.part.id : null }));
        return;
      }
      if (isDouble) {
        if (!r) return;
        const n = r.hit.face ? r.hit.face.normal.clone().transformDirection(r.e.mesh.matrixWorld) : new THREE.Vector3(0, 0, 1);
        if (n.dot(raycaster.ray.direction) > 0) n.negate();
        spawnRipple(r.hit.point, n, THREE.MathUtils.clamp(r.e.part.r * 0.8, 0.03, 0.12));
        s.dig(r.e.part.id, withChildren(r.e.part), [r.hit.point.x, r.hit.point.y, r.hit.point.z]);
        s.select(null);
        return;
      }
      if (!r) {
        s.select(null);
        return;
      }
      s.select(s.selected === r.e.part.id && ev.pointerType === 'mouse' ? null : r.e.part.id);
    };

    el.addEventListener('pointermove', onMove);
    el.addEventListener('pointerleave', onLeave);
    el.addEventListener('pointerdown', onDown);
    el.addEventListener('pointerup', onUp);
    return () => {
      cancelAnimationFrame(raf);
      el.removeEventListener('pointermove', onMove);
      el.removeEventListener('pointerleave', onLeave);
      el.removeEventListener('pointerdown', onDown);
      el.removeEventListener('pointerup', onUp);
    };
  }, [gl, camera, group, byMesh, raycaster, onHover, model]);

  // ------------------------------------------------------------ animasyon ve materyaller
  const selColor = useMemo(() => new THREE.Color('#2f8cff'), []);
  const hovColor = useMemo(() => new THREE.Color('#62b4ff'), []);
  const quizColor = useMemo(() => new THREE.Color('#30a46c'), []);
  const black = useMemo(() => new THREE.Color(0, 0, 0), []);
  const planes = useMemo(() => [clipPlane], []);
  const noPlanes = useMemo(() => [] as THREE.Plane[], []);
  const lastColorMode = useRef<string>('anatomic');

  // Canlı fizyoloji: kalp ve akciğer parçalarının ölçekleneceği merkezler
  const physio = useMemo(() => {
    const heart = entries.filter((e) => e.part.cat === 'heart');
    const lungR = entries.filter((e) => /lobe of right lung/i.test(e.part.en));
    const lungL = entries.filter((e) => /lobe of left lung/i.test(e.part.en));
    const mid = (list: Entry[]) =>
      list.reduce((v, e) => v.add(e.part.center), new THREE.Vector3()).multiplyScalar(1 / Math.max(1, list.length));
    const map = new Map<Entry, { kind: 'heart' | 'lung'; c: THREE.Vector3 }>();
    const hc = mid(heart), rc = mid(lungR), lc = mid(lungL);
    heart.forEach((e) => map.set(e, { kind: 'heart', c: hc }));
    lungR.forEach((e) => map.set(e, { kind: 'lung', c: rc }));
    lungL.forEach((e) => map.set(e, { kind: 'lung', c: lc }));
    return map;
  }, [entries]);

  // Açılış: önce tüm materyaller görünmez halde bir kez çizilip shader'lar derlenir,
  // ardından iskeletten deriye katman katman belirme ve yavaş kamera yaklaşması başlar
  const intro = useRef<{ compiled: boolean; base: number | null; warm: number }>({ compiled: false, base: null, warm: 0 });

  // Isıtma listesi: her farklı shader programı için bir temsilci. Windows'ta (ANGLE/D3D11) sürücü
  // derlemesi ilk çizime kadar ertelenir; hepsi aynı karede çizilirse sayfa saniyelerce kilitlenir.
  // Bu yüzden her karede yalnız bir program, pikselleri atılan (çözülme=1) bir çizimle ısıtılır.
  const warmList = useMemo(() => {
    const seen = new Set<string>();
    const list: ({ kind: 'mesh'; e: Entry } | { kind: 'batch'; b: Batch; e: Entry })[] = [];
    for (const e of entries) {
      const m = e.mat;
      const key = [m.type, JSON.stringify(m.defines), m.side, m.transparent].join('|');
      if (!seen.has(key)) {
        seen.add(key);
        list.push({ kind: 'mesh', e });
      }
    }
    for (const b of batches) {
      const e = entries.find((x) => x.batch === b);
      if (e) list.push({ kind: 'batch', b, e });
    }
    return list;
  }, [entries, batches]);

  // Shader'ları arka planda (KHR_parallel_shader_compile) derle: ana iş parçacığı kilitlenmesin.
  // Derleme için tüm yapılar bir anlığına görünür yapılır (çözülme=1, yani piksel üretmezler),
  // derleme komutları verildikten hemen sonra yeniden gizlenir.
  useEffect(() => {
    let alive = true;
    const it = intro.current;
    it.compiled = false;
    it.base = null;
    useStore.getState().setSceneReady(false);
    for (const e of entries) {
      e.mesh.visible = true;
      e.u.uMode.value = 1;
      e.u.uOrigin.value.set(0, SWEEP_TOP, 0);
      e.u.uRadius.value = SWEEP_TOP;
      e.u.uDissolve.value = 1;
    }
    for (const b of batches) b.u.uDissolve.value = 1;
    const done = () => {
      if (!alive) return;
      for (const b of batches) b.u.uDissolve.value = 0;
      it.compiled = true;
    };
    group.visible = true;
    batchGroup.visible = true;
    // Yüksek kalitede sahne son işlem için bir ara görüntüye (render target) çizilir; three.js bu durumda
    // farklı renk uzayı/ton eşleme ayarlı program kullanır. Önceden derleme de aynı koşulda yapılmazsa
    // programlar ilk çizimde baştan derlenir ve sayfa kilitlenir.
    const prevTarget = gl.getRenderTarget();
    const rt = useStore.getState().quality === 'high' ? new THREE.WebGLRenderTarget(1, 1, { type: THREE.HalfFloatType }) : null;
    if (rt) gl.setRenderTarget(rt);
    gl.compileAsync(scene, camera).then(done, done);
    gl.setRenderTarget(prevTarget);
    rt?.dispose();
    // Derleme sürerken bu materyallerle çizim yapılırsa sürücü derlemenin bitmesini bekler
    // ve sayfa kilitlenir; bu yüzden gruplar derleme bitene kadar çizimden çıkarılır.
    for (const e of entries) e.mesh.visible = false;
    group.visible = false;
    batchGroup.visible = false;
    return () => {
      alive = false;
    };
  }, [entries, batches, group, batchGroup, gl, scene, camera]);

  useFrame((_, dtRaw) => {
    const dt = Math.min(dtRaw, 0.05);
    const s = useStore.getState();
    const now = performance.now();
    const it = intro.current;
    if (it.base === null) {
      // Derleme bitene kadar hiçbir şey çizme; sonra programları kare kare ısıt, en son açılışı başlat
      for (const e of entries) e.mesh.visible = false;
      if (!it.compiled) return;
      if (it.warm < warmList.length) {
        group.visible = true;
        batchGroup.visible = true;
        for (const b of batches) {
          b.u.uDissolve.value = 1;
          for (const e of entries) if (e.batch === b && e.inBatch) {
            b.mesh.setVisibleAt(e.instance, false);
            e.inBatch = false;
          }
        }
        const w = warmList[it.warm++];
        if (w.kind === 'mesh') w.e.mesh.visible = true;
        else {
          w.b.mesh.setVisibleAt(w.e.instance, true);
          w.e.inBatch = true;
        }
        return;
      }
      for (const b of batches) b.u.uDissolve.value = 0;
      it.base = now;
      group.visible = true;
      batchGroup.visible = true;
      s.setSceneReady(true);
      camera.position.set(1.5, 1.45, 5.4);
      s.requestCamera('home', undefined, 1.1);
      return;
    }
    const quizTarget = s.quiz?.last && !s.quiz.last.ok ? s.quiz.target : null;
    // Quiz'de yanlış cevapta hedef derin bir yapıysa çevresini saydamlaştır
    const quizDeep = !!quizTarget && model.byId.get(quizTarget)?.cat !== 'skin';
    const ghost = (s.reveal && !!s.selected) || quizDeep;
    const xray = s.viewMode === 'xray' || ghost;
    const wire = s.viewMode === 'wire';
    const clipOn = s.clipAxis !== 'none';
    const pulse = 0.45 + Math.sin(now / 260) * 0.12;
    // Deri tamamen opaksa içerideki yapılar görünmez: çizmeyerek hem hız kazanılır
    // hem de sadeleştirilmiş derinin altından taşan kas/damar parçaları gizlenir.
    // (Parçalar kategori sırasına göre dizili; deri, iç yapılardan önce işlenir.)
    let occlude = !clipOn && s.viewMode === 'solid' && !s.isolated && !ghost;

    // Toplu çizim materyalleri: kesit düzlemi ve tel kafes
    for (const b of batches) {
      const m = b.mat;
      const want = clipOn ? planes : noPlanes;
      if (m.clippingPlanes !== want) {
        m.clippingPlanes = want;
        m.needsUpdate = true;
      }
      if (m.wireframe !== wire) m.wireframe = wire;
    }
    const colorModeChanged = s.colorMode !== lastColorMode.current;
    lastColorMode.current = s.colorMode;

    // Fizyoloji: kalp atışı (~70/dk, iki vuruşlu) ve solunum (~14/dk)
    const tSec = now / 1000;
    const ph = (tSec % 0.86) / 0.86;
    const beat = Math.exp(-(((ph - 0.08) / 0.05) ** 2)) + 0.55 * Math.exp(-(((ph - 0.3) / 0.06) ** 2));
    const breath = 0.5 - 0.5 * Math.cos((tSec / 4.3) * Math.PI * 2);

    // Kazı: hedef değerleri ve deride açılan pencereler
    const dugOrigin = new Map<string, [number, number, number]>();
    for (const d of s.dug) for (const id of d.ids) dugOrigin.set(id, d.origin);
    let holeCount = 0;
    for (const e of skinEntries) {
      if (e.dig > 0 || dugOrigin.has(e.part.id) || s.hiddenParts.has(e.part.id)) {
        if (holeCount < MAX_HOLES) {
          HOLES.uHoles.value[holeCount].set(e.part.center.x, e.part.center.y, e.part.center.z, Math.max(e.part.r * 1.3, 0.09));
        }
        holeCount++;
      }
    }
    // Çok fazla delik varsa pencere yerine her şeyi çiz
    if (holeCount > MAX_HOLES) occlude = false;
    HOLES.uHoleCount.value = Math.min(holeCount, MAX_HOLES);

    for (const e of entries) {
      const { part, mesh, u } = e;
      const outer = part.cat === 'skin' || part.cat === 'hair' || part.cat === 'sense';
      const shown =
        now >= it.base + e.introDelay &&
        !s.hiddenCats.has(part.cat) &&
        !s.hiddenParts.has(part.id) &&
        (!s.isolated || s.isolated.has(part.id));

      // Katman soyma ve göster/gizle: baştan ayağa tarama
      const pt = isPeeled(part.cat, s.peelStep) ? 1 : 0;
      if (e.peel !== pt) e.peel = pt > e.peel ? Math.min(1, e.peel + dt / SWEEP_TIME) : Math.max(0, e.peel - dt / SWEEP_TIME);
      const ht = shown ? 0 : 1;
      if (e.hide !== ht) e.hide = ht > e.hide ? Math.min(1, e.hide + dt / SWEEP_TIME) : Math.max(0, e.hide - dt / (SWEEP_TIME * 1.1));
      const sweep = Math.max(e.peel, e.hide);

      // Kazı ilerlemesi (çift tıklama ile kaldırma / geri alma)
      const origin = dugOrigin.get(part.id);
      const digT = origin ? 1 : 0;
      if (origin && e.dig === 0) {
        u.uOrigin.value.set(origin[0], origin[1], origin[2]);
        u.uRadius.value = u.uOrigin.value.distanceTo(part.center) + part.r + 0.005;
      }
      if (e.dig !== digT) {
        const step = dt / DIG_TIME;
        e.dig = digT > e.dig ? Math.min(1, e.dig + step) : Math.max(0, e.dig - step);
      }

      if (e.dig > 0) {
        u.uMode.value = 0;
        // hızlı başlayıp yumuşak biten eğri
        u.uDissolve.value = 1 - (1 - e.dig) * (1 - e.dig);
      } else if (sweep > 0) {
        u.uMode.value = 1;
        u.uOrigin.value.set(0, SWEEP_TOP, 0);
        u.uDir.value.set(0, -1, 0);
        u.uRadius.value = SWEEP_TOP;
        u.uDissolve.value = easeInOut(sweep);
      } else {
        u.uDissolve.value = 0;
      }
      const isHole = part.cat === 'skin' && (e.dig > 0 || !!origin || s.hiddenParts.has(part.id));

      const isSel = s.selected === part.id;
      const isHov = s.hovered === part.id;
      const isQuiz = quizTarget === part.id;
      let opacity = e.glass ? e.appearance.glassOpacity ?? 0.1 : 1;
      // Normalde görünmeyen yapılar (göz sıvıları, zonula) seçilince / izole edilince görünür
      if (e.glass && opacity === 0 && (isSel || s.isolated?.has(part.id))) opacity = 0.45;
      if (xray && !isSel && !isQuiz) {
        if (ghost) opacity *= part.cat === 'bone' ? 0.12 : part.cat === 'skin' ? 0.03 : 0.06;
        else opacity *= part.cat === 'skin' ? 0.06 : part.cat === 'bone' ? 0.28 : 0.16;
      }

      if (part.cat === 'skin' && !isHole && (sweep > 0 || opacity < 0.995)) occlude = false;
      // Deri kapalıyken iç yapılar yalnız deride açılmış bir pencereye yakınsa çizilir
      let windowed = false;
      let inner = true;
      if (!outer && occlude) {
        windowed = true;
        inner = false;
        const n = HOLES.uHoleCount.value;
        for (let i = 0; i < n && !inner; i++) {
          const h = HOLES.uHoles.value[i];
          const dx = h.x - part.center.x, dy = h.y - part.center.y, dz = h.z - part.center.z;
          if (Math.sqrt(dx * dx + dy * dy + dz * dz) < h.w + part.r) inner = true;
        }
      }
      u.uWindowed.value = windowed ? 1 : 0;
      const visible = opacity > 0.01 && e.dig < 1 && sweep < 1 && inner;
      mesh.visible = visible;
      mesh.raycast = visible && sweep === 0 && !origin && e.dig === 0 ? RAYCAST : NO_RAYCAST;

      // Kalp atışı / solunum: parça merkezine göre hafif ölçekleme
      const phys = s.alive ? physio.get(e) : undefined;
      const sc = phys ? (phys.kind === 'heart' ? 1 - 0.045 * beat : 1 + 0.035 * breath) : 1;
      if (sc !== mesh.scale.x) {
        mesh.scale.setScalar(sc);
        if (phys) mesh.position.copy(phys.c).multiplyScalar(1 - sc);
        else mesh.position.set(0, 0, 0);
        mesh.updateMatrix();
      }

      // Sabit yapı: tek tek değil, görünüm grubundaki örnek olarak çiz (çizim komutu sayısını büyük ölçüde azaltır)
      const staticDraw =
        !!e.batch && visible && opacity === 1 && sweep === 0 && e.dig === 0 && !windowed && !isSel && !isHov && !isQuiz && sc === 1;
      if (e.batch) {
        if (colorModeChanged) e.batch.mesh.setColorAt(e.instance, s.colorMode === 'function' ? e.func : e.anatomic);
        if (staticDraw !== e.inBatch) {
          e.batch.mesh.setVisibleAt(e.instance, staticDraw);
          e.inBatch = staticDraw;
        }
      }
      if (staticDraw) mesh.visible = false;

      if (!visible || staticDraw) {
        e.lastOpacity = visible ? 1 : 0;
        continue;
      }

      const solid = e.glass || opacity > 0.995;
      const mat = solid ? e.mat : e.ghostMat;
      if (mesh.material !== mat) mesh.material = mat;
      mat.opacity = opacity;
      e.lastOpacity = opacity;
      if (!e.glass) mesh.renderOrder = solid ? 0 : part.cat === 'skin' ? 3 : 2;
      if (mat.wireframe !== wire) mat.wireframe = wire;

      if (!e.glass) mat.color.copy(s.colorMode === 'function' ? e.func : e.anatomic);
      if (isSel || isQuiz) {
        mat.emissive.copy(isQuiz ? quizColor : selColor);
        mat.emissiveIntensity = pulse;
      } else if (isHov) {
        mat.emissive.copy(hovColor);
        mat.emissiveIntensity = 0.28;
      } else if (mat.emissiveIntensity !== 0) {
        mat.emissive.copy(black);
        mat.emissiveIntensity = 0;
      }
      const want = clipOn ? planes : noPlanes;
      if (mat.clippingPlanes !== want) {
        mat.clippingPlanes = want;
        mat.needsUpdate = true;
      }
    }
  });

  return (
    <>
      <primitive object={group} />
      <primitive object={batchGroup} />
    </>
  );
}

function ClipUpdater({ extent }: { extent: THREE.Box3 }) {
  const size = useMemo(() => extent.getSize(new THREE.Vector3()), [extent]);
  const mid = useMemo(() => extent.getCenter(new THREE.Vector3()), [extent]);
  useFrame(() => {
    const s = useStore.getState();
    const sign = s.clipFlip ? -1 : 1;
    // clipPos: -1..1
    if (s.clipAxis === 'sagittal') {
      clipPlane.normal.set(-sign, 0, 0);
      clipPlane.constant = sign * (mid.x + (s.clipPos * size.x) / 2);
    } else if (s.clipAxis === 'coronal') {
      clipPlane.normal.set(0, 0, -sign);
      clipPlane.constant = sign * (mid.z + (s.clipPos * size.z) / 2);
    } else if (s.clipAxis === 'axial') {
      clipPlane.normal.set(0, -sign, 0);
      clipPlane.constant = sign * (mid.y + (s.clipPos * size.y) / 2);
    }
  });
  return null;
}

const HOME = { pos: new THREE.Vector3(0, 1.0, 4.1), look: new THREE.Vector3(0, 0.92, 0) };

function CameraRig({ model }: { model: ModelData }) {
  const { camera, controls, gl } = useThree() as unknown as {
    camera: THREE.PerspectiveCamera;
    controls: OrbitControlsImpl | null;
    gl: THREE.WebGLRenderer;
  };
  const req = useStore((s) => s.cameraRequest);
  const target = useRef<{ pos: THREE.Vector3; look: THREE.Vector3; speed: number } | null>(null);

  useEffect(() => {
    if (!req || !controls) return;
    const look = controls.target.clone();
    const dist = camera.position.distanceTo(controls.target);
    const D = Math.max(dist, 1.2);
    let pos: THREE.Vector3;
    switch (req.preset) {
      case 'home':
        target.current = { pos: HOME.pos.clone(), look: HOME.look.clone(), speed: req.speed ?? 5 };
        return;
      case 'front': pos = look.clone().add(new THREE.Vector3(0, 0, D)); break;
      case 'back': pos = look.clone().add(new THREE.Vector3(0, 0, -D)); break;
      // anatomik sol +x tarafında
      case 'left': pos = look.clone().add(new THREE.Vector3(D, 0, 0)); break;
      case 'right': pos = look.clone().add(new THREE.Vector3(-D, 0, 0)); break;
      case 'top': pos = look.clone().add(new THREE.Vector3(0, D, 0.001)); break;
      case 'bottom': pos = look.clone().add(new THREE.Vector3(0, -D, 0.001)); break;
      case 'iso': pos = look.clone().add(new THREE.Vector3(D * 0.6, D * 0.3, D * 0.75)); break;
      case 'focus': {
        const p = req.id ? model.byId.get(req.id) : null;
        if (!p) return;
        look.copy(p.center);
        const dir = camera.position.clone().sub(controls.target).normalize();
        const fov = THREE.MathUtils.degToRad(camera.fov);
        const aspect = gl.domElement.clientWidth / Math.max(1, gl.domElement.clientHeight);
        const fit = p.r / Math.sin(Math.min(fov, fov * aspect) / 2);
        pos = p.center.clone().addScaledVector(dir, THREE.MathUtils.clamp(fit * 1.5, 0.15, 4.5));
        break;
      }
    }
    target.current = { pos, look, speed: req.speed ?? 5 };
  }, [req, camera, controls, model, gl]);

  useFrame((_, dt) => {
    const t = target.current;
    if (!t || !controls) return;
    const k = Math.min(1, dt * t.speed);
    camera.position.lerp(t.pos, k);
    controls.target.lerp(t.look, k);
    controls.update();
    if (camera.position.distanceTo(t.pos) < 0.002 && controls.target.distanceTo(t.look) < 0.002) target.current = null;
  });

  useEffect(() => {
    const stop = () => (target.current = null);
    const el = gl.domElement;
    el.addEventListener('wheel', stop, { passive: true });
    el.addEventListener('pointerdown', stop);
    return () => {
      el.removeEventListener('wheel', stop);
      el.removeEventListener('pointerdown', stop);
    };
  }, [gl]);

  return null;
}

interface ViewerProps {
  model: ModelData;
  onHover: (p: Part | null, x?: number, y?: number) => void;
}

/** Kaliteye göre renderer ton eşlemesini ayarlar (yüksek kalitede ton eşleme son işlemde yapılır) */
function RendererSetup({ high }: { high: boolean }) {
  const { gl, scene } = useThree();
  // Ton eşlemenin kendisi Canvas'ın `flat` ayarıyla kurulur (yüksekte son işlemde yapılır);
  // burada sonradan değiştirilirse derlenmiş tüm programlar geçersiz kalıp yeniden derlenir.
  useEffect(() => {
    gl.toneMappingExposure = high ? 1 : 1.05;
    scene.background = high ? new THREE.Color('#1a212b') : null;
  }, [gl, scene, high]);
  return null;
}

export default function Viewer({ model, onHover }: ViewerProps) {
  const quality = useStore((s) => s.quality);
  const high = quality === 'high';
  return (
    <Canvas
      camera={{ position: HOME.pos.toArray(), fov: 30, near: 0.01, far: 60 }}
      dpr={high ? [1, 1.5] : [1, 2]}
      key={quality}
      flat={high}
      gl={{ antialias: !high, localClippingEnabled: true, alpha: true, powerPreference: 'high-performance' }}
      onCreated={(state) => {
        const { gl } = state;
        if (import.meta.env.DEV) (window as unknown as { __r3f: unknown }).__r3f = state;
        gl.localClippingEnabled = true;
      }}
    >
      <RendererSetup high={high} />
      {/* Stüdyo ışığı: sıcak ana ışık, soğuk dolgu, arkadan kenar ışıkları (siluetleri ayırır) */}
      <hemisphereLight args={['#fff6ee', '#3a3f48', 0.45]} />
      <directionalLight position={[2.2, 3.2, 3.6]} intensity={1.7} color="#fff1e4" />
      <directionalLight position={[-3, 1.2, 1.5]} intensity={0.55} color="#c9dcff" />
      <directionalLight position={[-1.5, 2.5, -4]} intensity={1.25} color="#d8e8ff" />
      <directionalLight position={[2.5, 0.5, -3]} intensity={0.8} color="#ffe0cc" />
      <directionalLight position={[0, -2, 3]} intensity={0.25} color="#ffe2d0" />
      <Environment resolution={256}>
        <Lightformer form="rect" intensity={2.2} position={[0, 4, 6]} scale={[8, 4, 1]} />
        <Lightformer form="rect" intensity={0.9} position={[-6, 1.5, 2]} rotation-y={Math.PI / 2.5} scale={[4, 6, 1]} color="#bcd6ff" />
        <Lightformer form="rect" intensity={1.4} position={[5, 2, -4]} rotation-y={-Math.PI / 1.4} scale={[3, 6, 1]} color="#ffe6d6" />
        <Lightformer form="ring" intensity={1.2} position={[0, 6, -2]} rotation-x={Math.PI / 2} scale={3} />
        <Lightformer form="rect" intensity={0.35} position={[0, -4, 2]} rotation-x={-Math.PI / 2} scale={[10, 10, 1]} color="#5a4a44" />
      </Environment>

      <Anatomy model={model} onHover={onHover} />
      <Ripples />

      <ClipUpdater extent={model.bounds} />
      <CameraRig model={model} />
      <OrbitControls
        makeDefault
        target={HOME.look.toArray()}
        enableDamping
        dampingFactor={0.09}
        minDistance={0.08}
        maxDistance={9}
        zoomSpeed={1.1}
        zoomToCursor
      />
      {high ? (
        <EffectComposer multisampling={0} enableNormalPass={false}>
          {/* Ekran uzaylı ortam gölgelemesi: kas aralarında, kemik girintilerinde derinlik */}
          <N8AO screenSpaceRadius aoRadius={36} distanceFalloff={0.35} intensity={2.6} quality="low" halfRes depthAwareUpsampling />
          {/* Yalnız çok parlak alanlar (çözülme kenarı, ıslak yansımalar) hafifçe parlar */}
          <Bloom mipmapBlur luminanceThreshold={0.92} luminanceSmoothing={0.2} intensity={0.55} radius={0.55} />
          <ToneMapping mode={ToneMappingMode.ACES_FILMIC} />
          <Vignette offset={0.28} darkness={0.55} />
          <SMAA />
        </EffectComposer>
      ) : (
        <GizmoHelper alignment="bottom-right" margin={[72, 88]}>
          <GizmoViewport axisColors={['#e5484d', '#46a758', '#3e8ef7']} labelColor="#111" />
        </GizmoHelper>
      )}
    </Canvas>
  );
}
