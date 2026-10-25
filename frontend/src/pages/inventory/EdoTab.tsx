import * as React from 'react';
import {
  Box, Typography, Button, TextField, Switch, FormControlLabel, Alert, CircularProgress, Chip, IconButton, Checkbox,
  Dialog, DialogTitle, DialogContent, DialogActions, Autocomplete, InputAdornment, createFilterOptions, useTheme, useMediaQuery,
} from '@mui/material';
import {
  Close as CloseIcon, SettingsOutlined as SettingsIcon, Sync as SyncIcon, ChevronRight as ChevronRightIcon,
  Add as AddIcon, ReceiptLongOutlined as DocIcon, QrCode2 as CodesIcon, AutoAwesome as RememberedIcon,
} from '@mui/icons-material';
import { useQueryClient } from '@tanstack/react-query';
import { useNavigate } from 'react-router-dom';
import {
  useEdoState, useEdoDocuments, useEdoDocument, useSaveEdoSettings, testEdoSettings, edoSync, edoImport, edoIgnore, edoRestore,
  EDO_STATUS, OKEI, CHZ_PREFILL_KEY, defaultFactor, type EdoState, type EdoStatus, type EdoLine,
} from '../../api/edo';
import { useStockItems, apiError, fmtMoney, fmtDate, fmtDateTime, fmtQty, UNIT_LABELS, type StockItem } from '../../api/inventory';
import { useCan, P } from '../../auth/permissions';
import { tokens } from '../../theme';
import { Panel, ListRow, EmptyState } from './ui';
import StockItemDialog from './StockItemDialog';

type CreateOption = { create: true; inputValue: string };
const isCreate = (o: unknown): o is CreateOption => !!o && typeof o === 'object' && 'create' in (o as object);
const itemFilter = createFilterOptions<StockItem>({ stringify: o => `${o.name} ${o.category ?? ''}` });


const unitOf = (l: EdoLine) => l.unitName || (l.okei ? OKEI[l.okei] : null) || 'ед.';

// ============ Настройки ============

const EdoSettingsDialog: React.FC<{ open: boolean; state: EdoState; onClose: () => void }> = ({ open, state, onClose }) => {
  const save = useSaveEdoSettings();
  const [enabled, setEnabled] = React.useState(true);
  const [login, setLogin] = React.useState('');
  const [password, setPassword] = React.useState('');
  const [account, setAccount] = React.useState('');
  const [minutes, setMinutes] = React.useState('15');
  const [days, setDays] = React.useState('14');
  const [busy, setBusy] = React.useState(false);
  const [result, setResult] = React.useState<{ ok: boolean; text: string } | null>(null);

  React.useEffect(() => {
    if (!open) return;
    setEnabled(state.enabled || !state.login);
    setLogin(state.login);
    setPassword('');
    setAccount(state.account ?? '');
    setMinutes(String(state.autoSyncMinutes));
    setDays(String(state.initialDays));
    setResult(null);
  }, [open]); // eslint-disable-line react-hooks/exhaustive-deps

  const input = () => ({
    enabled, login: login.trim(), password: password || null, account: account.trim() || null,
    autoSyncMinutes: Number(minutes) || 0, initialDays: Number(days) || 14,
  });

  const test = async () => {
    setBusy(true);
    setResult(null);
    try {
      const r = await testEdoSettings(input());
      setResult({ ok: r.ok, text: r.message });
    } catch (e) {
      setResult({ ok: false, text: apiError(e) });
    } finally {
      setBusy(false);
    }
  };

  const submit = async () => {
    setResult(null);
    try {
      await save.mutateAsync(input());
      onClose();
    } catch (e) {
      setResult({ ok: false, text: apiError(e) });
    }
  };

  return (
    <Dialog open={open} onClose={onClose} maxWidth="sm" fullWidth>
      <DialogTitle>Подключение к СБИС</DialogTitle>
      <DialogContent sx={{ display: 'flex', flexDirection: 'column', gap: 2, pt: '8px !important' }}>
        <Typography variant="body2" color="text.secondary">
          Сервис заходит в СБИС (Saby) от имени пользователя и забирает входящие УПД. Лучше завести отдельного
          сотрудника с доступом только к входящим документам и без подтверждения входа по SMS.
        </Typography>
        <FormControlLabel control={<Switch checked={enabled} onChange={e => setEnabled(e.target.checked)} />} label="Загружать накладные из СБИС" />
        <TextField label="Логин СБИС" value={login} onChange={e => setLogin(e.target.value)} autoComplete="off" />
        <TextField
          label="Пароль"
          type="password"
          value={password}
          onChange={e => setPassword(e.target.value)}
          autoComplete="new-password"
          placeholder={state.hasPassword ? 'Сохранён — введите, чтобы заменить' : ''}
          slotProps={{ inputLabel: { shrink: state.hasPassword || !!password } }}
        />
        <TextField label="Номер аккаунта (если кабинетов несколько)" value={account} onChange={e => setAccount(e.target.value)} />
        <Box sx={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 1.5 }}>
          <TextField
            label="Проверять каждые"
            type="number"
            value={minutes}
            onChange={e => setMinutes(e.target.value)}
            helperText="0 — только вручную"
            slotProps={{ input: { endAdornment: <InputAdornment position="end">мин</InputAdornment> }, htmlInput: { min: 0, max: 1440 } }}
          />
          <TextField
            label="Первая загрузка за"
            type="number"
            value={days}
            onChange={e => setDays(e.target.value)}
            helperText="С какой даты забрать накладные"
            slotProps={{ input: { endAdornment: <InputAdornment position="end">дн</InputAdornment> }, htmlInput: { min: 1, max: 90 } }}
          />
        </Box>
        {result && <Alert severity={result.ok ? 'success' : 'error'}>{result.text}</Alert>}
      </DialogContent>
      <DialogActions sx={{ px: 3, pb: 3, gap: 1 }}>
        <Button onClick={test} disabled={busy || !login.trim()} sx={{ mr: 'auto' }}
          startIcon={busy ? <CircularProgress size={16} color="inherit" /> : undefined}>
          Проверить вход
        </Button>
        <Button onClick={onClose} sx={{ color: 'text.secondary' }}>Отмена</Button>
        <Button variant="contained" color="secondary" onClick={submit} disabled={save.isPending}>Сохранить</Button>
      </DialogActions>
    </Dialog>
  );
};

// ============ Накладная ============

interface LineChoice { stockItemId: number | null; factor: string; skip: boolean }

const EdoDocumentDialog: React.FC<{ id: number | null; onClose: () => void; onOpenReceipt: (id: number) => void }> = ({ id, onClose, onOpenReceipt }) => {
  const theme = useTheme();
  const t = tokens[theme.palette.mode];
  const fullScreen = useMediaQuery(theme.breakpoints.down('md'));
  const qc = useQueryClient();
  const navigate = useNavigate();
  const can = useCan();
  const canManage = can(P.InventoryManage);
  const { data: doc, isLoading } = useEdoDocument(id);
  const { data: items = [] } = useStockItems({ kind: 'raw' });

  const [choices, setChoices] = React.useState<Record<number, LineChoice>>({});
  const [post, setPost] = React.useState(true);
  const [busy, setBusy] = React.useState(false);
  const [error, setError] = React.useState('');
  const [newItem, setNewItem] = React.useState<{ no: number; name: string } | null>(null);

  React.useEffect(() => {
    if (!doc?.lines) return;
    setChoices(Object.fromEntries(doc.lines.map(l => [l.no, { stockItemId: l.stockItemId, factor: String(l.factor), skip: false }])));
    setError('');
  }, [doc?.id, doc?.lines]); // eslint-disable-line react-hooks/exhaustive-deps

  const setChoice = (no: number, patch: Partial<LineChoice>) => setChoices(c => ({ ...c, [no]: { ...c[no], ...patch } }));

  const pickItem = (l: EdoLine, item: StockItem | null) => {
    const prev = choices[l.no];
    // Коэффициент по единицам — только если пользователь его ещё не менял
    const factor = item && (!prev || prev.factor === '1' || prev.stockItemId !== item.id) ? String(defaultFactor(l.okei, item.unit)) : prev?.factor ?? '1';
    setChoice(l.no, { stockItemId: item?.id ?? null, factor, skip: false });
  };

  const editable = !!doc && canManage && doc.status === 'new' && !!doc.lines;
  const lines = doc?.lines ?? [];
  const ready = lines.length > 0 && lines.every(l => {
    const c = choices[l.no];
    return c && (c.skip || (c.stockItemId && Number(c.factor) > 0));
  });
  const allCodes = lines.flatMap(l => l.codes);

  const doImport = async () => {
    if (!doc) return;
    setBusy(true);
    setError('');
    try {
      const r = await edoImport(doc.id, lines.map(l => ({
        no: l.no,
        stockItemId: choices[l.no]?.stockItemId ?? null,
        factor: Number(choices[l.no]?.factor) || 1,
        skip: !!choices[l.no]?.skip,
      })), post);
      await qc.invalidateQueries({ queryKey: ['edo'] });
      await qc.invalidateQueries({ queryKey: ['inventory'] });
      onClose();
      onOpenReceipt(r.stockDocumentId);
    } catch (e) {
      setError(apiError(e));
    } finally {
      setBusy(false);
    }
  };

  const setStatus = async (fn: (id: number) => Promise<unknown>) => {
    if (!doc) return;
    setBusy(true);
    setError('');
    try {
      await fn(doc.id);
      await qc.invalidateQueries({ queryKey: ['edo'] });
      onClose();
    } catch (e) {
      setError(apiError(e));
    } finally {
      setBusy(false);
    }
  };

  const toChz = () => {
    try { sessionStorage.setItem(CHZ_PREFILL_KEY, JSON.stringify({ codes: allCodes, comment: `УПД № ${doc?.number ?? ''} ${doc?.supplierName ?? ''}`.trim() })); } catch { /* ignore */ }
    onClose();
    navigate('/admin/inventory/chz');
  };

  const s = doc ? EDO_STATUS[doc.status] : null;

  return (
    <Dialog open={!!id} onClose={() => !busy && onClose()} fullScreen={fullScreen} maxWidth="md" fullWidth>
      <DialogTitle sx={{ display: 'flex', alignItems: 'center', gap: 1, pr: 1.5 }}>
        <Box sx={{ flex: 1, minWidth: 0 }}>
          <Typography sx={{ fontWeight: 700, fontSize: '1.15rem' }} noWrap>
            {doc ? `УПД № ${doc.number ?? '—'} от ${fmtDate(doc.date)}` : 'Накладная'}
          </Typography>
          {doc && <Typography variant="body2" color="text.secondary" noWrap>{doc.supplierName}{doc.supplierInn ? ` · ИНН ${doc.supplierInn}` : ''}</Typography>}
        </Box>
        {s && <Chip size="small" label={s.label} sx={{ bgcolor: `${s.color}1f`, color: s.color, fontWeight: 700 }} />}
        <IconButton aria-label="Закрыть" onClick={onClose} disabled={busy}><CloseIcon /></IconButton>
      </DialogTitle>
      <DialogContent sx={{ display: 'flex', flexDirection: 'column', gap: 1.5 }}>
        {isLoading || !doc ? <Box sx={{ display: 'grid', placeItems: 'center', py: 8 }}><CircularProgress /></Box> : (
          <>
            {doc.error && <Alert severity="error">{doc.error}</Alert>}
            {doc.stockDocumentId && (
              <Alert severity="success" action={<Button color="inherit" size="small" onClick={() => { onClose(); onOpenReceipt(doc.stockDocumentId!); }}>Открыть</Button>}>
                Оприходовано: приход #{doc.stockDocumentId}
              </Alert>
            )}
            {editable && (
              <Typography variant="body2" color="text.secondary">
                Выберите для каждой строки позицию склада. Выбор запоминается — в следующих накладных этого поставщика строки заполнятся сами.
              </Typography>
            )}

            <Box>
              {lines.map((l, i) => {
                const c = choices[l.no] ?? { stockItemId: l.stockItemId, factor: String(l.factor), skip: false };
                const item = c.stockItemId ? items.find(x => x.id === c.stockItemId) ?? null : null;
                const selected = item ?? (c.stockItemId && l.stockItemId === c.stockItemId
                  ? ({ id: l.stockItemId, name: l.stockItemName ?? '', unit: l.stockUnit ?? 'kg' } as StockItem) : null);
                const factor = Number(c.factor) || 0;
                const stockQty = selected && factor > 0 ? Math.round(l.quantity * factor * 1000) / 1000 : null;
                return (
                  <Box key={l.no} sx={{ py: 1.5, borderTop: i ? '1px solid' : 0, borderColor: 'divider', display: 'flex', flexDirection: 'column', gap: 1, opacity: c.skip ? 0.55 : 1 }}>
                    <Box sx={{ display: 'flex', gap: 1.5, alignItems: 'flex-start' }}>
                      <Box sx={{ flex: 1, minWidth: 0 }}>
                        <Typography sx={{ fontWeight: 600 }}>{l.no}. {l.name}</Typography>
                        <Typography variant="body2" color="text.secondary">
                          {[l.code && `арт. ${l.code}`, l.gtin && `GTIN ${l.gtin}`, l.vatRate && `НДС ${l.vatRate}`].filter(Boolean).join(' · ')}
                          {l.codes.length > 0 && <Chip size="small" icon={<CodesIcon />} label={`${l.codes.length} КМ`} sx={{ ml: 1, height: 20 }} />}
                        </Typography>
                      </Box>
                      <Box sx={{ textAlign: 'right', flexShrink: 0 }}>
                        <Typography sx={{ fontWeight: 700 }}>{fmtQty(l.quantity)} {unitOf(l)}</Typography>
                        <Typography variant="body2" color="text.secondary">{fmtMoney(l.sum)}</Typography>
                      </Box>
                    </Box>

                    {(editable || selected) && (
                      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: 'minmax(0, 1fr)', sm: 'minmax(0,1fr) 190px auto' }, gap: 1, alignItems: 'start' }}>
                        <Autocomplete<StockItem | CreateOption, false, false, false>
                          size="small"
                          options={items}
                          value={selected}
                          disabled={!editable || busy || c.skip}
                          onChange={(_, v) => {
                            if (isCreate(v)) setNewItem({ no: l.no, name: v.inputValue });
                            else pickItem(l, v ?? null);
                          }}
                          filterOptions={(opts, params) => {
                            const filtered = itemFilter(opts as StockItem[], params) as (StockItem | CreateOption)[];
                            filtered.push({ create: true, inputValue: params.inputValue.trim() || l.name });
                            return filtered;
                          }}
                          getOptionLabel={o => (isCreate(o) ? o.inputValue : o.name)}
                          isOptionEqualToValue={(a, b) => !isCreate(a) && !isCreate(b) && a.id === b.id}
                          renderOption={(props, o) => {
                            const { key, ...rest } = props as typeof props & { key: string };
                            return isCreate(o)
                              ? <li key={key} {...rest}><AddIcon fontSize="small" sx={{ mr: 1 }} />Создать «{o.inputValue}»</li>
                              : <li key={key} {...rest}>{o.name} <Typography component="span" color="text.secondary" sx={{ ml: 1 }}>{UNIT_LABELS[o.unit]}</Typography></li>;
                          }}
                          renderInput={params => (
                            <TextField
                              {...params}
                              placeholder="Позиция склада"
                              helperText={stockQty !== null && selected ? `На склад: ${fmtQty(stockQty, selected.unit)}` : ' '}
                              InputProps={{
                                ...params.InputProps,
                                startAdornment: l.remembered && c.stockItemId === l.stockItemId
                                  ? <><RememberedIcon fontSize="small" sx={{ color: t.muted, mr: 0.5 }} titleAccess="Подставлено из прошлых накладных" />{params.InputProps.startAdornment}</>
                                  : params.InputProps.startAdornment,
                              }}
                            />
                          )}
                        />
                        <TextField
                          size="small"
                          type="number"
                          label={`В 1 ${unitOf(l)}`}
                          value={c.factor}
                          disabled={!editable || busy || c.skip || !selected}
                          onChange={e => setChoice(l.no, { factor: e.target.value })}
                          helperText="Сколько единиц склада"
                          slotProps={{
                            input: { endAdornment: <InputAdornment position="end">{selected ? UNIT_LABELS[selected.unit] : ''}</InputAdornment> },
                            htmlInput: { min: 0, step: 'any', inputMode: 'decimal' },
                          }}
                        />
                        {editable && (
                          <FormControlLabel
                            sx={{ mr: 0, whiteSpace: 'nowrap' }}
                            control={<Checkbox size="small" checked={c.skip} onChange={e => setChoice(l.no, { skip: e.target.checked })} />}
                            label="Не приходовать"
                          />
                        )}
                      </Box>
                    )}
                  </Box>
                );
              })}
            </Box>

            <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', bgcolor: t.surfaceMuted, borderRadius: '14px', px: 2, py: 1.5, flexWrap: 'wrap', gap: 1 }}>
              <Typography sx={{ fontWeight: 700 }}>Итого с НДС: {fmtMoney(doc.total)}</Typography>
              {allCodes.length > 0 && can(P.Chz) && (
                <Button size="small" startIcon={<CodesIcon />} onClick={toChz}>Вывести {allCodes.length} КМ из оборота</Button>
              )}
            </Box>
            {error && <Alert severity="error">{error}</Alert>}
          </>
        )}
      </DialogContent>
      {doc && canManage && (
        <DialogActions sx={{ px: 3, pb: 3, gap: 1, flexWrap: 'wrap' }}>
          {doc.status === 'new' && (
            <Button onClick={() => setStatus(edoIgnore)} disabled={busy} sx={{ color: 'text.secondary', mr: 'auto' }}>Скрыть</Button>
          )}
          {doc.status === 'ignored' && (
            <Button onClick={() => setStatus(edoRestore)} disabled={busy} sx={{ mr: 'auto' }}>Вернуть в новые</Button>
          )}
          {editable && (
            <>
              <FormControlLabel control={<Switch checked={post} onChange={e => setPost(e.target.checked)} />} label="Сразу провести" />
              <Button variant="contained" color="secondary" onClick={doImport} disabled={busy || !ready}
                startIcon={busy ? <CircularProgress size={16} color="inherit" /> : undefined}>
                Оприходовать
              </Button>
            </>
          )}
        </DialogActions>
      )}

      <StockItemDialog
        open={!!newItem}
        initialName={newItem?.name}
        allowSemi={false}
        onClose={() => setNewItem(null)}
        onSaved={(item) => {
          const l = lines.find(x => x.no === newItem?.no);
          if (l) pickItem(l, item);
        }}
      />
    </Dialog>
  );
};

// ============ Вкладка ============

const FILTERS: { value: EdoStatus | 'all'; label: string }[] = [
  { value: 'new', label: 'Новые' },
  { value: 'imported', label: 'Оприходованные' },
  { value: 'ignored', label: 'Скрытые' },
  { value: 'error', label: 'С ошибкой' },
  { value: 'all', label: 'Все' },
];

const EdoTab: React.FC<{ onOpenReceipt: (id: number) => void }> = ({ onOpenReceipt }) => {
  const theme = useTheme();
  const t = tokens[theme.palette.mode];
  const qc = useQueryClient();
  const can = useCan();
  const { data: state, isLoading } = useEdoState();
  const [filter, setFilter] = React.useState<EdoStatus | 'all'>('new');
  const { data: docs = [], isLoading: docsLoading } = useEdoDocuments(filter);
  const [settingsOpen, setSettingsOpen] = React.useState(false);
  const [openId, setOpenId] = React.useState<number | null>(null);
  const [syncing, setSyncing] = React.useState(false);
  const [message, setMessage] = React.useState<{ ok: boolean; text: string } | null>(null);

  const sync = async () => {
    setSyncing(true);
    setMessage(null);
    try {
      const r = await edoSync();
      setMessage({ ok: true, text: r.received ? `Новых накладных: ${r.received}${r.errors ? `, с ошибкой: ${r.errors}` : ''}` : 'Новых накладных нет' });
    } catch (e) {
      setMessage({ ok: false, text: apiError(e) });
    } finally {
      setSyncing(false);
      await qc.invalidateQueries({ queryKey: ['edo'] });
    }
  };

  if (isLoading || !state) return <Box sx={{ display: 'grid', placeItems: 'center', py: 8 }}><CircularProgress /></Box>;
  const configured = state.enabled && !!state.login && state.hasPassword;

  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
      <Panel>
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5, flexWrap: 'wrap' }}>
          <Box sx={{ flex: 1, minWidth: 220 }}>
            <Typography sx={{ fontWeight: 700 }}>{configured ? `СБИС · ${state.login}` : 'СБИС не подключён'}</Typography>
            <Typography variant="body2" color="text.secondary">
              {configured
                ? `${state.lastSyncAt ? `Проверено ${fmtDateTime(state.lastSyncAt)}` : 'Ещё не проверялось'}${state.autoSyncMinutes ? ` · автоматически каждые ${state.autoSyncMinutes} мин` : ''}`
                : 'Входящие УПД от поставщиков будут сами появляться здесь — останется выбрать позиции склада и оприходовать.'}
            </Typography>
          </Box>
          {can(P.Settings) && (
            <Button startIcon={<SettingsIcon />} onClick={() => setSettingsOpen(true)} sx={{ bgcolor: t.surfaceMuted }}>
              {configured ? 'Настройки' : 'Подключить'}
            </Button>
          )}
          {configured && (
            <Button variant="contained" color="secondary" onClick={sync} disabled={syncing}
              startIcon={syncing ? <CircularProgress size={16} color="inherit" /> : <SyncIcon />}>
              Загрузить
            </Button>
          )}
        </Box>
        {state.lastError && configured && !message && <Alert severity="error" sx={{ mt: 1.5 }}>{state.lastError}</Alert>}
        {message && <Alert severity={message.ok ? 'success' : 'error'} sx={{ mt: 1.5 }} onClose={() => setMessage(null)}>{message.text}</Alert>}
      </Panel>

      <Box sx={{ display: 'flex', gap: 1, flexWrap: 'wrap' }}>
        {FILTERS.map(f => (
          <Chip
            key={f.value}
            label={f.value === 'new' && state.newCount ? `${f.label} · ${state.newCount}` : f.label}
            color={filter === f.value ? 'primary' : 'default'}
            onClick={() => setFilter(f.value)}
          />
        ))}
      </Box>

      <Panel padding={false}>
        {docsLoading ? <Box sx={{ display: 'grid', placeItems: 'center', py: 6 }}><CircularProgress /></Box>
          : docs.length === 0 ? <EmptyState icon={<DocIcon />} title={filter === 'new' ? 'Новых накладных нет' : 'Пусто'} />
          : docs.map(d => {
            const s = EDO_STATUS[d.status];
            return (
              <ListRow key={d.id} onClick={() => setOpenId(d.id)} muted={d.status === 'ignored'}>
                <DocIcon sx={{ color: 'text.secondary' }} />
                <Box sx={{ flex: 1, minWidth: 0 }}>
                  <Box sx={{ display: 'flex', gap: 1, alignItems: 'center', flexWrap: 'wrap' }}>
                    <Typography sx={{ fontWeight: 600 }} noWrap>{d.supplierName ?? `ИНН ${d.supplierInn ?? '—'}`}</Typography>
                    {filter === 'all' && <Chip size="small" label={s.label} sx={{ bgcolor: `${s.color}1f`, color: s.color, fontWeight: 700 }} />}
                  </Box>
                  <Typography variant="body2" color="text.secondary" noWrap>
                    № {d.number ?? '—'} от {fmtDate(d.date)} · {d.linesCount} поз.{d.codesCount ? ` · ${d.codesCount} КМ` : ''}{d.error ? ` · ${d.error}` : ''}
                  </Typography>
                </Box>
                <Typography sx={{ fontWeight: 700, whiteSpace: 'nowrap' }}>{fmtMoney(d.total)}</Typography>
                <ChevronRightIcon sx={{ color: 'text.secondary', display: { xs: 'none', sm: 'block' } }} />
              </ListRow>
            );
          })}
      </Panel>

      <EdoSettingsDialog open={settingsOpen} state={state} onClose={() => setSettingsOpen(false)} />
      <EdoDocumentDialog id={openId} onClose={() => setOpenId(null)} onOpenReceipt={onOpenReceipt} />
    </Box>
  );
};

export default EdoTab;
