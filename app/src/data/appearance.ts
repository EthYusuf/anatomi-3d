import type { Part } from '../model';

/** Prosedürel yüzey detayı (shader'da hesaplanır, doku dosyası gerekmez) */
export const DETAIL = {
  none: 0,
  muscle: 1,
  tendon: 2,
  bone: 3,
  skin: 4,
  wet: 5,
  iris: 6,
  sclera: 7,
  brain: 8,
  nerve: 9,
  vessel: 10,
} as const;

export interface Appearance {
  color?: string;
  roughness: number;
  /** Islak/parlak yüzey katmanı */
  clearcoat?: number;
  clearcoatRoughness?: number;
  /** Kadife benzeri kenar ışığı (kas lifleri, deri altı saçılımı) */
  sheen?: number;
  sheenColor?: string;
  sheenRoughness?: number;
  detail: number;
  /** Kabartma şiddeti */
  bump: number;
  /** Detay deseninin frekans çarpanı */
  freq?: number;
  /** Saydam, cam gibi yapı (kornea, lens) */
  glass?: boolean;
  /** Camın opaklığı (0 = tamamen görünmez sıvı; ör. ön kamara) */
  glassOpacity?: number;
}

export function appearanceOf(p: Part): Appearance {
  const en = p.en.toLowerCase();
  // Göz: kornea ince ve çok parlak bir cam; ön kamara sıvısı görünmez; göz bebeğinin
  // arkasındaki vitreus koyu, böylece göz bebeği gerçekteki gibi siyah görünür
  if (en === 'cornea') return { roughness: 0.02, clearcoat: 1, clearcoatRoughness: 0.01, detail: DETAIL.none, bump: 0, glass: true, glassOpacity: 0.05 };
  if (en === 'lens') return { roughness: 0.05, detail: DETAIL.none, bump: 0, glass: true, glassOpacity: 0 };
  if (/^anterior (chamber|segment) of eyeball$/.test(en)) return { roughness: 0, detail: DETAIL.none, bump: 0, glass: true, glassOpacity: 0 };
  // Zonula lifleri irisin arkasında kalır; önden bakınca görünmesin (seçilince görünür)
  if (en === 'zonular fibres') return { color: '#e8e2d4', roughness: 0.4, detail: DETAIL.none, bump: 0, glass: true, glassOpacity: 0 };
  if (en === 'vitreous body') return { color: '#0b0707', roughness: 0.9, detail: DETAIL.none, bump: 0 };
  if (en === 'iris') return { color: '#ffffff', roughness: 0.55, clearcoat: 0.6, clearcoatRoughness: 0.1, detail: DETAIL.iris, bump: 1.2 };
  if (en === 'sclera') return { color: '#f2ece2', roughness: 0.3, clearcoat: 1, clearcoatRoughness: 0.06, detail: DETAIL.sclera, bump: 0.2 };

  switch (p.cat) {
    case 'skin':
      if (p.m === 'Nail') return { color: '#e9cfc2', roughness: 0.25, clearcoat: 0.7, clearcoatRoughness: 0.15, detail: DETAIL.none, bump: 0 };
      return {
        color: '#d4a28e', roughness: 0.5, clearcoat: 0.08, clearcoatRoughness: 0.5,
        sheen: 0.3, sheenColor: '#ff8a6c', sheenRoughness: 0.55, detail: DETAIL.skin, bump: 0.12,
      };
    case 'hair':
      return { color: '#2a1d16', roughness: 0.6, sheen: 0.6, sheenColor: '#8a6a50', sheenRoughness: 0.35, detail: DETAIL.none, bump: 0 };
    case 'muscle':
      if (p.m === 'Tendon')
        return {
          color: '#e6dccb', roughness: 0.32, clearcoat: 0.5, clearcoatRoughness: 0.25,
          sheen: 0.6, sheenColor: '#ffffff', sheenRoughness: 0.3, detail: DETAIL.tendon, bump: 0.6,
        };
      return {
        color: '#a8352c', roughness: 0.48, clearcoat: 0.35, clearcoatRoughness: 0.35,
        sheen: 0.45, sheenColor: '#ff9d8a', sheenRoughness: 0.4, detail: DETAIL.muscle, bump: 1.1,
      };
    case 'fascia':
      return { color: '#e3dccb', roughness: 0.35, clearcoat: 0.6, clearcoatRoughness: 0.2, sheen: 0.4, sheenColor: '#ffffff', detail: DETAIL.tendon, bump: 0.4 };
    case 'bone':
      return { color: '#ecdcb8', roughness: 0.6, clearcoat: 0.06, detail: DETAIL.bone, bump: 0.7 };
    case 'teeth':
      return { color: p.m === 'Teeth-roots' ? '#e8d9b4' : '#f4f0e6', roughness: 0.18, clearcoat: 1, clearcoatRoughness: 0.08, detail: DETAIL.none, bump: 0 };
    case 'cartilage':
      return { color: '#b7d3d6', roughness: 0.28, clearcoat: 0.7, clearcoatRoughness: 0.15, sheen: 0.3, sheenColor: '#e8ffff', detail: DETAIL.wet, bump: 0.2 };
    case 'ligament':
      if (p.m === 'Cartilage') return { color: '#b9d5d8', roughness: 0.25, clearcoat: 0.8, clearcoatRoughness: 0.12, detail: DETAIL.wet, bump: 0.15 };
      return { color: '#d8cfb4', roughness: 0.35, clearcoat: 0.45, clearcoatRoughness: 0.3, sheen: 0.5, sheenColor: '#ffffff', detail: DETAIL.tendon, bump: 0.5 };
    case 'artery':
      return {
        color: p.m === 'Pulmonary artery' ? '#3d5fc4' : '#b8231f', roughness: 0.3, clearcoat: 0.7, clearcoatRoughness: 0.15,
        detail: DETAIL.vessel, bump: 0.25,
      };
    case 'vein':
      return {
        color: p.m === 'Pulmonary vein' ? '#c2393c' : '#34468f', roughness: 0.32, clearcoat: 0.7, clearcoatRoughness: 0.15,
        detail: DETAIL.vessel, bump: 0.25,
      };
    case 'heart':
      if (/leaflet|valve/i.test(p.en)) return { color: '#e8d2b8', roughness: 0.3, clearcoat: 0.6, detail: DETAIL.wet, bump: 0.2 };
      return {
        color: '#9c2a26', roughness: 0.42, clearcoat: 0.55, clearcoatRoughness: 0.25,
        sheen: 0.35, sheenColor: '#ff9d8a', detail: DETAIL.muscle, bump: 0.7, freq: 0.7,
      };
    case 'nerve':
      return { color: '#efd36a', roughness: 0.35, clearcoat: 0.4, clearcoatRoughness: 0.3, sheen: 0.5, sheenColor: '#fff4c0', detail: DETAIL.nerve, bump: 0.6 };
    case 'cns':
      return { color: undefined, roughness: 0.45, clearcoat: 0.35, clearcoatRoughness: 0.35, detail: DETAIL.brain, bump: 0.5 };
    case 'meninges':
      return { color: '#d6ccd8', roughness: 0.3, clearcoat: 0.6, detail: DETAIL.wet, bump: 0.2 };
    case 'sense':
      return { color: undefined, roughness: 0.35, clearcoat: 0.5, detail: DETAIL.wet, bump: 0.3 };
    case 'respiratory':
      if (/lobe of/i.test(p.en)) return { color: '#e3a0a0', roughness: 0.6, clearcoat: 0.45, clearcoatRoughness: 0.3, sheen: 0.4, sheenColor: '#ffd0d0', detail: DETAIL.wet, bump: 0.9, freq: 1.8 };
      return { color: '#e6d5c0', roughness: 0.35, clearcoat: 0.5, detail: DETAIL.tendon, bump: 0.4 };
    case 'digestive': {
      const organ: Record<string, string> = {
        liver: '#7a2f25', gallbladder: '#4f7a3a', pancreas: '#e0b38a', stomach: '#d99a8a', tongue: '#c9605f',
        oesophagus: '#d08a7c', duodenum: '#dca08a', jejunum: '#e0a590',
      };
      return { color: organ[p.en.toLowerCase()], roughness: 0.38, clearcoat: 0.75, clearcoatRoughness: 0.18, sheen: 0.25, sheenColor: '#ffc0b0', detail: DETAIL.wet, bump: 0.5 };
    }
    case 'urinary':
      return { color: p.en === 'Kidney' ? '#8a3027' : undefined, roughness: 0.38, clearcoat: 0.7, clearcoatRoughness: 0.2, detail: DETAIL.wet, bump: 0.4 };
    case 'genital':
    case 'endocrine':
      return { color: undefined, roughness: 0.4, clearcoat: 0.6, clearcoatRoughness: 0.2, detail: DETAIL.wet, bump: 0.4 };
    case 'serosa':
      return { color: '#eadccc', roughness: 0.25, clearcoat: 0.8, detail: DETAIL.wet, bump: 0.2 };
  }
  return { roughness: 0.5, detail: DETAIL.none, bump: 0 };
}
