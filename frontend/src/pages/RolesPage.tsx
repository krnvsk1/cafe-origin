import * as React from 'react';
import {
  Box, Typography, Button, CircularProgress, Dialog, DialogTitle, DialogContent, DialogActions, TextField,
  Switch, Alert, IconButton, MenuItem, Divider, useMediaQuery, useTheme, Snackbar,
} from '@mui/material';
import {
  Add as AddIcon, Close as CloseIcon, LockOutlined as LockIcon, PeopleOutline as PeopleIcon,
  ChevronRight as ChevronRightIcon,
} from '@mui/icons-material';
import {
  useRoles, usePermissionCatalog, useSaveRole, useDeleteRole, roleColor, type RoleInfo,
} from '../api/roles';
import { apiError } from '../api/inventory';
import { useAuthStore } from '../store/authStore';
import { tokens } from '../theme';

const plural = (n: number, one: string, few: string, many: string) => {
  const m10 = n % 10, m100 = n % 100;
  if (m10 === 1 && m100 !== 11) return one;
  if (m10 >= 2 && m10 <= 4 && (m100 < 12 || m100 > 14)) return few;
  return many;
};

/** Окно настройки роли: название и переключатели разрешений по группам */
const RoleEditor: React.FC<{
  open: boolean;
  role: RoleInfo | null;
  roles: RoleInfo[];
  onClose: () => void;
  onSaved: (message: string) => void;
}> = ({ open, role, roles, onClose, onSaved }) => {
  const theme = useTheme();
  const t = tokens[theme.palette.mode];
  const fullScreen = useMediaQuery(theme.breakpoints.down('sm'));
  const { data: catalog = [] } = usePermissionCatalog();
  const save = useSaveRole();
  const remove = useDeleteRole();
  const me = useAuthStore(s => s.user);
  const iAmAdmin = !!me?.roles.includes('admin');
  const myPerms = new Set(me?.permissions ?? []);

  const [title, setTitle] = React.useState('');
  const [description, setDescription] = React.useState('');
  const [perms, setPerms] = React.useState<Set<string>>(new Set());
  const [error, setError] = React.useState('');
  const [confirmDelete, setConfirmDelete] = React.useState(false);

  React.useEffect(() => {
    if (!open) return;
    setTitle(role?.title ?? '');
    setDescription(role?.description ?? '');
    setPerms(new Set(role?.permissions ?? []));
    setError('');
    setConfirmDelete(false);
  }, [open, role]);

  const locked = !!role?.isLocked;
  // Не-админ может выдавать только свои права
  const canToggle = (key: string) => !locked && (iAmAdmin || myPerms.has(key));

  const toggle = (key: string, on: boolean) =>
    setPerms(prev => {
      const next = new Set(prev);
      if (on) next.add(key); else next.delete(key);
      return next;
    });

  const toggleGroup = (keys: string[], on: boolean) =>
    setPerms(prev => {
      const next = new Set(prev);
      keys.filter(canToggle).forEach(k => (on ? next.add(k) : next.delete(k)));
      return next;
    });

  const copyFrom = (name: string) => {
    const source = roles.find(r => r.name === name);
    if (source) setPerms(new Set(source.permissions.filter(p => iAmAdmin || myPerms.has(p))));
  };

  const handleSave = async () => {
    if (!title.trim()) { setError('Введите название роли'); return; }
    try {
      await save.mutateAsync({ name: role?.name, data: { title: title.trim(), description: description.trim() || null, permissions: [...perms] } });
      onSaved(role ? 'Роль сохранена. Права действуют сразу.' : 'Роль создана — назначьте её сотрудникам в «Пользователях»');
      onClose();
    } catch (e) {
      setError(apiError(e));
    }
  };

  const handleDelete = async () => {
    if (!role) return;
    try {
      await remove.mutateAsync(role.name);
      onSaved('Роль удалена');
      onClose();
    } catch (e) {
      setError(apiError(e));
      setConfirmDelete(false);
    }
  };

  const busy = save.isPending || remove.isPending;

  return (
    <Dialog open={open} onClose={() => !busy && onClose()} fullScreen={fullScreen} maxWidth="sm" fullWidth>
      <DialogTitle sx={{ display: 'flex', alignItems: 'center', gap: 1, pr: 1.5 }}>
        <Box sx={{ flex: 1 }}>{role ? role.title : 'Новая роль'}</Box>
        <IconButton aria-label="Закрыть" onClick={onClose} disabled={busy}><CloseIcon /></IconButton>
      </DialogTitle>

      <DialogContent sx={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
        {error && <Alert severity="error" onClose={() => setError('')}>{error}</Alert>}

        <TextField
          label="Название"
          value={title}
          onChange={e => setTitle(e.target.value)}
          placeholder="Например: Повар, Старший смены, Кладовщик"
          autoFocus={!role}
          disabled={role?.name === 'admin'}
          slotProps={{ htmlInput: { maxLength: 60 } }}
          sx={{ mt: 1 }}
        />
        <TextField
          label="Описание"
          value={description}
          onChange={e => setDescription(e.target.value)}
          placeholder="Чем занимается сотрудник"
          multiline
          minRows={1}
          disabled={role?.name === 'admin'}
          slotProps={{ htmlInput: { maxLength: 300 } }}
        />

        {locked ? (
          <Alert severity="info" icon={<LockIcon />}>
            {role?.name === 'admin'
              ? 'Администратор всегда имеет доступ ко всему — так нельзя случайно потерять управление сайтом.'
              : 'Роль «Клиент» есть у всех покупателей, поэтому доступа к управлению у неё нет.'}
          </Alert>
        ) : (
          <>
            {!role && roles.some(r => !r.isLocked && r.permissions.length > 0) && (
              <TextField select label="Взять права из роли" value="" onChange={e => copyFrom(e.target.value)} size="small">
                {roles.filter(r => !r.isLocked && r.permissions.length > 0).map(r => (
                  <MenuItem key={r.name} value={r.name}>{r.title}</MenuItem>
                ))}
              </TextField>
            )}
            {!iAmAdmin && (
              <Typography variant="body2" color="text.secondary">
                Можно выдать только те права, которые есть у вас самих.
              </Typography>
            )}
          </>
        )}

        {catalog.map(group => {
          const keys = group.permissions.map(p => p.key);
          const toggleable = keys.filter(canToggle);
          const allOn = toggleable.length > 0 && toggleable.every(k => perms.has(k));
          return (
            <Box key={group.group} sx={{ bgcolor: t.surfaceMuted, borderRadius: '16px', px: 2, py: 1 }}>
              <Box sx={{ display: 'flex', alignItems: 'center', py: 0.5 }}>
                <Typography sx={{ fontWeight: 700, flex: 1 }}>{group.group}</Typography>
                {toggleable.length > 1 && (
                  <Button size="small" onClick={() => toggleGroup(keys, !allOn)} sx={{ color: 'text.secondary' }}>
                    {allOn ? 'Снять все' : 'Выбрать все'}
                  </Button>
                )}
              </Box>
              {group.permissions.map((p, i) => (
                <React.Fragment key={p.key}>
                  {i > 0 && <Divider />}
                  <Box
                    component="label"
                    sx={{ display: 'flex', alignItems: 'center', gap: 1.5, py: 1.25, cursor: canToggle(p.key) ? 'pointer' : 'default', opacity: canToggle(p.key) || locked ? 1 : 0.5 }}
                  >
                    <Box sx={{ flex: 1, minWidth: 0 }}>
                      <Typography sx={{ fontWeight: 600 }}>{p.title}</Typography>
                      <Typography variant="body2" color="text.secondary">{p.description}</Typography>
                    </Box>
                    <Switch
                      checked={perms.has(p.key)}
                      disabled={!canToggle(p.key) || busy}
                      onChange={e => toggle(p.key, e.target.checked)}
                      inputProps={{ 'aria-label': p.title }}
                    />
                  </Box>
                </React.Fragment>
              ))}
            </Box>
          );
        })}

        {confirmDelete && role && (
          <Alert
            severity="warning"
            action={
              <Box sx={{ display: 'flex', gap: 1 }}>
                <Button size="small" color="inherit" onClick={() => setConfirmDelete(false)}>Нет</Button>
                <Button size="small" variant="contained" color="error" onClick={handleDelete}>Удалить</Button>
              </Box>
            }
          >
            {role.usersCount > 0
              ? `У ${role.usersCount} ${plural(role.usersCount, 'сотрудника', 'сотрудников', 'сотрудников')} эта роль пропадёт вместе с доступом. Удалить?`
              : 'Удалить роль?'}
          </Alert>
        )}
      </DialogContent>

      <DialogActions sx={{ px: 3, pb: 3, pt: 1.5, gap: 1, borderTop: '1px solid', borderColor: 'divider' }}>
        {role && !role.isSystem && (
          <Button onClick={() => setConfirmDelete(true)} disabled={busy} sx={{ color: tokens.chili, mr: 'auto' }}>
            Удалить роль
          </Button>
        )}
        <Button onClick={onClose} disabled={busy} sx={{ color: 'text.secondary', ml: role && !role.isSystem ? 0 : 'auto' }}>Отмена</Button>
        {role?.name !== 'admin' && (
          <Button
            variant="contained"
            color="secondary"
            onClick={handleSave}
            disabled={busy}
            startIcon={save.isPending ? <CircularProgress size={16} color="inherit" /> : undefined}
          >
            {role ? 'Сохранить' : 'Создать роль'}
          </Button>
        )}
      </DialogActions>
    </Dialog>
  );
};

/**
 * Роли и доступы: какие окна и функции видит каждая роль. Админ создаёт свои роли.
 */
const RolesPage: React.FC = () => {
  const { data: roles = [], isLoading, error } = useRoles();
  const { data: catalog = [] } = usePermissionCatalog();
  const [editing, setEditing] = React.useState<RoleInfo | null | undefined>(undefined);
  const [toast, setToast] = React.useState('');
  const total = catalog.reduce((s, g) => s + g.permissions.length, 0);
  const titleOf = (key: string) => catalog.flatMap(g => g.permissions).find(p => p.key === key)?.title ?? key;

  return (
    <Box sx={{ py: { xs: 2, sm: 3 }, maxWidth: 1000, mx: 'auto' }}>
      <Box sx={{ display: 'flex', alignItems: 'flex-end', gap: 2, mb: 1, flexWrap: 'wrap' }}>
        <Box sx={{ flex: 1, minWidth: 240 }}>
          <Typography variant="h4" component="h1" sx={{ fontSize: { xs: '1.75rem', sm: '2.1rem' } }}>Роли и доступы</Typography>
          <Typography color="text.secondary" sx={{ mt: 0.5 }}>
            Отметьте, какие разделы и действия доступны каждой роли. Назначить роль сотруднику можно в «Пользователях».
          </Typography>
        </Box>
        <Button variant="contained" color="secondary" startIcon={<AddIcon />} onClick={() => setEditing(null)}>
          Новая роль
        </Button>
      </Box>

      {error && <Alert severity="error" sx={{ mt: 2 }}>{apiError(error)}</Alert>}

      {isLoading ? (
        <Box sx={{ display: 'grid', placeItems: 'center', py: 8 }}><CircularProgress /></Box>
      ) : (
        <Box sx={{ display: 'grid', gridTemplateColumns: { xs: 'minmax(0, 1fr)', md: 'repeat(2, minmax(0, 1fr))' }, gap: 1.5, mt: 2.5 }}>
          {roles.map(role => {
            const color = roleColor(role.name);
            return (
              <Box
                key={role.name}
                component="button"
                type="button"
                onClick={() => setEditing(role)}
                sx={{
                  textAlign: 'left',
                  border: 0,
                  font: 'inherit',
                  color: 'text.primary',
                  bgcolor: 'background.paper',
                  borderRadius: '20px',
                  p: 2.25,
                  cursor: 'pointer',
                  display: 'flex',
                  flexDirection: 'column',
                  gap: 1,
                  transition: 'box-shadow 0.15s ease',
                  '&:hover': { boxShadow: '0 6px 20px rgba(33,32,31,0.08)' },
                }}
              >
                <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.25, width: '100%' }}>
                  <Box sx={{ width: 12, height: 12, borderRadius: '50%', bgcolor: color, flexShrink: 0 }} />
                  <Typography sx={{ fontWeight: 800, fontSize: '1.1rem', flex: 1 }} noWrap>{role.title}</Typography>
                  {role.isLocked && <LockIcon fontSize="small" sx={{ color: 'text.secondary' }} />}
                  <ChevronRightIcon sx={{ color: 'text.secondary' }} />
                </Box>
                {role.description && (
                  <Typography variant="body2" color="text.secondary">{role.description}</Typography>
                )}
                <Box sx={{ display: 'flex', gap: 2, flexWrap: 'wrap', color: 'text.secondary', fontSize: '0.875rem' }}>
                  <Box sx={{ display: 'flex', alignItems: 'center', gap: 0.5 }}>
                    <PeopleIcon sx={{ fontSize: 18 }} />
                    {role.usersCount} {plural(role.usersCount, 'человек', 'человека', 'человек')}
                  </Box>
                  <span>
                    {role.name === 'admin'
                      ? 'полный доступ'
                      : role.permissions.length === 0
                        ? 'нет доступа к управлению'
                        : `${role.permissions.length} из ${total} прав`}
                  </span>
                </Box>
                {role.name !== 'admin' && role.permissions.length > 0 && (
                  <Box sx={{ display: 'flex', gap: 0.5, flexWrap: 'wrap' }}>
                    {role.permissions.slice(0, 6).map(p => (
                      <Box key={p} component="span" sx={{ px: 1, py: 0.25, borderRadius: '8px', bgcolor: 'action.hover', fontSize: '0.75rem', fontWeight: 600 }}>
                        {titleOf(p)}
                      </Box>
                    ))}
                    {role.permissions.length > 6 && (
                      <Box component="span" sx={{ px: 1, py: 0.25, fontSize: '0.75rem', color: 'text.secondary' }}>
                        +{role.permissions.length - 6}
                      </Box>
                    )}
                  </Box>
                )}
              </Box>
            );
          })}
        </Box>
      )}

      <RoleEditor
        open={editing !== undefined}
        role={editing ?? null}
        roles={roles}
        onClose={() => setEditing(undefined)}
        onSaved={setToast}
      />
      <Snackbar
        open={!!toast}
        autoHideDuration={4000}
        onClose={() => setToast('')}
        message={toast}
        anchorOrigin={{ vertical: 'bottom', horizontal: 'center' }}
      />
    </Box>
  );
};

export default RolesPage;
