export type Lang = 'tr' | 'en';

export type Category =
  | 'hair'
  | 'skin'
  | 'fascia'
  | 'muscle'
  | 'vein'
  | 'artery'
  | 'heart'
  | 'nerve'
  | 'cns'
  | 'meninges'
  | 'sense'
  | 'respiratory'
  | 'digestive'
  | 'urinary'
  | 'genital'
  | 'endocrine'
  | 'serosa'
  | 'ligament'
  | 'cartilage'
  | 'teeth'
  | 'bone';

export type SystemId = 'integument' | 'muscular' | 'skeletal' | 'cardio' | 'nervous' | 'visceral';

export interface CategoryInfo {
  id: Category;
  sys: SystemId;
  tr: string;
  en: string;
  color: string;
  roughness: number;
  /** Katman soyma adımı (dıştan içe, 1'den başlar). null = soyulmaz */
  peel: number | null;
  /** Açılışta gizli */
  hidden?: boolean;
}

export interface SystemInfo {
  id: SystemId;
  tr: string;
  en: string;
  icon: string;
}

export const SYSTEMS: SystemInfo[] = [
  { id: 'integument', tr: 'Deri', en: 'Integument', icon: '◐' },
  { id: 'muscular', tr: 'Kas Sistemi', en: 'Muscular System', icon: '≋' },
  { id: 'skeletal', tr: 'İskelet ve Eklemler', en: 'Skeleton & Joints', icon: '⟟' },
  { id: 'cardio', tr: 'Dolaşım Sistemi', en: 'Cardiovascular System', icon: '♥' },
  { id: 'nervous', tr: 'Sinir Sistemi', en: 'Nervous System', icon: '⚡' },
  { id: 'visceral', tr: 'İç Organlar', en: 'Viscera', icon: '◍' },
];

export const CATEGORIES: CategoryInfo[] = [
  { id: 'hair', sys: 'integument', tr: 'Kıllar', en: 'Hair', color: '#3a2c24', roughness: 0.8, peel: 1 },
  { id: 'skin', sys: 'integument', tr: 'Deri', en: 'Skin', color: '#d8a88e', roughness: 0.55, peel: 1 },
  { id: 'fascia', sys: 'muscular', tr: 'Fasya, Bursa ve Kılıflar', en: 'Fasciae, Bursae & Sheaths', color: '#d9d2bd', roughness: 0.5, peel: 2, hidden: true },
  { id: 'muscle', sys: 'muscular', tr: 'Kaslar', en: 'Muscles', color: '#b8433c', roughness: 0.55, peel: 3 },
  { id: 'vein', sys: 'cardio', tr: 'Venler', en: 'Veins', color: '#3c5fc4', roughness: 0.4, peel: 4 },
  { id: 'artery', sys: 'cardio', tr: 'Arterler', en: 'Arteries', color: '#d8322f', roughness: 0.35, peel: 5 },
  { id: 'nerve', sys: 'nervous', tr: 'Periferik Sinirler', en: 'Peripheral Nerves', color: '#f0cf3c', roughness: 0.45, peel: 6 },
  { id: 'heart', sys: 'cardio', tr: 'Kalp', en: 'Heart', color: '#b83a3a', roughness: 0.45, peel: 7 },
  { id: 'respiratory', sys: 'visceral', tr: 'Solunum Sistemi', en: 'Respiratory System', color: '#e79a9e', roughness: 0.6, peel: 7 },
  { id: 'digestive', sys: 'visceral', tr: 'Sindirim Sistemi', en: 'Digestive System', color: '#d98870', roughness: 0.5, peel: 7 },
  { id: 'urinary', sys: 'visceral', tr: 'Üriner Sistem', en: 'Urinary System', color: '#c9795b', roughness: 0.5, peel: 7 },
  { id: 'genital', sys: 'visceral', tr: 'Genital Sistem', en: 'Genital System', color: '#d98c9c', roughness: 0.5, peel: 7 },
  { id: 'endocrine', sys: 'visceral', tr: 'Endokrin Bezler', en: 'Endocrine Glands', color: '#e0b25a', roughness: 0.55, peel: 7 },
  { id: 'serosa', sys: 'visceral', tr: 'Periton ve Plevra', en: 'Peritoneum & Pleura', color: '#e8d6c4', roughness: 0.4, peel: 7, hidden: true },
  { id: 'cns', sys: 'nervous', tr: 'Beyin ve Omurilik', en: 'Brain & Spinal Cord', color: '#e7b7bb', roughness: 0.6, peel: null },
  { id: 'meninges', sys: 'nervous', tr: 'Meninksler', en: 'Meninges', color: '#cfc6d6', roughness: 0.45, peel: null, hidden: true },
  { id: 'sense', sys: 'nervous', tr: 'Duyu Organları', en: 'Sense Organs', color: '#eef0f2', roughness: 0.25, peel: null },
  { id: 'ligament', sys: 'skeletal', tr: 'Eklemler ve Bağlar', en: 'Joints & Ligaments', color: '#c9c3a6', roughness: 0.55, peel: null },
  { id: 'cartilage', sys: 'skeletal', tr: 'Kıkırdaklar', en: 'Cartilages', color: '#a9cdd1', roughness: 0.4, peel: null },
  { id: 'teeth', sys: 'skeletal', tr: 'Dişler', en: 'Teeth', color: '#f6f2e6', roughness: 0.25, peel: null },
  { id: 'bone', sys: 'skeletal', tr: 'Kemikler', en: 'Bones', color: '#e6dcc3', roughness: 0.7, peel: null },
];

export const CAT: Record<Category, CategoryInfo> = Object.fromEntries(
  CATEGORIES.map((c) => [c.id, c]),
) as Record<Category, CategoryInfo>;

/** Soyma adımları: her adımda soyulan kategoriler */
export const PEEL_STEPS: Category[][] = (() => {
  const steps = new Map<number, Category[]>();
  for (const c of CATEGORIES) if (c.peel !== null) steps.set(c.peel, [...(steps.get(c.peel) ?? []), c.id]);
  return [...steps.entries()].sort((a, b) => a[0] - b[0]).map((e) => e[1]);
})();

/** Z-Anatomy materyal adlarına göre ince renk ayarı (beyin lobları, akciğer lobları vb.) */
export const MATERIAL_TINT: Record<string, string> = {
  'Frontal lobe': '#e3a9a0',
  'Parietal lobe': '#d9b48c',
  'Temporal lobe': '#a9c2a0',
  'Occipital lobe': '#a6b5d6',
  Cerebellum: '#d7a1c4',
  'White matter': '#f1ece2',
  'Brain-Inner': '#dcc0c0',
  Nucleus: '#b89ad0',
  'Nucleus (afferent fibers)': '#8fb4e0',
  'Nucleus (efferent fibers)': '#e59a8c',
  LCR: '#9fd3e6',
  'Interlobar sulci': '#c8a3a6',
  Tendon: '#e9e2d0',
  'Teeth-roots': '#eadcb8',
  Iris: '#5b7c9c',
  Cornea: '#dfeff4',
  Mucosa: '#e39a9a',
  Gland: '#e0b25a',
  Ductus: '#d9c46a',
  Gallbladder: '#6f9b4c',
  Intestine: '#dca08a',
  'Pulmonary artery': '#4a6fd8',
  'Pulmonary vein': '#d8424a',
  Nail: '#f0d8cc',
  'Articular capsule': '#d7cfb2',
  Bursa: '#b9d3e0',
  Fat: '#f1d98a',
  Bronchi: '#dfd2c0',
  'Lung-1': '#e8a0a6', 'Lung-3': '#e3b0a0', 'Lung-4': '#dca4b6', 'Lung-6': '#e9b4a8', 'Lung-8': '#d99ca8',
};

/** Kas fonksiyon renklendirmesi (isteğe bağlı mod) */
export const MUSCLE_FUNCTION: Record<string, { color: string; tr: string; en: string }> = {
  Flexion: { color: '#e5484d', tr: 'Fleksiyon', en: 'Flexion' },
  'Flexion hand/foot': { color: '#f76b15', tr: 'El/ayak fleksiyonu', en: 'Hand/foot flexion' },
  'Flexion fingers': { color: '#ffb224', tr: 'Parmak fleksiyonu', en: 'Finger flexion' },
  Extension: { color: '#3e63dd', tr: 'Ekstansiyon', en: 'Extension' },
  'Extension hand/foot': { color: '#0090ff', tr: 'El/ayak ekstansiyonu', en: 'Hand/foot extension' },
  'Extensor extremities': { color: '#00a2c7', tr: 'Parmak ekstansiyonu', en: 'Finger extension' },
  Abductor: { color: '#30a46c', tr: 'Abdüksiyon', en: 'Abduction' },
  Adductor: { color: '#8e4ec6', tr: 'Addüksiyon', en: 'Adduction' },
  'External rotation': { color: '#12a594', tr: 'Dış rotasyon', en: 'External rotation' },
  'Internal rotator': { color: '#d6409f', tr: 'İç rotasyon', en: 'Internal rotation' },
  Levator: { color: '#99d52a', tr: 'Elevasyon', en: 'Elevation' },
  Depressor: { color: '#ad7f58', tr: 'Depresyon', en: 'Depression' },
  'Orbicularis/Constrictor': { color: '#e93d82', tr: 'Sfinkter/Konstriktör', en: 'Sphincter/Constrictor' },
  Masticator: { color: '#c2298a', tr: 'Çiğneme', en: 'Mastication' },
  Ingestion: { color: '#f5d90a', tr: 'Yutma', en: 'Swallowing' },
  Phonation: { color: '#7c66dc', tr: 'Fonasyon', en: 'Phonation' },
  Biarticular: { color: '#46a758', tr: 'İki eklemli', en: 'Biarticular' },
  Superficial: { color: '#e38b6b', tr: 'Yüzeyel', en: 'Superficial' },
  Diaphragm: { color: '#d4a5a5', tr: 'Solunum', en: 'Respiration' },
  Trapezius: { color: '#6e56cf', tr: 'Omuz kuşağı', en: 'Shoulder girdle' },
  Tendon: { color: '#e9e2d0', tr: 'Tendon', en: 'Tendon' },
};
