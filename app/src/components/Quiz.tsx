import { useCallback, useEffect, useRef } from 'react';
import { t } from '../data/i18n';
import { displayName, type ModelData } from '../model';
import { isPeeled, useStore } from '../store';
import { CloseIcon } from './icons';

function visibleCandidates(model: ModelData) {
  const s = useStore.getState();
  // Deri açık ve opaksa yalnız dış yüzey bölgeleri sorulur; aksi halde deri dışındaki görünen yapılar
  const skinOn =
    !s.hiddenCats.has('skin') && !isPeeled('skin', s.peelStep) && s.viewMode === 'solid' && s.clipAxis === 'none' && !s.isolated;
  return model.parts.filter(
    (p) =>
      p.r > 0.012 &&
      p.cat !== 'hair' &&
      (skinOn ? p.cat === 'skin' : p.cat !== 'skin') &&
      !s.hiddenCats.has(p.cat) &&
      !s.hiddenParts.has(p.id) &&
      !isPeeled(p.cat, s.peelStep) &&
      (!s.isolated || s.isolated.has(p.id)),
  );
}

export function startQuiz(model: ModelData) {
  const c = visibleCandidates(model);
  const s = useStore.getState();
  s.select(null);
  s.setQuiz({ target: c.length ? c[Math.floor(Math.random() * c.length)].id : '', score: 0, total: 0, streak: 0, last: null });
}

export default function Quiz({ model }: { model: ModelData }) {
  const lang = useStore((s) => s.lang);
  const quiz = useStore((s) => s.quiz);
  const setQuiz = useStore((s) => s.setQuiz);
  const timer = useRef<number>(0);

  const next = useCallback(
    (carry?: Partial<NonNullable<typeof quiz>>) => {
      const q = useStore.getState().quiz;
      if (!q) return;
      const c = visibleCandidates(model).filter((p) => p.id !== q.target);
      const target = c.length ? c[Math.floor(Math.random() * c.length)].id : '';
      setQuiz({ ...q, ...carry, target, last: null });
    },
    [model, setQuiz],
  );

  useEffect(() => {
    const onPick = (e: Event) => {
      const id = (e as CustomEvent<string | null>).detail;
      const q = useStore.getState().quiz;
      if (!q || !q.target || q.last) return;
      if (!id) return;
      const ok = id === q.target;
      setQuiz({
        ...q,
        score: q.score + (ok ? 1 : 0),
        total: q.total + 1,
        streak: ok ? q.streak + 1 : 0,
        last: { ok, picked: id },
      });
      if (ok) timer.current = window.setTimeout(() => next(), 1100);
      else useStore.getState().requestCamera('focus', q.target);
    };
    window.addEventListener('quiz-pick', onPick);
    return () => {
      window.removeEventListener('quiz-pick', onPick);
      clearTimeout(timer.current);
    };
  }, [next, setQuiz]);

  if (!quiz) return null;
  const target = model.byId.get(quiz.target);
  const picked = quiz.last?.picked ? model.byId.get(quiz.last.picked) : undefined;

  return (
    <div className={`quiz ${quiz.last ? (quiz.last.ok ? 'ok' : 'bad') : ''}`} role="dialog" aria-live="polite">
      <div className="quiz-top">
        <strong>{t('quizTitle', lang)}</strong>
        <span className="quiz-score">
          {t('quizScore', lang)}: {quiz.score}/{quiz.total}
          {quiz.streak > 1 && (
            <>
              {' · '}
              {t('quizStreak', lang)} {quiz.streak} 🔥
            </>
          )}
        </span>
        <button className="icon-btn sm" onClick={() => setQuiz(null)} aria-label={t('quizEnd', lang)}>
          <CloseIcon />
        </button>
      </div>
      {!target ? (
        <p>{t('quizEmpty', lang)}</p>
      ) : (
        <>
          <p className="quiz-q">
            {t('quizFind', lang)} <b>{displayName(target, lang)}</b>
          </p>
          {lang === 'tr' && target.la && <p className="quiz-sub">{target.en}</p>}
          {quiz.last && (
            <p className="quiz-res">
              {quiz.last.ok ? (
                t('quizCorrect', lang)
              ) : (
                <>
                  {t('quizWrong', lang)} <b>{picked ? displayName(picked, lang) : '—'}</b>. {t('quizMissed', lang)}
                </>
              )}
            </p>
          )}
          <div className="quiz-actions">
            {!quiz.last && (
              <>
                <button
                  className="ghost"
                  onClick={() => {
                    setQuiz({ ...quiz, total: quiz.total + 1, streak: 0, last: { ok: false, picked: null } });
                    useStore.getState().requestCamera('focus', quiz.target);
                  }}
                >
                  {t('quizReveal', lang)}
                </button>
                <button className="ghost" onClick={() => next()}>
                  {t('quizSkip', lang)}
                </button>
              </>
            )}
            {quiz.last && !quiz.last.ok && (
              <button className="primary" onClick={() => next()}>
                {t('quizNext', lang)} →
              </button>
            )}
          </div>
          <p className="quiz-hint">{t('quizScope', lang)}</p>
        </>
      )}
    </div>
  );
}
