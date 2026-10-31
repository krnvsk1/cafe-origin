// Токены гостевых заказов: позволяют следить за заказом без входа.
// Хранятся только на этом устройстве.
const KEY = 'cafe-order-tokens';

const read = (): Record<string, string> => {
  try {
    return JSON.parse(localStorage.getItem(KEY) || '{}');
  } catch {
    return {};
  }
};

export const saveOrderToken = (orderId: number, token?: string | null) => {
  if (!token) return;
  try {
    const all = read();
    all[String(orderId)] = token;
    // Храним не больше 20 последних заказов
    const entries = Object.entries(all).slice(-20);
    localStorage.setItem(KEY, JSON.stringify(Object.fromEntries(entries)));
  } catch {
    // localStorage недоступен — отслеживание будет работать только после входа
  }
};

export const getOrderToken = (orderId: number): string | null => read()[String(orderId)] ?? null;
