import { lazy, Suspense, useCallback, useEffect, useRef, useState } from 'react';
import { CAT } from './data/categories';
import { t } from './data/i18n';
import { displayName, loadModel, secondaryName, type ModelData, type Part } from './model';
import { useStore } from './store';
import Sidebar from './components/Sidebar';
import InfoPanel from './components/InfoPanel';
import Toolbar from './components/Toolbar';
import Quiz, { startQuiz } from './components/Quiz';
import DigBar from './components/DigBar';
import { InfoIcon, MenuIcon, QuizIcon } from './components/icons';
import './App.css';

const Viewer = lazy(() => import('./components/Viewer'));

function Tooltip({ tip }: { tip: { part: Part; x: number; y: number } | null }) {
  const lang = useStore((s) => s.lang);
  const quiz = useStore((s) => s.quiz);
  if (!tip || quiz) return null;
  const sub = secondaryName(tip.part, lang);
  return (
    <div className="tooltip" style={{ transform: `translate(${tip.x + 14}px, ${tip.y + 14}px)` }}>
      <span className="dot" style={{ background: CAT[tip.part.cat].color }} />
      <div>
        <div className="tt-main">{displayName(tip.part, lang)}</div>
        {sub && <div className="tt-sub">{sub}</div>}
      </div>
    </div>
  );
}

function useShortcuts(model: ModelData | null) {
  useEffect(() => {
    if (!model) return;
    const onKey = (e: KeyboardEvent) => {
      const el = e.target as HTMLElement;
      if (el.tagName === 'INPUT' || el.tagName === 'TEXTAREA') return;
      const s = useStore.getState();
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'z' && s.dug.length) {
        e.preventDefault();
        if (e.shiftKey) s.restoreDigs();
        else s.undoDig();
        return;
      }
      if (e.ctrlKey || e.metaKey || e.altKey) return;
      switch (e.key.toLowerCase()) {
        case 'escape':
          if (s.quiz) s.setQuiz(null);
          else if (s.isolated) s.isolate(null);
          else s.select(null);
          break;
        case 'h':
          s.hideSelected();
          break;
        case 'i':
          if (s.isolated) s.isolate(null);
          else if (s.selected) s.isolate([s.selected]);
          break;
        case 'f':
          if (s.selected) s.requestCamera('focus', s.selected);
          break;
        case 'x':
          s.setViewMode(s.viewMode === 'xray' ? 'solid' : 'xray');
          break;
        case ']':
          s.peel();
          break;
        case '[':
          s.unpeel();
          break;
        case 'u':
          s.unhideAll();
          break;
        case 'home':
          s.requestCamera('home');
          break;
        default:
          return;
      }
      e.preventDefault();
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [model]);
}

export default function App() {
  const lang = useStore((s) => s.lang);
  const setLang = useStore((s) => s.setLang);
  const togglePanel = useStore((s) => s.togglePanel);
  const panelLeft = useStore((s) => s.panelLeft);
  const panelRight = useStore((s) => s.panelRight);
  const quiz = useStore((s) => s.quiz);
  const sceneReady = useStore((s) => s.sceneReady);
  const setQuiz = useStore((s) => s.setQuiz);
  const [model, setModel] = useState<ModelData | null>(null);
  const [progress, setProgress] = useState(0);
  const [error, setError] = useState<string | null>(null);
  const [tip, setTip] = useState<{ part: Part; x: number; y: number } | null>(null);
  const [attempt, setAttempt] = useState(0);
  const started = useRef(-1);

  useEffect(() => {
    if (started.current === attempt) return;
    started.current = attempt;
    setError(null);
    loadModel(setProgress)
      .then((m) => {
        if (import.meta.env.DEV) Object.assign(window, { __model: m, __store: useStore });
        setModel(m);
      })
      .catch((e: unknown) => setError(String(e)));
  }, [attempt]);

  useEffect(() => {
    document.documentElement.lang = lang;
    document.title = `${t('appTitle', lang)} — ${t('subtitle', lang)}`;
  }, [lang]);

  useShortcuts(model);

  const onHover = useCallback((p: Part | null, x?: number, y?: number) => {
    setTip(p && x !== undefined && y !== undefined ? { part: p, x, y } : null);
  }, []);

  return (
    <div className={`app ${panelLeft ? 'l-open' : ''} ${panelRight ? 'r-open' : ''}`}>
      <header className="topbar">
        <button className="icon-btn" onClick={() => togglePanel('left')} aria-label={t('menu', lang)} aria-pressed={panelLeft}>
          <MenuIcon />
        </button>
        <div className="brand">
          <svg viewBox="0 0 32 32" width="26" height="26" aria-hidden>
            <defs>
              <linearGradient id="lg" x1="0" x2="1" y1="0" y2="1">
                <stop offset="0" stopColor="#ff7a6b" />
                <stop offset="1" stopColor="#3e8ef7" />
              </linearGradient>
            </defs>
            <circle cx="16" cy="6" r="4" fill="url(#lg)" />
            <path d="M8 12h16l-3 8h-2l-1 10h-4l-1-10h-2z" fill="url(#lg)" />
          </svg>
          <div>
            <h1>{t('appTitle', lang)}</h1>
            <p>{t('subtitle', lang)}</p>
          </div>
        </div>
        <div className="top-actions">
          {model && (
            <button
              className={`pill ${quiz ? 'on' : ''}`}
              onClick={() => (quiz ? setQuiz(null) : startQuiz(model))}
            >
              <QuizIcon /> <span>{t('quiz', lang)}</span>
            </button>
          )}
          <div className="lang" role="group" aria-label="Language">
            <button className={lang === 'tr' ? 'on' : ''} onClick={() => setLang('tr')}>
              TR
            </button>
            <button className={lang === 'en' ? 'on' : ''} onClick={() => setLang('en')}>
              EN
            </button>
          </div>
          <button className="icon-btn" onClick={() => togglePanel('right')} aria-label={t('info', lang)} aria-pressed={panelRight}>
            <InfoIcon />
          </button>
        </div>
      </header>

      <main className="stage">
        {model && (
          <Suspense fallback={null}>
            <Viewer model={model} onHover={onHover} />
          </Suspense>
        )}
        {model && (
          <>
            <Sidebar model={model} />
            <InfoPanel model={model} />
            <Toolbar />
            <Quiz model={model} />
            <DigBar model={model} />
            <Tooltip tip={tip} />
          </>
        )}
        {model && !sceneReady && (
          <div className="loader soft" role="status">
            <div className="spinner" aria-hidden />
            <p>{t('preparing', lang)}</p>
          </div>
        )}
        {!model && (
          <div className="loader">
            {error ? (
              <>
                <p className="err">
                  {t('loadError', lang)}: {error}
                </p>
                <button className="primary" onClick={() => setAttempt(attempt + 1)}>
                  {t('retry', lang)}
                </button>
              </>
            ) : (
              <>
                <div className="spinner" aria-hidden />
                <p>{t('loading', lang)}</p>
                <div className="bar">
                  <div style={{ width: `${Math.round(progress * 100)}%` }} />
                </div>
                <p className="muted small">
                  {Math.round(progress * 100)}% · {t('loadingHint', lang)}
                </p>
              </>
            )}
          </div>
        )}
        <footer className="credit">
          {t('source', lang)} ·{' '}
          <a href="https://www.z-anatomy.com" target="_blank" rel="noreferrer">
            z-anatomy.com
          </a>
        </footer>
      </main>
    </div>
  );
}
