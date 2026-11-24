import { todayMsk } from '../api/haccp';

export interface Period {
  from: string;
  to: string;
}

/** Ключ календарной даты без времени и часового пояса. */
export const dateKey = (iso: string) => iso.slice(0, 10);

const addDays = (date: string, days: number) => {
  const result = new Date(`${date}T12:00:00Z`);
  result.setUTCDate(result.getUTCDate() + days);
  return result.toISOString().slice(0, 10);
};

export const lastDays = (days: number): Period => {
  const to = todayMsk();
  return { from: addDays(to, -(days - 1)), to };
};
