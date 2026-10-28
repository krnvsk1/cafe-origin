import * as React from 'react';
import {
  Box, Typography, Button, Chip, Checkbox, CircularProgress, Alert, IconButton, Menu, MenuItem,
  Dialog, DialogTitle, DialogContent, DialogActions,
} from '@mui/material';
import { Sync as SyncIcon, MoreVert as MoreIcon, PetsOutlined as VetIcon, DoneAll as AcceptIcon } from '@mui/icons-material';
import { useQueryClient } from '@tanstack/react-query';
import {
  useMercuryState, useMercuryDocuments, mercurySync, mercuryAccept, mercuryIgnore, mercuryRestore, MERCURY_STATUS, type MercuryDocument,
} from '../../api/mercury';
import { apiError, fmtDate, fmtDateTime } from '../../api/inventory';
import { useCan, P } from '../../auth/permissions';
import { tokens } from '../../theme';
import { Panel, EmptyState } from './ui';
import IntegrationStatus from './IntegrationStatus';

const FILTERS: { value: 'new' | 'done' | 'ignored' | 'all'; label: string }[] = [
  { value: 'new', label: 'Не погашены' },
  { value: 'done', label: 'Погашены' },
  { value: 'ignored', label: 'Скрытые' },
  { value: 'all', label: 'Все' },
];

const qty = (d: MercuryDocument) => `${String(d.volume).replace('.', ',')} ${d.unitName ?? ''}`.trim();

/** Меркурий: входящие ВСД на площадку заведения; гашение — полное принятие партии */
const MercuryTab: React.FC = () => {
  const qc = useQueryClient();
  const can = useCan();
  const canManage = can(P.InventoryManage);
  const { data: state, isLoading } = useMercuryState();
  const [filter, setFilter] = React.useState<'new' | 'done' | 'ignored' | 'all'>('new');
  const { data: docs = [], isLoading: docsLoading } = useMercuryDocuments(filter);
  const [selected, setSelected] = React.useState<Set<number>>(new Set());
  const [busy, setBusy] = React.useState<'sync' | 'accept' | null>(null);
  const [message, setMessage] = React.useState<{ ok: boolean; text: string } | null>(null);
  const [confirm, setConfirm] = React.useState(false);
  const [menu, setMenu] = React.useState<{ anchor: HTMLElement; doc: MercuryDocument } | null>(null);

  React.useEffect(() => { setSelected(new Set()); }, [filter]);

  const refresh = () => qc.invalidateQueries({ queryKey: ['mercury'] });

  const sync = async () => {
    setBusy('sync');
    setMessage(null);
    try {
      const r = await mercurySync();
      setMessage({ ok: true, text: `${r.received ? `Новых ВСД: ${r.received}` : 'Новых ВСД нет'}${r.external ? ` · погашено вне сервиса: ${r.external}` : ''}` });
    } catch (e) {
      setMessage({ ok: false, text: apiError(e) });
    } finally {
      setBusy(null);
      await refresh();
    }
  };

  const accept = async () => {
    setConfirm(false);
    setBusy('accept');
    setMessage(null);
    try {
      const r = await mercuryAccept(Array.from(selected));
      setMessage({
        ok: r.errors.length === 0,
        text: [r.accepted ? `Погашено: ${r.accepted}` : '', ...r.errors].filter(Boolean).join('\n') || 'Ничего не погашено',
      });
      setSelected(new Set());
    } catch (e) {
      setMessage({ ok: false, text: apiError(e) });
    } finally {
      setBusy(null);
      await refresh();
    }
  };

  const act = async (fn: (id: number) => Promise<unknown>, doc: MercuryDocument) => {
    setMenu(null);
    try { await fn(doc.id); await refresh(); } catch (e) { setMessage({ ok: false, text: apiError(e) }); }
  };

  if (isLoading || !state) return <Box sx={{ display: 'grid', placeItems: 'center', py: 8 }}><CircularProgress /></Box>;
  const configured = state.enabled && !!state.login && state.hasPassword && state.hasApiKey;
  const selectable = docs.filter(d => d.status === 'new' || d.status === 'error');
  const chosen = docs.filter(d => selected.has(d.id));

  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
      <Panel>
        <IntegrationStatus
          section="mercury"
          ok={configured && !state.lastError}
          failed={configured}
          title={!configured ? 'Меркурий не подключён' : state.lastError ? 'Меркурий: ошибка загрузки' : `Меркурий подключён · ${state.initiatorLogin}`}
          details={configured
            ? `${state.sandbox ? 'тестовый контур · ' : ''}${state.lastSyncAt ? `проверено ${fmtDateTime(state.lastSyncAt)}` : 'ещё не проверялось'}${state.autoSyncMinutes ? ` · автоматически каждые ${state.autoSyncMinutes} мин` : ''}`
            : 'Входящие ВСД на мясо, курицу, молочку будут появляться здесь — останется проверить партию и погасить. Подключите в «Настройке интеграций».'}
          error={configured ? state.lastError : null}
        >
          {configured && (
            <Button variant="contained" color="secondary" onClick={sync} disabled={!!busy}
              startIcon={busy === 'sync' ? <CircularProgress size={16} color="inherit" /> : <SyncIcon />}>
              Загрузить
            </Button>
          )}
        </IntegrationStatus>
        {message && <Alert severity={message.ok ? 'success' : 'error'} sx={{ mt: 1.5, whiteSpace: 'pre-line' }} onClose={() => setMessage(null)}>{message.text}</Alert>}
      </Panel>

      <Box sx={{ display: 'flex', gap: 1, flexWrap: 'wrap', alignItems: 'center' }}>
        {FILTERS.map(f => (
          <Chip key={f.value} label={f.value === 'new' && state.newCount ? `${f.label} · ${state.newCount}` : f.label}
            color={filter === f.value ? 'primary' : 'default'} onClick={() => setFilter(f.value)} />
        ))}
        <Box sx={{ flex: 1 }} />
        {canManage && selectable.length > 0 && (
          <Button variant="contained" color="secondary" startIcon={busy === 'accept' ? <CircularProgress size={16} color="inherit" /> : <AcceptIcon />}
            disabled={!!busy || chosen.length === 0} onClick={() => setConfirm(true)}>
            Погасить · {chosen.length}
          </Button>
        )}
      </Box>

      <Panel padding={false}>
        {docsLoading ? <Box sx={{ display: 'grid', placeItems: 'center', py: 6 }}><CircularProgress /></Box>
          : docs.length === 0 ? <EmptyState icon={<VetIcon />} title={filter === 'new' ? 'Непогашенных ВСД нет' : 'Пусто'} />
          : (
            <>
              {canManage && selectable.length > 0 && (
                <Box sx={{ display: 'flex', alignItems: 'center', px: 1, py: 0.5 }}>
                  <Checkbox
                    checked={selectable.length > 0 && selectable.every(d => selected.has(d.id))}
                    indeterminate={chosen.length > 0 && chosen.length < selectable.length}
                    onChange={e => setSelected(e.target.checked ? new Set(selectable.map(d => d.id)) : new Set())}
                  />
                  <Typography variant="body2" color="text.secondary">Выбрать все непогашенные</Typography>
                </Box>
              )}
              {docs.map(d => {
                const s = MERCURY_STATUS[d.status];
                const pending = d.status === 'new' || d.status === 'error';
                return (
                  <Box key={d.id} sx={{ display: 'flex', alignItems: 'flex-start', gap: 1, px: 1, py: 1.25, borderTop: '1px solid', borderColor: 'divider',
                    opacity: d.status === 'ignored' ? 0.6 : 1 }}>
                    {canManage && (
                      <Checkbox disabled={!pending} checked={selected.has(d.id)} onChange={e => setSelected(prev => {
                        const n = new Set(prev);
                        if (e.target.checked) n.add(d.id); else n.delete(d.id);
                        return n;
                      })} />
                    )}
                    <Box sx={{ flex: 1, minWidth: 0, pl: canManage ? 0 : 1 }}>
                      <Box sx={{ display: 'flex', gap: 1, alignItems: 'baseline', flexWrap: 'wrap' }}>
                        <Typography sx={{ fontWeight: 600 }}>{d.productName ?? 'Продукция'}</Typography>
                        <Typography sx={{ fontWeight: 700 }}>{qty(d)}</Typography>
                        <Chip size="small" label={s.label} sx={{ bgcolor: `${s.color}1f`, color: s.color, fontWeight: 700 }} />
                      </Box>
                      <Typography variant="body2" color="text.secondary">
                        {[d.consignorName, d.waybill, d.issueDate && `ВСД от ${fmtDate(d.issueDate)}`,
                          d.productionDate && `выработка ${d.productionDate}`, d.expiryDate && `годен до ${d.expiryDate}`].filter(Boolean).join(' · ')}
                      </Typography>
                      {d.error && <Typography variant="body2" sx={{ color: tokens.chili }}>{d.error}</Typography>}
                      {d.processedAt && <Typography variant="caption" color="text.secondary">Погашен {fmtDateTime(d.processedAt)}</Typography>}
                    </Box>
                    {canManage && (d.status === 'new' || d.status === 'error' || d.status === 'ignored') && (
                      <IconButton aria-label="Действия" onClick={e => setMenu({ anchor: e.currentTarget, doc: d })}><MoreIcon /></IconButton>
                    )}
                  </Box>
                );
              })}
            </>
          )}
      </Panel>

      <Menu anchorEl={menu?.anchor} open={!!menu} onClose={() => setMenu(null)}>
        {menu && menu.doc.status !== 'ignored' && <MenuItem onClick={() => act(mercuryIgnore, menu.doc)}>Скрыть (не гасить здесь)</MenuItem>}
        {menu && menu.doc.status === 'ignored' && <MenuItem onClick={() => act(mercuryRestore, menu.doc)}>Вернуть в непогашенные</MenuItem>}
      </Menu>

      <Dialog open={confirm} onClose={() => setConfirm(false)} maxWidth="sm" fullWidth>
        <DialogTitle>Погасить {chosen.length} ВСД?</DialogTitle>
        <DialogContent sx={{ display: 'flex', flexDirection: 'column', gap: 1 }}>
          <Typography>
            Партии будут приняты <b>полностью, как в документе</b>: объём, производитель, сроки. Проверьте, что товар пришёл целиком и без замечаний.
          </Typography>
          <Typography variant="body2" color="text.secondary">
            Если пришло меньше, другой товар или есть порча — гасите такой ВСД в самом Меркурии с актом несоответствия, а здесь нажмите «Скрыть».
          </Typography>
          <Box component="ul" sx={{ m: 0, pl: 2.5 }}>
            {chosen.map(d => <li key={d.id}><Typography variant="body2">{d.productName} — {qty(d)}</Typography></li>)}
          </Box>
        </DialogContent>
        <DialogActions sx={{ px: 3, pb: 3 }}>
          <Button onClick={() => setConfirm(false)} sx={{ color: 'text.secondary' }}>Отмена</Button>
          <Button variant="contained" color="secondary" onClick={accept}>Погасить</Button>
        </DialogActions>
      </Dialog>
    </Box>
  );
};

export default MercuryTab;
