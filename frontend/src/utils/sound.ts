// Короткий звуковой сигнал без файлов (Web Audio API)
let ctx: AudioContext | null = null;

// Если браузер разрешит звук позже (после касания), сигнал старше этого уже не проигрываем — иначе пачка старых сигналов разом
const STALE_CHIME_MS = 2000;

const getContext = (): AudioContext | null => {
  if (ctx) return ctx;
  const AC = window.AudioContext || (window as unknown as { webkitAudioContext: typeof AudioContext }).webkitAudioContext;
  if (!AC) return null;
  ctx = new AC();
  return ctx;
};

interface Note {
  freq: number;
  /** Начало от старта мелодии, с */
  at: number;
  /** Длительность затухания, с */
  dur: number;
  type?: OscillatorType;
  gain?: number;
}

export type MelodyId = 'chime' | 'bell' | 'triple' | 'marimba' | 'alarm';

/** Мелодии сигнала — синтезируются на лету, без звуковых файлов */
export const MELODIES: Record<MelodyId, { label: string; notes: Note[] }> = {
  chime: {
    label: 'Динь-дон',
    notes: [{ freq: 880, at: 0, dur: 0.35 }, { freq: 1320, at: 0.18, dur: 0.35 }],
  },
  bell: {
    label: 'Колокольчик',
    notes: [
      { freq: 1568, at: 0, dur: 1.1, type: 'triangle', gain: 0.5 },
      { freq: 3136, at: 0, dur: 0.6, gain: 0.12 },
      { freq: 1568, at: 0.45, dur: 1.1, type: 'triangle', gain: 0.5 },
      { freq: 3136, at: 0.45, dur: 0.6, gain: 0.12 },
    ],
  },
  triple: {
    label: 'Тройной сигнал',
    notes: [0, 0.2, 0.4].map(at => ({ freq: 1046, at, dur: 0.14 })),
  },
  marimba: {
    label: 'Мелодия',
    notes: [523, 659, 784, 1046].map((freq, i) => ({ freq, at: i * 0.13, dur: 0.32, type: 'triangle' as const, gain: 0.5 })),
  },
  alarm: {
    label: 'Тревога (громко)',
    notes: [0, 0.14, 0.28, 0.42, 0.56, 0.7].map((at, i) => ({ freq: i % 2 ? 988 : 740, at, dur: 0.12, type: 'square' as const, gain: 0.22 })),
  },
};

const DEFAULT_MELODY: MelodyId = 'chime';
const MELODY_KEY = 'kitchen-melody';

/** Мелодия сигнала новых заказов; хранится на устройстве — у каждого кухонного планшета может быть своя */
export const getMelody = (): MelodyId => {
  try {
    const saved = localStorage.getItem(MELODY_KEY);
    return saved && saved in MELODIES ? (saved as MelodyId) : DEFAULT_MELODY;
  } catch {
    return DEFAULT_MELODY;
  }
};

export const setMelody = (id: MelodyId) => {
  try { localStorage.setItem(MELODY_KEY, id); } catch { /* ignore */ }
};

const DEFAULT_GAIN = 0.4;

const scheduleMelody = (audio: AudioContext, notes: Note[]) => {
  const now = audio.currentTime;
  notes.forEach(({ freq, at, dur, type = 'sine', gain: peak = DEFAULT_GAIN }) => {
    const osc = audio.createOscillator();
    const gain = audio.createGain();
    osc.type = type;
    osc.frequency.value = freq;
    gain.gain.setValueAtTime(0.0001, now + at);
    gain.gain.exponentialRampToValueAtTime(peak, now + at + 0.02);
    gain.gain.exponentialRampToValueAtTime(0.0001, now + at + dur);
    osc.connect(gain).connect(audio.destination);
    osc.start(now + at);
    osc.stop(now + at + dur + 0.05);
  });
};

/** Сыграть мелодию (выбранную на устройстве — getMelody) */
export const playChime = (melody: MelodyId) => {
  try {
    const audio = getContext();
    if (!audio) return;
    const notes = MELODIES[melody].notes;
    if (audio.state !== 'suspended') {
      scheduleMelody(audio, notes);
      return;
    }
    // Контекст на паузе (создан до касания или браузер его приостановил) — пробуем возобновить и только потом играем
    const requestedAt = Date.now();
    audio.resume()
      .then(() => { if (Date.now() - requestedAt < STALE_CHIME_MS) scheduleMelody(audio, notes); })
      .catch(() => { /* звук заблокирован браузером — об этом говорит isAudioBlocked */ });
  } catch {
    // звук недоступен — не критично
  }
};

/** Браузеры разрешают звук только после действия пользователя — «разблокируем» по касанию */
export const unlockAudio = () => {
  try {
    const audio = getContext();
    if (audio?.state === 'suspended') void audio.resume().catch(() => { /* ещё не разрешено */ });
  } catch {
    // ignore
  }
};

/** Звук заблокирован браузером: контекст на паузе до первого касания страницы */
export const isAudioBlocked = (): boolean => {
  try {
    return getContext()?.state === 'suspended';
  } catch {
    return false;
  }
};

/** Подписка на смену состояния звука (для useSyncExternalStore) */
export const subscribeAudioState = (onChange: () => void): (() => void) => {
  try {
    const audio = getContext();
    if (!audio) return () => {};
    audio.addEventListener('statechange', onChange);
    return () => audio.removeEventListener('statechange', onChange);
  } catch {
    return () => {};
  }
};
