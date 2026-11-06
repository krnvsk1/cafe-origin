/**
 * Цифровая клавиатура на телефоне и планшете для числовых полей.
 * У <input type="number"> iOS и Android по умолчанию показывают обычную клавиатуру с рядом цифр —
 * подставляем inputmode: целые (step="1") — цифры, остальные — цифры с запятой, телефон — набор номера.
 * Поле, где inputmode уже задан явно, не трогаем.
 */
const MODES: Record<string, (el: HTMLInputElement) => string> = {
  number: el => (el.step && /^\d+$/.test(el.step) ? 'numeric' : 'decimal'),
  tel: () => 'tel',
};

const apply = (el: Element) => {
  if (!(el instanceof HTMLInputElement)) return;
  const mode = MODES[el.type];
  if (!mode || el.dataset.autoInputmode === 'off') return;
  if (el.hasAttribute('inputmode') && el.dataset.autoInputmode !== '1') return;
  const value = mode(el);
  if (el.getAttribute('inputmode') !== value) el.setAttribute('inputmode', value);
  el.dataset.autoInputmode = '1';
  // Колесо мыши не должно менять число в поле (частая случайная ошибка на ноутбуке)
  if (!el.dataset.noWheel) {
    el.dataset.noWheel = '1';
    el.addEventListener('wheel', () => { if (document.activeElement === el) el.blur(); }, { passive: true });
  }
};

const scan = (root: ParentNode) => root.querySelectorAll('input[type="number"], input[type="tel"]').forEach(apply);

export const installNumericKeyboard = () => {
  if (typeof MutationObserver === 'undefined') return;
  scan(document);
  new MutationObserver(records => {
    for (const r of records) {
      if (r.type === 'attributes') { apply(r.target as Element); continue; }
      r.addedNodes.forEach(n => {
        if (n.nodeType !== 1) return;
        apply(n as Element);
        scan(n as Element);
      });
    }
  }).observe(document.documentElement, { childList: true, subtree: true, attributes: true, attributeFilter: ['type', 'step'] });
};
