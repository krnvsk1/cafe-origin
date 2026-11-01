import * as React from 'react';
import {
  Typography,
  Box,
  Button,
  TextField,
  Autocomplete,
  Alert,
  CircularProgress,
  Snackbar,
  InputAdornment,
  IconButton,
  Switch,
  ToggleButton,
  ToggleButtonGroup,
  Dialog,
  DialogTitle,
  DialogContent,
  DialogActions,
  Slider,
  Tooltip,
  Divider,
  useTheme,
  alpha,
} from '@mui/material';
import {
  ArrowBack as ArrowBackIcon,
  DeleteOutline as DeleteIcon,
  AddPhotoAlternateOutlined as AddPhotoIcon,
  Crop as CropIcon,
  Close as CloseIcon,
  Add as AddIcon,
  ChevronRight as ChevronRightIcon,
  RotateLeft as RotateLeftIcon,
  RotateRight as RotateRightIcon,
} from '@mui/icons-material';
import { useNavigate, useParams, useSearchParams } from 'react-router-dom';
import ReactCrop, { type Crop, type PixelCrop } from 'react-image-crop';
import 'react-image-crop/dist/ReactCrop.css';
import {
  useDish,
  useCreateDish,
  useUpdateDish,
  useDeleteDish,
  useDishes,
  useMenuSections,
  useUploadImage,
  useDishImages,
  useDeleteImage,
} from '../api/hooks';
import type { CreateDishDto, Dish, DishImage } from '../types';
import { resolveMediaUrl } from '../utils/media';
import MenuItemCard, { BADGES } from '../components/MenuItemCard';
import { tokens } from '../theme';
import { ORDER_TYPES, describeOrderTypes } from '../utils/orderTypes';

interface TempImage {
  file: File;
  preview: string;
}

type Kind = 'card' | 'variant';
type FieldErrors = Partial<Record<'name' | 'price' | 'description' | 'parentId', string>>;

/** Белый блок формы с заголовком */
const Section: React.FC<{
  title: string;
  subtitle?: string;
  action?: React.ReactNode;
  children: React.ReactNode;
}> = ({ title, subtitle, action, children }) => (
  <Box sx={{ bgcolor: 'background.paper', borderRadius: '24px', p: { xs: 2, sm: 3 } }}>
    <Box sx={{ display: 'flex', alignItems: 'flex-start', gap: 2, mb: 2.5 }}>
      <Box sx={{ flex: 1, minWidth: 0 }}>
        <Typography variant="h6" component="h2" sx={{ fontSize: '1.15rem' }}>{title}</Typography>
        {subtitle && (
          <Typography variant="body2" color="text.secondary" sx={{ mt: 0.25 }}>{subtitle}</Typography>
        )}
      </Box>
      {action}
    </Box>
    {children}
  </Box>
);

/** Подпись над полем — вместо «плавающей» подписи внутри серого поля */
const Field: React.FC<{
  label: string;
  htmlFor?: string;
  required?: boolean;
  hint?: string;
  children: React.ReactNode;
}> = ({ label, htmlFor, required, hint, children }) => (
  <Box>
    <Typography
      component="label"
      htmlFor={htmlFor}
      sx={{ display: 'block', fontWeight: 600, fontSize: '0.9rem', mb: 0.75 }}
    >
      {label}
      {required && <Box component="span" sx={{ color: tokens.chili, ml: 0.25 }}>*</Box>}
    </Typography>
    {children}
    {hint && (
      <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 0.75 }}>
        {hint}
      </Typography>
    )}
  </Box>
);

/** Строка настройки с переключателем */
const SettingRow: React.FC<{
  title: string;
  description: string;
  checked: boolean;
  onChange: (value: boolean) => void;
  disabled?: boolean;
}> = ({ title, description, checked, onChange, disabled }) => (
  <Box
    component="label"
    sx={{ display: 'flex', alignItems: 'center', gap: 2, py: 1.5, cursor: disabled ? 'default' : 'pointer' }}
  >
    <Box sx={{ flex: 1 }}>
      <Typography sx={{ fontWeight: 600 }}>{title}</Typography>
      <Typography variant="body2" color="text.secondary">{description}</Typography>
    </Box>
    <Switch checked={checked} onChange={(e) => onChange(e.target.checked)} disabled={disabled} />
  </Box>
);

const BADGE_OPTIONS: { value: CreateDishDto['badge']; label: string; color?: string }[] = [
  { value: null, label: 'Без метки' },
  ...Object.entries(BADGES).map(([value, b]) => ({
    value: value as CreateDishDto['badge'],
    label: b.label,
    color: b.color,
  })),
];

const CreateMenuItemPage: React.FC = () => {
  const theme = useTheme();
  const t = tokens[theme.palette.mode];
  const navigate = useNavigate();
  const { id } = useParams();
  const isEditMode = Boolean(id);
  const numericId = Number(id) || 0;

  // Фото и кадрирование
  const [uploading, setUploading] = React.useState(false);
  const [dragOver, setDragOver] = React.useState(false);
  const [tempImages, setTempImages] = React.useState<TempImage[]>([]);
  const [cropModalOpen, setCropModalOpen] = React.useState(false);
  const [currentCropImage, setCurrentCropImage] = React.useState<TempImage | null>(null);
  const [crop, setCrop] = React.useState<Crop>({ unit: '%', width: 90, height: 90, x: 5, y: 5 });
  const [zoom, setZoom] = React.useState(1);
  const [rotation, setRotation] = React.useState(0);
  const [completedCrop, setCompletedCrop] = React.useState<PixelCrop | null>(null);
  const [aspect, setAspect] = React.useState<number | undefined>(1);
  const imgRef = React.useRef<HTMLImageElement | null>(null);
  const fileInputRef = React.useRef<HTMLInputElement | null>(null);
  const [cropProcessing, setCropProcessing] = React.useState(false);

  const uploadImage = useUploadImage();
  const { data: images = [], refetch: refetchImages } = useDishImages(numericId);
  const deleteImage = useDeleteImage();

  // Данные товара
  const { data: allDishes = [] } = useDishes();
  const parentCards = React.useMemo(
    () => allDishes.filter(s => (s.parentId === null || s.parentId === undefined) && s.id !== numericId),
    [allDishes, numericId]
  );
  const variants = React.useMemo(
    () => (isEditMode ? allDishes.filter(s => s.parentId === numericId) : []),
    [allDishes, isEditMode, numericId]
  );
  const { data: menuSections = [] } = useMenuSections();
  const { data: existingDish, isLoading: isLoadingDish } = useDish(isEditMode ? numericId : 0);
  const createDish = useCreateDish();
  const updateDish = useUpdateDish();
  const deleteDish = useDeleteDish();

  const [searchParams] = useSearchParams();
  const urlParentId = searchParams.get('parentId');

  const [kind, setKind] = React.useState<Kind>(urlParentId ? 'variant' : 'card');
  const [formData, setFormData] = React.useState<CreateDishDto>({
    name: '',
    price: 0,
    description: '',
    isSpicy: false,
    isAvailable: true,
    parentId: urlParentId ? Number(urlParentId) : null,
    category: '',
    badge: null,
    availableOrderTypes: null,
  });
  const [errors, setErrors] = React.useState<FieldErrors>({});
  const [confirmDelete, setConfirmDelete] = React.useState(false);
  const [saved, setSaved] = React.useState(false);

  const [snackbar, setSnackbar] = React.useState({
    open: false,
    message: '',
    severity: 'success' as 'success' | 'error' | 'info' | 'warning',
  });

  // Заполняем форму при редактировании
  React.useEffect(() => {
    if (existingDish) {
      setFormData({
        name: existingDish.name,
        price: existingDish.price,
        description: existingDish.description,
        isSpicy: existingDish.isSpicy,
        isAvailable: existingDish.isAvailable,
        parentId: existingDish.parentId ?? null,
        category: existingDish.category ?? '',
        badge: existingDish.badge ?? null,
        availableOrderTypes: existingDish.availableOrderTypes ?? null,
      });
      setKind(existingDish.parentId ? 'variant' : 'card');
    }
  }, [existingDish]);

  // Переход между товарами без размонтирования — сбрасываем несохранённые фото и ошибки
  React.useEffect(() => {
    setTempImages([]);
    setErrors({});
    setSaved(false);
  }, [id]);

  const isPending = createDish.isPending || updateDish.isPending || deleteDish.isPending || saved;

  const showSnackbar = (message: string, severity: 'success' | 'error' | 'info' | 'warning') => {
    setSnackbar({ open: true, message, severity });
  };

  const handleChange = <K extends keyof CreateDishDto>(field: K, value: CreateDishDto[K]) => {
    setFormData(prev => ({ ...prev, [field]: value }));
    if (field in errors) setErrors(prev => ({ ...prev, [field]: undefined }));
  };

  const handleKindChange = (_: unknown, value: Kind | null) => {
    if (!value) return;
    setKind(value);
    setErrors({});
    if (value === 'card') handleChange('parentId', null);
  };

  const validateForm = (): boolean => {
    const next: FieldErrors = {};
    if (!formData.name?.trim()) next.name = 'Введите название';
    if (kind === 'variant') {
      if (!formData.parentId) next.parentId = 'Выберите карточку, к которой относится вариант';
      if (!formData.price || formData.price <= 0) next.price = 'Укажите цену';
    } else if (!formData.description?.trim()) {
      next.description = 'Добавьте описание — его видят гости';
    }
    if (formData.price < 0) next.price = 'Цена не может быть отрицательной';
    setErrors(next);
    const first = Object.keys(next)[0];
    if (first) {
      document.getElementById(`field-${first}`)?.focus();
      return false;
    }
    return true;
  };

  // Кадрирование с учётом зума и поворота
  const getCroppedImg = async (
    imageSrc: string,
    pixelCrop: PixelCrop,
    currentZoom: number,
    currentRotation: number
  ): Promise<Blob | null> => {
    const image = new Image();
    image.src = imageSrc;
    await new Promise(resolve => { image.onload = resolve; });

    const canvas = document.createElement('canvas');
    const ctx = canvas.getContext('2d');
    if (!ctx) return null;

    const cropWidth = pixelCrop.width;
    const cropHeight = pixelCrop.height;
    canvas.width = cropWidth;
    canvas.height = cropHeight;

    const rad = (currentRotation * Math.PI) / 180;
    const elImg = imgRef.current;
    const scaleX = elImg ? image.naturalWidth / (elImg.width / currentZoom) : image.naturalWidth;
    const scaleY = elImg ? image.naturalHeight / (elImg.height / currentZoom) : image.naturalHeight;

    ctx.save();
    ctx.translate(cropWidth / 2, cropHeight / 2);
    ctx.rotate(rad);
    ctx.scale(currentZoom, currentZoom);
    ctx.translate(-cropWidth / 2, -cropHeight / 2);
    ctx.drawImage(
      image,
      pixelCrop.x * scaleX,
      pixelCrop.y * scaleY,
      pixelCrop.width * scaleX,
      pixelCrop.height * scaleY,
      0,
      0,
      cropWidth,
      cropHeight
    );
    ctx.restore();

    return new Promise(resolve => {
      canvas.toBlob(blob => resolve(blob), 'image/jpeg', 0.92);
    });
  };

  const addFiles = (files: FileList | File[]) => {
    Array.from(files).forEach(file => {
      if (!file.type.startsWith('image/')) return;
      if (file.size > 10 * 1024 * 1024) {
        showSnackbar(`Файл «${file.name}» больше 10 МБ`, 'warning');
        return;
      }
      const preview = URL.createObjectURL(file);
      setTempImages(prev => [...prev, { file, preview }]);
    });
  };

  const handleFileSelect = (event: React.ChangeEvent<HTMLInputElement>) => {
    const files = event.target.files;
    if (!files || files.length === 0) return;
    addFiles(files);
    event.target.value = '';
  };

  const openFilePicker = () => {
    if (!uploading && !isPending) fileInputRef.current?.click();
  };

  const dropHandlers = {
    onDragOver: (e: React.DragEvent) => { e.preventDefault(); setDragOver(true); },
    onDragLeave: (e: React.DragEvent) => { e.preventDefault(); setDragOver(false); },
    onDrop: (e: React.DragEvent) => {
      e.preventDefault();
      setDragOver(false);
      if (e.dataTransfer.files.length > 0) addFiles(e.dataTransfer.files);
    },
  };

  const openCropModal = (image: TempImage) => {
    setCurrentCropImage(image);
    setCrop({ unit: '%', width: 90, height: 90, x: 5, y: 5 });
    setZoom(1);
    setRotation(0);
    setAspect(1);
    setCompletedCrop(null);
    setCropProcessing(false);
    setCropModalOpen(true);
  };

  const onImageLoad = (e: React.SyntheticEvent<HTMLImageElement>) => {
    imgRef.current = e.currentTarget;
  };

  const handleCropApply = async () => {
    if (!currentCropImage || !completedCrop) return;
    setCropProcessing(true);
    try {
      const croppedBlob = await getCroppedImg(currentCropImage.preview, completedCrop, zoom, rotation);
      if (croppedBlob) {
        const croppedFile = new File([croppedBlob], currentCropImage.file.name, { type: 'image/jpeg' });
        setTempImages(prev =>
          prev.map(img =>
            img === currentCropImage ? { ...img, file: croppedFile, preview: URL.createObjectURL(croppedBlob) } : img
          )
        );
      }
      setCropModalOpen(false);
      setCurrentCropImage(null);
    } catch (error) {
      console.error('Ошибка при кадрировании:', error);
      showSnackbar('Не удалось обработать изображение', 'error');
    } finally {
      setCropProcessing(false);
    }
  };

  const handleDeleteTempImage = (index: number) => {
    setTempImages(prev => {
      const next = [...prev];
      URL.revokeObjectURL(next[index].preview);
      next.splice(index, 1);
      return next;
    });
  };

  const handleDeleteExistingImage = async (imageId: number) => {
    try {
      await deleteImage.mutateAsync(imageId);
      await refetchImages();
      showSnackbar('Фото удалено', 'success');
    } catch (error: any) {
      showSnackbar(`Не удалось удалить фото: ${error.message}`, 'error');
    }
  };

  const uploadTempImages = async (dishId: number) => {
    if (tempImages.length === 0) return;
    setUploading(true);
    try {
      for (const tempImage of tempImages) {
        await uploadImage.mutateAsync({ dishId, file: tempImage.file });
      }
    } finally {
      setUploading(false);
    }
  };

  const handleSubmit = async () => {
    if (!validateForm()) return;

    const payload: CreateDishDto = {
      ...formData,
      name: formData.name.trim(),
      description: formData.description?.trim() ?? '',
      parentId: kind === 'variant' ? formData.parentId : null,
      // Раздел и метка нужны только карточке на витрине
      category: kind === 'card' ? formData.category?.trim() ?? '' : '',
      badge: kind === 'card' ? formData.badge ?? null : null,
      // Все способы = без ограничений
      availableOrderTypes: kind === 'card' && formData.availableOrderTypes && formData.availableOrderTypes.length < ORDER_TYPES.length
        ? formData.availableOrderTypes
        : [],
    };

    try {
      if (isEditMode) {
        await updateDish.mutateAsync({ id: numericId, ...payload });
        await uploadTempImages(numericId);
        showSnackbar(`«${payload.name}» сохранён`, 'success');
      } else {
        const result = await createDish.mutateAsync(payload);
        await uploadTempImages(result.id);
        showSnackbar(`«${result.name}» добавлен в меню`, 'success');
      }
      setSaved(true);
      setTimeout(() => navigate('/admin/menu'), 900);
    } catch (error: any) {
      const message = error?.response?.data?.message || error?.response?.data?.Message || error.message;
      showSnackbar(`Не удалось сохранить: ${message || 'неизвестная ошибка'}`, 'error');
    }
  };

  const handleDelete = async () => {
    try {
      await deleteDish.mutateAsync(numericId);
      setConfirmDelete(false);
      showSnackbar('Товар удалён', 'success');
      setSaved(true);
      setTimeout(() => navigate('/admin/menu'), 900);
    } catch (error: any) {
      setConfirmDelete(false);
      showSnackbar(`Не удалось удалить: ${error.message}`, 'error');
    }
  };

  if (isLoadingDish) {
    return (
      <Box sx={{ display: 'flex', flexDirection: 'column', justifyContent: 'center', alignItems: 'center', minHeight: '60vh', gap: 2 }}>
        <CircularProgress size={36} />
        <Typography color="text.secondary">Загружаем товар…</Typography>
      </Box>
    );
  }

  const allImages = [
    ...tempImages.map((img, i) => ({
      key: `temp-${i}`,
      preview: img.preview,
      onDelete: () => handleDeleteTempImage(i),
      onCrop: (() => openCropModal(img)) as (() => void) | undefined,
      isPrimary: false,
      isNew: true,
    })),
    ...images.map((image: DishImage) => ({
      key: `img-${image.id}`,
      preview: resolveMediaUrl(image.filePath),
      onDelete: () => handleDeleteExistingImage(image.id),
      onCrop: undefined as (() => void) | undefined,
      isPrimary: image.isPrimary,
      isNew: false,
    })),
  ];
  // Главное фото на витрине: отмеченное главным, иначе первое
  const coverImage = allImages.find(i => i.isPrimary)?.preview
    || (images[0] ? resolveMediaUrl(images[0].filePath) : '')
    || tempImages[0]?.preview;

  const parentCard = parentCards.find(c => c.id === formData.parentId) ?? null;

  const previewItem: Dish = {
    id: 0,
    name: formData.name.trim() || 'Название товара',
    price: formData.price || 0,
    description: formData.description || '',
    isSpicy: !!formData.isSpicy,
    isAvailable: formData.isAvailable ?? true,
    badge: formData.badge ?? null,
    category: formData.category,
    createdAt: '',
    primaryImage: coverImage || undefined,
    children: variants,
  };

  const title = isEditMode
    ? (kind === 'variant' ? 'Вариант товара' : 'Редактирование')
    : (kind === 'variant' ? 'Новый вариант' : 'Новый товар');

  const submitLabel = saved
    ? 'Сохранено'
    : isPending || uploading
      ? 'Сохраняем…'
      : isEditMode ? 'Сохранить' : kind === 'variant' ? 'Добавить вариант' : 'Добавить в меню';

  const actions = (
    <>
      <Button
        variant="contained"
        color="secondary"
        size="large"
        fullWidth
        onClick={handleSubmit}
        disabled={isPending || uploading}
        startIcon={isPending && !saved ? <CircularProgress size={18} color="inherit" /> : undefined}
      >
        {submitLabel}
      </Button>
      <Button
        size="large"
        fullWidth
        onClick={() => navigate('/admin/menu')}
        disabled={isPending}
        sx={{ color: 'text.secondary' }}
      >
        Отмена
      </Button>
    </>
  );

  return (
    <Box sx={{ py: { xs: 2, sm: 3 }, maxWidth: 1120, mx: 'auto', pb: { xs: 14, md: 6 } }}>
      {/* Шапка */}
      <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5, mb: 3 }}>
        <Tooltip title="Назад к меню">
          <IconButton
            onClick={() => navigate('/admin/menu')}
            disabled={isPending}
            aria-label="Назад к меню"
            sx={{ bgcolor: 'background.paper', width: 44, height: 44 }}
          >
            <ArrowBackIcon />
          </IconButton>
        </Tooltip>
        <Box sx={{ minWidth: 0 }}>
          <Typography variant="h4" component="h1" sx={{ fontSize: { xs: '1.6rem', sm: '2rem' } }}>
            {title}
          </Typography>
          {isEditMode && existingDish && (
            <Typography color="text.secondary" noWrap>
              {kind === 'variant' && parentCard ? `${parentCard.name} · ` : ''}{existingDish.name}
            </Typography>
          )}
        </Box>
      </Box>

      <Box
        sx={{
          display: 'grid',
          gridTemplateColumns: { xs: 'minmax(0, 1fr)', md: 'minmax(0, 1fr) 340px' },
          gap: { xs: 2, md: 3 },
          alignItems: 'start',
        }}
      >
        {/* Левая колонка — форма */}
        <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
          {/* Что создаём */}
          <Section
            title="Тип позиции"
            subtitle={kind === 'card'
              ? 'Карточка — то, что гость видит в меню. Размеры и порции добавляются к ней вариантами.'
              : 'Вариант — размер или порция внутри карточки, например «Большая» или «400 г».'}
          >
            <ToggleButtonGroup
              exclusive
              value={kind}
              onChange={handleKindChange}
              disabled={isPending || (isEditMode && variants.length > 0)}
              sx={{ display: 'flex', '& .MuiToggleButton-root': { flex: 1, py: 1.1 } }}
            >
              <ToggleButton value="card">Карточка в меню</ToggleButton>
              <ToggleButton value="variant">Вариант карточки</ToggleButton>
            </ToggleButtonGroup>

            {kind === 'variant' && (
              <Box sx={{ mt: 2.5 }}>
                <Field label="Карточка" htmlFor="field-parentId" required>
                  <Autocomplete
                    options={parentCards}
                    value={parentCard}
                    onChange={(_e, value) => handleChange('parentId', value?.id ?? null)}
                    disabled={isPending}
                    getOptionLabel={option => option.name}
                    groupBy={option => option.category || 'Без раздела'}
                    isOptionEqualToValue={(option, value) => option.id === value.id}
                    noOptionsText="Ничего не найдено"
                    renderInput={params => (
                      <TextField
                        {...params}
                        id="field-parentId"
                        placeholder="Начните вводить название"
                        error={!!errors.parentId}
                        helperText={errors.parentId}
                      />
                    )}
                  />
                </Field>
              </Box>
            )}
          </Section>

          {/* Основное */}
          <Section title="Основное">
            <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2.5 }}>
              <Field label={kind === 'variant' ? 'Название варианта' : 'Название'} htmlFor="field-name" required>
                <TextField
                  id="field-name"
                  fullWidth
                  placeholder={kind === 'variant' ? 'Например: Большая, 400 г, 30 см' : 'Например: Цезарь с курицей'}
                  value={formData.name}
                  onChange={e => handleChange('name', e.target.value)}
                  disabled={isPending}
                  error={!!errors.name}
                  helperText={errors.name}
                  slotProps={{ htmlInput: { maxLength: 100 } }}
                />
              </Field>

              <Box sx={{ display: 'grid', gridTemplateColumns: { xs: 'minmax(0, 1fr)', sm: '200px 1fr' }, gap: 2.5 }}>
                <Field
                  label="Цена"
                  htmlFor="field-price"
                  required={kind === 'variant'}
                  hint={kind === 'card' && !errors.price ? 'Если есть варианты, цена берётся из них' : undefined}
                >
                  <TextField
                    id="field-price"
                    fullWidth
                    type="number"
                    placeholder="0"
                    value={formData.price || ''}
                    onChange={e => handleChange('price', parseFloat(e.target.value) || 0)}
                    disabled={isPending}
                    error={!!errors.price}
                    helperText={errors.price}
                    slotProps={{
                      input: { endAdornment: <InputAdornment position="end">₽</InputAdornment> },
                      htmlInput: { min: 0, step: 10, inputMode: 'numeric' },
                    }}
                  />
                </Field>

                {kind === 'card' && (
                  <Field label="Раздел меню" htmlFor="field-category" hint="По разделам строятся вкладки на витрине">
                    <Autocomplete
                      freeSolo
                      options={menuSections}
                      value={formData.category || ''}
                      inputValue={formData.category || ''}
                      onInputChange={(_e, value) => handleChange('category', value)}
                      disabled={isPending}
                      renderInput={params => (
                        <TextField {...params} id="field-category" placeholder="Например: Салаты" />
                      )}
                    />
                  </Field>
                )}
              </Box>

              <Field
                label={kind === 'variant' ? 'Описание варианта' : 'Описание'}
                htmlFor="field-description"
                required={kind === 'card'}
                hint={!errors.description
                  ? (kind === 'variant' ? 'Необязательно — по умолчанию показывается описание карточки' : 'Состав и особенности блюда')
                  : undefined}
              >
                <TextField
                  id="field-description"
                  fullWidth
                  multiline
                  minRows={3}
                  placeholder="Лаваш, курица, свежие овощи, фирменный соус…"
                  value={formData.description || ''}
                  onChange={e => handleChange('description', e.target.value)}
                  disabled={isPending}
                  error={!!errors.description}
                  helperText={errors.description}
                  slotProps={{ htmlInput: { maxLength: 500 } }}
                />
              </Field>

              {kind === 'card' && (
                <Field label="Метка на карточке">
                  <Box sx={{ display: 'flex', gap: 1, flexWrap: 'wrap' }}>
                    {BADGE_OPTIONS.map(opt => {
                      const selected = (formData.badge ?? null) === opt.value;
                      return (
                        <Box
                          key={opt.label}
                          component="button"
                          type="button"
                          onClick={() => handleChange('badge', opt.value)}
                          disabled={isPending}
                          aria-pressed={selected}
                          sx={{
                            display: 'inline-flex',
                            alignItems: 'center',
                            gap: 0.75,
                            px: 1.75,
                            height: 40,
                            borderRadius: '12px',
                            border: '1.5px solid',
                            borderColor: selected ? 'text.primary' : 'transparent',
                            bgcolor: selected ? 'background.paper' : t.surfaceMuted,
                            color: 'text.primary',
                            font: 'inherit',
                            fontWeight: 600,
                            fontSize: '0.9rem',
                            cursor: 'pointer',
                            transition: 'border-color 0.15s ease, background-color 0.15s ease',
                            '&:hover': { borderColor: selected ? 'text.primary' : t.line },
                            '&:focus-visible': { outline: `2px solid ${theme.palette.text.primary}`, outlineOffset: 2 },
                          }}
                        >
                          {opt.color && (
                            <Box component="span" sx={{ width: 10, height: 10, borderRadius: '50%', bgcolor: opt.color }} />
                          )}
                          {opt.label}
                        </Box>
                      );
                    })}
                  </Box>
                </Field>
              )}
            </Box>
          </Section>

          {/* Свойства */}
          <Section title="Свойства">
            <Box sx={{ mt: -1.5 }}>
              <SettingRow
                title="В наличии"
                description="Выключите, чтобы временно убрать позицию из заказа"
                checked={formData.isAvailable ?? true}
                onChange={v => handleChange('isAvailable', v)}
                disabled={isPending}
              />
              <Divider />
              <SettingRow
                title="Острое"
                description="На карточке появится метка «Острое»"
                checked={!!formData.isSpicy}
                onChange={v => handleChange('isSpicy', v)}
                disabled={isPending}
              />
            </Box>
          </Section>

          {/* Где продаётся */}
          {kind === 'card' ? (
            <Section
              title="Где продаётся"
              subtitle="Например, пиво — только в зале: на доставку его не оформить ни на сайте, ни на кассе"
            >
              <Box sx={{ display: 'flex', gap: 1, flexWrap: 'wrap' }}>
                {ORDER_TYPES.map(type => {
                  const current = formData.availableOrderTypes?.length ? formData.availableOrderTypes : [...ORDER_TYPES];
                  const selected = current.includes(type);
                  return (
                    <Box
                      key={type}
                      component="button"
                      type="button"
                      aria-pressed={selected}
                      disabled={isPending}
                      onClick={() => {
                        const next = selected ? current.filter(t => t !== type) : [...current, type];
                        if (next.length === 0) return; // хотя бы один способ
                        handleChange('availableOrderTypes', next.length === ORDER_TYPES.length ? null : ORDER_TYPES.filter(t => next.includes(t)));
                      }}
                      sx={{
                        display: 'inline-flex', alignItems: 'center', gap: 0.75, px: 1.75, height: 40,
                        borderRadius: '12px', border: '1.5px solid',
                        borderColor: selected ? 'text.primary' : 'transparent',
                        bgcolor: selected ? 'background.paper' : t.surfaceMuted,
                        color: selected ? 'text.primary' : 'text.secondary',
                        font: 'inherit', fontWeight: 600, fontSize: '0.9rem', cursor: 'pointer',
                      }}
                    >
                      {selected ? '✓' : '+'} {type}
                    </Box>
                  );
                })}
              </Box>
              {formData.availableOrderTypes?.length ? (
                <Typography variant="body2" color="text.secondary" sx={{ mt: 1.25 }}>
                  Гости увидят пометку «{describeOrderTypes(formData.availableOrderTypes)}»
                </Typography>
              ) : null}
            </Section>
          ) : parentCard?.availableOrderTypes?.length ? (
            <Alert severity="info">
              Карточка «{parentCard.name}» продаётся {describeOrderTypes(parentCard.availableOrderTypes)} — это действует и на вариант
            </Alert>
          ) : null}

          {/* Варианты карточки */}
          {isEditMode && kind === 'card' && (
            <Section
              title="Варианты"
              subtitle={variants.length === 0
                ? 'Добавьте размеры или порции — гость выберет нужный при заказе'
                : 'Размеры и порции, которые гость выбирает в карточке'}
              action={
                <Button
                  startIcon={<AddIcon />}
                  onClick={() => navigate(`/admin/create?parentId=${numericId}`)}
                  disabled={isPending}
                  sx={{ bgcolor: t.surfaceMuted, flexShrink: 0 }}
                >
                  Добавить
                </Button>
              }
            >
              {variants.length > 0 && (
                <Box sx={{ mx: { xs: -1, sm: -1.5 } }}>
                  {variants.map(v => (
                    <Box
                      key={v.id}
                      component="button"
                      type="button"
                      onClick={() => navigate(`/admin/edit/${v.id}`)}
                      sx={{
                        display: 'flex',
                        alignItems: 'center',
                        gap: 1.5,
                        width: '100%',
                        px: { xs: 1, sm: 1.5 },
                        py: 1.25,
                        border: 0,
                        borderRadius: '14px',
                        bgcolor: 'transparent',
                        color: 'text.primary',
                        font: 'inherit',
                        textAlign: 'left',
                        cursor: 'pointer',
                        '&:hover': { bgcolor: 'action.hover' },
                      }}
                    >
                      <Box
                        sx={{
                          width: 8,
                          height: 8,
                          borderRadius: '50%',
                          flexShrink: 0,
                          bgcolor: v.isAvailable ? tokens.herb : t.line,
                        }}
                        title={v.isAvailable ? 'В наличии' : 'Нет в наличии'}
                      />
                      <Typography sx={{ flex: 1, fontWeight: 500 }} noWrap>{v.name}</Typography>
                      {!v.isAvailable && (
                        <Typography variant="body2" color="text.secondary">нет в наличии</Typography>
                      )}
                      <Typography sx={{ fontWeight: 700, fontVariantNumeric: 'tabular-nums' }}>{v.price} ₽</Typography>
                      <ChevronRightIcon sx={{ color: 'text.secondary' }} />
                    </Box>
                  ))}
                </Box>
              )}
            </Section>
          )}

          {isEditMode && (
            <Box sx={{ display: 'flex', justifyContent: { xs: 'center', md: 'flex-start' } }}>
              <Button
                startIcon={<DeleteIcon />}
                onClick={() => setConfirmDelete(true)}
                disabled={isPending}
                sx={{ color: tokens.chili, '&:hover': { bgcolor: alpha(tokens.chili, 0.08) } }}
              >
                Удалить {kind === 'variant' ? 'вариант' : 'товар'}
              </Button>
            </Box>
          )}
        </Box>

        {/* Правая колонка — фото, предпросмотр, кнопки */}
        <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2, position: { md: 'sticky' }, top: { md: 88 } }}>
          <Section
            title="Фото"
            subtitle={allImages.length > 0 ? 'Первое фото — обложка на витрине' : undefined}
          >
            <input ref={fileInputRef} type="file" hidden multiple accept="image/*" onChange={handleFileSelect} />

            {allImages.length === 0 ? (
              <Box
                {...dropHandlers}
                role="button"
                tabIndex={0}
                onClick={openFilePicker}
                onKeyDown={e => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); openFilePicker(); } }}
                sx={{
                  aspectRatio: '4 / 3',
                  borderRadius: '18px',
                  border: '2px dashed',
                  borderColor: dragOver ? 'text.primary' : t.line,
                  bgcolor: dragOver ? alpha(tokens.saffron, 0.15) : t.surfaceMuted,
                  display: 'flex',
                  flexDirection: 'column',
                  alignItems: 'center',
                  justifyContent: 'center',
                  gap: 1,
                  textAlign: 'center',
                  px: 2,
                  cursor: isPending ? 'default' : 'pointer',
                  outline: 'none',
                  transition: 'all 0.15s ease',
                  '&:hover, &:focus-visible': { borderColor: 'text.secondary' },
                }}
              >
                <AddPhotoIcon sx={{ fontSize: 36, color: 'text.secondary' }} />
                <Typography sx={{ fontWeight: 600 }}>Добавить фото</Typography>
                <Typography variant="body2" color="text.secondary">
                  Перетащите сюда или нажмите. JPG или PNG до 10 МБ
                </Typography>
              </Box>
            ) : (
              <Box
                {...dropHandlers}
                sx={{
                  display: 'grid',
                  gridTemplateColumns: 'repeat(3, 1fr)',
                  gap: 1,
                  borderRadius: '18px',
                  outline: dragOver ? `2px dashed ${theme.palette.text.primary}` : 'none',
                  outlineOffset: 4,
                }}
              >
                {allImages.map((img, index) => (
                  <Box
                    key={img.key}
                    sx={{
                      position: 'relative',
                      aspectRatio: '1',
                      borderRadius: '14px',
                      overflow: 'hidden',
                      bgcolor: t.surfaceMuted,
                      '&:hover .photo-actions, &:focus-within .photo-actions': { opacity: 1 },
                    }}
                  >
                    <Box component="img" src={img.preview} alt="" sx={{ width: '100%', height: '100%', objectFit: 'cover', display: 'block' }} />
                    {(img.isPrimary || (index === 0 && !allImages.some(i => i.isPrimary))) && (
                      <Box
                        sx={{
                          position: 'absolute', left: 6, bottom: 6, px: 0.75, py: 0.125,
                          borderRadius: '6px', bgcolor: tokens.saffron, color: '#21201F',
                          fontSize: '0.68rem', fontWeight: 700,
                        }}
                      >
                        Обложка
                      </Box>
                    )}
                    {img.isNew && (
                      <Box
                        sx={{
                          position: 'absolute', left: 6, top: 6, px: 0.75, py: 0.125,
                          borderRadius: '6px', bgcolor: 'rgba(33,32,31,0.72)', color: '#fff',
                          fontSize: '0.68rem', fontWeight: 600,
                        }}
                      >
                        Новое
                      </Box>
                    )}
                    <Box
                      className="photo-actions"
                      sx={{
                        position: 'absolute', top: 4, right: 4, display: 'flex', flexDirection: 'column', gap: 0.5,
                        opacity: { xs: 1, md: 0 }, transition: 'opacity 0.15s ease',
                      }}
                    >
                      <IconButton
                        size="small"
                        aria-label="Удалить фото"
                        onClick={img.onDelete}
                        disabled={isPending}
                        sx={{ width: 28, height: 28, bgcolor: 'rgba(255,255,255,0.94)', color: tokens.chili, '&:hover': { bgcolor: '#fff' } }}
                      >
                        <CloseIcon sx={{ fontSize: 16 }} />
                      </IconButton>
                      {img.onCrop && (
                        <IconButton
                          size="small"
                          aria-label="Кадрировать"
                          onClick={img.onCrop}
                          disabled={isPending}
                          sx={{ width: 28, height: 28, bgcolor: 'rgba(255,255,255,0.94)', color: '#21201F', '&:hover': { bgcolor: '#fff' } }}
                        >
                          <CropIcon sx={{ fontSize: 15 }} />
                        </IconButton>
                      )}
                    </Box>
                  </Box>
                ))}
                <Box
                  component="button"
                  type="button"
                  onClick={openFilePicker}
                  disabled={isPending}
                  aria-label="Добавить ещё фото"
                  sx={{
                    aspectRatio: '1',
                    borderRadius: '14px',
                    border: '2px dashed',
                    borderColor: t.line,
                    bgcolor: 'transparent',
                    color: 'text.secondary',
                    display: 'grid',
                    placeItems: 'center',
                    cursor: 'pointer',
                    '&:hover': { borderColor: 'text.secondary', color: 'text.primary' },
                  }}
                >
                  {uploading ? <CircularProgress size={22} /> : <AddIcon />}
                </Box>
              </Box>
            )}
          </Section>

          {kind === 'card' ? (
            <Section title="На витрине">
              <Box sx={{ maxWidth: 220, mx: 'auto', pointerEvents: 'none', bgcolor: 'background.default', borderRadius: '26px', p: 0.5 }} aria-hidden>
                <MenuItemCard item={previewItem} onOpen={() => {}} />
              </Box>
            </Section>
          ) : (
            parentCard && (
              <Section title="В карточке">
                <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5 }}>
                  <Box sx={{ width: 56, height: 56, borderRadius: '14px', overflow: 'hidden', bgcolor: t.surfaceMuted, flexShrink: 0, display: 'grid', placeItems: 'center' }}>
                    {parentCard.primaryImage || parentCard.images?.[0]
                      ? <Box component="img" src={resolveMediaUrl(parentCard.primaryImage || parentCard.images?.[0]?.filePath)} alt="" sx={{ width: '100%', height: '100%', objectFit: 'cover' }} />
                      : <Typography aria-hidden sx={{ fontSize: 28 }}>🍽️</Typography>}
                  </Box>
                  <Box sx={{ minWidth: 0 }}>
                    <Typography sx={{ fontWeight: 600 }} noWrap>{parentCard.name}</Typography>
                    <Typography variant="body2" color="text.secondary" noWrap>
                      {formData.name.trim() || 'Вариант'} · {formData.price || 0} ₽
                    </Typography>
                  </Box>
                </Box>
              </Section>
            )
          )}

          {/* Кнопки — на компьютере в колонке */}
          <Box sx={{ display: { xs: 'none', md: 'flex' }, flexDirection: 'column', gap: 1 }}>
            {actions}
          </Box>
        </Box>
      </Box>

      {/* Кнопки — на телефоне закреплены снизу */}
      <Box
        sx={{
          display: { xs: 'flex', md: 'none' },
          position: 'fixed',
          left: 0,
          right: 0,
          bottom: 0,
          zIndex: theme.zIndex.appBar,
          gap: 1,
          px: 2,
          pt: 1.5,
          pb: 'calc(12px + env(safe-area-inset-bottom))',
          bgcolor: 'background.paper',
          borderTop: `1px solid ${t.line}`,
          flexDirection: 'row-reverse',
          '& > .MuiButton-root:last-of-type': { width: 'auto', flexShrink: 0 },
        }}
      >
        {actions}
      </Box>

      {/* Подтверждение удаления */}
      <Dialog open={confirmDelete} onClose={() => !deleteDish.isPending && setConfirmDelete(false)} maxWidth="xs" fullWidth>
        <DialogTitle>Удалить «{formData.name}»?</DialogTitle>
        <DialogContent>
          <Typography color="text.secondary">
            {kind === 'card' && variants.length > 0
              ? `Вместе с карточкой пропадут её варианты (${variants.length}). Отменить это нельзя.`
              : 'Позиция пропадёт из меню. Отменить это нельзя.'}
          </Typography>
        </DialogContent>
        <DialogActions sx={{ px: 3, pb: 3, gap: 1 }}>
          <Button onClick={() => setConfirmDelete(false)} disabled={deleteDish.isPending} sx={{ color: 'text.secondary' }}>
            Отмена
          </Button>
          <Button
            variant="contained"
            color="error"
            onClick={handleDelete}
            disabled={deleteDish.isPending}
            startIcon={deleteDish.isPending ? <CircularProgress size={16} color="inherit" /> : undefined}
          >
            Удалить
          </Button>
        </DialogActions>
      </Dialog>

      {/* Кадрирование */}
      <Dialog
        open={cropModalOpen}
        onClose={() => !cropProcessing && setCropModalOpen(false)}
        maxWidth="sm"
        fullWidth
      >
        <DialogTitle sx={{ display: 'flex', alignItems: 'center', pr: 1.5 }}>
          <Box sx={{ flex: 1 }}>Кадрирование</Box>
          <IconButton aria-label="Закрыть" onClick={() => !cropProcessing && setCropModalOpen(false)}>
            <CloseIcon />
          </IconButton>
        </DialogTitle>
        {currentCropImage && (
          <>
            <Box
              sx={{
                height: { xs: 280, sm: 360 },
                bgcolor: t.surfaceMuted,
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
                overflow: 'hidden',
              }}
            >
              <Box sx={{ transform: `scale(${zoom}) rotate(${rotation}deg)`, transition: 'transform 0.15s ease' }}>
                <ReactCrop crop={crop} onChange={c => setCrop(c)} onComplete={c => setCompletedCrop(c)} aspect={aspect}>
                  <img
                    ref={imgRef}
                    src={currentCropImage.preview}
                    onLoad={onImageLoad}
                    style={{ maxWidth: '100%', maxHeight: 360, display: 'block' }}
                    alt=""
                  />
                </ReactCrop>
              </Box>
            </Box>

            <DialogContent sx={{ pt: '20px !important', display: 'flex', flexDirection: 'column', gap: 2 }}>
              <Box>
                <Typography variant="body2" sx={{ fontWeight: 600, mb: 1 }}>Пропорции</Typography>
                <ToggleButtonGroup
                  exclusive
                  size="small"
                  value={aspect ?? 'free'}
                  onChange={(_, v) => {
                    if (v === null) return;
                    setAspect(v === 'free' ? undefined : v);
                    setCrop({ unit: '%', width: 90, height: 90, x: 5, y: 5 });
                  }}
                  sx={{ display: 'flex', '& .MuiToggleButton-root': { flex: 1 } }}
                >
                  <ToggleButton value={1}>1:1</ToggleButton>
                  <ToggleButton value={4 / 3}>4:3</ToggleButton>
                  <ToggleButton value={16 / 9}>16:9</ToggleButton>
                  <ToggleButton value="free">Свободно</ToggleButton>
                </ToggleButtonGroup>
              </Box>

              <Box sx={{ display: 'grid', gridTemplateColumns: { xs: 'minmax(0, 1fr)', sm: '1fr 1fr' }, gap: 3 }}>
                <Box>
                  <Box sx={{ display: 'flex', justifyContent: 'space-between' }}>
                    <Typography variant="body2" sx={{ fontWeight: 600 }}>Масштаб</Typography>
                    <Typography variant="body2" color="text.secondary" sx={{ fontVariantNumeric: 'tabular-nums' }}>
                      {Math.round(zoom * 100)}%
                    </Typography>
                  </Box>
                  <Slider
                    value={zoom}
                    min={0.5}
                    max={3}
                    step={0.1}
                    onChange={(_, val) => setZoom(val as number)}
                    aria-label="Масштаб"
                  />
                </Box>
                <Box>
                  <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
                    <Typography variant="body2" sx={{ fontWeight: 600 }}>Поворот</Typography>
                    <Box sx={{ display: 'flex', alignItems: 'center' }}>
                      <IconButton size="small" aria-label="Повернуть влево" onClick={() => setRotation(r => (r - 90 + 360) % 360)}>
                        <RotateLeftIcon fontSize="small" />
                      </IconButton>
                      <IconButton size="small" aria-label="Повернуть вправо" onClick={() => setRotation(r => (r + 90) % 360)}>
                        <RotateRightIcon fontSize="small" />
                      </IconButton>
                    </Box>
                  </Box>
                  <Slider
                    value={rotation}
                    min={0}
                    max={359}
                    step={1}
                    onChange={(_, val) => setRotation(val as number)}
                    aria-label="Поворот"
                  />
                </Box>
              </Box>
            </DialogContent>

            <DialogActions sx={{ px: 3, pb: 3, gap: 1 }}>
              <Button onClick={() => setCropModalOpen(false)} disabled={cropProcessing} sx={{ color: 'text.secondary' }}>
                Отмена
              </Button>
              <Button
                variant="contained"
                color="secondary"
                onClick={handleCropApply}
                disabled={cropProcessing || !completedCrop}
                startIcon={cropProcessing ? <CircularProgress size={16} color="inherit" /> : undefined}
              >
                Применить
              </Button>
            </DialogActions>
          </>
        )}
      </Dialog>

      <Snackbar
        open={snackbar.open}
        autoHideDuration={5000}
        onClose={() => setSnackbar(s => ({ ...s, open: false }))}
        anchorOrigin={{ vertical: 'bottom', horizontal: 'center' }}
        sx={{ bottom: { xs: 96, md: 24 } }}
      >
        <Alert
          onClose={() => setSnackbar(s => ({ ...s, open: false }))}
          severity={snackbar.severity}
          variant="filled"
          sx={{ width: '100%' }}
        >
          {snackbar.message}
        </Alert>
      </Snackbar>
    </Box>
  );
};

export default CreateMenuItemPage;
