// Z-Anatomy (CC BY-SA 4.0) GLB dosyalarından web için optimize tam vücut modeli üretir.
//
// Girdi : _raw/za/*.glb (fbx2gltf ile FBX'ten dönüştürülmüş), _raw/za_repo/Resources/*
// Çıktı : app/public/body/<kategori>.glb   (meshopt ile sıkıştırılmış, her yapı ayrı düğüm)
//         app/public/body/parts.json       (yapı listesi, hiyerarşi, Latince adlar)
//         app/public/body/desc/<n>.txt     (yapı açıklamaları)
//
// Kullanım: node tools/build_za.mjs [--scale=1]
import { Document, NodeIO } from '@gltf-transform/core';
import { EXTMeshoptCompression, KHRMeshQuantization } from '@gltf-transform/extensions';
import { meshopt } from '@gltf-transform/functions';
import { MeshoptEncoder, MeshoptSimplifier } from 'meshoptimizer';
import { copyFileSync, existsSync, mkdirSync, readFileSync, readdirSync, rmSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { join } from 'node:path';

const ROOT = fileURLToPath(new URL('..', import.meta.url));
const RAW = join(ROOT, '_raw');
const OUT = join(ROOT, 'app', 'public', 'body');
const SCALE = Number(process.argv.find((a) => a.startsWith('--scale='))?.slice(8) ?? 1);

const FILES = ['SkeletalSystem100', 'MuscularSystem100', 'NervousSystem100', 'CardioVascular41', 'VisceralSystem100', 'Joints100', 'Regions'];

// Kategori başına sadeleştirme hatası (metre, mutlak) ve alt sınır oranı
const SIMPLIFY = {
  skin: 0.0006, hair: 0.0008,
  muscle: 0.0009, fascia: 0.0012,
  bone: 0.0005, cartilage: 0.0006, teeth: 0.0002, ligament: 0.0007,
  heart: 0.0005, artery: 0.0005, vein: 0.0005,
  cns: 0.0004, nerve: 0.0005, sense: 0.0002, meninges: 0.001,
  respiratory: 0.0008, digestive: 0.0008, urinary: 0.0006, genital: 0.0006, endocrine: 0.0003, serosa: 0.0015,
};

// ------------------------------------------------------------------ sınıflandırma
function classify(file, path, name, mat) {
  const p = path;
  switch (file) {
    case 'SkeletalSystem100':
      if (p.includes('Teeth.g')) return 'teeth';
      if (/cartilage/i.test(p) || /cartilage/i.test(name)) return 'cartilage';
      return 'bone';
    case 'MuscularSystem100':
      if (p.includes('Fasciae.g') || p.includes('Bursae') || p.includes('Tendon sheaths') || mat === 'Fascia') return 'fascia';
      return 'muscle';
    case 'NervousSystem100':
      if (p.includes('Sense organs.g')) return 'sense';
      if (p.includes('Meninges.g')) return 'meninges';
      if (p.includes('Central nervous system.g')) return 'cns';
      return 'nerve';
    case 'CardioVascular41':
      if (p.includes('Heart.g')) return 'heart';
      if (p.includes('Arterial system.g')) return 'artery';
      return 'vein';
    case 'VisceralSystem100':
      if (/Pleura|Peritoneal|Thoracic cavity|Abdominopelvic|omentum|Mesocolon|Meso-appendix/i.test(p + '/' + name)) return 'serosa';
      if (p.includes('Respiratory system.g')) return 'respiratory';
      if (p.includes('Urinary system.g')) return 'urinary';
      if (p.includes('Genital systems.g')) return 'genital';
      if (p.includes('Endocrine glands.g')) return 'endocrine';
      return 'digestive';
    case 'Joints100':
      return 'ligament';
    case 'Regions':
      if (p.includes('Hairs.g')) return 'hair';
      return 'skin';
  }
  return null;
}

function skip(name, path) {
  if (!/[a-z]/i.test(name)) return true; // bozuk kodlanmış adlar
  if (/\.(j|i|g)$/.test(name)) return true; // etiket / grup düğümleri
  if (/\.[oe]\d*[lr]?$/.test(name)) return true; // kas yapışma alanı işaretleri
  if (/segment of liver/.test(name)) return true; // karaciğer segmentleri bütün karaciğerle çakışır
  if (/Papillary process/.test(name)) return true;
  return false;
}

// ------------------------------------------------------------------ yardımcılar
function mul(m, x, y, z, out, o) {
  out[o] = m[0] * x + m[4] * y + m[8] * z + m[12];
  out[o + 1] = m[1] * x + m[5] * y + m[9] * z + m[13];
  out[o + 2] = m[2] * x + m[6] * y + m[10] * z + m[14];
}

/** Konuma göre kaynak (weld) — FBX dikişlerindeki çift vertexleri birleştirir */
function weld(pos, idx) {
  const map = new Map();
  const remap = new Uint32Array(pos.length / 3);
  const out = [];
  const q = 1e5; // 0.01 mm
  for (let i = 0; i < pos.length / 3; i++) {
    const k = `${Math.round(pos[i * 3] * q)},${Math.round(pos[i * 3 + 1] * q)},${Math.round(pos[i * 3 + 2] * q)}`;
    let j = map.get(k);
    if (j === undefined) {
      j = out.length / 3;
      map.set(k, j);
      out.push(pos[i * 3], pos[i * 3 + 1], pos[i * 3 + 2]);
    }
    remap[i] = j;
  }
  const ni = [];
  for (let t = 0; t < idx.length; t += 3) {
    const a = remap[idx[t]], b = remap[idx[t + 1]], c = remap[idx[t + 2]];
    if (a !== b && b !== c && a !== c) ni.push(a, b, c);
  }
  return { pos: new Float32Array(out), idx: new Uint32Array(ni) };
}

function compact(pos, idx) {
  const remap = new Int32Array(pos.length / 3).fill(-1);
  const out = [];
  const ni = new Uint32Array(idx.length);
  let n = 0;
  for (let i = 0; i < idx.length; i++) {
    let r = remap[idx[i]];
    if (r < 0) {
      r = remap[idx[i]] = n++;
      out.push(pos[idx[i] * 3], pos[idx[i] * 3 + 1], pos[idx[i] * 3 + 2]);
    }
    ni[i] = r;
  }
  return { pos: new Float32Array(out), idx: ni };
}

const baseName = (n) => n.replace(/\.[lr]$/, '').replace(/^\((.*)\)$/, '$1').replace(/\s+/g, ' ').trim();
const sideOf = (n) => (n.endsWith('.l') ? 'l' : n.endsWith('.r') ? 'r' : '');
const slug = (s) => s.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '');

/** Düğümün tüm primitive'lerini dünya koordinatlarında tek diziye toplar */
function extract(node, mesh) {
  const m = node.getWorldMatrix();
  const posArr = [];
  const idxArr = [];
  const matTris = new Map();
  let base = 0;
  for (const prim of mesh.listPrimitives()) {
    const pa = prim.getAttribute('POSITION');
    if (!pa) continue;
    const n = pa.getCount();
    const w = new Float32Array(n * 3);
    const el = [0, 0, 0];
    for (let i = 0; i < n; i++) {
      pa.getElement(i, el);
      mul(m, el[0], el[1], el[2], w, i * 3);
    }
    posArr.push(w);
    const ia = prim.getIndices();
    const cnt = ia ? ia.getCount() : n;
    for (let i = 0; i < cnt; i++) idxArr.push(base + (ia ? ia.getScalar(i) : i));
    base += n;
    const mn = prim.getMaterial()?.getName() ?? '';
    matTris.set(mn, (matTris.get(mn) ?? 0) + cnt / 3);
  }
  const all = new Float32Array(base * 3);
  let o = 0;
  for (const a of posArr) {
    all.set(a, o);
    o += a.length;
  }
  return { all, idxArr, matTris };
}

// ------------------------------------------------------------------ çeviri ve açıklamalar
const latin = new Map();
for (const line of readFileSync(join(RAW, 'za_repo/Resources/Translations0.txt'), 'utf8').split(/\r?\n/).slice(1)) {
  const [en, la] = line.split(';');
  if (en && la) latin.set(en.trim().toLowerCase(), la.trim());
}
const DESC_DIR = join(RAW, 'za_repo/Resources/Descriptions/OriginalDescriptions');
const descFiles = new Map(readdirSync(DESC_DIR).map((f) => [baseName(f.replace(/\.txt$/, '')).toLowerCase(), f]));

// ------------------------------------------------------------------ ana akış
await MeshoptSimplifier.ready;
await MeshoptEncoder.ready;
const io = new NodeIO().registerExtensions([EXTMeshoptCompression, KHRMeshQuantization]).registerDependencies({
  'meshopt.encoder': MeshoptEncoder,
});

const parts = [];
const groups = [];
const groupIdx = new Map();
const usedIds = new Set();
const stats = {};

for (const file of FILES) {
  const doc = await io.read(join(RAW, 'za', `${file}.glb`));
  for (const node of doc.getRoot().listNodes()) {
    const mesh = node.getMesh();
    const name = node.getName();
    if (!mesh || skip(name)) continue;

    const chain = [];
    for (let p = node.getParentNode(); p; p = p.getParentNode()) chain.unshift(p.getName());
    const path = chain.join(' / ');

    // tüm primitive'leri dünya koordinatında birleştir, baskın materyali seç
    const { all, idxArr, matTris } = extract(node, mesh);
    if (idxArr.length / 3 <= 12) continue;
    const mat = [...matTris.entries()].sort((a, b) => b[1] - a[1])[0][0].replace(/\.\d+$/, '');
    const cat = classify(file, path, name, mat);
    if (!cat) continue;

    let { pos, idx } = weld(all, new Uint32Array(idxArr));
    if (idx.length < 36) continue;

    const origTris = idx.length / 3;
    const err = SIMPLIFY[cat] * SCALE;
    const minIdx = Math.max(36, Math.floor(idx.length * 0.04 / 3) * 3);
    // Deri yamaları komşu mesh'lerle kenar paylaşır: sınırları kilitle ki aralarında çatlak oluşmasın
    const flags = cat === 'skin' ? ['ErrorAbsolute', 'LockBorder'] : ['ErrorAbsolute', 'Prune'];
    let [simp] = MeshoptSimplifier.simplify(idx, pos, 3, minIdx, err, flags);
    if (simp.length >= 36) ({ pos, idx } = compact(pos, simp));

    // ağırlık merkezi ve yarıçap
    const nv = pos.length / 3;
    const c = [0, 0, 0];
    const mn = [Infinity, Infinity, Infinity];
    const mx = [-Infinity, -Infinity, -Infinity];
    for (let i = 0; i < nv; i++)
      for (let k = 0; k < 3; k++) {
        const v = pos[i * 3 + k];
        c[k] += v / nv;
        if (v < mn[k]) mn[k] = v;
        if (v > mx[k]) mx[k] = v;
      }
    let r = 0;
    for (let i = 0; i < nv; i++)
      r = Math.max(r, Math.hypot(pos[i * 3] - c[0], pos[i * 3 + 1] - c[1], pos[i * 3 + 2] - c[2]));

    // hiyerarşi: en yakın grup (.g) düğümü ve varsa üst yapı
    const gName = [...chain].reverse().find((x) => x.endsWith('.g'))?.replace(/\.g$/, '') ?? file;
    const gPath = chain.filter((x) => x.endsWith('.g')).map((x) => x.replace(/\.g$/, ''));
    const gKey = gPath.join(' / ');
    if (!groupIdx.has(gKey)) {
      groupIdx.set(gKey, groups.length);
      groups.push(gPath);
    }
    const parentName = chain.length && !chain.at(-1).endsWith('.g') && chain.at(-1) !== 'RootNode' ? chain.at(-1) : null;

    const bn = baseName(name);
    let id = slug(name.replace(/\.([lr])$/, '-$1')) || `part-${parts.length}`;
    while (usedIds.has(id)) id += '_';
    usedIds.add(id);
    const la = latin.get(bn.toLowerCase());
    const df = descFiles.get(bn.toLowerCase());

    parts.push({
      id, cat, file, name, en: bn, la, side: sideOf(name), mat, g: groupIdx.get(gKey), gName,
      parentName, desc: df, pos, idx,
      c: c.map((x) => +x.toFixed(4)), r: +r.toFixed(4),
      mn: mn.map((x) => +x.toFixed(4)), mx: mx.map((x) => +x.toFixed(4)),
    });
    const s = (stats[cat] ??= { n: 0, before: 0, after: 0 });
    s.n++;
    s.before += origTris;
    s.after += idx.length / 3;
  }
  console.log(file, 'done, parts so far', parts.length);
}

// üst yapı adlarını id'ye çevir
const byName = new Map(parts.map((p) => [p.name, p.id]));
for (const p of parts) p.parent = p.parentName ? byName.get(p.parentName) ?? null : null;

// ------------------------------------------------------------------ çıktı
rmSync(OUT, { recursive: true, force: true });
mkdirSync(join(OUT, 'desc'), { recursive: true });

const cats = [...new Set(parts.map((p) => p.cat))];
const fileSizes = {};
for (const cat of cats) {
  const doc = new Document();
  const buf = doc.createBuffer();
  const scene = doc.createScene(cat);
  for (const p of parts.filter((x) => x.cat === cat)) {
    const pa = doc.createAccessor().setType('VEC3').setArray(p.pos).setBuffer(buf);
    const nv = p.pos.length / 3;
    const ia = doc.createAccessor().setType('SCALAR').setArray(nv > 65535 ? p.idx : new Uint16Array(p.idx)).setBuffer(buf);
    const prim = doc.createPrimitive().setAttribute('POSITION', pa).setIndices(ia);
    const mesh = doc.createMesh(p.id).addPrimitive(prim);
    scene.addChild(doc.createNode(p.id).setMesh(mesh));
  }
  await doc.transform(meshopt({ encoder: MeshoptEncoder, level: 'medium', quantizePosition: 14 }));
  const bin = await io.writeBinary(doc);
  writeFileSync(join(OUT, `${cat}.glb`), bin);
  fileSizes[cat] = bin.length;
}

let descCount = 0;
const descNames = new Set();
for (const p of parts) {
  if (!p.desc) continue;
  const target = `${slug(p.en)}.txt`;
  if (!descNames.has(target)) {
    copyFileSync(join(DESC_DIR, p.desc), join(OUT, 'desc', target));
    descNames.add(target);
    descCount++;
  }
  p.desc = slug(p.en);
}

const meta = {
  source: 'Z-Anatomy (Lluís Vinent Juanico et al.), CC BY-SA 4.0 — https://www.z-anatomy.com',
  groups: groups.map((path) => path.map((g) => {
    const la = latin.get(g.toLowerCase());
    return la ? [g, la] : [g];
  })),
  files: Object.fromEntries(cats.map((c) => [c, fileSizes[c]])),
  parts: parts.map((p) => {
    const o = { id: p.id, cat: p.cat, en: p.en, g: p.g, c: p.c, r: p.r, mn: p.mn, mx: p.mx };
    if (p.la) o.la = p.la;
    if (p.side) o.side = p.side;
    if (p.mat) o.m = p.mat;
    if (p.parent) o.parent = p.parent;
    if (p.desc) o.d = p.desc;
    return o;
  }),
};
writeFileSync(join(OUT, 'parts.json'), JSON.stringify(meta));

let tb = 0, ta = 0;
for (const [k, s] of Object.entries(stats)) {
  console.log(`${k.padEnd(12)} ${String(s.n).padStart(5)} parts ${String(s.before).padStart(8)} -> ${String(s.after).padStart(8)} tris  ${(fileSizes[k] / 1e6).toFixed(2)} MB`);
  tb += s.before;
  ta += s.after;
}
console.log('TOTAL', parts.length, 'parts', tb, '->', ta, 'tris', (Object.values(fileSizes).reduce((a, b) => a + b, 0) / 1e6).toFixed(2), 'MB');
console.log('latin', parts.filter((p) => p.la).length, 'desc', parts.filter((p) => p.desc).length, 'files', descCount);
if (!existsSync(join(OUT, 'parts.json'))) process.exit(1);
