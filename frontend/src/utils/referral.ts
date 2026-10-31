// Код пригласившего друга из ссылки вида https://сайт/?ref=ABC123
const KEY = 'cafe-ref';

export const captureReferralFromUrl = () => {
  try {
    const params = new URLSearchParams(window.location.search);
    const code = params.get('ref');
    if (code && /^[A-Za-z0-9]{4,20}$/.test(code)) {
      localStorage.setItem(KEY, code.toUpperCase());
      // Убираем ?ref= из адресной строки, чтобы ссылкой не делились повторно
      params.delete('ref');
      const query = params.toString();
      window.history.replaceState(null, '', `${window.location.pathname}${query ? `?${query}` : ''}${window.location.hash}`);
    }
  } catch {
    // localStorage недоступен — не страшно
  }
};

export const getStoredReferral = (): string | null => {
  try { return localStorage.getItem(KEY); } catch { return null; }
};

export const clearStoredReferral = () => {
  try { localStorage.removeItem(KEY); } catch { /* ignore */ }
};
