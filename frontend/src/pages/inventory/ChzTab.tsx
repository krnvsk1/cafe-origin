import * as React from 'react';
import {
  Box, Typography, Button, TextField, MenuItem, Switch, FormControlLabel, Alert, CircularProgress, Chip, IconButton,
  Dialog, DialogTitle, DialogContent, DialogActions, Collapse, useTheme, useMediaQuery,
} from '@mui/material';
import {
  QrCodeScanner as ScanIcon, Close as CloseIcon, CheckCircleOutline as OkIcon, ErrorOutline as ErrIcon,
  SettingsOutlined as SettingsIcon, Key as KeyIcon, ChevronRight as ChevronRightIcon, Logout as LogoutIcon,
} from '@mui/icons-material';
import { useQueryClient } from '@tanstack/react-query';
import {
  useChzState, useChzDocuments, useSaveChzSettings, chzAuthStart, chzAuthComplete, chzLogout, chzPrepare, chzSubmit,
  chzRefresh, chzDelete, fetchChzDocument, CHZ_STATUS, type ChzDocumentDetail, type ChzState,
} from '../../api/chz';
import { apiError, fmtDateTime, fmtDate, toDateInput } from '../../api/inventory';
import { listCertificates, signBase64, signText, type Certificate } from '../../utils/cryptopro';
import { useCan, P } from '../../auth/permissions';
import { tokens } from '../../theme';
import { Panel, ListRow, EmptyState } from './ui';

const CERT_KEY = 'chz-certificate';
const readCert = (): string | null => { try { return localStorage.getItem(CERT_KEY); } catch { return null; } };
/** Сессия ЧЗ истекла (сервер отвечает 409 needSignIn) */
const needsSignIn = (e: unknown) => !!(e as { response?: { data?: { needSignIn?: boolean } } })?.response?.data?.needSignIn;
const writeCert = (v: string | null) => { try { v ? localStorage.setItem(CERT_KEY, v) : localStorage.removeItem(CERT_KEY); } catch { /* ignore */ } };

// ============ Настройки ============

const ChzSettings: React.FC<{ state: ChzState; onDone: () => void }> = ({ state, onDone }) => {
  const save = useSaveChzSettings();
  const [enabled, setEnabled] = React.useState(state.enabled || !state.inn);
  const [inn, setInn] = React.useState(state.inn);
  const [sandbox, setSandbox] = React.useState(state.sandbox);
  const [groups, setGroups] = React.useState<string[]>(state.productGroups.length ? state.productGroups : ['milk', 'water', 'beer']);
  const [error, setError] = React.useState('');

  const submit = async () => {
    setError('');
    try {
      await save.mutateAsync({ enabled, inn: inn.trim(), sandbox, productGroups: groups });
      onDone();
    } catch (e) {
      setError(apiError(e));
    }
  };

  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
      {error && <Alert severity="error">{error}</Alert>}
      <FormControlLabel control={<Switch checked={enabled} onChange={e => setEnabled(e.target.checked)} />} label="Работать с Честным знаком" />
      <TextField
        label="ИНН организации"
        value={inn}
        onChange={e => setInn(e.target.value.replace(/\D/g, '').slice(0, 12))}
        helperText="Тот же, что в сертификате КЭП руководителя"
        slotProps={{ htmlInput: { inputMode: 'numeric' } }}
        sx={{ maxWidth: 320 }}
      />
      <Box>
        <Typography sx={{ fontWeight: 600, mb: 1 }}>Товарные группы</Typography>
        <Box sx={{ display: 'flex', gap: 1, flexWrap: 'wrap' }}>
          {state.groups.map(g => {
            const on = groups.includes(g.key);
            return (
              <Chip
                key={g.key}
                label={g.title}
                color={on ? 'primary' : 'default'}
                onClick={() => setGroups(on ? groups.filter(x => x !== g.key) : [...groups, g.key])}
              />
            );
          })}
        </Box>
      </Box>
      <FormControlLabel
        control={<Switch checked={sandbox} onChange={e => setSandbox(e.target.checked)} />}
        label={<Box><Typography>Тестовый контур (песочница)</Typography><Typography variant="body2" color="text.secondary">Для проверки без реального вывода кодов</Typography></Box>}
        sx={{ alignItems: 'flex-start', '& .MuiSwitch-root': { mt: -0.5 } }}
      />
      <Box sx={{ display: 'flex', gap: 1 }}>
        <Button variant="contained" color="secondary" onClick={submit} disabled={save.isPending}
          startIcon={save.isPending ? <CircularProgress size={16} color="inherit" /> : undefined}>
          Сохранить
        </Button>
        {state.enabled && <Button onClick={onDone} sx={{ color: 'text.secondary' }}>Отмена</Button>}
      </Box>
    </Box>
  );
};

// ============ Сертификат и вход ============

const SignInPanel: React.FC<{ state: ChzState; thumbprint: string | null; onThumbprint: (t: string | null) => void }> = ({ state, thumbprint, onThumbprint }) => {
  const qc = useQueryClient();
  const [certs, setCerts] = React.useState<Certificate[] | null>(null);
  const [loading, setLoading] = React.useState(false);
  const [error, setError] = React.useState('');

  const loadCerts = async () => {
    setLoading(true);
    setError('');
    try {
      const list = await listCertificates();
      setCerts(list);
      if (list.length === 0) setError('Сертификаты с закрытым ключом не найдены — вставьте токен и обновите список');
      else if (!thumbprint || !list.some(c => c.thumbprint === thumbprint)) {
        const match = list.find(c => c.inn === state.inn && c.validTo > new Date()) ?? list[0];
        onThumbprint(match.thumbprint);
      }
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setLoading(false);
    }
  };

  const signIn = async () => {
    if (!thumbprint) return;
    setLoading(true);
    setError('');
    try {
      const key = await chzAuthStart();
      const signature = await signText(thumbprint, key.data);
      await chzAuthComplete(key.uuid, signature);
      await qc.invalidateQueries({ queryKey: ['chz', 'status'] });
    } catch (e) {
      setError(e instanceof Error && !(e as { response?: unknown }).response ? e.message : apiError(e));
    } finally {
      setLoading(false);
    }
  };

  const selected = certs?.find(c => c.thumbprint === thumbprint);

  if (state.signedIn) {
    return (
      <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5, flexWrap: 'wrap' }}>
        <OkIcon sx={{ color: tokens.herb }} />
        <Typography sx={{ flex: 1, minWidth: 200 }}>
          Вход выполнен{state.expiresAt ? ` до ${new Date(state.expiresAt).toLocaleTimeString('ru-RU', { hour: '2-digit', minute: '2-digit' })}` : ''}
          {state.sandbox ? ' · песочница' : ''}
        </Typography>
        <Button size="small" startIcon={<LogoutIcon />} sx={{ color: 'text.secondary' }}
          onClick={async () => { await chzLogout(); await qc.invalidateQueries({ queryKey: ['chz', 'status'] }); }}>
          Выйти
        </Button>
      </Box>
    );
  }

  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', gap: 1.5 }}>
      {error && <Alert severity="error" onClose={() => setError('')}>{error}</Alert>}
      {certs === null ? (
        <Box sx={{ display: 'flex', gap: 1.5, alignItems: 'center', flexWrap: 'wrap' }}>
          <Typography sx={{ flex: 1, minWidth: 220 }} color="text.secondary">
            Для работы нужен вход с сертификатом КЭП. Подпись делает плагин КриптоПро на этом компьютере.
          </Typography>
          <Button variant="contained" color="secondary" onClick={loadCerts} disabled={loading}
            startIcon={loading ? <CircularProgress size={16} color="inherit" /> : <KeyIcon />}>
            Выбрать сертификат
          </Button>
        </Box>
      ) : (
        <Box sx={{ display: 'flex', gap: 1.5, alignItems: 'flex-start', flexWrap: 'wrap' }}>
          <TextField
            select
            label="Сертификат КЭП"
            value={thumbprint ?? ''}
            onChange={e => onThumbprint(e.target.value)}
            sx={{ flex: '1 1 320px' }}
            helperText={selected && state.inn && selected.inn && selected.inn !== state.inn ? `ИНН в сертификате (${selected.inn}) не совпадает с настройками` : ' '}
            error={!!(selected && state.inn && selected.inn && selected.inn !== state.inn)}
          >
            {certs.map(c => (
              <MenuItem key={c.thumbprint} value={c.thumbprint} disabled={c.validTo < new Date()}>
                <Box>
                  <Typography>{c.name || c.organization || c.thumbprint}</Typography>
                  <Typography variant="caption" color="text.secondary">
                    {c.organization}{c.inn ? ` · ИНН ${c.inn}` : ''} · до {fmtDate(c.validTo.toISOString())}{c.validTo < new Date() ? ' (истёк)' : ''}
                  </Typography>
                </Box>
              </MenuItem>
            ))}
          </TextField>
          <Button variant="contained" color="secondary" onClick={signIn} disabled={loading || !thumbprint} sx={{ minHeight: 56 }}
            startIcon={loading ? <CircularProgress size={16} color="inherit" /> : undefined}>
            Войти
          </Button>
        </Box>
      )}
    </Box>
  );
};

// ============ Документ ============

const DocumentDialog: React.FC<{ id: number | null; thumbprint: string | null; signedIn: boolean; onClose: () => void }> = ({ id, thumbprint, signedIn, onClose }) => {
  const qc = useQueryClient();
  const theme = useTheme();
  const fullScreen = useMediaQuery(theme.breakpoints.down('sm'));
  const [doc, setDoc] = React.useState<ChzDocumentDetail | null>(null);
  const [busy, setBusy] = React.useState(false);
  const [error, setError] = React.useState('');

  React.useEffect(() => {
    setDoc(null);
    setError('');
    if (id) fetchChzDocument(id).then(setDoc).catch(e => setError(apiError(e)));
  }, [id]);

  const run = async (fn: () => Promise<ChzDocumentDetail | void>) => {
    setBusy(true);
    setError('');
    try {
      const r = await fn();
      if (r) setDoc(r);
      await qc.invalidateQueries({ queryKey: ['chz', 'documents'] });
    } catch (e) {
      if (needsSignIn(e)) await qc.invalidateQueries({ queryKey: ['chz', 'status'] });
      setError(e instanceof Error && !(e as { response?: unknown }).response ? e.message : apiError(e));
    } finally {
      setBusy(false);
    }
  };

  const resend = () => run(async () => {
    if (!doc?.productDocument || !thumbprint) throw new Error('Выберите сертификат и войдите');
    const signature = await signBase64(thumbprint, doc.productDocument, true);
    return chzSubmit(doc.id, signature);
  });

  const s = doc ? CHZ_STATUS[doc.status] : null;

  return (
    <Dialog open={!!id} onClose={() => !busy && onClose()} fullScreen={fullScreen} maxWidth="sm" fullWidth>
      <DialogTitle sx={{ display: 'flex', alignItems: 'center', gap: 1, pr: 1.5 }}>
        <Box sx={{ flex: 1 }}>{doc?.documentNumber ?? 'Документ'}</Box>
        {s && <Chip size="small" label={s.label} sx={{ bgcolor: `${s.color}1f`, color: s.color, fontWeight: 700 }} />}
        <IconButton aria-label="Закрыть" onClick={onClose} disabled={busy}><CloseIcon /></IconButton>
      </DialogTitle>
      <DialogContent sx={{ display: 'flex', flexDirection: 'column', gap: 1.5 }}>
        {error && <Alert severity="error">{error}</Alert>}
        {!doc ? <Box sx={{ display: 'grid', placeItems: 'center', py: 6 }}><CircularProgress /></Box> : (
          <>
            {doc.result && <Alert severity={doc.status === 'ok' ? 'success' : doc.status === 'error' ? 'error' : 'info'}>{doc.result}</Alert>}
            <Typography color="text.secondary">
              {fmtDate(doc.actionDate)} · {doc.codesCount} код(ов){doc.chzDocumentId ? ` · № в ЧЗ ${doc.chzDocumentId}` : ''}
            </Typography>
            {doc.comment && <Typography>«{doc.comment}»</Typography>}
            <Box sx={{ maxHeight: 260, overflowY: 'auto', fontFamily: 'monospace', fontSize: '0.8rem', bgcolor: 'action.hover', borderRadius: '12px', p: 1.5 }}>
              {doc.codes.map(c => <div key={c}>{c}</div>)}
            </Box>
          </>
        )}
      </DialogContent>
      <DialogActions sx={{ px: 3, pb: 3, gap: 1, flexWrap: 'wrap' }}>
        {doc && (doc.status === 'draft' || doc.status === 'error') && (
          <Button onClick={() => run(async () => { await chzDelete(doc.id); onClose(); })} disabled={busy} sx={{ color: tokens.chili, mr: 'auto' }}>
            Удалить
          </Button>
        )}
        {doc?.status === 'sent' && (
          <Button onClick={() => run(() => chzRefresh(doc.id))} disabled={busy || !signedIn}>Проверить статус</Button>
        )}
        {doc && (doc.status === 'draft' || doc.status === 'error') && (
          <Button variant="contained" color="secondary" onClick={resend} disabled={busy || !signedIn || !thumbprint}
            startIcon={busy ? <CircularProgress size={16} color="inherit" /> : undefined}>
            Подписать и отправить
          </Button>
        )}
      </DialogActions>
    </Dialog>
  );
};

// ============ Вкладка ============

const ChzTab: React.FC = () => {
  const theme = useTheme();
  const t = tokens[theme.palette.mode];
  const qc = useQueryClient();
  const can = useCan();
  const { data: state, isLoading } = useChzState();
  const { data: docs = [] } = useChzDocuments();

  const [settingsOpen, setSettingsOpen] = React.useState(false);
  const [thumbprint, setThumbprint] = React.useState<string | null>(readCert());
  const [group, setGroup] = React.useState('');
  const [action, setAction] = React.useState('PRODUCTION_USE');
  const [date, setDate] = React.useState(toDateInput());
  const [codes, setCodes] = React.useState<string[]>([]);
  const [scan, setScan] = React.useState('');
  const [bulk, setBulk] = React.useState(false);
  const [bulkText, setBulkText] = React.useState('');
  const [comment, setComment] = React.useState('');
  const [busy, setBusy] = React.useState(false);
  const [message, setMessage] = React.useState<{ ok: boolean; text: string } | null>(null);
  const [openId, setOpenId] = React.useState<number | null>(null);
  const scanRef = React.useRef<HTMLInputElement | null>(null);

  React.useEffect(() => { writeCert(thumbprint); }, [thumbprint]);
  React.useEffect(() => {
    if (state && !group) setGroup(state.productGroups[0] ?? 'milk');
  }, [state, group]);

  // Сканер работает как клавиатура: вводит код и Enter. Разделитель GS (\u001d) сохраняем
  const addCode = (raw: string) => {
    const code = raw.trim();
    if (code.length < 18) return;
    setCodes(prev => (prev.includes(code) ? prev : [...prev, code]));
  };

  const send = async () => {
    if (!state || !thumbprint) return;
    setBusy(true);
    setMessage(null);
    try {
      const draft = await chzPrepare({ productGroup: group, action, date, codes: codes.join('\n'), comment: comment.trim() || undefined });
      const signature = await signBase64(thumbprint, draft.productDocument!, true);
      const sent = await chzSubmit(draft.id, signature);
      setCodes([]);
      setComment('');
      await qc.invalidateQueries({ queryKey: ['chz', 'documents'] });

      if (sent.status === 'error') {
        setMessage({ ok: false, text: sent.result ?? 'Честный знак отклонил документ' });
        return;
      }
      setMessage({ ok: true, text: `${sent.documentNumber} отправлен, Честный знак проверяет коды…` });
      // Проверка обычно занимает до минуты
      for (let i = 0; i < 12; i++) {
        await new Promise(r => setTimeout(r, 5000));
        const r = await chzRefresh(sent.id);
        if (r.status !== 'sent') {
          setMessage({ ok: r.status === 'ok', text: `${r.documentNumber}: ${r.result ?? CHZ_STATUS[r.status].label}` });
          break;
        }
      }
      await qc.invalidateQueries({ queryKey: ['chz', 'documents'] });
    } catch (e) {
      if (needsSignIn(e)) await qc.invalidateQueries({ queryKey: ['chz', 'status'] });
      setMessage({ ok: false, text: e instanceof Error && !(e as { response?: unknown }).response ? e.message : apiError(e) });
    } finally {
      setBusy(false);
    }
  };

  if (isLoading || !state) return <Box sx={{ display: 'grid', placeItems: 'center', py: 8 }}><CircularProgress /></Box>;

  const configured = state.enabled && !!state.inn;
  const groupTitle = (k: string) => state.groups.find(g => g.key === k)?.title ?? k;
  const actionTitle = (k: string) => state.actions.find(a => a.key === k)?.title ?? k;

  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
      {/* Подключение */}
      <Panel>
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5, mb: configured && !settingsOpen ? 1.5 : 0, flexWrap: 'wrap' }}>
          <Typography sx={{ fontWeight: 700, flex: 1 }}>
            {configured ? `Честный знак · ИНН ${state.inn}` : 'Честный знак не подключён'}
          </Typography>
          {can(P.Settings) && configured && (
            <Button size="small" startIcon={<SettingsIcon />} onClick={() => setSettingsOpen(o => !o)} sx={{ bgcolor: t.surfaceMuted }}>
              Настройки
            </Button>
          )}
        </Box>
        <Collapse in={!configured || settingsOpen}>
          <Box sx={{ pt: 2 }}>
            {can(P.Settings)
              ? <ChzSettings state={state} onDone={() => setSettingsOpen(false)} />
              : <Typography color="text.secondary">Попросите администратора указать ИНН организации и товарные группы.</Typography>}
          </Box>
        </Collapse>
        {configured && !settingsOpen && <SignInPanel state={state} thumbprint={thumbprint} onThumbprint={setThumbprint} />}
      </Panel>

      {configured && (
        <Panel>
          <Typography sx={{ fontWeight: 700, fontSize: '1.1rem', mb: 0.5 }}>Вывод из оборота</Typography>
          <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
            Для продуктов, пущенных в сырьё (молоко, сливки, сыр в блюда), — «производственные цели». Отсканируйте коды с упаковок — по одному или списком.
          </Typography>

          <Box sx={{ display: 'grid', gridTemplateColumns: { xs: 'minmax(0, 1fr)', sm: '1fr 1fr 170px' }, gap: 1.5, mb: 2 }}>
            <TextField select label="Товарная группа" value={group} onChange={e => setGroup(e.target.value)}>
              {state.productGroups.map(g => <MenuItem key={g} value={g}>{groupTitle(g)}</MenuItem>)}
            </TextField>
            <TextField select label="Причина" value={action} onChange={e => setAction(e.target.value)}>
              {state.actions.map(a => <MenuItem key={a.key} value={a.key}>{a.title}</MenuItem>)}
            </TextField>
            <TextField type="date" label="Дата" value={date} onChange={e => setDate(e.target.value)} slotProps={{ inputLabel: { shrink: true }, htmlInput: { max: toDateInput() } }} />
          </Box>

          <Box sx={{ display: 'flex', gap: 1, alignItems: 'flex-start', mb: 1 }}>
            {!bulk ? (
              <TextField
                inputRef={scanRef}
                fullWidth
                label="Код маркировки"
                placeholder="Наведите сканер на DataMatrix"
                value={scan}
                onChange={e => setScan(e.target.value)}
                onKeyDown={e => {
                  if (e.key === 'Enter') {
                    e.preventDefault();
                    addCode(scan);
                    setScan('');
                  }
                }}
                autoFocus
                slotProps={{ input: { startAdornment: <ScanIcon sx={{ mr: 1, color: 'text.secondary' }} /> } }}
              />
            ) : (
              <TextField
                fullWidth
                multiline
                minRows={4}
                label="Коды списком, по одному в строке"
                value={bulkText}
                onChange={e => setBulkText(e.target.value)}
              />
            )}
            <Button onClick={() => {
              if (bulk && bulkText.trim()) { bulkText.split(/\r?\n/).forEach(addCode); setBulkText(''); }
              setBulk(b => !b);
              setTimeout(() => scanRef.current?.focus(), 50);
            }} sx={{ bgcolor: t.surfaceMuted, minHeight: 56, flexShrink: 0 }}>
              {bulk ? 'Добавить' : 'Списком'}
            </Button>
          </Box>

          {codes.length > 0 && (
            <Box sx={{ display: 'flex', flexWrap: 'wrap', gap: 0.75, mb: 1.5, maxHeight: 160, overflowY: 'auto' }}>
              {codes.map(c => (
                <Chip key={c} size="small" label={c.replace(/\u001d.*/, '').slice(0, 31)} onDelete={() => setCodes(codes.filter(x => x !== c))}
                  sx={{ fontFamily: 'monospace' }} />
              ))}
            </Box>
          )}

          <TextField fullWidth size="small" label="Комментарий" value={comment} onChange={e => setComment(e.target.value)} sx={{ mb: 2 }} />

          {message && <Alert severity={message.ok ? 'success' : 'error'} sx={{ mb: 1.5 }} onClose={() => setMessage(null)}>{message.text}</Alert>}

          <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5, flexWrap: 'wrap' }}>
            <Button
              variant="contained"
              color="secondary"
              size="large"
              onClick={send}
              disabled={busy || codes.length === 0 || !state.signedIn || !thumbprint}
              startIcon={busy ? <CircularProgress size={18} color="inherit" /> : undefined}
            >
              Подписать и вывести · {codes.length}
            </Button>
            {codes.length > 0 && <Button onClick={() => setCodes([])} sx={{ color: 'text.secondary' }}>Очистить</Button>}
            {!state.signedIn && <Typography variant="body2" color="text.secondary">Сначала войдите с сертификатом</Typography>}
          </Box>
        </Panel>
      )}

      {docs.length > 0 && (
        <Panel padding={false}>
          {docs.map(d => {
            const s = CHZ_STATUS[d.status];
            return (
              <ListRow key={d.id} onClick={() => setOpenId(d.id)}>
                {d.status === 'ok' ? <OkIcon sx={{ color: tokens.herb }} /> : d.status === 'error' ? <ErrIcon sx={{ color: tokens.chili }} /> : <ScanIcon sx={{ color: 'text.secondary' }} />}
                <Box sx={{ flex: 1, minWidth: 0 }}>
                  <Box sx={{ display: 'flex', gap: 1, alignItems: 'center', flexWrap: 'wrap' }}>
                    <Typography sx={{ fontWeight: 600 }}>{d.documentNumber}</Typography>
                    <Chip size="small" label={s.label} sx={{ bgcolor: `${s.color}1f`, color: s.color, fontWeight: 700 }} />
                  </Box>
                  <Typography variant="body2" color="text.secondary" noWrap>
                    {groupTitle(d.productGroup)} · {actionTitle(d.action).toLowerCase()} · {d.codesCount} шт · {fmtDateTime(d.createdAt)}
                  </Typography>
                </Box>
                <ChevronRightIcon sx={{ color: 'text.secondary', display: { xs: 'none', sm: 'block' } }} />
              </ListRow>
            );
          })}
        </Panel>
      )}
      {configured && docs.length === 0 && (
        <Panel><EmptyState icon={<ScanIcon />} title="Выводов из оборота пока не было" /></Panel>
      )}

      <DocumentDialog id={openId} thumbprint={thumbprint} signedIn={state.signedIn} onClose={() => setOpenId(null)} />
    </Box>
  );
};

export default ChzTab;
