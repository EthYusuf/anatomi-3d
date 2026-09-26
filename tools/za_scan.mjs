// Z-Anatomy GLB dosyalarındaki mesh düğümlerini dünya koordinatlarında sınır kutularıyla listeler.
import { NodeIO } from '@gltf-transform/core';
import { writeFileSync } from 'node:fs';

const DIR = new URL('../_raw/za/', import.meta.url);
const FILES = ['SkeletalSystem100', 'MuscularSystem100', 'NervousSystem100', 'CardioVascular41', 'VisceralSystem100', 'Joints100', 'Regions'];

function mul(m, v) {
  return [
    m[0] * v[0] + m[4] * v[1] + m[8] * v[2] + m[12],
    m[1] * v[0] + m[5] * v[1] + m[9] * v[2] + m[13],
    m[2] * v[0] + m[6] * v[1] + m[10] * v[2] + m[14],
  ];
}

const io = new NodeIO();
const out = [];
for (const f of FILES) {
  const doc = await io.read(new URL(`${f}.glb`, DIR).pathname.slice(1));
  for (const node of doc.getRoot().listNodes()) {
    const mesh = node.getMesh();
    if (!mesh) continue;
    const m = node.getWorldMatrix();
    const mn = [Infinity, Infinity, Infinity];
    const mx = [-Infinity, -Infinity, -Infinity];
    let verts = 0, tris = 0;
    for (const prim of mesh.listPrimitives()) {
      const pos = prim.getAttribute('POSITION');
      const n = pos.getCount();
      verts += n;
      tris += (prim.getIndices()?.getCount() ?? n) / 3;
      const el = [0, 0, 0];
      for (let i = 0; i < n; i += 7) {
        pos.getElement(i, el);
        const w = mul(m, el);
        for (let k = 0; k < 3; k++) {
          if (w[k] < mn[k]) mn[k] = w[k];
          if (w[k] > mx[k]) mx[k] = w[k];
        }
      }
    }
    const parents = [];
    let p = node.getParentNode();
    while (p) {
      parents.unshift(p.getName());
      p = p.getParentNode();
    }
    out.push({ file: f, name: node.getName(), path: parents.join(' / '), verts, tris, mn, mx });
  }
  console.log(f, out.length);
}
writeFileSync(new URL('../_raw/za/scan.json', import.meta.url).pathname.slice(1), JSON.stringify(out));
