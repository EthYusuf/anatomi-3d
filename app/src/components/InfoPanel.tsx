import { useEffect, useMemo, useState } from 'react';
import { CAT, MUSCLE_FUNCTION, SYSTEMS } from '../data/categories';
import { t } from '../data/i18n';
import { displayName, secondaryName, type ModelData, type Part } from '../model';
import { useStore } from '../store';
import { CloseIcon, EyeOffIcon, FocusIcon } from './icons';

interface Section {
  level: number;
  title: string;
  paras: string[];
}

interface Desc {
  sections: Section[];
  url?: string;
}

const SKIP_SECTIONS = /animals|invertebrate|other species|in other|society|culture|etymology|see also|gallery|additional images|references|history/i;
const cache = new Map<string, Promise<Desc | null>>();

function parseDesc(txt: string): Desc {
  const lines = txt.split(/\r?\n/).map((l) => l.trim());
  const sections: Section[] = [{ level: 1, title: '', paras: [] }];
  let url: string | undefined;
  let skipLevel = 0;
  let titleSeen = false;
  for (const l of lines) {
    if (!l) continue;
    if (/^https?:\/\//.test(l)) {
      url = l;
      continue;
    }
    if (!titleSeen) {
      titleSeen = true;
      // İlk satır BÜYÜK HARFLİ başlıktır (bazen '/' ile başlar); aynı satırda cümle başlıyorsa ayır
      const words = l.replace(/^\//, '').split(/\s+/);
      let k = 0;
      while (k < words.length && words[k] === words[k].toUpperCase() && !/^["“]/.test(words[k])) k++;
      if (k > 0) {
        const rest = words.slice(k).join(' ');
        if (rest) sections[0].paras.push(rest);
        continue;
      }
    }
    const m = l.match(/^(=+)\s*(.*?)\s*=+$/);
    if (m) {
      const level = m[1].length;
      if (skipLevel && level > skipLevel) continue;
      skipLevel = SKIP_SECTIONS.test(m[2]) ? level : 0;
      if (!skipLevel) sections.push({ level, title: m[2], paras: [] });
      continue;
    }
    if (skipLevel) continue;
    // Wikipedia'dan kalan boş parantezleri temizle
    const clean = l.replace(/\(\s*[,;]?\s*\)/g, '').replace(/\s+([,.;])/g, '$1');
    const cur = sections[sections.length - 1];
    // Z-Anatomy metinlerinde her cümle ayrı satırda; paragrafları birleştir
    if (cur.paras.length && cur.paras[cur.paras.length - 1].length < 380) cur.paras[cur.paras.length - 1] += ' ' + clean;
    else cur.paras.push(clean);
  }
  return { sections: sections.filter((s) => s.paras.length), url };
}

function loadDesc(name: string): Promise<Desc | null> {
  let p = cache.get(name);
  if (!p) {
    p = fetch(`${import.meta.env.BASE_URL}body/desc/${name}.txt`)
      .then((r) => (r.ok ? r.text() : null))
      .then((txt) => (txt ? parseDesc(txt) : null))
      .catch(() => null);
    cache.set(name, p);
  }
  return p;
}

function Description({ part }: { part: Part }) {
  const lang = useStore((s) => s.lang);
  const [desc, setDesc] = useState<Desc | null | undefined>(undefined);
  const [more, setMore] = useState(false);

  useEffect(() => {
    let alive = true;
    setDesc(undefined);
    setMore(false);
    if (!part.d) {
      setDesc(null);
      return;
    }
    loadDesc(part.d).then((d) => alive && setDesc(d));
    return () => {
      alive = false;
    };
  }, [part.d]);

  if (desc === undefined) return <div className="skeleton" />;
  if (!desc || !desc.sections.length) return <p className="muted">{t('noDesc', lang)}</p>;
  const shown = more ? desc.sections : desc.sections.slice(0, 1);
  const hasMore = desc.sections.length > 1 || desc.sections[0].paras.length > 2;
  return (
    <div className="desc" lang="en">
      {shown.map((s, i) => (
        <div key={i}>
          {s.title && (s.level <= 2 ? <h4>{s.title}</h4> : <h5>{s.title}</h5>)}
          {(more || i > 0 ? s.paras : s.paras.slice(0, 2)).map((p, j) => (
            <p key={j}>{p}</p>
          ))}
        </div>
      ))}
      {hasMore && (
        <button className="link" onClick={() => setMore(!more)}>
          {more ? t('readLess', lang) : t('readMore', lang)}
        </button>
      )}
      <p className="source-note">
        {t('descNote', lang)}{' '}
        {desc.url && (
          <a href={desc.url} target="_blank" rel="noreferrer">
            Wikipedia ↗
          </a>
        )}
      </p>
    </div>
  );
}

export default function InfoPanel({ model }: { model: ModelData }) {
  const lang = useStore((s) => s.lang);
  const open = useStore((s) => s.panelRight);
  const selected = useStore((s) => s.selected);
  const isolated = useStore((s) => s.isolated);
  const select = useStore((s) => s.select);
  const hideSelected = useStore((s) => s.hideSelected);
  const isolate = useStore((s) => s.isolate);
  const reveal = useStore((s) => s.reveal);
  const setReveal = useStore((s) => s.setReveal);
  const togglePanel = useStore((s) => s.togglePanel);
  const part = selected ? model.byId.get(selected) : undefined;

  const related = useMemo(() => {
    if (!part) return null;
    const parent = part.parent ? model.byId.get(part.parent) : undefined;
    const children = model.parts.filter((p) => p.parent === part.id);
    const other = part.side
      ? model.parts.find((p) => p.en === part.en && p.cat === part.cat && p.side && p.side !== part.side)
      : undefined;
    return { parent, children, other };
  }, [part, model]);

  const info = part ? CAT[part.cat] : null;
  const sys = info ? SYSTEMS.find((s) => s.id === info.sys) : null;
  const fn = part?.cat === 'muscle' && part.m ? MUSCLE_FUNCTION[part.m] : undefined;
  const path = part ? model.groups[part.g] ?? [] : [];

  return (
    <aside className={`panel right ${open ? 'open' : ''}`} aria-hidden={!open}>
      {!part || !info ? (
        <div className="panel-body empty-state">
          <div className="empty-illu" aria-hidden>
            ⌖
          </div>
          <p>{t('noSelection', lang)}</p>
          <p className="muted small">{t('helpText', lang)}</p>
          <p className="muted small">{t('helpKeys', lang)}</p>
        </div>
      ) : (
        <div className="panel-body info">
          <div className="info-head">
            <span className="chip" style={{ '--c': info.color } as React.CSSProperties}>
              {info[lang]}
            </span>
            <button className="icon-btn" onClick={() => select(null)} aria-label="close">
              <CloseIcon />
            </button>
          </div>
          <h2>{displayName(part, lang)}</h2>
          {secondaryName(part, lang) && <p className="alt-name">{secondaryName(part, lang)}</p>}

          <div className="actions">
            <button onClick={() => useStore.getState().requestCamera('focus', part.id)}>
              <FocusIcon /> {t('focus', lang)}
            </button>
            <button onClick={hideSelected}>
              <EyeOffIcon /> {t('hide', lang)}
            </button>
            <button className={isolated ? 'on' : ''} onClick={() => isolate(isolated ? null : [part.id])}>
              ◎ {isolated ? t('exitIsolate', lang) : t('isolate', lang)}
            </button>
            <button className={reveal ? 'on' : ''} onClick={() => setReveal(!reveal)} aria-pressed={reveal}>
              ◌ {t('reveal', lang)}
            </button>
          </div>

          <dl className="facts">
            {lang === 'tr' && part.tr && part.la && (
              <>
                <dt>{t('latin', lang)}</dt>
                <dd>{part.la}</dd>
              </>
            )}
            {lang === 'tr' && (part.la || part.tr) && (
              <>
                <dt>{t('english', lang)}</dt>
                <dd>{part.en}</dd>
              </>
            )}
            {lang === 'en' && part.la && (
              <>
                <dt>{t('latin', lang)}</dt>
                <dd>{part.la}</dd>
              </>
            )}
            <dt>{t('category', lang)}</dt>
            <dd>{sys?.[lang]}</dd>
            {part.side && (
              <>
                <dt>{t('side', lang)}</dt>
                <dd>{part.side === 'l' ? t('sideL', lang) : t('sideR', lang)}</dd>
              </>
            )}
            {fn && (
              <>
                <dt>{lang === 'tr' ? 'Hareket' : 'Action'}</dt>
                <dd>
                  <span className="swatch" style={{ background: fn.color }} /> {fn[lang]}
                </dd>
              </>
            )}
          </dl>

          {path.length > 0 && (
            <nav className="crumbs" aria-label={t('hierarchy', lang)}>
              {path.map(([en, la], i) => (
                <span key={i}>{lang === 'tr' ? la ?? en : en}</span>
              ))}
            </nav>
          )}

          {related && (related.parent || related.other || related.children.length > 0) && (
            <div className="related">
              {related.parent && (
                <div>
                  <h4>{t('partOf', lang)}</h4>
                  <button className="rel" onClick={() => select(related.parent!.id)}>
                    {displayName(related.parent, lang)}
                  </button>
                </div>
              )}
              {related.other && (
                <div>
                  <h4>{t('counterpart', lang)}</h4>
                  <button className="rel" onClick={() => select(related.other!.id, true)}>
                    {displayName(related.other, lang)}
                  </button>
                </div>
              )}
              {related.children.length > 0 && (
                <div>
                  <h4>{t('branches', lang)}</h4>
                  <div className="rel-list">
                    {related.children.slice(0, 30).map((c) => (
                      <button key={c.id} className="rel" onClick={() => select(c.id)}>
                        {displayName(c, lang)}
                      </button>
                    ))}
                  </div>
                </div>
              )}
            </div>
          )}

          <h3 className="sec-title">{t('description', lang)}</h3>
          <Description part={part} />
        </div>
      )}
      <button className="panel-close" onClick={() => togglePanel('right', false)} aria-label="close panel">
        <CloseIcon />
      </button>
    </aside>
  );
}
