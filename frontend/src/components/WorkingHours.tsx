import * as React from 'react';
import { Typography } from '@mui/material';
import { useRestaurantSettings } from '../api/hooks';

/** Часы работы из настроек заведения */
const WorkingHours: React.FC<{ prefix?: string }> = ({ prefix = 'Работаем' }) => {
  const { data } = useRestaurantSettings();
  if (!data) return null;
  return (
    <Typography variant="body2" color="text.secondary">
      {prefix} {data.openTime}–{data.closeTime}
      {!data.isOpenNow && ', сейчас закрыто'}
    </Typography>
  );
};

export default WorkingHours;
