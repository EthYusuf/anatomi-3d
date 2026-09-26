import * as THREE from 'three';
import { GLTFLoader } from 'three/examples/jsm/loaders/GLTFLoader.js';
import { MeshoptDecoder } from 'three/examples/jsm/libs/meshopt_decoder.module.js';
import { acceleratedRaycast, computeBoundsTree, disposeBoundsTree } from 'three-mesh-bvh';
import { CAT, CATEGORIES, type Category } from './data/categories';
import { turkishName } from './data/tr';

// BVH ile hızlı seçim (binlerce parça, ~700K üçgen)
THREE.BufferGeometry.prototype.computeBoundsTree = computeBoundsTree;
THREE.BufferGeometry.prototype.disposeBoundsTree = disposeBoundsTree;
THREE.Mesh.prototype.raycast = acceleratedRaycast;

export interface PartMeta {
  id: string;
  cat: Category;
  /** İngilizce ad (taraf eki olmadan) */
  en: string;
  /** Latince ad (Terminologia Anatomica) */
  la?: string;
  side?: 'l' | 'r';
  /** Z-Anatomy materyal adı (renk tonu / kas fonksiyonu) */
  m?: string;
  /** groups dizisindeki hiyerarşi indeksi */
  g: number;
  parent?: string;
  /** Açıklama dosyası adı */
  d?: string;
  c: [number, number, number];
  r: number;
  mn: [number, number, number];
  mx: [number, number, number];
}

export interface Part extends PartMeta {
  geometry: THREE.BufferGeometry;
  center: THREE.Vector3;
  /** Katman soyma animasyonunda parçanın uçacağı yön */
  dir: THREE.Vector3;
  /** Türkçe ad (sözlükte varsa) */
  tr?: string;
  /** Ana eksen (en büyük yayılım yönü; kas lifi / damar yönü) */
  axis: THREE.Vector3;
  /** En küçük yayılım yönü (yassı yapılarda yüzey normali; ör. iris diski) */
  minor: THREE.Vector3;
  /** Arama için normalize edilmiş metin */
  search: string;
}

/** [İngilizce, Latince?] çiftlerinden oluşan grup yolu */
export type GroupPath = [string, string?][];

export interface ModelData {
  parts: Part[];
  byId: Map<string, Part>;
  groups: GroupPath[];
  source: string;
  bounds: THREE.Box3;
}

const BASE = `${import.meta.env.BASE_URL}body/`;

export const normalize = (s: string) =>
  s
    .toLocaleLowerCase('tr')
    .normalize('NFD')
    .replace(/[̀-ͯ]/g, '')
    .replace(/ı/g, 'i');

/** Kategori dosyalarını indirme sırası: önce dış görünüş ve iskelet */
const LOAD_ORDER: Category[] = ['skin', 'hair', 'bone', 'muscle', 'teeth', 'cartilage', 'ligament', 'artery', 'vein', 'heart', 'nerve', 'cns'];

async function fetchWithProgress(url: string, onBytes: (n: number) => void): Promise<ArrayBuffer> {
  const res = await fetch(url);
  if (!res.ok) throw new Error(`${url}: ${res.status}`);
  if (!res.body) {
    const b = await res.arrayBuffer();
    onBytes(b.byteLength);
    return b;
  }
  const reader = res.body.getReader();
  const chunks: Uint8Array[] = [];
  let got = 0;
  for (;;) {
    const { done, value } = await reader.read();
    if (done) break;
    chunks.push(value);
    got += value.length;
    onBytes(value.length);
  }
  const all = new Uint8Array(got);
  let o = 0;
  for (const c of chunks) {
    all.set(c, o);
    o += c.length;
  }
  return all.buffer;
}

export async function loadModel(onProgress: (f: number) => void): Promise<ModelData> {
  const meta = (await (await fetch(`${BASE}parts.json`)).json()) as {
    source: string;
    groups: GroupPath[];
    files: Record<string, number>;
    parts: PartMeta[];
  };

  const cats = Object.keys(meta.files) as Category[];
  cats.sort((a, b) => {
    const ia = LOAD_ORDER.indexOf(a), ib = LOAD_ORDER.indexOf(b);
    return (ia < 0 ? 99 : ia) - (ib < 0 ? 99 : ib);
  });
  const total = Object.values(meta.files).reduce((a, b) => a + b, 0);
  let loaded = 0;

  const loader = new GLTFLoader();
  loader.setMeshoptDecoder(MeshoptDecoder);

  const geoms = new Map<string, THREE.BufferGeometry>();
  // Paralel indir, sırayla çöz
  const buffers = cats.map((c) =>
    fetchWithProgress(`${BASE}${c}.glb`, (n) => {
      loaded += n;
      onProgress(Math.min(0.97, loaded / total));
    }),
  );
  for (let i = 0; i < cats.length; i++) {
    const gltf = await loader.parseAsync(await buffers[i], BASE);
    gltf.scene.updateMatrixWorld(true);
    gltf.scene.traverse((o) => {
      const mesh = o as THREE.Mesh;
      if (!mesh.isMesh) return;
      // Kuantize (int16) konumları dünya koordinatlarında float32'ye aç
      const src = mesh.geometry.getAttribute('position');
      const pos = new Float32Array(src.count * 3);
      const v = new THREE.Vector3();
      for (let k = 0; k < src.count; k++) {
        v.fromBufferAttribute(src, k).applyMatrix4(mesh.matrixWorld);
        pos[k * 3] = v.x;
        pos[k * 3 + 1] = v.y;
        pos[k * 3 + 2] = v.z;
      }
      const g = new THREE.BufferGeometry();
      g.setAttribute('position', new THREE.BufferAttribute(pos, 3));
      // İndeksi kopyala: glTF'te aynı topolojili parçalar (sağ/sol) indeks tamponunu paylaşabiliyor;
      // BVH kurulumu indeksi yerinde yeniden sıraladığı için paylaşılan tampon diğer parçanın BVH'sini bozar.
      g.setIndex(mesh.geometry.getIndex()!.clone());
      g.computeVertexNormals();
      g.computeBoundingSphere();
      g.computeBoundingBox();
      g.computeBoundsTree();
      geoms.set(mesh.name || mesh.parent?.name || '', g);
    });
    // tarayıcı nefes alsın
    await new Promise((r) => setTimeout(r, 0));
  }
  onProgress(1);

  // Deri yamaları ayrı mesh'ler; sınırlarda gölge dikişi olmasın diye normalleri ortak konumlarda birleştir
  smoothAcross(meta.parts.filter((p) => p.cat === 'skin').map((p) => geoms.get(p.id)).filter((g) => !!g));

  const catOrder = new Map(CATEGORIES.map((c, i) => [c.id, i]));
  const bounds = new THREE.Box3();
  const parts: Part[] = [];
  for (const pm of meta.parts) {
    const geometry = geoms.get(pm.id);
    if (!geometry) continue;
    const center = new THREE.Vector3(...pm.c);
    if (pm.cat === 'bone' || pm.cat === 'skin') bounds.expandByPoint(new THREE.Vector3(...pm.mn)).expandByPoint(new THREE.Vector3(...pm.mx));
    // Parçayı vücut ekseninden dışarı ve biraz öne doğru uçur
    const dir = new THREE.Vector3(center.x, 0, center.z + 0.05);
    if (dir.lengthSq() < 1e-6) dir.set(0, 0, 1);
    dir.normalize();
    dir.z += 0.6;
    dir.normalize();
    const tr = turkishName(pm.en);
    const [axis, minor] = principalAxes(geometry, center);
    parts.push({
      ...pm,
      tr,
      axis,
      minor,
      geometry,
      center,
      dir,
      search: normalize(`${pm.en} ${pm.la ?? ''} ${tr ?? ''} | ${CAT[pm.cat].tr} ${CAT[pm.cat].en}`),
    });
  }
  parts.sort((a, b) => (catOrder.get(a.cat)! - catOrder.get(b.cat)!) || a.en.localeCompare(b.en));

  return { parts, byId: new Map(parts.map((p) => [p.id, p])), groups: meta.groups, source: meta.source, bounds };
}

/** Köşe dağılımının kovaryansından ana ve küçük eksenler (Jacobi özdeğer ayrışımı) */
function principalAxes(g: THREE.BufferGeometry, c: THREE.Vector3): [THREE.Vector3, THREE.Vector3] {
  const p = g.getAttribute('position') as THREE.BufferAttribute;
  const step = Math.max(1, Math.floor(p.count / 800));
  const a = [0, 0, 0, 0, 0, 0]; // xx yy zz xy xz yz
  let n = 0;
  for (let i = 0; i < p.count; i += step) {
    const x = p.getX(i) - c.x, y = p.getY(i) - c.y, z = p.getZ(i) - c.z;
    a[0] += x * x; a[1] += y * y; a[2] += z * z; a[3] += x * y; a[4] += x * z; a[5] += y * z;
    n++;
  }
  const m = [
    [a[0] / n, a[3] / n, a[4] / n],
    [a[3] / n, a[1] / n, a[5] / n],
    [a[4] / n, a[5] / n, a[2] / n],
  ];
  const v = [[1, 0, 0], [0, 1, 0], [0, 0, 1]];
  for (let sweep = 0; sweep < 12; sweep++) {
    for (const [i, j] of [[0, 1], [0, 2], [1, 2]]) {
      if (Math.abs(m[i][j]) < 1e-14) continue;
      const th = 0.5 * Math.atan2(2 * m[i][j], m[j][j] - m[i][i]);
      const cs = Math.cos(th), sn = Math.sin(th);
      for (let k = 0; k < 3; k++) {
        const mik = m[i][k], mjk = m[j][k];
        m[i][k] = cs * mik - sn * mjk;
        m[j][k] = sn * mik + cs * mjk;
      }
      for (let k = 0; k < 3; k++) {
        const mki = m[k][i], mkj = m[k][j];
        m[k][i] = cs * mki - sn * mkj;
        m[k][j] = sn * mki + cs * mkj;
      }
      for (let k = 0; k < 3; k++) {
        const vki = v[k][i], vkj = v[k][j];
        v[k][i] = cs * vki - sn * vkj;
        v[k][j] = sn * vki + cs * vkj;
      }
    }
  }
  const ev = [m[0][0], m[1][1], m[2][2]];
  const order = [0, 1, 2].sort((x, y) => ev[y] - ev[x]);
  const col = (k: number) => new THREE.Vector3(v[0][k], v[1][k], v[2][k]).normalize();
  const minor = col(order[2]);
  // disk normalleri öne (+z) baksın
  if (minor.z < 0) minor.negate();
  return [col(order[0]), minor];
}

function smoothAcross(geoms: THREE.BufferGeometry[]) {
  const q = 1e4;
  const acc = new Map<string, [number, number, number]>();
  const key = (p: THREE.BufferAttribute, i: number) =>
    `${Math.round(p.getX(i) * q)},${Math.round(p.getY(i) * q)},${Math.round(p.getZ(i) * q)}`;
  for (const g of geoms) {
    const p = g.getAttribute('position') as THREE.BufferAttribute;
    const n = g.getAttribute('normal') as THREE.BufferAttribute;
    for (let i = 0; i < p.count; i++) {
      const k = key(p, i);
      const a = acc.get(k);
      if (a) {
        a[0] += n.getX(i);
        a[1] += n.getY(i);
        a[2] += n.getZ(i);
      } else acc.set(k, [n.getX(i), n.getY(i), n.getZ(i)]);
    }
  }
  const v = new THREE.Vector3();
  for (const g of geoms) {
    const p = g.getAttribute('position') as THREE.BufferAttribute;
    const n = g.getAttribute('normal') as THREE.BufferAttribute;
    for (let i = 0; i < p.count; i++) {
      const a = acc.get(key(p, i))!;
      v.set(a[0], a[1], a[2]).normalize();
      n.setXYZ(i, v.x, v.y, v.z);
    }
    n.needsUpdate = true;
  }
}

/** Yapının görüntülenecek adı */
export function displayName(p: PartMeta & { tr?: string }, lang: 'tr' | 'en'): string {
  const side = p.side ? (lang === 'tr' ? (p.side === 'l' ? ' (sol)' : ' (sağ)') : p.side === 'l' ? ' (left)' : ' (right)') : '';
  if (lang === 'tr') return (p.tr ?? p.la ?? p.en) + side;
  return p.en + side;
}

/** İkincil ad: TR modunda Latince (yoksa İngilizce), EN modunda Latince */
export function secondaryName(p: PartMeta & { tr?: string }, lang: 'tr' | 'en'): string | undefined {
  if (lang === 'tr') return p.tr ? p.la ?? p.en : p.la ? p.en : undefined;
  return p.la;
}
