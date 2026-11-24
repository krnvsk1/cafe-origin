import React, { useState, useMemo } from 'react';
import { useNavigate } from 'react-router-dom';
import { plural } from '../utils/plural';
import AddonGroupsTab from '../components/AddonGroupsTab';
import {
  Container,
  Typography,
  Box,
  Button,
  IconButton,
  Switch,
  Dialog,
  DialogTitle,
  DialogContent,
  DialogActions,
  TextField,
  Select,
  MenuItem,
  FormControl,
  InputLabel,
  FormControlLabel,
  Alert,
  Snackbar,
  Tab,
  Tabs,
  Grid2 as Grid,
  CircularProgress,
  InputAdornment,
  ToggleButton,
  ToggleButtonGroup,
  useMediaQuery,
  useTheme,
} from '@mui/material';
import {
  Add as AddIcon,
  Fastfood as FastfoodIcon,
  Search as SearchIcon,
  Close as CloseIcon,
} from '@mui/icons-material';
import { useDishes, useDeleteDish, useUpdateDishAvailability, useUpdateDishOrder } from '../api/hooks';
import { apiError } from '../api/inventory';
import MenuDishRow from '../components/MenuDishRow';
import MenuSectionsBoard from '../components/MenuSectionsBoard';
import { buildSections, toReorderItems, type MenuSection } from '../utils/menuSections';
import { KitchenStationsButton } from '../components/KitchenStationControls';
import { useKitchenStations } from '../api/staff';
import { 
  useAddonCategories, 
  useCreateAddonCategory,
  useUpdateAddonCategory,
  useDeleteAddonCategory,
  useCreateAddon,
  useUpdateAddon,
  useDeleteAddon,
  useLinkAddonToDish
} from '../hooks/useAddons';
import type { AddonCategory, Addon, Dish } from '../types';
import MenuImportButton from '../components/MenuImportButton';

interface TabPanelProps {
  children?: React.ReactNode;
  index: number;
  value: number;
}

function TabPanel(props: TabPanelProps) {
  const { children, value, index, ...other } = props;
  return (
    <div
      role="tabpanel"
      hidden={value !== index}
      {...other}
    >
      {value === index && <Box sx={{ py: 2 }}>{children}</Box>}
    </div>
  );
}

const AdminMenuPage: React.FC = () => {
  const navigate = useNavigate();
  const [tabValue, setTabValue] = useState(0);
  const muiTheme = useTheme();
  const narrow = useMediaQuery(muiTheme.breakpoints.down('sm'));
  const [expandedCategory, setExpandedCategory] = useState<number | null>(null);
  
  // Состояния для диалогов
  const [categoryDialog, setCategoryDialog] = useState<{
    open: boolean;
    mode: 'create' | 'edit';
    category?: AddonCategory;
  }>({ open: false, mode: 'create' });

  const [addonDialog, setAddonDialog] = useState<{
    open: boolean;
    mode: 'create' | 'edit';
    categoryId?: number;
    addon?: Addon;
  }>({ open: false, mode: 'create' });

  // Новый диалог для привязки группы к товару
  const [linkCategoryDialog, setLinkCategoryDialog] = useState<{
    open: boolean;
    dishId?: number;
    dishName?: string;
  }>({ open: false });

  const [snackbar, setSnackbar] = useState({
    open: false,
    message: '',
    severity: 'success' as 'success' | 'error'
  });

  // Хуки для данных
  const { data: dishes = [] } = useDishes();

  // Разделяем на родителей и дочерние
  const parents = useMemo(() => 
    dishes.filter(s => !s.parentId),
    [dishes]
  );
  const { data: categories = [] } = useAddonCategories();
  const { data: stations = [] } = useKitchenStations();

  // Фильтры и поиск: по названию, разделу, наличию, кухне
  const [search, setSearch] = useState('');
  const [stock, setStock] = useState<'all' | 'on' | 'off'>('all');
  const [station, setStation] = useState<'all' | number>('all');
  const q = search.trim().toLowerCase();
  // Разделы витрины по порядку; во время сохранения переноса — уже в новом порядке
  const [movedSections, setMovedSections] = useState<MenuSection[] | null>(null);
  React.useEffect(() => { setMovedSections(null); }, [dishes]);
  const allSections = useMemo(() => movedSections ?? buildSections(parents), [movedSections, parents]);
  const shownParents = useMemo(() => parents.filter(p => {
    const kids = dishes.filter(s => s.parentId === p.id);
    const items = kids.length ? kids : [p];
    if (q && ![p, ...kids].some(x => x.name.toLowerCase().includes(q) || (x.description ?? '').toLowerCase().includes(q))) return false;
    if (stock === 'on' && !items.some(x => x.isAvailable)) return false;
    if (stock === 'off' && items.every(x => x.isAvailable)) return false;
    if (station !== 'all') {
      const def = stations[0]?.id;
      const of = (x: typeof p) => x.kitchenStationId ?? p.kitchenStationId ?? def;
      if (![p, ...kids].some(x => of(x) === station)) return false;
    }
    return true;
  }), [parents, dishes, q, stock, station, stations]);
  const shownCategories = useMemo(() => !q ? categories : categories
    .map(c => (c.name.toLowerCase().includes(q) ? c : { ...c, addons: c.addons.filter(a => a.name.toLowerCase().includes(q)) }))
    .filter(c => c.name.toLowerCase().includes(q) || c.addons.length > 0), [categories, q]);
  const filtered = q !== '' || stock !== 'all' || station !== 'all';
  // Блюда, у которых ни один вариант не продаётся
  const offCount = useMemo(() => parents.filter(p => {
    const kids = dishes.filter(d => d.parentId === p.id);
    return kids.length > 0 && kids.every(k => !k.isAvailable);
  }).length, [parents, dishes]);
  const boardSections = useMemo(() => {
    if (!filtered) return allSections;
    const shown = new Set(shownParents.map(p => p.id));
    return allSections.map(s => ({ ...s, cards: s.cards.filter(c => shown.has(c.id)) })).filter(s => s.cards.length > 0);
  }, [filtered, allSections, shownParents]);
  const resetFilters = () => { setSearch(''); setStock('all'); setStation('all'); };
  
  // Мутации
  const deleteProduct = useDeleteDish();
  const updateOrder = useUpdateDishOrder();
  const handleReorder = (next: MenuSection[]) => {
    setMovedSections(next);
    updateOrder.mutate(toReorderItems(next), {
      onError: () => { setMovedSections(null); showMessage('Не удалось сохранить порядок', 'error'); },
    });
  };
  const updateAvailability = useUpdateDishAvailability();
  
  const createCategory = useCreateAddonCategory();
  const updateCategory = useUpdateAddonCategory();
  const deleteCategory = useDeleteAddonCategory();
  const createAddon = useCreateAddon();
  const updateAddon = useUpdateAddon();
  const deleteAddon = useDeleteAddon();
  const linkAddon = useLinkAddonToDish();

  const showMessage = (message: string, severity: 'success' | 'error') => {
    setSnackbar({ open: true, message, severity });
  };

  // Обработчики для категорий
  const handleSaveCategory = async (data: Partial<AddonCategory>) => {
    try {
      if (categoryDialog.mode === 'create') {
        await createCategory.mutateAsync(data);
        showMessage('Категория добавок создана', 'success');
      } else if (categoryDialog.category?.id) {
        await updateCategory.mutateAsync({ id: categoryDialog.category.id, ...data });
        showMessage('Категория добавок обновлена', 'success');
      }
      setCategoryDialog({ open: false, mode: 'create' });
    } catch {
      showMessage('Ошибка при сохранении категории', 'error');
    }
  };

  // Удаление категории добавок
  const handleDeleteCategory = async (categoryId: number, categoryName: string) => {
    if (window.confirm(`Удалить категорию "${categoryName}"? Все добавки в этой категории станут недоступны.`)) {
      try {
        await deleteCategory.mutateAsync(categoryId);
        showMessage('Категория удалена', 'success');
      } catch {
        showMessage('Ошибка при удалении категории', 'error');
      }
    }
  };

  // Обработчики для добавок - с явным полем категории
  const handleSaveAddon = async (data: Partial<Addon> & { addonCategoryId: number }) => {
    try {
      console.log('📤 ===== НАЧАЛО ОТПРАВКИ ДОБАВКИ =====');
      console.log('📤 Данные из формы:', data);
      
      if (addonDialog.mode === 'create') {
        // Отправляем данные с явным ID категории
        const addonData = {
          name: data.name!,
          description: data.description || '',
          price: data.price || 0,
          addonCategoryId: data.addonCategoryId,  // Явно из формы
          isAvailable: data.isAvailable ?? true,
          displayOrder: 0
        };
        
        console.log('📤 Данные для отправки:', JSON.stringify(addonData, null, 2));
        
        const result = await createAddon.mutateAsync(addonData);
        
        console.log('✅ Добавка создана:', result);
        showMessage('Добавка создана', 'success');
        setAddonDialog({ open: false, mode: 'create' });
      } else if (addonDialog.mode === 'edit' && addonDialog.addon?.id) {
        // Для редактирования отправляем только изменяемые поля
        const updateData: Partial<Addon> & { id: number } = {
          id: addonDialog.addon.id
        };
        
        if (data.name) updateData.name = data.name;
        if (data.description !== undefined) updateData.description = data.description;
        if (data.price !== undefined) updateData.price = data.price;
        if (data.isAvailable !== undefined) updateData.isAvailable = data.isAvailable;
        
        await updateAddon.mutateAsync(updateData);
        showMessage('Добавка обновлена', 'success');
        setAddonDialog({ open: false, mode: 'create' });
      }
    } catch (error: unknown) {
      console.error('❌ ===== ОШИБКА =====');
      showMessage(apiError(error, 'Ошибка при сохранении добавки'), 'error');
    }
  };

  // Привязка группы добавок к товару
  const handleLinkCategoryToDish = async (categoryId: number, dishId: number) => {
    try {
      // Получаем все добавки из этой группы
      const category = categories.find(c => c.id === categoryId);
      
      if (!category) return;
      
      // Привязываем каждую добавку из группы к товару
      for (const addon of category.addons) {
        await linkAddon.mutateAsync({
          addonId: addon.id,
          dishId,
          isDefault: false,
          maxQuantity: 1
        });
      }
      
      showMessage(`Группа "${category.name}" привязана к товару`, 'success');
      setLinkCategoryDialog({ open: false });
    } catch {
      showMessage('Ошибка при привязке группы', 'error');
    }
  };

  const handleDeleteAddon = async (addonId: number) => {
    if (window.confirm('Удалить эту добавку?')) {
      try {
        await deleteAddon.mutateAsync(addonId);
        showMessage('Добавка удалена', 'success');
      } catch {
        showMessage('Ошибка при удалении добавки', 'error');
      }
    }
  };

  const handleToggleCategory = (categoryId: number) => {
    setExpandedCategory(expandedCategory === categoryId ? null : categoryId);
  };

  // Блюдо в разделе: ручка переноса, варианты по нажатию, действия в «⋯»
  const renderCard = (parent: Dish, dragHandle: React.ReactNode) => (
    <MenuDishRow
      card={parent}
      variants={dishes.filter(d => d.parentId === parent.id)}
      dragHandle={dragHandle}
      showKitchen={stations.length > 1}
      onEdit={id => navigate(`/admin/edit/${id}`)}
      onAddVariant={id => navigate(`/admin/create?parentId=${id}`)}
      onLinkAddons={card => setLinkCategoryDialog({ open: true, dishId: card.id, dishName: card.name })}
      onDelete={(dish, isCard) => {
        const kids = isCard ? dishes.filter(d => d.parentId === dish.id).length : 0;
        const text = isCard
          ? `Удалить блюдо «${dish.name}»?${kids > 0 ? ` Вместе с ним удалятся ${kids} ${plural(kids, ['вариант', 'варианта', 'вариантов'])}.` : ''}`
          : `Удалить вариант «${dish.name}»?`;
        if (window.confirm(text)) deleteProduct.mutate(dish.id);
      }}
      onAvailability={(ids, isAvailable) => ids.forEach(id => updateAvailability.mutate({ id, isAvailable }))}
      onError={text => showMessage(text, 'error')}
    />
  );

  return (
    <Container maxWidth="lg" disableGutters sx={{ py: { xs: 1, sm: 2 } }}>
      {/* Заголовок: что в меню и главное действие вкладки */}
      <Box sx={{ mb: { xs: 2, sm: 3 }, display: 'flex', justifyContent: 'space-between', alignItems: 'flex-end', flexWrap: 'wrap', gap: { xs: 1.5, sm: 2 } }}>
        <Box>
          <Typography variant="h4" component="h1" fontWeight={800} sx={{ fontSize: { xs: '1.75rem', sm: undefined } }}>Меню</Typography>
          <Typography color="text.secondary">
            {parents.length} {plural(parents.length, ['блюдо', 'блюда', 'блюд'])}
            {' · '}{allSections.length} {plural(allSections.length, ['раздел', 'раздела', 'разделов'])}
            {offCount > 0 && <> · <Box component="span" sx={{ color: 'warning.main' }}>{offCount} не {offCount === 1 ? 'продаётся' : 'продаются'}</Box></>}
          </Typography>
        </Box>
        <Box sx={{ display: 'flex', gap: 1, flexWrap: 'wrap', alignItems: 'center' }}>
          <MenuImportButton compact={narrow} />
          <KitchenStationsButton compact={narrow} />
          {tabValue === 0 ? (
            <Button variant="contained" startIcon={<AddIcon />} onClick={() => navigate('/admin/create')}>Добавить блюдо</Button>
          ) : (
            <Button variant="contained" startIcon={<AddIcon />} onClick={() => setCategoryDialog({ open: true, mode: 'create' })}>Добавить группу</Button>
          )}
        </Box>
      </Box>

      {/* Табы */}
      <Box>
        <Tabs
          value={tabValue}
          onChange={(_, v) => setTabValue(v)}
          sx={{ borderBottom: 1, borderColor: 'divider' }}
        >
          <Tab label={`Блюда · ${parents.length}`} />
          <Tab label={`Добавки · ${categories.length}`} />
        </Tabs>

        {/* Поиск и фильтры */}
        <Box sx={{ display: 'flex', gap: 1.5, flexWrap: 'wrap', alignItems: 'center', pt: 2 }}>
          <TextField size="small" placeholder={tabValue === 0 ? 'Поиск по названию и описанию' : 'Поиск групп и добавок'} value={search}
            onChange={e => setSearch(e.target.value)} sx={{ flex: '1 1 240px' }}
            slotProps={{ input: {
              startAdornment: <InputAdornment position="start"><SearchIcon fontSize="small" /></InputAdornment>,
              endAdornment: search ? <InputAdornment position="end"><IconButton size="small" onClick={() => setSearch('')} aria-label="Очистить"><CloseIcon fontSize="small" /></IconButton></InputAdornment> : undefined,
            } }} />
          {tabValue === 0 && (
            <>
              <ToggleButtonGroup size="small" exclusive value={stock} onChange={(_, v) => v && setStock(v)} aria-label="Наличие">
                <ToggleButton value="all" sx={{ px: 1.5 }}>Все</ToggleButton>
                <ToggleButton value="on" sx={{ px: 1.5 }}>В продаже</ToggleButton>
                <ToggleButton value="off" sx={{ px: 1.5 }}>Не продаются</ToggleButton>
              </ToggleButtonGroup>
              {stations.length > 1 && (
                <TextField select size="small" label="Кухня" value={station}
                  onChange={e => setStation(e.target.value === 'all' ? 'all' : Number(e.target.value))} sx={{ minWidth: 150 }}>
                  <MenuItem value="all">Все кухни</MenuItem>
                  {stations.map(st => <MenuItem key={st.id} value={st.id}>{st.name}</MenuItem>)}
                </TextField>
              )}
            </>
          )}
          {filtered && (
            <Button size="small" onClick={resetFilters} sx={{ color: 'text.secondary' }}>
              Сбросить{tabValue === 0 ? ` · найдено ${shownParents.length} из ${parents.length}` : ''}
            </Button>
          )}
        </Box>

        {/* Панель товаров */}
        <TabPanel value={tabValue} index={0}>
          {parents.length > 0 && (
            <>
              {filtered && shownParents.length > 0 && (
                <Alert severity="info" sx={{ mb: 2 }}>Порядок меняется без поиска и фильтров — сбросьте их, чтобы перетаскивать разделы и карточки.</Alert>
              )}
              <MenuSectionsBoard
                sections={boardSections}
                locked={filtered}
                onChange={handleReorder}
                renderCard={renderCard}
              />
            </>
          )}

          {parents.length > 0 && shownParents.length === 0 && (
            <Box sx={{ textAlign: 'center', py: 6 }}>
              <Typography color="text.secondary" sx={{ mb: 1 }}>Ничего не найдено</Typography>
              <Button onClick={resetFilters}>Сбросить фильтры</Button>
            </Box>
          )}

          {parents.length === 0 && (
            <Box sx={{ textAlign: 'center', py: 6 }}>
              <FastfoodIcon sx={{ fontSize: 48, color: 'text.disabled', mb: 1 }} />
              <Typography color="text.secondary">
                Нет блюд. Создайте первую карточку.
              </Typography>
            </Box>
          )}
        </TabPanel>

        {/* Панель добавок */}
        <TabPanel value={tabValue} index={1}>
          <AddonGroupsTab
            categories={shownCategories}
            expandedId={expandedCategory}
            onToggle={handleToggleCategory}
            onCreateGroup={() => setCategoryDialog({ open: true, mode: 'create' })}
            onEditGroup={(category) => setCategoryDialog({ open: true, mode: 'edit', category })}
            onDeleteGroup={(category) => handleDeleteCategory(category.id, category.name)}
            onCreateAddon={(categoryId) => setAddonDialog({ open: true, mode: 'create', categoryId })}
            onEditAddon={(addon, categoryId) => setAddonDialog({ open: true, mode: 'edit', addon, categoryId })}
            onDeleteAddon={handleDeleteAddon}
          />
        </TabPanel>
      </Box>

      {/* Диалог для категории добавок */}
      <CategoryDialog
        open={categoryDialog.open}
        onClose={() => setCategoryDialog({ open: false, mode: 'create' })}
        category={categoryDialog.category}
        onSave={handleSaveCategory}
        loading={createCategory.isPending || updateCategory.isPending}
      />

      {/* Диалог для добавки */}
      <AddonDialog
        open={addonDialog.open}
        onClose={() => setAddonDialog({ open: false, mode: 'create' })}
        addon={addonDialog.addon}
        categoryId={addonDialog.categoryId}
        categories={categories}
        onSave={handleSaveAddon}
        loading={createAddon.isPending || updateAddon.isPending}
      />

      {/* Диалог для привязки группы к товару */}
      <LinkCategoryDialog
        open={linkCategoryDialog.open}
        onClose={() => setLinkCategoryDialog({ open: false })}
        dishId={linkCategoryDialog.dishId}
        dishName={linkCategoryDialog.dishName}
        categories={categories}
        onLink={handleLinkCategoryToDish}
        loading={linkAddon.isPending}
      />

      <Snackbar
        open={snackbar.open}
        autoHideDuration={4000}
        onClose={() => setSnackbar({ ...snackbar, open: false })}
      >
        <Alert severity={snackbar.severity} sx={{ width: '100%' }}>
          {snackbar.message}
        </Alert>
      </Snackbar>
    </Container>
  );
};

// Компонент диалога для категории
const CategoryDialog: React.FC<{
  open: boolean;
  onClose: () => void;
  category?: AddonCategory;
  onSave: (data: Partial<AddonCategory>) => void;
  loading: boolean;
}> = ({ open, onClose, category, onSave, loading }) => {
  const [formData, setFormData] = useState<Partial<AddonCategory>>({
    name: '',
    description: '',
    isRequired: false,
    minSelections: 0,
    maxSelections: 0
  });

  React.useEffect(() => {
    if (category) {
      setFormData({
        name: category.name || '',
        description: category.description || '',
        isRequired: category.isRequired || false,
        minSelections: category.minSelections || 0,
        maxSelections: category.maxSelections || 0
      });
    } else {
      setFormData({
        name: '',
        description: '',
        isRequired: false,
        minSelections: 0,
        maxSelections: 0
      });
    }
  }, [category]);

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    onSave(formData);
  };

  return (
    <Dialog open={open} onClose={onClose} maxWidth="sm" fullWidth>
      <form onSubmit={handleSubmit}>
        <DialogTitle>
          {category ? 'Редактировать группу' : 'Новая группа добавок'}
        </DialogTitle>
        <DialogContent>
          <TextField
            fullWidth
            label="Название группы"
            value={formData.name}
            onChange={(e) => setFormData({ ...formData, name: e.target.value })}
            margin="normal"
            required
          />
          <TextField
            fullWidth
            label="Описание"
            value={formData.description}
            onChange={(e) => setFormData({ ...formData, description: e.target.value })}
            margin="normal"
            multiline
            rows={2}
          />
          <FormControlLabel
            control={
              <Switch
                checked={formData.isRequired}
                onChange={(e) => setFormData({ ...formData, isRequired: e.target.checked })}
              />
            }
            label="Обязательная категория"
          />
          <Grid container spacing={2} sx={{ mt: 1 }}>
            <Grid size={{ xs: 6 }}>
              <TextField
                fullWidth
                type="number"
                label="Мин. выбор"
                value={formData.minSelections}
                onChange={(e) => setFormData({ ...formData, minSelections: parseInt(e.target.value) || 0 })}
                inputProps={{ min: 0 }}
              />
            </Grid>
            <Grid size={{ xs: 6 }}>
              <TextField
                fullWidth
                type="number"
                label="Макс. выбор"
                value={formData.maxSelections}
                onChange={(e) => setFormData({ ...formData, maxSelections: parseInt(e.target.value) || 0 })}
                inputProps={{ min: 0 }}
                helperText="0 = без ограничений"
              />
            </Grid>
          </Grid>
        </DialogContent>
        <DialogActions>
          <Button onClick={onClose}>Отмена</Button>
          <Button type="submit" variant="contained" disabled={loading}>
            Сохранить
          </Button>
        </DialogActions>
      </form>
    </Dialog>
  );
};

// Обновленный компонент диалога для добавки с выбором категории
const AddonDialog: React.FC<{
  open: boolean;
  onClose: () => void;
  addon?: Addon;
  categoryId?: number;
  categories: AddonCategory[];
  onSave: (data: Partial<Addon> & { addonCategoryId: number }) => void;
  loading: boolean;
}> = ({ open, onClose, addon, categoryId, categories, onSave, loading }) => {
  const [formData, setFormData] = useState<{
    name: string;
    description: string;
    price: number;
    isAvailable: boolean;
    addonCategoryId: number;
  }>({
    name: '',
    description: '',
    price: 0,
    isAvailable: true,
    addonCategoryId: categoryId || (categories[0]?.id || 0)
  });

  React.useEffect(() => {
    if (addon) {
      setFormData({
        name: addon.name || '',
        description: addon.description || '',
        price: addon.price || 0,
        isAvailable: addon.isAvailable ?? true,
        addonCategoryId: addon.addonCategoryId || categoryId || (categories[0]?.id || 0)
      });
    } else {
      setFormData({
        name: '',
        description: '',
        price: 0,
        isAvailable: true,
        addonCategoryId: categoryId || (categories[0]?.id || 0)
      });
    }
  }, [addon, categoryId, categories]);

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    
    if (!formData.name.trim()) {
      alert('Введите название добавки');
      return;
    }
    
    if (formData.price < 0) {
      alert('Цена не может быть отрицательной');
      return;
    }
    
    if (!formData.addonCategoryId) {
      alert('Выберите категорию');
      return;
    }
    
    onSave({
      name: formData.name.trim(),
      description: formData.description.trim(),
      price: formData.price,
      isAvailable: formData.isAvailable,
      addonCategoryId: formData.addonCategoryId
    });
  };

  return (
    <Dialog open={open} onClose={onClose} maxWidth="sm" fullWidth>
      <form onSubmit={handleSubmit}>
        <DialogTitle>
          {addon ? 'Редактировать добавку' : 'Новая добавка'}
        </DialogTitle>
        <DialogContent>
          <FormControl fullWidth margin="normal" required>
            <InputLabel>Категория добавки</InputLabel>
            <Select
              value={formData.addonCategoryId}
              label="Категория добавки"
              onChange={(e) => setFormData({ 
                ...formData, 
                addonCategoryId: Number(e.target.value) 
              })}
              disabled={!!addon}
            >
              {categories.map((cat) => (
                <MenuItem key={cat.id} value={cat.id}>
                  {cat.name}
                </MenuItem>
              ))}
            </Select>
          </FormControl>

          <TextField
            fullWidth
            label="Название добавки"
            value={formData.name}
            onChange={(e) => setFormData({ ...formData, name: e.target.value })}
            margin="normal"
            required
          />
          
          <TextField
            fullWidth
            label="Описание"
            value={formData.description}
            onChange={(e) => setFormData({ ...formData, description: e.target.value })}
            margin="normal"
            multiline
            rows={2}
          />
          
          <TextField
            fullWidth
            type="number"
            label="Цена"
            value={formData.price}
            onChange={(e) => setFormData({ 
              ...formData, 
              price: parseFloat(e.target.value) || 0 
            })}
            margin="normal"
            required
            inputProps={{ min: 0, step: 0.5 }}
          />
          
          <FormControlLabel
            control={
              <Switch
                checked={formData.isAvailable}
                onChange={(e) => setFormData({ 
                  ...formData, 
                  isAvailable: e.target.checked 
                })}
              />
            }
            label="Доступно"
          />
        </DialogContent>
        <DialogActions>
          <Button onClick={onClose}>Отмена</Button>
          <Button type="submit" variant="contained" disabled={loading}>
            {loading ? <CircularProgress size={24} /> : 'Сохранить'}
          </Button>
        </DialogActions>
      </form>
    </Dialog>
  );
};

// Компонент диалога для привязки группы к товару
const LinkCategoryDialog: React.FC<{
  open: boolean;
  onClose: () => void;
  dishId?: number;
  dishName?: string;
  categories: AddonCategory[];
  onLink: (categoryId: number, dishId: number) => void;
  loading: boolean;
}> = ({ open, onClose, dishId, dishName, categories, onLink, loading }) => {
  const [selectedCategory, setSelectedCategory] = useState<number | ''>('');

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    if (dishId && selectedCategory) {
      onLink(selectedCategory as number, dishId);
      onClose();
    }
  };

  return (
    <Dialog open={open} onClose={onClose} maxWidth="sm" fullWidth>
      <form onSubmit={handleSubmit}>
        <DialogTitle>
          Привязать группу добавок к товару "{dishName}"
        </DialogTitle>
        <DialogContent>
          <FormControl fullWidth margin="normal">
            <InputLabel>Выберите группу добавок</InputLabel>
            <Select
              value={selectedCategory}
              label="Выберите группу добавок"
              onChange={(e) => setSelectedCategory(e.target.value as number)}
              required
            >
              {categories.map((category) => (
                <MenuItem key={category.id} value={category.id}>
                  {category.name} ({category.addons?.length || 0} добавок)
                </MenuItem>
              ))}
            </Select>
          </FormControl>
        </DialogContent>
        <DialogActions>
          <Button onClick={onClose}>Отмена</Button>
          <Button 
            type="submit" 
            variant="contained" 
            disabled={!selectedCategory || loading}
          >
            Привязать группу
          </Button>
        </DialogActions>
      </form>
    </Dialog>
  );
};

export default AdminMenuPage;
