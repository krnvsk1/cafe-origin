import { displayValue, fmtMsk, rangeText, type HaccpEntry, type HaccpJournal, type HaccpMeta } from '../../api/haccp';

const esc = (s: string) => s.replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]!));

/**
 * Печатная форма журнала за период (в браузере → «Сохранить как PDF»).
 * Колонки как в выгрузке Excel: по образцам приложений СанПиН + отклонение, корректирующее действие, кто внёс.
 */
export const printJournal = (meta: HaccpMeta, journal: HaccpJournal, entries: HaccpEntry[], from: string, to: string) => {
  const objects = new Map(meta.objects.map(o => [o.id, o]));
  const staff = new Map(meta.staff.map(s => [s.id, s]));
  const header = ['№', 'Дата и время'];
  if (journal.objectKind) header.push(journal.objectKind === 'ccp' ? 'Контрольная точка (предел)' : 'Оборудование / помещение (норма)');
  if (journal.usesStaff) header.push('ФИО работника', 'Должность');
  header.push(...journal.fields.map(f => (f.unit ? `${f.label}, ${f.unit}` : f.label)));
  header.push('Отклонение', 'Корректирующее действие', 'Внёс (подпись)');

  const rows = [...entries].sort((a, b) => a.occurredAt.localeCompare(b.occurredAt)).map((e, i) => {
    const cells = [String(i + 1), fmtMsk(e.occurredAt)];
    if (journal.objectKind) {
      const o = e.objectId ? objects.get(e.objectId) : undefined;
      cells.push(o ? `${o.location ? `${o.location}, ` : ''}${o.name}${o.minValue != null || o.maxValue != null ? ` (${rangeText(o.minValue, o.maxValue)})` : ''}` : '');
    }
    if (journal.usesStaff) {
      const s = e.staffId ? staff.get(e.staffId) : undefined;
      cells.push(s?.fullName ?? '', s?.position ?? '');
    }
    cells.push(...journal.fields.map(f => displayValue(f, e.values[f.key])));
    cells.push(e.isAnnulled ? `АННУЛИРОВАНО: ${e.annulReason ?? ''}` : e.deviation ?? '');
    cells.push(e.correctiveAction ?? '');
    const notes = [e.late ? 'внесено позже' : '', e.version > 1 ? `ред. ${e.version}` : ''].filter(Boolean).join(', ');
    cells.push(`${e.createdByName ?? ''}${notes ? ` (${notes})` : ''}`);
    return { cells, bad: e.hasDeviation, annulled: e.isAnnulled };
  });

  const period = `${from.split('-').reverse().join('.')} — ${to.split('-').reverse().join('.')}`;
  const html = `<!doctype html><html lang="ru"><head><meta charset="utf-8"><title>${esc(journal.title)} ${period}</title>
<style>
  @page { size: A4 landscape; margin: 10mm; }
  body { font: 10px/1.35 Arial, sans-serif; color: #000; margin: 0; }
  h1 { font-size: 15px; margin: 0 0 4px; }
  .sub { margin-bottom: 8px; color: #333; }
  table { border-collapse: collapse; width: 100%; }
  th, td { border: 1px solid #000; padding: 3px 4px; vertical-align: top; text-align: left; }
  th { background: #eee; font-weight: bold; }
  tr { page-break-inside: avoid; }
  tr.bad td { background: #fde2de; }
  tr.annulled td { text-decoration: line-through; color: #666; }
  .foot { margin-top: 10px; display: flex; justify-content: space-between; }
</style></head><body>
<h1>${esc(journal.title)}</h1>
<div class="sub">Период: ${period} · ${esc(journal.basis)} · Электронный журнал, распечатан ${fmtMsk(new Date().toISOString())}</div>
<table><thead><tr>${header.map(h => `<th>${esc(h)}</th>`).join('')}</tr></thead>
<tbody>${rows.length ? rows.map(r => `<tr class="${r.bad ? 'bad' : ''} ${r.annulled ? 'annulled' : ''}">${r.cells.map(c => `<td>${esc(c)}</td>`).join('')}</tr>`).join('')
    : `<tr><td colspan="${header.length}">Записей за период нет</td></tr>`}</tbody></table>
<div class="foot"><span>Ответственный за ведение журнала: ____________________</span><span>Подпись: ____________</span></div>
<script>window.onload = () => { window.print(); };</script>
</body></html>`;
  const w = window.open('', '_blank');
  if (!w) return false;
  w.document.open();
  w.document.write(html);
  w.document.close();
  return true;
};
