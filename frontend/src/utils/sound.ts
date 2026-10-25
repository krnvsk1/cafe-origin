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

const scheduleChime = (audio: AudioContext) => {
  const now = audio.currentTime;
  [880, 1320].forEach((freq, i) => {
    const osc = audio.createOscillator();
    const gain = audio.createGain();
    osc.type = 'sine';
    osc.frequency.value = freq;
    gain.gain.setValueAtTime(0.0001, now + i * 0.18);
    gain.gain.exponentialRampToValueAtTime(0.4, now + i * 0.18 + 0.02);
    gain.gain.exponentialRampToValueAtTime(0.0001, now + i * 0.18 + 0.35);
    osc.connect(gain).connect(audio.destination);
    osc.start(now + i * 0.18);
    osc.stop(now + i * 0.18 + 0.4);
  });
};

export const playChime = () => {
  try {
    const audio = getContext();
    if (!audio) return;
    if (audio.state !== 'suspended') {
      scheduleChime(audio);
      return;
    }
    // Контекст на паузе (создан до касания или браузер его приостановил) — пробуем возобновить и только потом играем
    const requestedAt = Date.now();
    audio.resume()
      .then(() => { if (Date.now() - requestedAt < STALE_CHIME_MS) scheduleChime(audio); })
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
