import * as React from 'react';
import {
  Box, Typography, Button, IconButton, Alert, Snackbar, Chip, TextField, Tooltip, CircularProgress, alpha, useTheme,
} from '@mui/material';
import {
  Add as AddIcon, Delete as DeleteIcon, RotateRight as RotateIcon, CropSquare as RectIcon, RadioButtonUnchecked as RoundIcon,
  Save as SaveIcon, TableRestaurant as TablesIcon,
} from '@mui/icons-material';
import { useFloorPlan, useSaveFloorPlan, useAllTables, type FloorPlan, type FloorWall } from '../api/waiter';
import { apiError } from '../api/inventory';
import { tokens } from '../theme';
import { TablesSettings } from './WaiterPage';

const GRID = 10;
const snap = (v: number) => Math.round(v / GRID) * GRID;
const clamp = (v: number, lo: number, hi: number) => Math.max(lo, Math.min(hi, v));

type Sel = { type: 'table'; id: number } | { type: 'wall'; id: string } | null;
type Drag = { sel: NonNullable<Sel>; mode: 'move' | 'resize'; startX: number; startY: number; orig: { x: number; y: number; w: number; h: number } };

const KIND_LABEL: Record<FloorWall['kind'], string> = { wall: 'Перегородка', bar: 'Барная стойка', plant: 'Растение / декор' };

/** Схема зала: менеджер расставляет столы и перегородки, официант видит зал как есть */
const FloorEditorPage: React.FC = () => {
  const theme = useTheme();
  const { data: saved, isLoading } = useFloorPlan();
  const { data: allTables = [] } = useAllTables(true);
  const save = useSaveFloorPlan();
  const [plan, setPlan] = React.useState<FloorPlan | null>(null);
  const [dirty, setDirty] = React.useState(false);
  const [sel, setSel] = React.useState<Sel>(null);
  const [err, setErr] = React.useState('');
  const [toast, setToast] = React.useState('');
  const [tablesOpen, setTablesOpen] = React.useState(false);
  const boxRef = React.useRef<HTMLDivElement>(null);
  const drag = React.useRef<Drag | null>(null);

  React.useEffect(() => { if (saved && !dirty) setPlan(JSON.parse(JSON.stringify(saved))); }, [saved, dirty]);

  const update = (fn: (p: FloorPlan) => FloorPlan) => { setPlan(p => (p ? fn(p) : p)); setDirty(true); };

  if (isLoading || !plan) return <Box sx={{ display: 'grid', placeItems: 'center', py: 8 }}><CircularProgress /></Box>;

  const active = allTables.filter(t => t.isActive);
  const nameOf = new Map(allTables.map(t => [t.id, t.name]));
  const placed = new Set(plan.tables.map(t => t.tableId));
  const unplaced = active.filter(t => !placed.has(t.id));

  const scale = () => (boxRef.current ? boxRef.current.clientWidth / plan.width : 1);

  const rectOf = (s: NonNullable<Sel>) =>
    s.type === 'table' ? plan.tables.find(t => t.tableId === s.id) : plan.walls.find(w => w.id === s.id);

  const setRect = (s: NonNullable<Sel>, r: { x: number; y: number; w: number; h: number }) =>
    setPlan(p => p && (s.type === 'table'
      ? { ...p, tables: p.tables.map(t => (t.tableId === s.id ? { ...t, ...r } : t)) }
      : { ...p, walls: p.walls.map(w => (w.id === s.id ? { ...w, ...r } : w)) }));

  const onDown = (e: React.PointerEvent, s: NonNullable<Sel>, mode: Drag['mode']) => {
    e.stopPropagation();
    const r = rectOf(s);
    if (!r) return;
    setSel(s);
    drag.current = { sel: s, mode, startX: e.clientX, startY: e.clientY, orig: { x: r.x, y: r.y, w: r.w, h: r.h } };
    (e.target as Element).setPointerCapture?.(e.pointerId);
  };

  const onMove = (e: React.PointerEvent) => {
    const d = drag.current;
    if (!d) return;
    const k = scale();
    const dx = (e.clientX - d.startX) / k;
    const dy = (e.clientY - d.startY) / k;
    const o = d.orig;
    if (d.mode === 'move') {
      setRect(d.sel, { ...o, x: clamp(snap(o.x + dx), 0, plan.width - o.w), y: clamp(snap(o.y + dy), 0, plan.height - o.h) });
    } else {
      setRect(d.sel, { ...o, w: clamp(snap(o.w + dx), GRID, plan.width - o.x), h: clamp(snap(o.h + dy), GRID, plan.height - o.y) });
    }
    setDirty(true);
  };
  const onUp = () => { drag.current = null; };

  const freeSpot = (w: number, h: number) => {
    for (let y = 20; y + h <= plan.height; y += 20)
      for (let x = 20; x + w <= plan.width; x += 20) {
        const hit = [...plan.tables, ...plan.walls].some(r => x < r.x + r.w && x + w > r.x && y < r.y + r.h && y + h > r.y);
        if (!hit) return { x, y };
      }
    return { x: 20, y: 20 };
  };

  const addWall = (kind: FloorWall['kind']) => {
    const [w, h] = kind === 'wall' ? [200, 10] : kind === 'bar' ? [240, 60] : [40, 40];
    const p = freeSpot(w, h);
    const id = `${kind}-${Date.now().toString(36)}`;
    update(pl => ({ ...pl, walls: [...pl.walls, { id, kind, w, h, ...p }] }));
    setSel({ type: 'wall', id });
  };

  const placeTable = (tableId: number) => {
    const p = freeSpot(80, 80);
    update(pl => ({ ...pl, tables: [...pl.tables, { tableId, w: 80, h: 80, shape: 'rect', ...p }] }));
    setSel({ type: 'table', id: tableId });
  };

  const placeAll = () => {
    let pl = plan;
    const add: FloorPlan['tables'] = [];
    for (const t of unplaced) {
      const tmp = { ...pl, tables: [...pl.tables, ...add] };
      let spot = { x: 20, y: 20 };
      outer: for (let y = 20; y + 80 <= tmp.height; y += 20)
        for (let x = 20; x + 80 <= tmp.width; x += 20) {
          const hit = [...tmp.tables, ...tmp.walls].some(r => x < r.x + r.w + 20 && x + 100 > r.x && y < r.y + r.h + 20 && y + 100 > r.y);
          if (!hit) { spot = { x, y }; break outer; }
        }
      add.push({ tableId: t.id, w: 80, h: 80, shape: 'rect', ...spot });
    }
    pl = { ...pl, tables: [...pl.tables, ...add] };
    setPlan(pl);
    setDirty(true);
  };

  const selected = sel ? rectOf(sel) : undefined;
  const selTable = sel?.type === 'table' ? plan.tables.find(t => t.tableId === sel.id) : undefined;

  const rotate = () => sel && selected && update(p => {
    const r = { ...selected, w: selected.h, h: selected.w };
    r.x = clamp(r.x, 0, p.width - r.w); r.y = clamp(r.y, 0, p.height - r.h);
    return sel.type === 'table'
      ? { ...p, tables: p.tables.map(t => (t.tableId === sel.id ? { ...t, ...r } : t)) }
      : { ...p, walls: p.walls.map(w => (w.id === sel.id ? { ...w, ...r } : w)) };
  });
  const remove = () => {
    if (!sel) return;
    update(p => (sel.type === 'table' ? { ...p, tables: p.tables.filter(t => t.tableId !== sel.id) } : { ...p, walls: p.walls.filter(w => w.id !== sel.id) }));
    setSel(null);
  };
  const toggleShape = () => selTable && update(p => ({
    ...p, tables: p.tables.map(t => (t.tableId === selTable.tableId ? { ...t, shape: t.shape === 'round' ? 'rect' : 'round' } : t)),
  }));

  const submit = async () => {
    setErr('');
    try {
      const clean = { ...plan, tables: plan.tables.filter(t => nameOf.has(t.tableId)) };
      await save.mutateAsync(clean);
      setDirty(false);
      setToast('Схема зала сохранена');
    } catch (e) { setErr(apiError(e)); }
  };

  const setSize = (key: 'width' | 'height', v: number) => update(p => ({ ...p, [key]: clamp(snap(v || 0), 200, 3000) }));

  return (
    <Box sx={{ maxWidth: 1300, mx: 'auto', p: { xs: 2, md: 3 }, display: 'flex', flexDirection: 'column', gap: 2 }}>
      <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5, flexWrap: 'wrap' }}>
        <Typography variant="h4" sx={{ fontWeight: 800, flex: 1 }}>Зал и столы</Typography>
        <Button variant="outlined" startIcon={<TablesIcon />} onClick={() => setTablesOpen(true)}>Список столов</Button>
        <Button variant="contained" startIcon={<SaveIcon />} onClick={submit} disabled={!dirty || save.isPending}>Сохранить</Button>
      </Box>
      <Typography variant="body2" color="text.secondary">
        Перетаскивайте столы и перегородки, тяните за уголок, чтобы изменить размер. Официант увидит зал так же на вкладке «Зал».
      </Typography>

      <Box sx={{ display: 'flex', gap: 1, flexWrap: 'wrap', alignItems: 'center' }}>
        <Button size="small" startIcon={<AddIcon />} onClick={() => addWall('wall')}>Перегородка</Button>
        <Button size="small" startIcon={<AddIcon />} onClick={() => addWall('bar')}>Барная стойка</Button>
        <Button size="small" startIcon={<AddIcon />} onClick={() => addWall('plant')}>Декор</Button>
        <Box sx={{ flex: 1 }} />
        <TextField size="small" type="number" label="Ширина" value={plan.width} onChange={e => setSize('width', Number(e.target.value))} sx={{ width: 100 }} />
        <TextField size="small" type="number" label="Глубина" value={plan.height} onChange={e => setSize('height', Number(e.target.value))} sx={{ width: 100 }} />
      </Box>

      {unplaced.length > 0 && (
        <Box sx={{ display: 'flex', gap: 0.75, flexWrap: 'wrap', alignItems: 'center' }}>
          <Typography variant="body2" color="text.secondary">Не на схеме:</Typography>
          {unplaced.map(t => <Chip key={t.id} label={`Стол ${t.name}`} onClick={() => placeTable(t.id)} icon={<AddIcon />} />)}
          {unplaced.length > 1 && <Button size="small" onClick={placeAll}>Расставить все</Button>}
        </Box>
      )}

      {sel && selected && (
        <Box sx={{ display: 'flex', gap: 1, alignItems: 'center', p: 1, borderRadius: '12px', bgcolor: 'action.hover', flexWrap: 'wrap' }}>
          <Typography sx={{ fontWeight: 700, mr: 1 }}>
            {sel.type === 'table' ? `Стол ${nameOf.get(sel.id) ?? ''}` : KIND_LABEL[(selected as FloorWall).kind]}
          </Typography>
          <Typography variant="body2" color="text.secondary">{selected.w} × {selected.h}</Typography>
          <Box sx={{ flex: 1 }} />
          {selTable && (
            <Tooltip title={selTable.shape === 'round' ? 'Сделать прямоугольным' : 'Сделать круглым'}>
              <IconButton onClick={toggleShape}>{selTable.shape === 'round' ? <RectIcon /> : <RoundIcon />}</IconButton>
            </Tooltip>
          )}
          <Tooltip title="Повернуть"><IconButton onClick={rotate}><RotateIcon /></IconButton></Tooltip>
          <Tooltip title="Убрать со схемы"><IconButton color="error" onClick={remove}><DeleteIcon /></IconButton></Tooltip>
        </Box>
      )}

      <Box sx={{ overflowX: 'auto' }}>
        <Box ref={boxRef} onPointerMove={onMove} onPointerUp={onUp} onPointerCancel={onUp} onPointerDown={() => setSel(null)}
          sx={{ position: 'relative', width: '100%', minWidth: 560, aspectRatio: `${plan.width} / ${plan.height}`, borderRadius: '16px',
            border: '1px solid', borderColor: 'divider', bgcolor: 'background.paper', touchAction: 'none', userSelect: 'none',
            backgroundImage: `radial-gradient(${alpha(theme.palette.text.primary, 0.12)} 1px, transparent 1px)`,
            backgroundSize: `${(20 / plan.width) * 100}% ${(20 / plan.height) * 100}%` }}>
          {plan.walls.map(w => {
            const on = sel?.type === 'wall' && sel.id === w.id;
            return (
              <Box key={w.id} onPointerDown={e => onDown(e, { type: 'wall', id: w.id }, 'move')}
                sx={{ position: 'absolute', left: `${(w.x / plan.width) * 100}%`, top: `${(w.y / plan.height) * 100}%`,
                  width: `${(w.w / plan.width) * 100}%`, height: `${(w.h / plan.height) * 100}%`, cursor: 'move',
                  borderRadius: w.kind === 'plant' ? '50%' : '3px', outline: on ? `2px solid ${theme.palette.primary.main}` : 'none', outlineOffset: 2,
                  bgcolor: w.kind === 'bar' ? alpha(tokens.saffron, 0.5) : w.kind === 'plant' ? alpha(tokens.herb, 0.45) : theme.palette.text.secondary }}>
                {on && <Handle onPointerDown={e => onDown(e, { type: 'wall', id: w.id }, 'resize')} />}
              </Box>
            );
          })}
          {plan.tables.map(t => {
            const name = nameOf.get(t.tableId);
            if (name == null) return null;
            const on = sel?.type === 'table' && sel.id === t.tableId;
            return (
              <Box key={t.tableId} onPointerDown={e => onDown(e, { type: 'table', id: t.tableId }, 'move')}
                sx={{ position: 'absolute', left: `${(t.x / plan.width) * 100}%`, top: `${(t.y / plan.height) * 100}%`,
                  width: `${(t.w / plan.width) * 100}%`, height: `${(t.h / plan.height) * 100}%`, cursor: 'move',
                  borderRadius: t.shape === 'round' ? '50%' : '10px', border: '2px solid',
                  borderColor: on ? 'primary.main' : 'text.secondary', bgcolor: on ? alpha(theme.palette.primary.main, 0.12) : 'background.default',
                  display: 'grid', placeItems: 'center' }}>
                <Typography sx={{ fontWeight: 800, pointerEvents: 'none' }}>{name}</Typography>
                {on && <Handle onPointerDown={e => onDown(e, { type: 'table', id: t.tableId }, 'resize')} />}
              </Box>
            );
          })}
        </Box>
      </Box>

      {err && <Alert severity="error">{err}</Alert>}
      {tablesOpen && <TablesSettings onClose={() => setTablesOpen(false)} />}
      <Snackbar open={!!toast} autoHideDuration={2500} onClose={() => setToast('')} message={toast} />
    </Box>
  );
};

const Handle: React.FC<{ onPointerDown: (e: React.PointerEvent) => void }> = ({ onPointerDown }) => (
  <Box onPointerDown={onPointerDown}
    sx={{ position: 'absolute', right: -8, bottom: -8, width: 16, height: 16, borderRadius: '4px', bgcolor: 'primary.main',
      border: '2px solid', borderColor: 'background.paper', cursor: 'nwse-resize', touchAction: 'none' }} />
);

export default FloorEditorPage;
