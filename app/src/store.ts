import { create } from 'zustand';
import type { Category, Lang } from './data/categories';
import { CATEGORIES, PEEL_STEPS } from './data/categories';

export type ViewMode = 'solid' | 'xray' | 'wire';
export type ColorMode = 'anatomic' | 'function';
export type ClipAxis = 'none' | 'sagittal' | 'coronal' | 'axial';
export type CameraPreset = 'front' | 'back' | 'left' | 'right' | 'top' | 'bottom' | 'iso' | 'home' | 'focus';

/** Çift tıklamayla kaldırılan (kazılan) yapı */
export interface DigEntry {
  id: string;
  /** Birlikte kalkan yapılar (yapının kendisi + aynı kategorideki alt yapıları) */
  ids: string[];
  /** Çözülme animasyonunun yayılacağı dünya noktası (tıklanan yer) */
  origin: [number, number, number];
}

export interface QuizState {
  target: string;
  score: number;
  total: number;
  streak: number;
  /** Son cevabın sonucu */
  last: { ok: boolean; picked: string | null } | null;
}

interface State {
  lang: Lang;
  /** Kaç soyma adımı uygulandı (0 = tam vücut) */
  peelStep: number;
  hiddenCats: Set<Category>;
  hiddenParts: Set<string>;
  /** Çift tıklamayla kaldırılan yapılar (sırayla; geri alma yığını) */
  dug: DigEntry[];
  /** İzole modu: yalnız bu parçalar görünür */
  isolated: Set<string> | null;
  selected: string | null;
  /** Seçili yapı dışındakileri saydamlaştır (derin yapıları göstermek için) */
  reveal: boolean;
  hovered: string | null;
  viewMode: ViewMode;
  colorMode: ColorMode;
  clipAxis: ClipAxis;
  clipPos: number;
  clipFlip: boolean;
  cameraRequest: { preset: CameraPreset; id?: string; speed?: number; nonce: number } | null;
  panelLeft: boolean;
  panelRight: boolean;
  quiz: QuizState | null;
  /** Görüntü kalitesi: yüksek = ortam gölgelemesi, parlama, yumuşatma */
  quality: 'high' | 'fast';
  /** Kalp atışı ve solunum animasyonu */
  alive: boolean;
  /** Shader'lar derlendi, sahne çizilmeye başladı */
  sceneReady: boolean;

  setLang: (l: Lang) => void;
  peel: () => void;
  unpeel: () => void;
  setPeel: (n: number) => void;
  resetAll: () => void;
  toggleCat: (c: Category) => void;
  setCats: (cats: Category[], hidden: boolean) => void;
  showOnlyCats: (cats: Category[]) => void;
  togglePart: (id: string) => void;
  setPartsHidden: (ids: string[], hidden: boolean) => void;
  hideSelected: () => void;
  unhideAll: () => void;
  isolate: (ids: string[] | null) => void;
  select: (id: string | null, focus?: boolean) => void;
  setReveal: (v: boolean) => void;
  hover: (id: string | null) => void;
  setViewMode: (m: ViewMode) => void;
  setColorMode: (m: ColorMode) => void;
  setClipAxis: (a: ClipAxis) => void;
  setClipPos: (v: number) => void;
  toggleClipFlip: () => void;
  requestCamera: (p: CameraPreset, id?: string, speed?: number) => void;
  togglePanel: (side: 'left' | 'right', open?: boolean) => void;
  setQuiz: (q: QuizState | null) => void;
  setQuality: (q: 'high' | 'fast') => void;
  toggleAlive: () => void;
  setSceneReady: (v: boolean) => void;
  dig: (id: string, ids: string[], origin: [number, number, number]) => void;
  undoDig: () => void;
  restoreDigs: () => void;
}

let nonce = 0;
const defaultHidden = () => new Set(CATEGORIES.filter((c) => c.hidden).map((c) => c.id));
const isNarrow = () => typeof window !== 'undefined' && window.innerWidth < 900;

export const useStore = create<State>((set, get) => ({
  lang: (() => {
    try {
      const l = localStorage.getItem('anatomi-lang');
      if (l === 'en' || l === 'tr') return l;
    } catch {
      /* depolama yok */
    }
    return 'tr';
  })(),
  peelStep: 0,
  hiddenCats: defaultHidden(),
  hiddenParts: new Set(),
  dug: [],
  isolated: null,
  selected: null,
  reveal: false,
  hovered: null,
  viewMode: 'solid',
  colorMode: 'anatomic',
  clipAxis: 'none',
  clipPos: 0,
  clipFlip: false,
  cameraRequest: null,
  panelLeft: !isNarrow(),
  panelRight: !isNarrow(),
  quiz: null,
  quality: (() => {
    try {
      const q = localStorage.getItem('anatomi-quality');
      if (q === 'high' || q === 'fast') return q;
    } catch {
      /* depolama yok */
    }
    return isNarrow() ? 'fast' : 'high';
  })(),
  alive: true,
  sceneReady: false,

  setLang: (lang) => {
    try {
      localStorage.setItem('anatomi-lang', lang);
    } catch {
      /* depolama yok */
    }
    document.documentElement.lang = lang;
    set({ lang });
  },
  peel: () => set((s) => ({ peelStep: Math.min(PEEL_STEPS.length, s.peelStep + 1) })),
  unpeel: () => set((s) => ({ peelStep: Math.max(0, s.peelStep - 1) })),
  setPeel: (peelStep) => set({ peelStep }),
  resetAll: () =>
    set({
      peelStep: 0,
      hiddenCats: defaultHidden(),
      hiddenParts: new Set(),
      dug: [],
      isolated: null,
      selected: null,
      clipAxis: 'none',
      clipPos: 0,
      viewMode: 'solid',
      colorMode: 'anatomic',
      cameraRequest: { preset: 'home', nonce: ++nonce },
    }),
  toggleCat: (c) =>
    set((s) => {
      const n = new Set(s.hiddenCats);
      if (n.has(c)) n.delete(c);
      else n.add(c);
      return { hiddenCats: n };
    }),
  setCats: (cats, hidden) =>
    set((s) => {
      const n = new Set(s.hiddenCats);
      for (const c of cats) {
        if (hidden) n.add(c);
        else n.delete(c);
      }
      return { hiddenCats: n };
    }),
  showOnlyCats: (cats) =>
    set({
      hiddenCats: new Set(CATEGORIES.map((c) => c.id).filter((c) => !cats.includes(c))),
      peelStep: 0,
      isolated: null,
    }),
  togglePart: (id) =>
    set((s) => {
      const n = new Set(s.hiddenParts);
      if (n.has(id)) n.delete(id);
      else n.add(id);
      return { hiddenParts: n, selected: s.selected === id && !n.has(id) ? s.selected : s.selected === id ? null : s.selected };
    }),
  setPartsHidden: (ids, hidden) =>
    set((s) => {
      const n = new Set(s.hiddenParts);
      for (const id of ids) {
        if (hidden) n.add(id);
        else n.delete(id);
      }
      return { hiddenParts: n };
    }),
  hideSelected: () => {
    const { selected } = get();
    if (!selected) return;
    set((s) => ({ hiddenParts: new Set(s.hiddenParts).add(selected), selected: null }));
  },
  unhideAll: () => set({ hiddenParts: new Set(), hiddenCats: defaultHidden(), isolated: null, peelStep: 0, dug: [] }),
  isolate: (ids) =>
    set(() => {
      if (!ids) return { isolated: null, cameraRequest: { preset: 'home', nonce: ++nonce } };
      return { isolated: new Set(ids), cameraRequest: { preset: 'focus', id: ids[0], nonce: ++nonce } };
    }),
  select: (selected, focus) =>
    set(() => ({
      selected,
      reveal: !!(focus && selected),
      ...(focus && selected ? { cameraRequest: { preset: 'focus' as const, id: selected, nonce: ++nonce } } : {}),
      ...(selected && isNarrow() ? { panelRight: true, panelLeft: false } : {}),
    })),
  setReveal: (reveal) => set({ reveal }),
  hover: (hovered) => set({ hovered }),
  setViewMode: (viewMode) => set({ viewMode }),
  setColorMode: (colorMode) => set({ colorMode }),
  setClipAxis: (clipAxis) => set({ clipAxis, clipPos: 0 }),
  setClipPos: (clipPos) => set({ clipPos }),
  toggleClipFlip: () => set((s) => ({ clipFlip: !s.clipFlip })),
  requestCamera: (preset, id, speed) => set({ cameraRequest: { preset, id, speed, nonce: ++nonce } }),
  togglePanel: (side, open) =>
    set((s) => {
      const cur = side === 'left' ? s.panelLeft : s.panelRight;
      const v = open ?? !cur;
      const other = isNarrow() && v ? (side === 'left' ? { panelRight: false } : { panelLeft: false }) : {};
      return side === 'left' ? { panelLeft: v, ...other } : { panelRight: v, ...other };
    }),
  setQuiz: (quiz) => set({ quiz }),
  setQuality: (quality) => {
    try {
      localStorage.setItem('anatomi-quality', quality);
    } catch {
      /* depolama yok */
    }
    set({ quality });
  },
  toggleAlive: () => set((s) => ({ alive: !s.alive })),
  setSceneReady: (sceneReady) => set({ sceneReady }),
  dig: (id, ids, origin) =>
    set((s) => {
      if (s.dug.some((d) => d.ids.includes(id))) return {};
      return {
        dug: [...s.dug, { id, ids, origin }],
        selected: s.selected && ids.includes(s.selected) ? null : s.selected,
        hovered: s.hovered && ids.includes(s.hovered) ? null : s.hovered,
      };
    }),
  undoDig: () => set((s) => ({ dug: s.dug.slice(0, -1) })),
  restoreDigs: () => set({ dug: [] }),
}));

/** Soyma adımına göre bir kategori soyulmuş mu */
export function isPeeled(cat: Category, step: number): boolean {
  for (let i = 0; i < step && i < PEEL_STEPS.length; i++) if (PEEL_STEPS[i].includes(cat)) return true;
  return false;
}
