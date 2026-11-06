/**
 * Клавиатура только с цифрами на телефоне и планшете.
 * На iPhone раскладки numeric и tel показывают буквы под цифрами («2 ABC»), без букв — только decimal
 * (цифры и запятая). Поэтому всем числовым полям — type="number", type="tel" и полям с inputmode numeric/tel —
 * ставим inputmode="decimal". Отключить для поля: data-auto-inputmode="off".
 */
const DIGITS = 'decimal';

const needsDigits = (el: HTMLInputElement) => {
  if (el.dataset.autoInputmode === 'off') return false;
  if (el.type === 'number' || el.type === 'tel') return true;
  const mode = el.getAttribute('inputmode');
  return mode === 'numeric' || mode === 'tel';
};

const apply = (el: Element) => {
  if (!(el instanceof HTMLInputElement) || !needsDigits(el)) return;
  if (el.getAttribute('inputmode') !== DIGITS) el.setAttribute('inputmode', DIGITS);
  // Колесо мыши не должно менять число в поле (частая случайная ошибка на ноутбуке)
  if (el.type === 'number' && !el.dataset.noWheel) {
    el.dataset.noWheel = '1';
    el.addEventListener('wheel', () => { if (document.activeElement === el) el.blur(); }, { passive: true });
  }
};

const SELECTOR = 'input[type="number"], input[type="tel"], input[inputmode="numeric"], input[inputmode="tel"]';
const scan = (root: ParentNode) => root.querySelectorAll(SELECTOR).forEach(apply);

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
  }).observe(document.documentElement, { childList: true, subtree: true, attributes: true, attributeFilter: ['type', 'inputmode'] });
};
