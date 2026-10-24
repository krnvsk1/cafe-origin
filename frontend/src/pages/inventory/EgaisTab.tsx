import * as React from 'react';
import {
  Box, Typography, Button, CircularProgress, Alert, Chip, ToggleButtonGroup, ToggleButton, Dialog, DialogTitle,
  DialogContent, DialogActions, IconButton, TextField, Autocomplete, InputAdornment, useMediaQuery, useTheme,
  createFilterOptions,
} from '@mui/material';
import {
  Sync as SyncIcon, Close as CloseIcon, ChevronRight as ChevronRightIcon, LocalBarOutlined as BeerIcon,
  Add as AddIcon, CheckCircleOutline as OkIcon, ErrorOutline as ErrIcon, SettingsOutlined as SettingsIcon,
} from '@mui/icons-material';
import {
  useEgaisState, useEgaisDocuments, useEgaisDocument, useEgaisSync, useEgaisMapping, useEgaisAct, EGAIS_STATUS,
  type EgaisLine,
} from '../../api/egais';
import { useStockItems, apiError, fmtMoney, fmtDate, fmtDateTime, fmtQty, UNIT_LABELS, type StockItem } from '../../api/inventory';
import { useCan, P } from '../../auth/permissions';
import { tokens } from '../../theme';
import { Panel, ListRow, EmptyState, Pill } from './ui';
import StockItemDialog from './StockItemDialog';
import EgaisSettingsDialog from './EgaisSettingsDialog';

type CreateOption = { create: true; inputValue: string };
const isCreate = (o: unknown): o is CreateOption => !!o && typeof o === 'object' && 'create' in (o as object);
const itemFilter = createFilterOptions<StockItem>({ stringify: o => `${o.name} ${o.category ?? ''}` });

/** Как ЕГАИС считает количество: бутылки — штуки, разливное — декалитры */
const egaisUnit = (l: EgaisLine) => (l.unitType?.toLowerCase() === 'unpacked' ? 'дал' : 'шт');

// ============ Накладная ============

const EgaisDocumentDialog: React.FC<{ id: number | null; onClose: () => void }> = ({ id, onClose }) => {
  const theme = useTheme();
  const t = tokens[theme.palette.mode];
  const fullScreen = useMediaQuery(theme.breakpoints.down('md'));
  const can = useCan();
  const canManage = can(P.InventoryManage);
  const { data: doc, isLoading } = useEgaisDocument(id);
  const { data: items = [] } = useStockItems({ kind: 'raw' });
  const mapping = useEgaisMapping();
  const act = useEgaisAct();

  const [real, setReal] = React.useState<Record<number, string>>({});
  const [mode, setMode] = React.useState<'accept' | 'differences' | 'reject'>('accept');
  const [note, setNote] = React.useState('');
  const [error, setError] = React.useState('');
  const [newItem, setNewItem] = React.useState<{ lineId: number; name: string } | null>(null);

  React.useEffect(() => {
    if (!doc) return;
    setReal(Object.fromEntries(doc.lines.map(l => [l.id, String(l.realQuantity ?? l.quantity)])));
    setError('');
  }, [doc?.id]); // eslint-disable-line react-hooks/exhaustive-deps

  React.useEffect(() => { if (!id) { setMode('accept'); setNote(''); } }, [id]);

  const editable = !!doc && (doc.status === 'ready' || doc.status === 'error') && canManage;

  const mapLine = async (lineId: number, stockItemId: number | null) => {
    if (!doc) return;
    setError('');
    try {
      await mapping.mutateAsync({ id: doc.id, lines: [{ lineId, stockItemId }] });
    } catch (e) {
      setError(apiError(e));
    }
  };

  const submit = async () => {
    if (!doc) return;
    setError('');
    try {
      await act.mutateAsync({
        id: doc.id,
        decision: mode,
        note: note.trim() || undefined,
        lines: mode === 'differences'
          ? doc.lines.map(l => ({ lineId: l.id, realQuantity: parseFloat((real[l.id] ?? '').replace(',', '.')) || 0 }))
          : [],
      });
    } catch (e) {
      setError(apiError(e));
    }
  };

  const unmapped = doc?.lines.filter(l => !l.stockItemId).length ?? 0;
  const conversionProblem = doc?.lines.find(l => l.conversionError);
  const status = doc ? EGAIS_STATUS[doc.status] : null;
  const busy = mapping.isPending || act.isPending;

  return (
    <Dialog open={!!id} onClose={() => !busy && onClose()} fullScreen={fullScreen} maxWidth="md" fullWidth>
      <DialogTitle sx={{ display: 'flex', alignItems: 'flex-start', gap: 1, pr: 1.5 }}>
        <Box sx={{ flex: 1, minWidth: 0 }}>
          <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, flexWrap: 'wrap' }}>
            ТТН № {doc?.number ?? '…'}
            {status && <Chip size="small" label={status.label} sx={{ bgcolor: `${status.color}1f`, color: status.color, fontWeight: 700 }} />}
          </Box>
          {doc && (
            <Typography color="text.secondary" sx={{ fontWeight: 400 }}>
              {doc.shipperName ?? 'Поставщик'}{doc.shipperInn ? ` · ИНН ${doc.shipperInn}` : ''} · {fmtDate(doc.date)}
            </Typography>
          )}
        </Box>
        <IconButton aria-label="Закрыть" onClick={onClose} disabled={busy}><CloseIcon /></IconButton>
      </DialogTitle>

      <DialogContent sx={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
        {isLoading || !doc ? (
          <Box sx={{ display: 'grid', placeItems: 'center', py: 8 }}><CircularProgress /></Box>
        ) : (
          <>
            {error && <Alert severity="error" onClose={() => setError('')}>{error}</Alert>}
            {doc.status === 'awaiting_regid' && (
              <Alert severity="info">ЕГАИС ещё не прислал регистрационный номер накладной. Обычно это несколько минут — нажмите «Проверить входящие» позже.</Alert>
            )}
            {doc.status === 'act_sent' && (
              <Alert severity="info">Акт отправлен {fmtDateTime(doc.actSentAt)}. Ждём квитанцию ЕГАИС — она придёт при следующей проверке входящих.</Alert>
            )}
            {doc.status === 'error' && (
              <Alert severity="error">ЕГАИС не принял акт: {doc.resultComment || 'причина не указана'}. Исправьте и отправьте снова.</Alert>
            )}
            {['accepted', 'differences', 'rejected'].includes(doc.status) && doc.resultComment && (
              <Alert severity="success">Ответ ЕГАИС: {doc.resultComment}</Alert>
            )}
            {doc.stockDocumentId && (
              <Alert severity="success" icon={<OkIcon />}>Товар оприходован на склад (приход #{doc.stockDocumentId}).</Alert>
            )}

            <Box>
              {doc.lines.map((l, i) => {
                const selected = l.stockItemId
                  ? items.find(x => x.id === l.stockItemId) ?? ({ id: l.stockItemId, name: l.stockItemName ?? '', unit: l.stockUnit ?? 'l' } as StockItem)
                  : null;
                return (
                  <Box key={l.id} sx={{ py: 1.5, borderTop: i ? '1px solid' : 0, borderColor: 'divider', display: 'flex', flexDirection: 'column', gap: 1 }}>
                    <Box sx={{ display: 'flex', gap: 1.5, alignItems: 'flex-start' }}>
                      <Box sx={{ flex: 1, minWidth: 0 }}>
                        <Typography sx={{ fontWeight: 600 }}>{l.productName}</Typography>
                        <Typography variant="body2" color="text.secondary">
                          {l.unitType?.toLowerCase() === 'unpacked' ? 'Разливное' : `Тара ${l.capacity ? `${l.capacity} л` : '—'}`}
                          {l.alcVolume ? ` · ${l.alcVolume}%` : ''}{l.alcCode ? ` · код ${l.alcCode}` : ''}
                        </Typography>
                      </Box>
                      <Box sx={{ textAlign: 'right', flexShrink: 0 }}>
                        <Typography sx={{ fontWeight: 700 }}>{fmtQty(l.quantity)} {egaisUnit(l)}</Typography>
                        <Typography variant="body2" color="text.secondary">× {fmtMoney(l.price)} = {fmtMoney(Math.round(l.quantity * l.price * 100) / 100)}</Typography>
                      </Box>
                    </Box>

                    <Box sx={{ display: 'grid', gridTemplateColumns: { xs: 'minmax(0, 1fr)', sm: mode === 'differences' && editable ? 'minmax(0,1fr) 160px' : 'minmax(0,1fr)' }, gap: 1, alignItems: 'start' }}>
                      <Autocomplete<StockItem | CreateOption, false, false, false>
                        size="small"
                        options={items}
                        value={selected}
                        disabled={!editable || busy}
                        onChange={(_, v) => {
                          if (isCreate(v)) setNewItem({ lineId: l.id, name: v.inputValue });
                          else void mapLine(l.id, v?.id ?? null);
                        }}
                        filterOptions={(opts, params) => {
                          const filtered = itemFilter(opts as StockItem[], params) as (StockItem | CreateOption)[];
                          const q = params.inputValue.trim();
                          filtered.push({ create: true, inputValue: q || l.productName });
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
                            error={!!l.conversionError}
                            helperText={l.conversionError
                              ?? (l.stockItemId && l.stockQuantity !== null ? `На склад: ${fmtQty(l.stockQuantity, l.stockUnit)}` : undefined)}
                          />
                        )}
                      />
                      {mode === 'differences' && editable && (
                        <TextField
                          size="small"
                          type="number"
                          label="Принято"
                          value={real[l.id] ?? ''}
                          onChange={e => setReal(r => ({ ...r, [l.id]: e.target.value }))}
                          slotProps={{
                            input: { endAdornment: <InputAdornment position="end">{egaisUnit(l)}</InputAdornment> },
                            htmlInput: { min: 0, max: l.quantity, step: 'any', inputMode: 'decimal' },
                          }}
                        />
                      )}
                    </Box>
                  </Box>
                );
              })}
            </Box>

            {editable && (
              <Box sx={{ bgcolor: t.surfaceMuted, borderRadius: '16px', p: 2, display: 'flex', flexDirection: 'column', gap: 1.5 }}>
                <ToggleButtonGroup exclusive value={mode} onChange={(_, v) => v && setMode(v)} sx={{ display: 'flex', flexWrap: 'wrap', '& .MuiToggleButton-root': { flex: 1 } }}>
                  <ToggleButton value="accept">Принять всё</ToggleButton>
                  <ToggleButton value="differences">С расхождениями</ToggleButton>
                  <ToggleButton value="reject">Отказать</ToggleButton>
                </ToggleButtonGroup>
                <Typography variant="body2" color="text.secondary">
                  {mode === 'accept' && 'В ЕГАИС уйдёт акт подтверждения, товар оприходуется на склад.'}
                  {mode === 'differences' && 'Укажите фактически принятое количество — в ЕГАИС уйдёт акт расхождений, поставщик должен его подтвердить.'}
                  {mode === 'reject' && 'Накладная будет полностью отклонена, на склад ничего не поступит.'}
                </Typography>
                {mode !== 'accept' && (
                  <TextField
                    size="small"
                    label={mode === 'reject' ? 'Причина отказа' : 'Комментарий (например, бой 2 бутылки)'}
                    value={note}
                    onChange={e => setNote(e.target.value)}
                    slotProps={{ htmlInput: { maxLength: 500 } }}
                  />
                )}
                {mode !== 'reject' && unmapped > 0 && (
                  <Typography variant="body2" sx={{ color: tokens.chili }}>Укажите позицию склада для всех строк ({unmapped} не указано)</Typography>
                )}
              </Box>
            )}
          </>
        )}
      </DialogContent>

      <DialogActions sx={{ px: 3, pb: 3, pt: 1.5, gap: 1, borderTop: '1px solid', borderColor: 'divider' }}>
        <Button onClick={onClose} disabled={busy} sx={{ color: 'text.secondary', mr: 'auto' }}>Закрыть</Button>
        {editable && (
          <Button
            variant="contained"
            color={mode === 'reject' ? 'error' : 'secondary'}
            onClick={submit}
            disabled={busy || (mode !== 'reject' && (unmapped > 0 || !!conversionProblem)) || (mode === 'reject' && !note.trim())}
            startIcon={act.isPending ? <CircularProgress size={16} color="inherit" /> : undefined}
          >
            {mode === 'accept' ? 'Принять и отправить акт' : mode === 'differences' ? 'Отправить акт расхождений' : 'Отказать'}
          </Button>
        )}
      </DialogActions>

      <StockItemDialog
        open={!!newItem}
        initialName={newItem?.name}
        allowSemi={false}
        onClose={() => setNewItem(null)}
        onSaved={(item) => { if (newItem) void mapLine(newItem.lineId, item.id); }}
      />
    </Dialog>
  );
};

// ============ Вкладка ============

const EgaisTab: React.FC = () => {
  const theme = useTheme();
  const t = tokens[theme.palette.mode];
  const can = useCan();
  const { data: state, isLoading: stateLoading } = useEgaisState();
  const [filter, setFilter] = React.useState<'active' | ''>('active');
  const { data: docs = [], isLoading } = useEgaisDocuments(filter);
  const sync = useEgaisSync();
  const [openId, setOpenId] = React.useState<number | null>(null);
  const [message, setMessage] = React.useState<{ ok: boolean; text: string } | null>(null);
  const [settingsOpen, setSettingsOpen] = React.useState(false);
  const canSettings = can(P.Settings);

  const runSync = async () => {
    setMessage(null);
    try {
      const r = await sync.mutateAsync();
      const parts = [
        r.wayBills && `накладных: ${r.wayBills}`,
        r.regInfos && `регистраций: ${r.regInfos}`,
        r.tickets && `квитанций: ${r.tickets}`,
      ].filter(Boolean);
      setMessage({
        ok: r.errors.length === 0,
        text: (parts.length ? `Получено — ${parts.join(', ')}.` : 'Новых документов нет.') + (r.errors.length ? ` Ошибки: ${r.errors.join('; ')}` : ''),
      });
    } catch (e) {
      setMessage({ ok: false, text: apiError(e) });
    }
  };

  if (stateLoading) return <Box sx={{ display: 'grid', placeItems: 'center', py: 8 }}><CircularProgress /></Box>;

  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
      <Panel>
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 2, flexWrap: 'wrap' }}>
          <Box sx={{ flex: 1, minWidth: 240 }}>
            <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
              {state?.enabled && state.reachable ? <OkIcon sx={{ color: tokens.herb }} /> : <ErrIcon sx={{ color: state?.enabled ? tokens.chili : 'text.secondary' }} />}
              <Typography sx={{ fontWeight: 700 }}>
                {!state?.enabled ? 'ЕГАИС не подключён' : state.reachable ? 'УТМ на связи' : 'УТМ недоступен'}
              </Typography>
            </Box>
            <Typography variant="body2" color="text.secondary" sx={{ mt: 0.5 }}>
              {!state?.enabled
                ? (canSettings ? 'Нажмите «Подключить», укажите адрес УТМ и ФСРАР ID заведения.' : 'Попросите администратора подключить ЕГАИС.')
                : `${state.utmUrl} · ФСРАР ID ${state.fsrarId ?? 'не указан'} · проверено ${state.lastSyncAt ? fmtDateTime(state.lastSyncAt) : 'ещё не было'}`}
            </Typography>
            {state?.lastSyncError && <Typography variant="body2" sx={{ color: tokens.chili, mt: 0.5 }}>{state.lastSyncError}</Typography>}
          </Box>
          {canSettings && (
            <Button
              onClick={() => setSettingsOpen(true)}
              startIcon={<SettingsIcon />}
              variant={state?.enabled ? 'text' : 'contained'}
              color={state?.enabled ? 'inherit' : 'secondary'}
              sx={state?.enabled ? { bgcolor: t.surfaceMuted } : undefined}
            >
              {state?.enabled ? 'Настройки' : 'Подключить ЕГАИС'}
            </Button>
          )}
          {state?.enabled && can(P.InventoryManage) && (
            <Button variant="contained" color="secondary" onClick={runSync} disabled={sync.isPending}
              startIcon={sync.isPending ? <CircularProgress size={16} color="inherit" /> : <SyncIcon />}>
              Проверить входящие
            </Button>
          )}
        </Box>
        {message && <Alert severity={message.ok ? 'success' : 'warning'} sx={{ mt: 1.5 }} onClose={() => setMessage(null)}>{message.text}</Alert>}
      </Panel>

      <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5, flexWrap: 'wrap' }}>
        <ToggleButtonGroup exclusive size="small" value={filter} onChange={(_, v) => v !== null && setFilter(v)}>
          <ToggleButton value="active">К обработке</ToggleButton>
          <ToggleButton value="">Все накладные</ToggleButton>
        </ToggleButtonGroup>
        {state && state.toProcess > 0 && <Pill color="#C77700" bg="rgba(232,137,12,0.12)">к приёмке: {state.toProcess}</Pill>}
      </Box>

      <Panel padding={false}>
        {isLoading ? (
          <Box sx={{ display: 'grid', placeItems: 'center', py: 6 }}><CircularProgress size={32} /></Box>
        ) : docs.length === 0 ? (
          <EmptyState
            icon={<BeerIcon />}
            title={filter ? 'Нет накладных к обработке' : 'Накладных из ЕГАИС пока нет'}
            text="Когда поставщик отгрузит пиво, накладная появится здесь после проверки входящих"
          />
        ) : docs.map(d => {
          const s = EGAIS_STATUS[d.status];
          return (
            <ListRow key={d.id} onClick={() => setOpenId(d.id)}>
              <Box sx={{ flex: 1, minWidth: 0 }}>
                <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, flexWrap: 'wrap' }}>
                  <Typography sx={{ fontWeight: 600 }} noWrap>{d.shipperName ?? 'Поставщик'}</Typography>
                  <Chip size="small" label={s.label} sx={{ bgcolor: `${s.color}1f`, color: s.color, fontWeight: 700 }} />
                  {d.unmapped > 0 && ['ready', 'error', 'awaiting_regid'].includes(d.status) && (
                    <Pill bg={t.surfaceMuted}>не сопоставлено: {d.unmapped}</Pill>
                  )}
                </Box>
                <Typography variant="body2" color="text.secondary" noWrap>
                  № {d.number} от {fmtDate(d.date)} · {d.linesCount} поз.{d.resultComment && d.status === 'error' ? ` · ${d.resultComment}` : ''}
                </Typography>
              </Box>
              <Typography sx={{ fontWeight: 700 }}>{fmtMoney(Math.round(d.total * 100) / 100)}</Typography>
              <ChevronRightIcon sx={{ color: 'text.secondary', display: { xs: 'none', sm: 'block' } }} />
            </ListRow>
          );
        })}
      </Panel>

      <EgaisDocumentDialog id={openId} onClose={() => setOpenId(null)} />
      <EgaisSettingsDialog open={settingsOpen} onClose={() => setSettingsOpen(false)} />
    </Box>
  );
};

export default EgaisTab;
