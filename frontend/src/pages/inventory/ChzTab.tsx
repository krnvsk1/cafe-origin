import * as React from 'react';
import {
  Box, Typography, Button, TextField, MenuItem, Alert, CircularProgress, Chip, IconButton,
  Dialog, DialogTitle, DialogContent, DialogActions, Checkbox, useTheme, useMediaQuery,
} from '@mui/material';
import {
  QrCodeScanner as ScanIcon, Close as CloseIcon, CheckCircleOutline as OkIcon, ErrorOutline as ErrIcon,
  Key as KeyIcon, ChevronRight as ChevronRightIcon, Logout as LogoutIcon, CloudDownloadOutlined as LoadIcon,
} from '@mui/icons-material';
import { useQueryClient } from '@tanstack/react-query';
import {
  useChzState, useChzDocuments, chzAuthStart, chzAuthComplete, chzLogout, chzPrepare, chzSubmit,
  chzRefresh, chzDelete, fetchChzDocument, fetchChzBalance, CHZ_STATUS, type ChzDocumentDetail, type ChzState, type ChzBalance,
} from '../../api/chz';
import { apiError, fmtDateTime, fmtDate, toDateInput } from '../../api/inventory';
import { listCertificates, signBase64, signText, type Certificate } from '../../utils/cryptopro';
import { tokens } from '../../theme';
import { Panel, ListRow, EmptyState } from './ui';
import IntegrationStatus from './IntegrationStatus';
import { CHZ_PREFILL_KEY } from '../../api/edo';

const CERT_KEY = 'chz-certificate';
const readCert = (): string | null => { try { return localStorage.getItem(CERT_KEY); } catch { return null; } };
/** Сессия ЧЗ истекла (сервер отвечает 409 needSignIn) */
const needsSignIn = (e: unknown) => !!(e as { response?: { data?: { needSignIn?: boolean } } })?.response?.data?.needSignIn;
const writeCert = (v: string | null) => { try { if (v) localStorage.setItem(CERT_KEY, v); else localStorage.removeItem(CERT_KEY); } catch { /* ignore */ } };

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

  // Новый документ без кодов, на которые пожаловался ЧЗ; старый с ошибкой удаляем
  const resendWithoutBad = () => run(async () => {
    if (!doc || !thumbprint) throw new Error('Выберите сертификат и войдите');
    const bad = new Set(doc.badCodes);
    const good = doc.codes.filter(c => !bad.has(c));
    const draft = await chzPrepare({
      productGroup: doc.productGroup, action: doc.action, date: doc.actionDate.slice(0, 10),
      codes: good.join('\n'), comment: doc.comment ?? undefined,
    });
    const signature = await signBase64(thumbprint, draft.productDocument!, true);
    const sent = await chzSubmit(draft.id, signature);
    await chzDelete(doc.id);
    return sent;
  });

  const s = doc ? CHZ_STATUS[doc.status] : null;
  const bad = new Set(doc?.badCodes ?? []);
  const canDropBad = !!doc && doc.status === 'error' && bad.size > 0 && bad.size < doc.codes.length;

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
            {doc.result && (
              <Alert severity={doc.status === 'ok' ? 'success' : doc.status === 'error' ? 'error' : 'info'}
                sx={{ maxHeight: 220, overflowY: 'auto', wordBreak: 'break-word' }}>
                {doc.result}
              </Alert>
            )}
            {canDropBad && (
              <Typography>
                Честный знак не принял {bad.size} из {doc.codes.length} кодов (выделены красным). Остальные можно вывести отдельным документом.
              </Typography>
            )}
            <Typography color="text.secondary">
              {fmtDate(doc.actionDate)} · {doc.codesCount} код(ов){doc.chzDocumentId ? ` · № в ЧЗ ${doc.chzDocumentId}` : ''}
            </Typography>
            {doc.comment && <Typography>«{doc.comment}»</Typography>}
            <Box sx={{ maxHeight: 260, overflowY: 'auto', fontFamily: 'monospace', fontSize: '0.8rem', bgcolor: 'action.hover', borderRadius: '12px', p: 1.5 }}>
              {doc.codes.map(c => <div key={c} style={bad.has(c) ? { color: tokens.chili, fontWeight: 700 } : undefined}>{c}</div>)}
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
        {canDropBad && (
          <Button variant="contained" color="secondary" onClick={resendWithoutBad} disabled={busy || !signedIn || !thumbprint}
            startIcon={busy ? <CircularProgress size={16} color="inherit" /> : undefined}>
            Вывести без проблемных · {doc!.codes.length - bad.size}
          </Button>
        )}
        {doc && doc.status === 'error' && !canDropBad && (
          <Button variant="contained" color="secondary" onClick={resendWithoutBad} disabled={busy || !signedIn || !thumbprint}
            startIcon={busy ? <CircularProgress size={16} color="inherit" /> : undefined}>
            Собрать и отправить заново
          </Button>
        )}
        {doc && doc.status === 'draft' && (
          <Button variant="contained" color="secondary" onClick={resend} disabled={busy || !signedIn || !thumbprint}
            startIcon={busy ? <CircularProgress size={16} color="inherit" /> : undefined}>
            Подписать и отправить
          </Button>
        )}
      </DialogActions>
    </Dialog>
  );
};

// ============ Баланс в ЧЗ ============

const BalancePanel: React.FC<{
  state: ChzState;
  busy: boolean;
  canSign: boolean;
  actionTitle: string;
  onWithdraw: (codes: string[], group: string) => Promise<boolean>;
  message: { ok: boolean; text: string } | null;
  onCloseMessage: () => void;
}> = ({ state, busy, canSign, actionTitle, onWithdraw, message, onCloseMessage }) => {
  const [group, setGroup] = React.useState(state.productGroups[0] ?? 'milk');
  const [days, setDays] = React.useState(365);
  const [data, setData] = React.useState<ChzBalance | null>(null);
  const [selected, setSelected] = React.useState<Set<string>>(new Set());
  const [loading, setLoading] = React.useState(false);
  const [error, setError] = React.useState('');
  const [confirm, setConfirm] = React.useState(false);
  const qc = useQueryClient();
  const groupTitle = state.groups.find(g => g.key === group)?.title ?? group;

  const load = async () => {
    setLoading(true);
    setError('');
    try {
      const r = await fetchChzBalance(group, days);
      setData(r);
      setSelected(new Set(r.items.map(i => i.gtin)));
    } catch (e) {
      if (needsSignIn(e)) await qc.invalidateQueries({ queryKey: ['chz', 'status'] });
      setError(apiError(e));
      setData(null);
    } finally {
      setLoading(false);
    }
  };

  const chosen = data ? data.items.filter(i => selected.has(i.gtin)) : [];
  const chosenCount = chosen.reduce((s, i) => s + i.count, 0);
  const toggle = (gtin: string) => setSelected(prev => {
    const n = new Set(prev);
    if (n.has(gtin)) n.delete(gtin); else n.add(gtin);
    return n;
  });

  const run = async () => {
    setConfirm(false);
    const ok = await onWithdraw(chosen.flatMap(i => i.codes), group);
    if (ok) await load();
  };

  return (
    <Panel>
      <Typography sx={{ fontWeight: 700, fontSize: '1.1rem', mb: 0.5 }}>На балансе в Честном знаке</Typography>
      <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
        Коды «в обороте», которые числятся за вашим ИНН. Можно вывести всё разом или выбрать товары по GTIN.
      </Typography>
      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: 'minmax(0, 1fr)', sm: '1fr 200px auto' }, gap: 1.5, mb: 2 }}>
        <TextField select label="Товарная группа" value={group} onChange={e => { setGroup(e.target.value); setData(null); }}>
          {state.productGroups.map(g => <MenuItem key={g} value={g}>{state.groups.find(x => x.key === g)?.title ?? g}</MenuItem>)}
        </TextField>
        <TextField select label="Произведено за" value={days} onChange={e => { setDays(Number(e.target.value)); setData(null); }}>
          <MenuItem value={30}>30 дней</MenuItem>
          <MenuItem value={90}>3 месяца</MenuItem>
          <MenuItem value={365}>год</MenuItem>
          <MenuItem value={1095}>3 года</MenuItem>
        </TextField>
        <Button variant="contained" color="secondary" onClick={load} disabled={loading || busy} sx={{ minHeight: 56 }}
          startIcon={loading ? <CircularProgress size={16} color="inherit" /> : <LoadIcon />}>
          Запросить
        </Button>
      </Box>

      {error && <Alert severity="error" sx={{ mb: 1.5 }}>{error}</Alert>}
      {message && <Alert severity={message.ok ? 'success' : 'error'} sx={{ mb: 1.5, whiteSpace: 'pre-line', maxHeight: 260, overflowY: 'auto', wordBreak: 'break-word' }} onClose={onCloseMessage}>{message.text}</Alert>}

      {data && data.total === 0 && <Alert severity="info">По группе «{groupTitle}» на балансе ничего нет</Alert>}
      {data && data.total > 0 && (
        <>
          {data.truncated && <Alert severity="warning" sx={{ mb: 1.5 }}>Показаны первые {data.total} кодов — после вывода запросите ещё раз</Alert>}
          <Box sx={{ display: 'flex', alignItems: 'center', mb: 0.5 }}>
            <Checkbox
              checked={selected.size === data.items.length}
              indeterminate={selected.size > 0 && selected.size < data.items.length}
              onChange={e => setSelected(e.target.checked ? new Set(data.items.map(i => i.gtin)) : new Set())}
            />
            <Typography sx={{ fontWeight: 600 }}>Всего {data.total} шт · {data.items.length} товаров</Typography>
          </Box>
          <Box sx={{ maxHeight: 320, overflowY: 'auto', mb: 2 }}>
            {data.items.map(i => (
              <Box key={i.gtin} onClick={() => toggle(i.gtin)}
                sx={{ display: 'flex', alignItems: 'center', gap: 1, cursor: 'pointer', borderRadius: '10px', '&:hover': { bgcolor: 'action.hover' } }}>
                <Checkbox checked={selected.has(i.gtin)} />
                <Typography sx={{ fontFamily: 'monospace', flex: 1, minWidth: 0 }} noWrap>{i.gtin}</Typography>
                <Typography variant="body2" color="text.secondary" sx={{ display: { xs: 'none', sm: 'block' } }}>
                  {i.lastEmission ? `от ${fmtDate(i.lastEmission)}` : ''}
                </Typography>
                <Typography sx={{ fontWeight: 700, minWidth: 56, textAlign: 'right', pr: 1 }}>{i.count} шт</Typography>
              </Box>
            ))}
          </Box>
          <Button variant="contained" color="secondary" size="large" onClick={() => setConfirm(true)}
            disabled={busy || chosenCount === 0 || !canSign}
            startIcon={busy ? <CircularProgress size={18} color="inherit" /> : undefined}>
            Вывести из оборота · {chosenCount}
          </Button>
        </>
      )}

      <Dialog open={confirm} onClose={() => setConfirm(false)} maxWidth="xs" fullWidth>
        <DialogTitle>Вывести {chosenCount} кодов?</DialogTitle>
        <DialogContent>
          <Typography>
            Группа «{groupTitle}», причина — «{actionTitle}» (меняется в форме «Вывод из оборота» выше).
            Отменить вывод из оборота нельзя.
          </Typography>
          {chosenCount > 5000 && (
            <Typography color="text.secondary" sx={{ mt: 1 }}>
              Будет {Math.ceil(chosenCount / 5000)} документа — каждый нужно подписать.
            </Typography>
          )}
        </DialogContent>
        <DialogActions sx={{ px: 3, pb: 3 }}>
          <Button onClick={() => setConfirm(false)} sx={{ color: 'text.secondary' }}>Отмена</Button>
          <Button variant="contained" color="secondary" onClick={run}>Подписать и вывести</Button>
        </DialogActions>
      </Dialog>
    </Panel>
  );
};

// ============ Вкладка ============

const ChzTab: React.FC = () => {
  const theme = useTheme();
  const t = tokens[theme.palette.mode];
  const qc = useQueryClient();
  const { data: state, isLoading } = useChzState();
  const { data: docs = [] } = useChzDocuments();

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
  // Где показать результат: в форме вывода или в панели баланса
  const [messageAt, setMessageAt] = React.useState<'form' | 'balance'>('form');
  const [openId, setOpenId] = React.useState<number | null>(null);
  const scanRef = React.useRef<HTMLInputElement | null>(null);

  React.useEffect(() => { writeCert(thumbprint); }, [thumbprint]);

  // Коды из накладной СБИС («Вывести КМ из оборота»)
  React.useEffect(() => {
    try {
      const raw = sessionStorage.getItem(CHZ_PREFILL_KEY);
      if (!raw) return;
      sessionStorage.removeItem(CHZ_PREFILL_KEY);
      const p = JSON.parse(raw) as { codes?: string[]; comment?: string };
      if (p.codes?.length) {
        setCodes(Array.from(new Set(p.codes)));
        if (p.comment) setComment(p.comment);
        setMessage({ ok: true, text: `Из накладной добавлено кодов: ${p.codes.length}. Проверьте товарную группу и причину, затем подпишите.` });
      }
    } catch { /* ignore */ }
  }, []);
  React.useEffect(() => {
    if (state && !group) setGroup(state.productGroups[0] ?? 'milk');
  }, [state, group]);

  // Сканер работает как клавиатура: вводит код и Enter. Разделитель GS (\u001d) сохраняем
  const addCode = (raw: string) => {
    const code = raw.trim();
    if (code.length < 18) return;
    setCodes(prev => (prev.includes(code) ? prev : [...prev, code]));
  };

  /** Вывести коды: документы по 5000 кодов, каждый подписывается КЭП, затем ждём результат проверки */
  const withdraw = async (list: string[], pg: string, note: string | undefined, at: 'form' | 'balance'): Promise<boolean> => {
    if (!state || !thumbprint || list.length === 0) return false;
    setMessageAt(at);
    setBusy(true);
    setMessage(null);
    const results: string[] = [];
    let allOk = true;
    try {
      const chunks: string[][] = [];
      for (let i = 0; i < list.length; i += 5000) chunks.push(list.slice(i, i + 5000));
      const sentIds: number[] = [];
      for (const [i, chunk] of chunks.entries()) {
        if (chunks.length > 1) setMessage({ ok: true, text: `Документ ${i + 1} из ${chunks.length}: подпишите…` });
        const draft = await chzPrepare({ productGroup: pg, action, date, codes: chunk.join('\n'), comment: note });
        const signature = await signBase64(thumbprint, draft.productDocument!, true);
        const sent = await chzSubmit(draft.id, signature);
        if (sent.status === 'error') {
          allOk = false;
          results.push(`${sent.documentNumber}: ${sent.result ?? 'Честный знак отклонил документ'}\n→ Откройте документ в истории ниже: там можно вывести коды без проблемных`);
        } else sentIds.push(sent.id);
      }
      await qc.invalidateQueries({ queryKey: ['chz', 'documents'] });

      if (sentIds.length) {
        setMessage({ ok: true, text: `Отправлено документов: ${sentIds.length}, Честный знак проверяет коды…` });
        // Проверка обычно занимает до минуты
        let pending = [...sentIds];
        for (let i = 0; i < 12 && pending.length; i++) {
          await new Promise(r => setTimeout(r, 5000));
          const next: number[] = [];
          for (const id of pending) {
            const r = await chzRefresh(id);
            if (r.status === 'sent') next.push(id);
            else {
              if (r.status !== 'ok') allOk = false;
              results.push(`${r.documentNumber}: ${r.result ?? CHZ_STATUS[r.status].label}`
                + (r.status === 'error' ? '\n→ Откройте документ в истории ниже: там можно вывести коды без проблемных' : ''));
            }
          }
          pending = next;
        }
        if (pending.length) results.push(`Ещё проверяются: ${pending.length} — статус обновится в истории`);
      }
      setMessage({ ok: allOk, text: results.join('\n') });
      await qc.invalidateQueries({ queryKey: ['chz', 'documents'] });
      return true;
    } catch (e) {
      if (needsSignIn(e)) await qc.invalidateQueries({ queryKey: ['chz', 'status'] });
      setMessage({ ok: false, text: [...results, e instanceof Error && !(e as { response?: unknown }).response ? e.message : apiError(e)].join('\n') });
      return false;
    } finally {
      setBusy(false);
    }
  };

  const send = async () => {
    if (await withdraw(codes, group, comment.trim() || undefined, 'form')) {
      setCodes([]);
      setComment('');
    }
  };

  if (isLoading || !state) return <Box sx={{ display: 'grid', placeItems: 'center', py: 8 }}><CircularProgress /></Box>;

  const configured = state.enabled && !!state.inn;
  const groupTitle = (k: string) => state.groups.find(g => g.key === k)?.title ?? k;
  const actionTitle = (k: string) => state.actions.find(a => a.key === k)?.title ?? k;

  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
      {/* Статус подключения; настройки — в «Настройке интеграций» */}
      <Panel>
        <IntegrationStatus
          section="chz"
          ok={configured}
          title={configured ? `Честный знак подключён · ИНН ${state.inn}` : 'Честный знак не подключён'}
          details={configured
            ? [
                state.sandbox ? 'тестовый контур (песочница)' : 'боевой контур',
                state.fiasId ? 'место деятельности указано' : null,
                `группы: ${state.productGroups.map(groupTitle).join(', ')}`,
              ].filter(Boolean).join(' · ')
            : 'Укажите ИНН организации и товарные группы в «Настройке интеграций».'}
          warning={configured && !state.fiasId
            ? 'Не указан код ФИАС места деятельности — без него Честный знак отклоняет вывод пива и воды («МОД не найден»).'
            : null}
        />
        {configured && <Box sx={{ mt: 1.5 }}><SignInPanel state={state} thumbprint={thumbprint} onThumbprint={setThumbprint} /></Box>}
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
                <Chip key={c} size="small" label={c.split(String.fromCharCode(29), 1)[0].slice(0, 31)} onDelete={() => setCodes(codes.filter(x => x !== c))}
                  sx={{ fontFamily: 'monospace' }} />
              ))}
            </Box>
          )}

          <TextField fullWidth size="small" label="Комментарий" value={comment} onChange={e => setComment(e.target.value)} sx={{ mb: 2 }} />

          {message && messageAt === 'form' && <Alert severity={message.ok ? 'success' : 'error'} sx={{ mb: 1.5, whiteSpace: 'pre-line', maxHeight: 260, overflowY: 'auto', wordBreak: 'break-word' }} onClose={() => setMessage(null)}>{message.text}</Alert>}

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

      {configured && state.signedIn && (
        <BalancePanel
          state={state}
          busy={busy}
          canSign={!!thumbprint}
          actionTitle={actionTitle(action)}
          onWithdraw={(list, pg) => withdraw(list, pg, 'Вывод остатков с баланса', 'balance')}
          message={messageAt === 'balance' ? message : null}
          onCloseMessage={() => setMessage(null)}
        />
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
