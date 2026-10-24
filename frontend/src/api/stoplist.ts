import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiClient } from './client';

export interface StopListItem {
  id: number;
  name: string;
  price: number;
  isAvailable: boolean;
  parentId: number | null;
  parentName: string | null;
  hasChildren: boolean;
}

export interface StopListAddon {
  id: number;
  name: string;
  price: number;
  isAvailable: boolean;
  categoryName: string;
}

export interface StopListData {
  items: StopListItem[];
  addons: StopListAddon[];
}

export const useStopList = () =>
  useQuery<StopListData>({
    queryKey: ['stoplist'],
    queryFn: () => apiClient.get('/api/stoplist').then(res => res.data),
  });

type Kind = 'items' | 'addons';

export const useSetAvailability = () => {
  const queryClient = useQueryClient();
  return useMutation<unknown, Error, { kind: Kind; id: number; isAvailable: boolean }, { previous?: StopListData }>({
    mutationFn: ({ kind, id, isAvailable }) =>
      apiClient.patch(`/api/stoplist/${kind}/${id}`, { isAvailable }).then(res => res.data),
    // Мгновенно переключаем в интерфейсе, откатываем при ошибке
    onMutate: async ({ kind, id, isAvailable }) => {
      await queryClient.cancelQueries({ queryKey: ['stoplist'] });
      const previous = queryClient.getQueryData<StopListData>(['stoplist']);
      if (previous) {
        queryClient.setQueryData<StopListData>(['stoplist'], {
          ...previous,
          [kind]: (previous[kind] as Array<StopListItem | StopListAddon>).map(x =>
            x.id === id ? { ...x, isAvailable } : x
          ),
        } as StopListData);
      }
      return { previous };
    },
    onError: (_err, _vars, context) => {
      if (context?.previous) queryClient.setQueryData(['stoplist'], context.previous);
    },
    onSettled: () => {
      queryClient.invalidateQueries({ queryKey: ['stoplist'] });
      queryClient.invalidateQueries({ queryKey: ['shawarmas'] });
      queryClient.invalidateQueries({ queryKey: ['addon-categories'] });
      queryClient.invalidateQueries({ queryKey: ['shawarma-addons'] });
    },
  });
};
