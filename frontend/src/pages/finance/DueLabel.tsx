import * as React from 'react';
import { alpha } from '@mui/material';
import { Pill } from '../inventory/ui';
import { tokens } from '../../theme';
import { plural } from '../../components/CartPanel';

const DAY_FORMS: [string, string, string] = ['день', 'дня', 'дней'];
const WARNING_COLOR = '#C77700';
// До срока осталось столько дней или меньше — подсвечиваем
const SOON_DAYS = 3;

/** Метка срока: «просрочено на 3 дня», «сегодня», «через 5 дней», «срок не указан» */
export const DueLabel: React.FC<{ dueDate: string | null; daysLeft: number | null; paid: boolean }> = ({ dueDate, daysLeft, paid }) => {
  if (paid) return <Pill color={tokens.herb} bg={alpha(tokens.herb, 0.12)}>оплачено</Pill>;
  if (!dueDate || daysLeft === null) return <Pill>срок не указан</Pill>;
  if (daysLeft < 0) {
    const n = -daysLeft;
    return <Pill color={tokens.chili} bg={alpha(tokens.chili, 0.12)}>просрочено на {n} {plural(n, DAY_FORMS)}</Pill>;
  }
  if (daysLeft === 0) return <Pill color={WARNING_COLOR} bg={alpha(WARNING_COLOR, 0.14)}>сегодня</Pill>;
  if (daysLeft <= SOON_DAYS) return <Pill color={WARNING_COLOR} bg={alpha(WARNING_COLOR, 0.14)}>через {daysLeft} {plural(daysLeft, DAY_FORMS)}</Pill>;
  return <Pill>через {daysLeft} {plural(daysLeft, DAY_FORMS)}</Pill>;
};
