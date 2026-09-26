import { useEffect, useState } from 'react';
import { t } from '../data/i18n';
import { displayName, type ModelData } from '../model';
import { useStore } from '../store';
import { CloseIcon, ResetIcon } from './icons';

const HINT_KEY = 'anatomi-dig-hint-seen';

/** Çift tıklamayla kaldırılan yapıların durumu: son kaldırılan, geri al, tümünü geri getir */
export default function DigBar({ model }: { model: ModelData }) {
  const lang = useStore((s) => s.lang);
  const dug = useStore((s) => s.dug);
  const quiz = useStore((s) => s.quiz);
  const undoDig = useStore((s) => s.undoDig);
  const restoreDigs = useStore((s) => s.restoreDigs);
  const [hint, setHint] = useState(() => {
    try {
      return !localStorage.getItem(HINT_KEY);
    } catch {
      return true;
    }
  });

  const closeHint = () => {
    setHint(false);
    try {
      localStorage.setItem(HINT_KEY, '1');
    } catch {
      /* depolama yok */
    }
  };

  // İlk kazıdan sonra ipucunu bir daha gösterme
  useEffect(() => {
    if (dug.length && hint) closeHint();
  }, [dug.length, hint]);

  if (quiz) return null;

  if (!dug.length) {
    if (!hint) return null;
    return (
      <div className="digbar hint" role="status">
        <span className="dig-dot" aria-hidden />
        <span>{t('digHint', lang)}</span>
        <button className="icon-btn sm" onClick={closeHint} aria-label="close">
          <CloseIcon />
        </button>
      </div>
    );
  }

  const last = model.byId.get(dug[dug.length - 1].id);
  return (
    <div className="digbar" role="status" aria-live="polite">
      <span className="dig-count">{dug.length}</span>
      <span className="dig-text">
        {last && (
          <>
            <b key={last.id} className="dig-last">
              {displayName(last, lang)}
            </b>{' '}
            {t('digRemoved', lang)}
          </>
        )}
      </span>
      <button className="dig-btn" onClick={undoDig} title="Ctrl+Z">
        <ResetIcon /> {t('digUndo', lang)}
      </button>
      {dug.length > 1 && (
        <button className="dig-btn ghost-btn" onClick={restoreDigs} title="Ctrl+Shift+Z">
          {t('digRestoreAll', lang)}
        </button>
      )}
    </div>
  );
}
