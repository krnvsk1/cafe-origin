import type { RestaurantSettings } from '../types';

// Смещение часового пояса (в минутах) относительно UTC в заданный момент
const tzOffsetMinutes = (timeZone: string, date: Date): number => {
  const parts = new Intl.DateTimeFormat('en-US', {
    timeZone,
    hourCycle: 'h23',
    year: 'numeric', month: '2-digit', day: '2-digit',
    hour: '2-digit', minute: '2-digit', second: '2-digit',
  }).formatToParts(date);
  const get = (type: string) => Number(parts.find(p => p.type === type)?.value ?? 0);
  const asUtc = Date.UTC(get('year'), get('month') - 1, get('day'), get('hour'), get('minute'), get('second'));
  return Math.round((asUtc - date.getTime()) / 60000);
};

// Местное время заведения (год, месяц, день, часы, минуты) → момент UTC
const zonedToUtc = (timeZone: string, y: number, m: number, d: number, hh: number, mm: number): Date => {
  const guess = new Date(Date.UTC(y, m, d, hh, mm));
  const offset = tzOffsetMinutes(timeZone, guess);
  return new Date(guess.getTime() - offset * 60000);
};

const parseHm = (hm: string): [number, number] => {
  const [h, m] = hm.split(':').map(Number);
  return [h || 0, m || 0];
};

export interface ScheduleDay {
  key: string;      // YYYY-MM-DD
  label: string;    // «Сегодня», «Завтра», «пт, 30 окт.»
  slots: { iso: string; label: string }[];
}

/**
 * Доступные дни и 15-минутные слоты для заказа ко времени.
 * Учитывает часовой пояс заведения, часы работы и минимальное время на приготовление.
 */
export const buildScheduleDays = (settings: RestaurantSettings, stepMinutes = 15): ScheduleDay[] => {
  const tz = settings.timeZone || 'Europe/Moscow';
  const now = new Date();
  const earliest = now.getTime() + settings.minLeadMinutes * 60000;
  const [oh, om] = parseHm(settings.openTime);
  const [ch, cm] = parseHm(settings.closeTime);
  const overnight = ch * 60 + cm <= oh * 60 + om;

  const dayFmt = new Intl.DateTimeFormat('ru-RU', { timeZone: tz, weekday: 'short', day: 'numeric', month: 'short' });
  const timeFmt = new Intl.DateTimeFormat('ru-RU', { timeZone: tz, hour: '2-digit', minute: '2-digit' });
  const ymdFmt = new Intl.DateTimeFormat('en-CA', { timeZone: tz, year: 'numeric', month: '2-digit', day: '2-digit' });

  const days: ScheduleDay[] = [];
  for (let i = 0; i <= settings.maxDaysAhead; i++) {
    const ref = new Date(now.getTime() + i * 86400000);
    const [y, m, d] = ymdFmt.format(ref).split('-').map(Number);
    const open = zonedToUtc(tz, y, m - 1, d, oh, om);
    const close = zonedToUtc(tz, y, m - 1, overnight ? d + 1 : d, ch, cm);

    const slots: { iso: string; label: string }[] = [];
    for (let t = open.getTime(); t <= close.getTime(); t += stepMinutes * 60000) {
      if (t < earliest) continue;
      if (t > now.getTime() + settings.maxDaysAhead * 86400000) break;
      const slot = new Date(t);
      slots.push({ iso: slot.toISOString(), label: timeFmt.format(slot) });
    }
    if (slots.length === 0) continue;

    const label = i === 0 ? 'Сегодня' : i === 1 ? 'Завтра' : dayFmt.format(open);
    days.push({ key: `${y}-${String(m).padStart(2, '0')}-${String(d).padStart(2, '0')}`, label, slots });
  }
  return days;
};

/** Человекочитаемое время заказа ко времени, например «пт, 30 окт., 19:30» */
export const formatScheduled = (iso: string, timeZone = 'Europe/Moscow'): string =>
  new Date(iso).toLocaleString('ru-RU', {
    timeZone, weekday: 'short', day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit',
  });
