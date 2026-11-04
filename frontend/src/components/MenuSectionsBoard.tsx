import * as React from 'react';
import { Box, Paper, Typography, Chip, Tooltip, alpha, useTheme } from '@mui/material';
import {
  DragIndicator as DragIcon,
  ExpandMore as ExpandMoreIcon,
  ChevronRight as ChevronRightIcon,
} from '@mui/icons-material';
import type { Dish } from '../types';

/** Раздел витрины: название (поле category карточек) и карточки по порядку */
export interface MenuSection {
  name: string;
  cards: Dish[];
}

const NO_SECTION = 'Без раздела';

/** Разделы в порядке витрины: по первой карточке, карточки — по sortOrder */
export const buildSections = (parents: Dish[]): MenuSection[] => {
  const sorted = [...parents].sort((a, b) => (a.sortOrder ?? 0) - (b.sortOrder ?? 0) || a.id - b.id);
  const map = new Map<string, Dish[]>();
  sorted.forEach(p => {
    const key = p.category?.trim() ?? '';
    if (!map.has(key)) map.set(key, []);
    map.get(key)!.push(p);
  });
  return Array.from(map, ([name, cards]) => ({ name, cards }));
};

/** Сквозная нумерация: порядок разделов на витрине задаётся первой карточкой раздела */
export const toReorderItems = (sections: MenuSection[]) =>
  sections
    .flatMap(s => s.cards.map(c => ({ id: c.id, category: s.name })))
    .map((c, i) => ({ ...c, sortOrder: (i + 1) * 10 }));

type Drag =
  | { kind: 'section'; from: number }
  | { kind: 'card'; section: number; from: number };

/** Куда бросить: перед/после раздела или карточки, либо в конец свёрнутого раздела */
type Target =
  | { kind: 'section'; index: number; after: boolean }
  | { kind: 'card'; section: number; index: number; after: boolean }
  | { kind: 'into'; section: number };

const sameTarget = (a: Target | null, b: Target | null) => JSON.stringify(a) === JSON.stringify(b);

/** Применить перенос к копии разделов */
const applyMove = (sections: MenuSection[], drag: Drag, target: Target): MenuSection[] | null => {
  const next = sections.map(s => ({ ...s, cards: [...s.cards] }));
  if (drag.kind === 'section') {
    if (target.kind !== 'section') return null;
    let to = target.index + (target.after ? 1 : 0);
    if (to > drag.from) to -= 1;
    if (to === drag.from) return null;
    const [moved] = next.splice(drag.from, 1);
    next.splice(to, 0, moved);
    return next;
  }
  const [card] = next[drag.section].cards.splice(drag.from, 1);
  let toSection: number;
  let to: number;
  if (target.kind === 'into' || target.kind === 'section') {
    toSection = target.kind === 'into' ? target.section : target.index;
    to = next[toSection].cards.length;
  } else {
    toSection = target.section;
    to = target.index + (target.after ? 1 : 0);
    if (toSection === drag.section && to > drag.from) to -= 1;
  }
  if (toSection === drag.section && to === drag.from) return null;
  next[toSection].cards.splice(to, 0, card);
  return next;
};

/** Цель под пальцем/курсором по data-атрибутам */
const findTarget = (x: number, y: number, drag: Drag): Target | null => {
  const el = document.elementFromPoint(x, y)?.closest<HTMLElement>('[data-drop]');
  if (!el) return null;
  const [kind, a, b] = el.dataset.drop!.split(':');
  const rect = el.getBoundingClientRect();
  const after = y > rect.top + rect.height / 2;
  if (drag.kind === 'section') {
    if (kind === 'sec' || kind === 'into') return { kind: 'section', index: Number(a), after };
    if (kind === 'card') return { kind: 'section', index: Number(a), after: true };
    return null;
  }
  if (kind === 'card') return { kind: 'card', section: Number(a), index: Number(b), after };
  if (kind === 'sec' || kind === 'into') return { kind: 'into', section: Number(a) };
  return null;
};

interface Props {
  sections: MenuSection[];
  /** Новый порядок после переноса (уже применён на экране) */
  onChange: (sections: MenuSection[]) => void;
  renderCard: (card: Dish, dragHandle: React.ReactNode) => React.ReactNode;
  /** При поиске и фильтрах порядок не меняют, разделы раскрыты */
  locked?: boolean;
}

/**
 * Разделы меню со своими карточками: изначально свёрнуты; разделы и карточки переставляются
 * перетаскиванием за ручку (мышью и пальцем), карточку можно перенести в другой раздел —
 * в том числе на заголовок свёрнутого раздела.
 */
const MenuSectionsBoard: React.FC<Props> = ({ sections, onChange, renderCard, locked = false }) => {
  const theme = useTheme();
  const [open, setOpen] = React.useState<Set<string>>(() => new Set());
  const [drag, setDrag] = React.useState<Drag | null>(null);
  const [target, setTarget] = React.useState<Target | null>(null);
  const targetRef = React.useRef<Target | null>(null);
  const pointerY = React.useRef(0);

  // Автопрокрутка у краёв экрана во время переноса
  React.useEffect(() => {
    if (!drag) return;
    let raf = 0;
    const tick = () => {
      const y = pointerY.current;
      const edge = 80;
      if (y < edge) window.scrollBy(0, -Math.ceil((edge - y) / 6));
      else if (y > window.innerHeight - edge) window.scrollBy(0, Math.ceil((y - (window.innerHeight - edge)) / 6));
      raf = requestAnimationFrame(tick);
    };
    raf = requestAnimationFrame(tick);
    return () => cancelAnimationFrame(raf);
  }, [drag]);

  const startDrag = (e: React.PointerEvent, d: Drag) => {
    if (locked || e.button > 0) return;
    e.preventDefault();
    e.stopPropagation();
    pointerY.current = e.clientY;
    setDrag(d);
    targetRef.current = null;
    setTarget(null);

    const move = (ev: PointerEvent) => {
      pointerY.current = ev.clientY;
      const t = findTarget(ev.clientX, ev.clientY, d);
      if (!sameTarget(t, targetRef.current)) { targetRef.current = t; setTarget(t); }
    };
    const end = (ev: PointerEvent) => {
      window.removeEventListener('pointermove', move);
      window.removeEventListener('pointerup', end);
      window.removeEventListener('pointercancel', end);
      const t = ev.type === 'pointerup' ? targetRef.current : null;
      setDrag(null);
      setTarget(null);
      targetRef.current = null;
      if (!t) return;
      const next = applyMove(sections, d, t);
      if (next) onChange(next);
    };
    window.addEventListener('pointermove', move);
    window.addEventListener('pointerup', end);
    window.addEventListener('pointercancel', end);
  };

  const handle = (d: Drag, label: string) => locked ? null : (
    <Tooltip title={label}>
      <Box
        component="span"
        role="button"
        aria-label={label}
        onPointerDown={e => startDrag(e, d)}
        onClick={e => e.stopPropagation()}
        sx={{
          display: 'inline-flex', alignItems: 'center', justifyContent: 'center', p: 0.5, borderRadius: 1,
          color: 'text.secondary', cursor: drag ? 'grabbing' : 'grab', touchAction: 'none', userSelect: 'none',
          '&:hover': { bgcolor: 'action.hover', color: 'text.primary' },
        }}
      >
        <DragIcon />
      </Box>
    </Tooltip>
  );

  const line = (show: boolean, after: boolean) => show ? {
    [after ? 'borderBottom' : 'borderTop']: `3px solid ${theme.palette.primary.main}`,
  } : {};

  const toggle = (name: string) => setOpen(prev => {
    const n = new Set(prev);
    if (n.has(name)) n.delete(name); else n.add(name);
    return n;
  });

  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', gap: 1.5, userSelect: drag ? 'none' : undefined }}>
      {sections.map((section, si) => {
        const expanded = locked || open.has(section.name);
        const dragging = drag?.kind === 'section' && drag.from === si;
        const secLine = target?.kind === 'section' && target.index === si;
        const into = target?.kind === 'into' && target.section === si;
        return (
          <Paper
            key={section.name || '∅'}
            variant="outlined"
            sx={{
              borderRadius: 3, overflow: 'hidden', opacity: dragging ? 0.45 : 1,
              ...line(!!secLine, !!(secLine && target.after)),
              ...(into ? { borderColor: 'primary.main', bgcolor: alpha(theme.palette.primary.main, 0.06) } : {}),
            }}
          >
            <Box
              data-drop={`sec:${si}`}
              onClick={() => !locked && toggle(section.name)}
              sx={{
                display: 'flex', alignItems: 'center', gap: 1, px: 1.5, py: 1.25,
                cursor: locked ? 'default' : 'pointer',
                bgcolor: alpha(theme.palette.primary.main, expanded ? 0.05 : 0.02),
              }}
            >
              {handle({ kind: 'section', from: si }, 'Перетащите, чтобы поменять порядок разделов')}
              {!locked && (expanded ? <ExpandMoreIcon color="action" /> : <ChevronRightIcon color="action" />)}
              <Typography variant="h6" fontWeight={700} sx={{ flex: 1, minWidth: 0 }} noWrap>
                {section.name || NO_SECTION}
              </Typography>
              <Chip size="small" label={section.cards.length} />
            </Box>
            {expanded && (
              <Box data-drop={`into:${si}`} sx={{ display: 'flex', flexDirection: 'column', gap: 1.5, p: 1.5, pt: 0.5 }}>
                {section.cards.map((card, ci) => {
                  const cardDragging = drag?.kind === 'card' && drag.section === si && drag.from === ci;
                  const cardLine = target?.kind === 'card' && target.section === si && target.index === ci;
                  return (
                    <Box
                      key={card.id}
                      data-drop={`card:${si}:${ci}`}
                      sx={{ opacity: cardDragging ? 0.45 : 1, borderRadius: 3, ...line(!!cardLine, !!(cardLine && target.after)) }}
                    >
                      {renderCard(card, handle({ kind: 'card', section: si, from: ci }, 'Перетащите карточку — можно в другой раздел'))}
                    </Box>
                  );
                })}
                {section.cards.length === 0 && (
                  <Typography variant="body2" color="text.secondary" sx={{ py: 2, textAlign: 'center' }}>Пусто</Typography>
                )}
              </Box>
            )}
          </Paper>
        );
      })}
      {drag?.kind === 'card' && (
        <Typography variant="caption" color="text.secondary" sx={{ textAlign: 'center' }}>
          Отпустите над карточкой или над заголовком раздела — карточка переедет в этот раздел
        </Typography>
      )}
    </Box>
  );
};

export default MenuSectionsBoard;
