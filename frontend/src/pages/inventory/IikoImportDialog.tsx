import * as React from 'react';
import {
  Box, Typography, Button, TextField, Alert, CircularProgress, Checkbox, Dialog, DialogTitle, DialogContent, DialogActions,
  Autocomplete, FormControlLabel, Switch, IconButton, Collapse, useTheme, useMediaQuery,
} from '@mui/material';
import { Close as CloseIcon, ExpandMore as ExpandIcon } from '@mui/icons-material';
import { useQueryClient } from '@tanstack/react-query';
import { fetchIikoConnection, iikoPreview, iikoImport, type IikoPreview, type IikoTarget, type IikoImportResult } from '../../api/iiko';
import { apiError, fmtQty } from '../../api/inventory';
import { tokens } from '../../theme';

interface Props { open: boolean; onClose: () => void }

const targetKey = (t: IikoTarget | null) => (t ? `${t.kind}:${t.id}` : '');

/**
 * Перенос техкарт из iiko: вход → список блюд iiko с найденными блюдами нашего меню → импорт выбранных.
 */
const IikoImportDialog: React.FC<Props> = ({ open, onClose }) => {
  const theme = useTheme();
  const t = tokens[theme.palette.mode];
  const fullScreen = useMediaQuery(theme.breakpoints.down('md'));
  const qc = useQueryClient();

  const [url, setUrl] = React.useState('');
  const [login, setLogin] = React.useState('');
  const [password, setPassword] = React.useState('');
  const [preview, setPreview] = React.useState<IikoPreview | null>(null);
  const [map, setMap] = React.useState<Record<string, IikoTarget | null>>({});
  const [checked, setChecked] = React.useState<Set<string>>(new Set());
  const [expanded, setExpanded] = React.useState<string | null>(null);
  const [overwrite, setOverwrite] = React.useState(false);
  const [busy, setBusy] = React.useState(false);
  const [error, setError] = React.useState('');
  const [result, setResult] = React.useState<IikoImportResult | null>(null);

  React.useEffect(() => {
    if (!open) return;
    setPreview(null);
    setResult(null);
    setError('');
    setPassword('');
    fetchIikoConnection().then(c => { if (c.url) setUrl(c.url); if (c.login) setLogin(c.login); }).catch(() => undefined);
  }, [open]);

  const load = async () => {
    setBusy(true);
    setError('');
    try {
      const p = await iikoPreview(url, login, password);
      setPreview(p);
      setMap(Object.fromEntries(p.dishes.map(d => [d.iikoId, d.match])));
      setChecked(new Set(p.dishes.filter(d => d.match).map(d => d.iikoId)));
    } catch (e) {
      setError(apiError(e));
    } finally {
      setBusy(false);
    }
  };

  const run = async () => {
    if (!preview) return;
    setBusy(true);
    setError('');
    try {
      const dishes = preview.dishes
        .filter(d => checked.has(d.iikoId) && map[d.iikoId])
        .map(d => ({ iikoId: d.iikoId, kind: map[d.iikoId]!.kind, id: map[d.iikoId]!.id }));
      const r = await iikoImport(preview.snapshotId, dishes, overwrite);
      setResult(r);
      await qc.invalidateQueries({ queryKey: ['inventory'] });
    } catch (e) {
      setError(apiError(e));
    } finally {
      setBusy(false);
    }
  };

  const selected = preview ? preview.dishes.filter(d => checked.has(d.iikoId) && map[d.iikoId]) : [];
  const withCards = selected.filter(d => map[d.iikoId]?.hasCard).length;

  return (
    <Dialog open={open} onClose={() => !busy && onClose()} fullScreen={fullScreen} maxWidth="md" fullWidth>
      <DialogTitle sx={{ display: 'flex', alignItems: 'center', pr: 1.5 }}>
        <Box sx={{ flex: 1 }}>Техкарты из iiko</Box>
        <IconButton aria-label="Закрыть" onClick={onClose} disabled={busy}><CloseIcon /></IconButton>
      </DialogTitle>
      <DialogContent sx={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
        {result ? (
          <>
            <Alert severity="success">
              Перенесено техкарт блюд: {result.cards}, полуфабрикатов: {result.semiCards}.
              {result.createdItems > 0 && ` Новых позиций склада: ${result.createdItems} (категория «Из iiko» — проверьте единицы).`}
              {result.skipped > 0 && ` Пропущено: ${result.skipped}${!overwrite ? ' (у блюда уже была техкарта или в карте iiko нет ингредиентов)' : ''}.`}
            </Alert>
            {result.warnings.length > 0 && (
              <Alert severity="warning">
                <Box component="ul" sx={{ m: 0, pl: 2 }}>{result.warnings.map(w => <li key={w}>{w}</li>)}</Box>
              </Alert>
            )}
          </>
        ) : !preview ? (
          <>
            <Typography color="text.secondary">
              Сервис зайдёт на сервер iiko под пользователем iikoOffice и заберёт действующие техкарты блюд и заготовок.
              В iiko ничего не меняется.
            </Typography>
            <TextField
              label="Адрес сервера iiko"
              value={url}
              onChange={e => setUrl(e.target.value)}
              placeholder="http://192.168.1.20:8080/resto"
              helperText="Как в окне входа iikoOffice; компьютер с iiko должен быть в той же сети"
            />
            <Box sx={{ display: 'grid', gridTemplateColumns: { xs: 'minmax(0,1fr)', sm: '1fr 1fr' }, gap: 1.5 }}>
              <TextField label="Логин iiko" value={login} onChange={e => setLogin(e.target.value)} autoComplete="off" />
              <TextField label="Пароль" type="password" value={password} onChange={e => setPassword(e.target.value)} autoComplete="new-password"
                onKeyDown={e => { if (e.key === 'Enter' && url && login) void load(); }} />
            </Box>
            <Typography variant="body2" color="text.secondary">
              Пароль не сохраняется. На время загрузки сервис займёт одно подключение к iiko, как ещё один iikoOffice.
            </Typography>
          </>
        ) : (
          <>
            <Typography color="text.secondary">
              В iiko {preview.dishes.length} блюд с техкартами, {preview.prepared} заготовок, {preview.goods} продуктов
              (на нашем складе уже есть {preview.goodsMatched}, остальные создадутся). Отметьте блюда и проверьте, с чем они совпали в нашем меню.
            </Typography>
            <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
              <Checkbox
                checked={preview.dishes.length > 0 && preview.dishes.every(d => !map[d.iikoId] || checked.has(d.iikoId))}
                onChange={e => setChecked(e.target.checked ? new Set(preview.dishes.filter(d => map[d.iikoId]).map(d => d.iikoId)) : new Set())}
              />
              <Typography sx={{ fontWeight: 600 }}>Выбрано {selected.length}</Typography>
            </Box>
            <Box>
              {preview.dishes.map((d, i) => {
                const target = map[d.iikoId] ?? null;
                const isOpen = expanded === d.iikoId;
                return (
                  <Box key={d.iikoId} sx={{ borderTop: i ? '1px solid' : 0, borderColor: 'divider', py: 1 }}>
                    <Box sx={{ display: 'grid', gridTemplateColumns: { xs: 'auto minmax(0,1fr)', sm: 'auto minmax(0,1fr) minmax(0,1fr)' }, gap: 1, alignItems: 'center' }}>
                      <Checkbox
                        checked={checked.has(d.iikoId)}
                        disabled={!target}
                        onChange={e => setChecked(prev => {
                          const n = new Set(prev);
                          if (e.target.checked) n.add(d.iikoId); else n.delete(d.iikoId);
                          return n;
                        })}
                      />
                      <Box sx={{ minWidth: 0, cursor: 'pointer' }} onClick={() => setExpanded(isOpen ? null : d.iikoId)}>
                        <Typography sx={{ fontWeight: 600 }} noWrap>
                          {d.name}
                          <ExpandIcon fontSize="small" sx={{ verticalAlign: 'middle', color: 'text.secondary', transform: isOpen ? 'rotate(180deg)' : 'none', transition: 'transform .2s' }} />
                        </Typography>
                        <Typography variant="body2" color="text.secondary" noWrap>
                          {d.type === 'MODIFIER' ? 'модификатор · ' : ''}{d.ingredients.length} ингр.
                        </Typography>
                      </Box>
                      <Box sx={{ gridColumn: { xs: '2', sm: 'auto' } }}>
                        <Autocomplete<IikoTarget, false, false, false>
                          size="small"
                          options={preview.targets}
                          groupBy={o => (o.kind === 'dish' ? 'Блюда' : 'Добавки')}
                          value={target}
                          onChange={(_, v) => {
                            setMap(m => ({ ...m, [d.iikoId]: v }));
                            setChecked(prev => {
                              const n = new Set(prev);
                              if (v) n.add(d.iikoId); else n.delete(d.iikoId);
                              return n;
                            });
                          }}
                          getOptionLabel={o => o.name}
                          isOptionEqualToValue={(a, b) => targetKey(a) === targetKey(b)}
                          renderOption={(props, o) => {
                            const { key, ...rest } = props as typeof props & { key: string };
                            return (
                              <li key={key} {...rest}>
                                {o.name}
                                {o.hasCard && <Typography component="span" variant="body2" color="text.secondary" sx={{ ml: 1 }}>есть техкарта</Typography>}
                              </li>
                            );
                          }}
                          renderInput={params => (
                            <TextField {...params} placeholder="Не переносить"
                              helperText={target?.hasCard ? (overwrite ? 'Техкарта будет заменена' : 'Уже есть техкарта — пропустится') : ' '} />
                          )}
                        />
                      </Box>
                    </Box>
                    <Collapse in={isOpen}>
                      <Box sx={{ ml: { xs: 0, sm: 6 }, mt: 0.5, bgcolor: t.surfaceMuted, borderRadius: '12px', px: 1.5, py: 1 }}>
                        {d.ingredients.map((ing, j) => (
                          <Box key={j} sx={{ display: 'flex', justifyContent: 'space-between', gap: 2 }}>
                            <Typography variant="body2">{ing.name}{ing.type === 'PREPARED' ? ' (заготовка)' : ''}</Typography>
                            <Typography variant="body2" color="text.secondary" sx={{ whiteSpace: 'nowrap' }}>
                              {fmtQty(ing.gross)} {ing.unit ?? ''}
                            </Typography>
                          </Box>
                        ))}
                        {d.yield !== 1 && <Typography variant="body2" color="text.secondary">Выход по карте iiko: {fmtQty(d.yield)} {d.unit ?? ''}</Typography>}
                      </Box>
                    </Collapse>
                  </Box>
                );
              })}
            </Box>
            <FormControlLabel
              control={<Switch checked={overwrite} onChange={e => setOverwrite(e.target.checked)} />}
              label={`Заменять уже заполненные техкарты${withCards ? ` (${withCards})` : ''}`}
            />
          </>
        )}
        {error && <Alert severity="error">{error}</Alert>}
      </DialogContent>
      <DialogActions sx={{ px: 3, pb: 3, gap: 1 }}>
        {result ? (
          <Button variant="contained" color="secondary" onClick={onClose}>Готово</Button>
        ) : !preview ? (
          <Button variant="contained" color="secondary" onClick={load} disabled={busy || !url.trim() || !login.trim()}
            startIcon={busy ? <CircularProgress size={16} color="inherit" /> : undefined}>
            Загрузить из iiko
          </Button>
        ) : (
          <>
            <Button onClick={() => setPreview(null)} disabled={busy} sx={{ color: 'text.secondary', mr: 'auto' }}>Назад</Button>
            <Button variant="contained" color="secondary" onClick={run} disabled={busy || selected.length === 0}
              startIcon={busy ? <CircularProgress size={16} color="inherit" /> : undefined}>
              Перенести {selected.length}
            </Button>
          </>
        )}
      </DialogActions>
    </Dialog>
  );
};

export default IikoImportDialog;
