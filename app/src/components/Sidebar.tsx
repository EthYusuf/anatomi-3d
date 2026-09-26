import { memo, useEffect, useMemo, useRef, useState } from 'react';
import { CAT, CATEGORIES, SYSTEMS, type Category, type SystemId } from '../data/categories';
import { t } from '../data/i18n';
import { displayName, normalize, secondaryName, type ModelData, type Part } from '../model';
import { SEARCH_SYNONYMS } from '../data/tr';
import { useStore } from '../store';
import { EyeIcon, EyeOffIcon, ChevronIcon, SearchIcon, CloseIcon } from './icons';

interface TreeNode {
  key: string;
  label: string;
  sub?: string;
  children: TreeNode[];
  parts: Part[];
  /** Bu düğümün altındaki tüm parça id'leri */
  all: string[];
}

function buildTree(model: ModelData, lang: 'tr' | 'en'): TreeNode[] {
  const roots: TreeNode[] = [];
  const sysNodes = new Map<SystemId, TreeNode>();
  for (const s of SYSTEMS) {
    const n: TreeNode = { key: s.id, label: s[lang], children: [], parts: [], all: [] };
    sysNodes.set(s.id, n);
    roots.push(n);
  }
  const index = new Map<string, TreeNode>();
  for (const p of model.parts) {
    const sys = sysNodes.get(CAT[p.cat].sys)!;
    const path = model.groups[p.g] ?? [];
    let node = sys;
    let key = sys.key;
    for (const [en, la] of path) {
      key += '/' + en;
      let child = index.get(key);
      if (!child) {
        child = {
          key,
          label: lang === 'tr' ? la ?? en : en,
          sub: lang === 'tr' ? (la ? en : undefined) : la,
          children: [],
          parts: [],
          all: [],
        };
        index.set(key, child);
        node.children.push(child);
      }
      node = child;
    }
    node.parts.push(p);
  }
  const fill = (n: TreeNode): string[] => {
    n.all = [...n.parts.map((p) => p.id), ...n.children.flatMap(fill)];
    return n.all;
  };
  roots.forEach(fill);
  return roots.filter((r) => r.all.length);
}

const TreeRow = memo(function TreeRow({ node, depth, lang }: { node: TreeNode; depth: number; lang: 'tr' | 'en' }) {
  const [open, setOpen] = useState(false);
  const hiddenParts = useStore((s) => s.hiddenParts);
  const setPartsHidden = useStore((s) => s.setPartsHidden);
  const isolate = useStore((s) => s.isolate);
  const hiddenCount = node.all.reduce((a, id) => a + (hiddenParts.has(id) ? 1 : 0), 0);
  const allHidden = hiddenCount === node.all.length;

  return (
    <li>
      <div className="tree-row" style={{ paddingLeft: 6 + depth * 12 }}>
        <button className="tree-toggle" onClick={() => setOpen(!open)} aria-expanded={open}>
          <ChevronIcon className={open ? 'rot' : ''} />
        </button>
        <button className="tree-label" onClick={() => setOpen(!open)} title={node.sub}>
          <span className={allHidden ? 'dim' : ''}>{node.label}</span>
          <span className="count">{node.all.length}</span>
        </button>
        <button
          className="icon-btn sm"
          title={t('isolateGroup', lang)}
          onClick={() => isolate(node.all)}
          aria-label={t('isolateGroup', lang)}
        >
          ◎
        </button>
        <button
          className={`icon-btn sm ${hiddenCount && !allHidden ? 'partial' : ''}`}
          onClick={() => setPartsHidden(node.all, !allHidden)}
          aria-label={allHidden ? t('show', lang) : t('hide', lang)}
        >
          {allHidden ? <EyeOffIcon /> : <EyeIcon />}
        </button>
      </div>
      {open && (
        <ul>
          {node.children.map((c) => (
            <TreeRow key={c.key} node={c} depth={depth + 1} lang={lang} />
          ))}
          {node.parts.map((p) => (
            <PartRow key={p.id} part={p} depth={depth + 1} lang={lang} />
          ))}
        </ul>
      )}
    </li>
  );
});

function PartRow({ part, depth, lang }: { part: Part; depth: number; lang: 'tr' | 'en' }) {
  const hidden = useStore((s) => s.hiddenParts.has(part.id));
  const selected = useStore((s) => s.selected === part.id);
  const togglePart = useStore((s) => s.togglePart);
  const select = useStore((s) => s.select);
  return (
    <li>
      <div className={`tree-row part ${selected ? 'sel' : ''}`} style={{ paddingLeft: 6 + depth * 12 + 18 }}>
        <span className="dot" style={{ background: CAT[part.cat].color }} />
        <button className="tree-label" onClick={() => select(part.id, true)} title={secondaryName(part, lang)}>
          <span className={hidden ? 'dim' : ''}>{displayName(part, lang)}</span>
        </button>
        <button className="icon-btn sm" onClick={() => togglePart(part.id)} aria-label={hidden ? t('show', lang) : t('hide', lang)}>
          {hidden ? <EyeOffIcon /> : <EyeIcon />}
        </button>
      </div>
    </li>
  );
}

function SystemsTab({ model, lang }: { model: ModelData; lang: 'tr' | 'en' }) {
  const hiddenCats = useStore((s) => s.hiddenCats);
  const toggleCat = useStore((s) => s.toggleCat);
  const setCats = useStore((s) => s.setCats);
  const showOnlyCats = useStore((s) => s.showOnlyCats);
  const counts = useMemo(() => {
    const m = new Map<Category, number>();
    for (const p of model.parts) m.set(p.cat, (m.get(p.cat) ?? 0) + 1);
    return m;
  }, [model]);

  return (
    <div className="systems">
      {SYSTEMS.map((s) => {
        const cats = CATEGORIES.filter((c) => c.sys === s.id && counts.get(c.id));
        if (!cats.length) return null;
        const allHidden = cats.every((c) => hiddenCats.has(c.id));
        return (
          <section key={s.id} className="sys">
            <header>
              <span className="sys-icon" aria-hidden>
                {s.icon}
              </span>
              <h3>{s[lang]}</h3>
              <button className="link" onClick={() => showOnlyCats(cats.map((c) => c.id))} title={t('showOnly', lang)}>
                {t('showOnly', lang)}
              </button>
              <button
                className="icon-btn sm"
                onClick={() => setCats(cats.map((c) => c.id), !allHidden)}
                aria-label={allHidden ? t('show', lang) : t('hide', lang)}
              >
                {allHidden ? <EyeOffIcon /> : <EyeIcon />}
              </button>
            </header>
            <ul>
              {cats.map((c) => {
                const off = hiddenCats.has(c.id);
                return (
                  <li key={c.id}>
                    <label className={`cat ${off ? 'off' : ''}`}>
                      <input type="checkbox" checked={!off} onChange={() => toggleCat(c.id)} />
                      <span className="swatch" style={{ background: c.color }} />
                      <span className="name">{c[lang]}</span>
                      <span className="count">{counts.get(c.id)}</span>
                    </label>
                  </li>
                );
              })}
            </ul>
          </section>
        );
      })}
    </div>
  );
}

function SearchResults({ model, q, lang, onPick }: { model: ModelData; q: string; lang: 'tr' | 'en'; onPick: () => void }) {
  const select = useStore((s) => s.select);
  const setCats = useStore((s) => s.setCats);
  const setPartsHidden = useStore((s) => s.setPartsHidden);
  const results = useMemo(() => {
    const terms = normalize(q).split(/\s+/).filter(Boolean);
    if (!terms.length) return [];
    // Türkçe genel terimleri İngilizce karşılıklarıyla da ara (ör. "kalp" -> "heart")
    const alts = terms.map((w) => [w, ...SEARCH_SYNONYMS.filter(([tr]) => tr === w).map(([, en]) => en)]);
    const out: { p: Part; score: number }[] = [];
    for (const p of model.parts) {
      if (!alts.every((a) => a.some((w) => p.search.includes(w)))) continue;
      const starts = p.search.startsWith(terms[0]) || normalize(p.la ?? '').startsWith(terms[0]);
      out.push({ p, score: (starts ? 0 : 10) + p.search.length / 100 });
    }
    return out.sort((a, b) => a.score - b.score).slice(0, 80).map((x) => x.p);
  }, [model, q]);

  if (!results.length) return <p className="empty">{t('noResults', lang)}</p>;
  return (
    <ul className="results">
      {results.map((p) => (
        <li key={p.id}>
          <button
            onClick={() => {
              // gizli kategorideyse görünür yap
              setCats([p.cat], false);
              setPartsHidden([p.id], false);
              select(p.id, true);
              onPick();
            }}
          >
            <span className="dot" style={{ background: CAT[p.cat].color }} />
            <span className="r-main">{displayName(p, lang)}</span>
            {secondaryName(p, lang) && <span className="r-sub">{secondaryName(p, lang)}</span>}
          </button>
        </li>
      ))}
    </ul>
  );
}

export default function Sidebar({ model }: { model: ModelData }) {
  const lang = useStore((s) => s.lang);
  const open = useStore((s) => s.panelLeft);
  const togglePanel = useStore((s) => s.togglePanel);
  const [tab, setTab] = useState<'systems' | 'tree'>('systems');
  const [q, setQ] = useState('');
  const tree = useMemo(() => buildTree(model, lang), [model, lang]);
  const inputRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'k') {
        e.preventDefault();
        togglePanel('left', true);
        setTimeout(() => inputRef.current?.focus(), 30);
      }
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [togglePanel]);

  return (
    <aside className={`panel left ${open ? 'open' : ''}`} aria-hidden={!open}>
      <div className="search">
        <SearchIcon />
        <input
          ref={inputRef}
          value={q}
          onChange={(e) => setQ(e.target.value)}
          placeholder={t('search', lang)}
          onKeyDown={(e) => e.key === 'Escape' && setQ('')}
          aria-label={t('search', lang)}
        />
        {q && (
          <button className="icon-btn sm" onClick={() => setQ('')} aria-label="clear">
            <CloseIcon />
          </button>
        )}
      </div>
      {q.trim() ? (
        <div className="panel-body">
          <SearchResults model={model} q={q} lang={lang} onPick={() => window.innerWidth < 900 && togglePanel('left', false)} />
        </div>
      ) : (
        <>
          <div className="tabs" role="tablist">
            <button role="tab" aria-selected={tab === 'systems'} className={tab === 'systems' ? 'on' : ''} onClick={() => setTab('systems')}>
              {t('systems', lang)}
            </button>
            <button role="tab" aria-selected={tab === 'tree'} className={tab === 'tree' ? 'on' : ''} onClick={() => setTab('tree')}>
              {t('tree', lang)}
            </button>
          </div>
          <div className="panel-body">
            {tab === 'systems' ? (
              <SystemsTab model={model} lang={lang} />
            ) : (
              <ul className="tree">
                {tree.map((n) => (
                  <TreeRow key={n.key} node={n} depth={0} lang={lang} />
                ))}
              </ul>
            )}
          </div>
        </>
      )}
    </aside>
  );
}
