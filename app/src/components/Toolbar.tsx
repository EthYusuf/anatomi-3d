import { useState } from 'react';
import { CAT, PEEL_STEPS } from '../data/categories';
import { t } from '../data/i18n';
import { useStore, type CameraPreset, type ClipAxis, type ViewMode } from '../store';
import { BodyIcon, CameraIcon, CutIcon, LayersIcon, MinusIcon, PlusIcon, ResetIcon } from './icons';

type Pop = 'section' | 'camera' | 'display' | null;

export default function Toolbar() {
  const lang = useStore((s) => s.lang);
  const peelStep = useStore((s) => s.peelStep);
  const peel = useStore((s) => s.peel);
  const unpeel = useStore((s) => s.unpeel);
  const viewMode = useStore((s) => s.viewMode);
  const setViewMode = useStore((s) => s.setViewMode);
  const colorMode = useStore((s) => s.colorMode);
  const setColorMode = useStore((s) => s.setColorMode);
  const clipAxis = useStore((s) => s.clipAxis);
  const quality = useStore((s) => s.quality);
  const setQuality = useStore((s) => s.setQuality);
  const alive = useStore((s) => s.alive);
  const toggleAlive = useStore((s) => s.toggleAlive);
  const clipPos = useStore((s) => s.clipPos);
  const setClipAxis = useStore((s) => s.setClipAxis);
  const setClipPos = useStore((s) => s.setClipPos);
  const toggleClipFlip = useStore((s) => s.toggleClipFlip);
  const requestCamera = useStore((s) => s.requestCamera);
  const resetAll = useStore((s) => s.resetAll);
  const [pop, setPop] = useState<Pop>(null);

  const cur = peelStep === 0 ? t('fullBody', lang) : PEEL_STEPS[peelStep - 1].map((c) => CAT[c][lang]).slice(0, 2).join(', ');
  const next = peelStep < PEEL_STEPS.length ? PEEL_STEPS[peelStep].map((c) => CAT[c][lang]).slice(0, 2).join(', ') : null;
  const toggle = (p: Pop) => setPop(pop === p ? null : p);

  return (
    <div className="toolbar-wrap">
      {pop === 'section' && (
        <div className="popover" role="dialog">
          <div className="seg">
            {(['none', 'sagittal', 'coronal', 'axial'] as ClipAxis[]).map((a) => (
              <button key={a} className={clipAxis === a ? 'on' : ''} onClick={() => setClipAxis(a)}>
                {t(a, lang)}
              </button>
            ))}
          </div>
          {clipAxis !== 'none' && (
            <div className="slider-row">
              <input
                type="range"
                min={-1}
                max={1}
                step={0.002}
                value={clipPos}
                onChange={(e) => setClipPos(Number(e.target.value))}
                aria-label={t('section', lang)}
              />
              <button className="ghost" onClick={toggleClipFlip}>
                ⇄ {t('flip', lang)}
              </button>
            </div>
          )}
        </div>
      )}
      {pop === 'camera' && (
        <div className="popover" role="dialog">
          <div className="grid-3">
            {(['front', 'back', 'iso', 'left', 'right', 'home', 'top', 'bottom'] as CameraPreset[]).map((p) => (
              <button key={p} onClick={() => requestCamera(p)}>
                {t(p as 'front', lang)}
              </button>
            ))}
          </div>
        </div>
      )}
      {pop === 'display' && (
        <div className="popover" role="dialog">
          <div className="pop-label">{t('display', lang)}</div>
          <div className="seg">
            {(['solid', 'xray', 'wire'] as ViewMode[]).map((m) => (
              <button key={m} className={viewMode === m ? 'on' : ''} onClick={() => setViewMode(m)}>
                {t(m, lang)}
              </button>
            ))}
          </div>
          <div className="pop-label">{t('colors', lang)}</div>
          <div className="seg">
            <button className={colorMode === 'anatomic' ? 'on' : ''} onClick={() => setColorMode('anatomic')}>
              {t('colorAnatomic', lang)}
            </button>
            <button className={colorMode === 'function' ? 'on' : ''} onClick={() => setColorMode('function')}>
              {t('colorFunction', lang)}
            </button>
          </div>
          <div className="pop-label">{t('quality', lang)}</div>
          <div className="seg">
            <button className={quality === 'high' ? 'on' : ''} onClick={() => setQuality('high')}>
              {t('qualityHigh', lang)}
            </button>
            <button className={quality === 'fast' ? 'on' : ''} onClick={() => setQuality('fast')}>
              {t('qualityFast', lang)}
            </button>
          </div>
          <label className="switch-row">
            <input type="checkbox" checked={alive} onChange={toggleAlive} />
            <span>{t('alive', lang)}</span>
          </label>
        </div>
      )}

      <div className="toolbar" role="toolbar">
        <div className="peel">
          <button className="icon-btn" onClick={unpeel} disabled={peelStep === 0} title={t('unpeel', lang)} aria-label={t('unpeel', lang)}>
            <MinusIcon />
          </button>
          <div className="peel-info">
            <div className="peel-dots" aria-hidden>
              {PEEL_STEPS.map((_, i) => (
                <span key={i} className={i < peelStep ? 'on' : ''} />
              ))}
            </div>
            <span className="peel-label" title={next ? `${t('peel', lang)}: ${next}` : undefined}>
              {peelStep === 0 ? cur : `− ${cur}`}
            </span>
          </div>
          <button
            className="icon-btn"
            onClick={peel}
            disabled={peelStep >= PEEL_STEPS.length}
            title={next ? `${t('peel', lang)}: ${next}` : ''}
            aria-label={t('peel', lang)}
          >
            <PlusIcon />
          </button>
        </div>
        <span className="sep" />
        <button className={`tb ${pop === 'display' || viewMode !== 'solid' || colorMode !== 'anatomic' ? 'on' : ''}`} onClick={() => toggle('display')}>
          <LayersIcon />
          <span>{t('display', lang)}</span>
        </button>
        <button className={`tb ${pop === 'section' || clipAxis !== 'none' ? 'on' : ''}`} onClick={() => toggle('section')}>
          <CutIcon />
          <span>{t('section', lang)}</span>
        </button>
        <button className={`tb ${pop === 'camera' ? 'on' : ''}`} onClick={() => toggle('camera')}>
          <CameraIcon />
          <span>{t('views', lang)}</span>
        </button>
        <button className="tb" onClick={() => requestCamera('home')}>
          <BodyIcon />
          <span>{t('home', lang)}</span>
        </button>
        <button
          className="tb"
          onClick={() => {
            resetAll();
            setPop(null);
          }}
        >
          <ResetIcon />
          <span>{t('reset', lang)}</span>
        </button>
      </div>
    </div>
  );
}
