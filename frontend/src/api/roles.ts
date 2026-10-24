import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiClient } from './client';

export interface RoleInfo {
  name: string;
  title: string;
  description: string | null;
  /** Встроенная роль — не удаляется */
  isSystem: boolean;
  /** Права не настраиваются (администратор, клиент) */
  isLocked: boolean;
  permissions: string[];
  usersCount: number;
}

export interface PermissionGroup {
  group: string;
  permissions: { key: string; title: string; description: string }[];
}

export interface RoleInput {
  title: string;
  description?: string | null;
  permissions: string[];
}

export const useRoles = (enabled = true) =>
  useQuery<RoleInfo[]>({
    queryKey: ['roles'],
    queryFn: () => apiClient.get('/api/roles').then(r => r.data),
    enabled,
    staleTime: 60 * 1000,
  });

export const usePermissionCatalog = () =>
  useQuery<PermissionGroup[]>({
    queryKey: ['roles', 'catalog'],
    queryFn: () => apiClient.get('/api/roles/permissions').then(r => r.data),
    staleTime: Infinity,
  });

export const useSaveRole = () => {
  const qc = useQueryClient();
  return useMutation<{ name: string }, Error, { name?: string; data: RoleInput }>({
    mutationFn: ({ name, data }) =>
      (name ? apiClient.put(`/api/roles/${encodeURIComponent(name)}`, data) : apiClient.post('/api/roles', data)).then(r => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['roles'] }),
  });
};

export const useDeleteRole = () => {
  const qc = useQueryClient();
  return useMutation<unknown, Error, string>({
    mutationFn: (name) => apiClient.delete(`/api/roles/${encodeURIComponent(name)}`),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['roles'] });
      qc.invalidateQueries({ queryKey: ['users'] });
    },
  });
};

/** Цвет метки роли: у встроенных — свой, у созданных — по имени */
const BUILT_IN_COLORS: Record<string, string> = {
  admin: '#E2462C',
  manager: '#2563EB',
  courier: '#2E9E5B',
  user: '#7B776F',
};
const PALETTE = ['#7A4DFF', '#C77700', '#0891B2', '#DB2777', '#4E7D2E', '#9333EA', '#B45309'];

export const roleColor = (name: string) => {
  if (BUILT_IN_COLORS[name]) return BUILT_IN_COLORS[name];
  let h = 0;
  for (const ch of name) h = (h * 31 + ch.charCodeAt(0)) >>> 0;
  return PALETTE[h % PALETTE.length];
};
