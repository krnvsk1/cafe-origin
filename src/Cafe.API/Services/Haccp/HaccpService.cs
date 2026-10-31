using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Cafe.API.Data;
using Cafe.API.Models;

namespace Cafe.API.Services.Haccp
{
    public class HaccpException : Exception
    {
        public HaccpException(string message) : base(message) { }
    }

    public record HaccpActor(int? UserId, string Name, bool CanManage);

    public record HaccpEntryInput(
        string Journal,
        int? ObjectId,
        int? StaffId,
        int? StockDocumentId,
        DateTime? OccurredAt,
        JsonObject? Values,
        string? CorrectiveAction);

    /// <summary>
    /// Журналы ХАССП: записи с автором и временем, правка и аннулирование — только с причиной и историей,
    /// отклонения и корректирующие действия, задачи «не заполнено сегодня», выгрузка за период.
    /// </summary>
    public class HaccpService
    {
        /// <summary>Своя запись правится без прав ответственного в течение суток после внесения</summary>
        public static readonly TimeSpan OwnEditWindow = TimeSpan.FromHours(24);
        /// <summary>Запись о замере, внесённая позже этого срока, помечается «внесено позже»</summary>
        public static readonly TimeSpan LateThreshold = TimeSpan.FromHours(2);

        private readonly ApplicationDbContext _context;

        public HaccpService(ApplicationDbContext context) => _context = context;

        public async Task<string> UserNameAsync(int? userId)
        {
            if (userId == null) return "—";
            var u = await _context.Users.AsNoTracking().Where(x => x.Id == userId)
                .Select(x => new { x.FirstName, x.LastName, x.UserName, x.PhoneNumber }).FirstOrDefaultAsync();
            if (u == null) return "—";
            var name = $"{u.FirstName} {u.LastName}".Trim();
            return name.Length > 0 ? name : u.UserName ?? u.PhoneNumber ?? "—";
        }

        private async Task<(HaccpJournal Journal, HaccpObject? Obj, HaccpStaff? Staff)> ResolveAsync(string journalKey, int? objectId, int? staffId)
        {
            var journal = HaccpJournals.Find(journalKey) ?? throw new HaccpException("Неизвестный журнал");
            HaccpObject? obj = null;
            if (journal.ObjectKind != null)
            {
                if (objectId == null) throw new HaccpException("Выберите оборудование или точку контроля");
                obj = await _context.HaccpObjects.FirstOrDefaultAsync(o => o.Id == objectId && o.Kind == journal.ObjectKind)
                      ?? throw new HaccpException("Оборудование не найдено");
            }
            HaccpStaff? staff = null;
            if (journal.UsesStaff)
            {
                if (staffId == null) throw new HaccpException("Выберите сотрудника");
                staff = await _context.HaccpStaff.FirstOrDefaultAsync(s => s.Id == staffId) ?? throw new HaccpException("Сотрудник не найден");
            }
            return (journal, obj, staff);
        }

        private static DateTime CheckOccurredAt(DateTime? occurredAt, DateTime now, HaccpActor actor)
        {
            var at = occurredAt.HasValue ? DateTime.SpecifyKind(occurredAt.Value.ToUniversalTime(), DateTimeKind.Utc) : now;
            if (at > now.AddMinutes(10)) throw new HaccpException("Время замера не может быть в будущем");
            if (at < now - OwnEditWindow && !actor.CanManage)
                throw new HaccpException("Запись старше суток может внести только ответственный за журналы");
            return at;
        }

        private static void Apply(HaccpEntry e, HaccpJournals.CheckResult check, string? corrective)
        {
            if (check.Errors.Count > 0) throw new HaccpException(string.Join("; ", check.Errors));
            corrective = string.IsNullOrWhiteSpace(corrective) ? null : corrective.Trim();
            if (check.Deviations.Count > 0 && corrective == null)
                throw new HaccpException($"Отклонение: {string.Join("; ", check.Deviations)}. Укажите корректирующее действие");
            e.Data = HaccpJournals.Serialize(check.Values);
            e.HasDeviation = check.Deviations.Count > 0;
            e.Deviation = e.HasDeviation ? Trunc(string.Join("; ", check.Deviations), 1000) : null;
            e.CorrectiveAction = corrective == null ? null : Trunc(corrective, 1000);
        }

        public async Task<List<HaccpEntry>> CreateAsync(IReadOnlyList<HaccpEntryInput> inputs, HaccpActor actor)
        {
            if (inputs.Count == 0) throw new HaccpException("Нет записей");
            if (inputs.Count > 200) throw new HaccpException("Слишком много записей за раз");
            var now = DateTime.UtcNow;
            var created = new List<HaccpEntry>();
            foreach (var (input, i) in inputs.Select((x, i) => (x, i)))
            {
                try
                {
                    var (journal, obj, staff) = await ResolveAsync(input.Journal, input.ObjectId, input.StaffId);
                    var e = new HaccpEntry
                    {
                        Journal = journal.Key,
                        ObjectId = obj?.Id,
                        StaffId = staff?.Id,
                        StockDocumentId = input.StockDocumentId,
                        OccurredAt = CheckOccurredAt(input.OccurredAt, now, actor),
                        CreatedByUserId = actor.UserId,
                        CreatedByName = Trunc(actor.Name, 200),
                        CreatedAt = now,
                    };
                    Apply(e, HaccpJournals.Check(journal, input.Values, obj, staff, now), input.CorrectiveAction);
                    created.Add(e);
                }
                catch (HaccpException ex) when (inputs.Count > 1)
                {
                    throw new HaccpException($"Строка {i + 1}: {ex.Message}");
                }
            }
            _context.HaccpEntries.AddRange(created);
            await _context.SaveChangesAsync();
            return created;
        }

        private static void EnsureCanChange(HaccpEntry e, HaccpActor actor, DateTime now)
        {
            if (e.IsAnnulled) throw new HaccpException("Запись аннулирована — её нельзя менять");
            if (actor.CanManage) return;
            if (e.CreatedByUserId != actor.UserId) throw new HaccpException("Чужую запись может исправить только ответственный за журналы");
            if (now - e.CreatedAt > OwnEditWindow) throw new HaccpException("Прошло больше суток — исправить может только ответственный за журналы");
        }

        private static HaccpEntryRevision Snapshot(HaccpEntry e, string action, string reason, HaccpActor actor, DateTime now) => new()
        {
            EntryId = e.Id,
            Version = e.Version,
            Action = action,
            OccurredAt = e.OccurredAt,
            Data = e.Data,
            CorrectiveAction = e.CorrectiveAction,
            Reason = Trunc(reason, 500),
            ChangedByUserId = actor.UserId,
            ChangedByName = Trunc(actor.Name, 200),
            ChangedAt = now
        };

        public async Task<HaccpEntry> UpdateAsync(int id, HaccpEntryInput input, string? reason, HaccpActor actor)
        {
            if (string.IsNullOrWhiteSpace(reason)) throw new HaccpException("Укажите причину исправления — она сохранится в истории");
            var now = DateTime.UtcNow;
            var e = await _context.HaccpEntries.FirstOrDefaultAsync(x => x.Id == id) ?? throw new HaccpException("Запись не найдена");
            EnsureCanChange(e, actor, now);

            var (journal, obj, staff) = await ResolveAsync(e.Journal, input.ObjectId ?? e.ObjectId, input.StaffId ?? e.StaffId);
            var revision = Snapshot(e, "edit", reason.Trim(), actor, now);
            e.ObjectId = obj?.Id;
            e.StaffId = staff?.Id;
            e.OccurredAt = input.OccurredAt.HasValue ? CheckOccurredAt(input.OccurredAt, now, actor) : e.OccurredAt;
            Apply(e, HaccpJournals.Check(journal, input.Values, obj, staff, now), input.CorrectiveAction);
            if (revision.Data == e.Data && revision.OccurredAt == e.OccurredAt && revision.CorrectiveAction == e.CorrectiveAction)
                throw new HaccpException("Ничего не изменилось");
            e.Version++;
            e.UpdatedAt = now;
            _context.HaccpEntryRevisions.Add(revision);
            await _context.SaveChangesAsync();
            return e;
        }

        public async Task<HaccpEntry> AnnulAsync(int id, string? reason, HaccpActor actor)
        {
            if (string.IsNullOrWhiteSpace(reason)) throw new HaccpException("Укажите причину аннулирования");
            var now = DateTime.UtcNow;
            var e = await _context.HaccpEntries.FirstOrDefaultAsync(x => x.Id == id) ?? throw new HaccpException("Запись не найдена");
            EnsureCanChange(e, actor, now);
            _context.HaccpEntryRevisions.Add(Snapshot(e, "annul", reason.Trim(), actor, now));
            e.IsAnnulled = true;
            e.AnnulReason = Trunc(reason.Trim(), 500);
            e.Version++;
            e.UpdatedAt = now;
            await _context.SaveChangesAsync();
            return e;
        }

        // ================= Сегодня =================

        /// <param name="Expected">Сколько записей нужно за день по графику</param>
        /// <param name="Due">Сколько из них уже должно быть сделано (время наступило)</param>
        /// <param name="Status">done — всё за день; ok — сделано всё, что уже наступило; partial / todo — есть просроченные; upcoming — время ещё не пришло</param>
        public record TodayTask(string Journal, string Title, int? ObjectId, string? ObjectName, int Expected, int Due, int Done, string Status,
            string? Next, string? Hint);
        public record StaffAlert(int StaffId, string FullName, string What, DateTime Until, bool Expired);
        public record ReceiptTask(int StockDocumentId, string? Number, DateTime Date, string? Supplier, int Lines);
        public record Today(DateTime Date, List<TodayTask> Tasks, List<StaffAlert> Staff, List<ReceiptTask> Receipts, int Deviations);

        public static string TaskStatus(int done, int due, int expected) =>
            done >= expected ? "done"
            : done >= due ? (due == 0 ? "upcoming" : "ok")
            : done > 0 ? "partial" : "todo";

        public async Task<Today> TodayAsync(DateTime nowUtc)
        {
            var dayStart = HaccpJournals.MoscowDayStartUtc(nowUtc);
            var dayEnd = dayStart.AddDays(1);
            var entries = await _context.HaccpEntries.AsNoTracking()
                .Where(e => !e.IsAnnulled && e.OccurredAt >= dayStart && e.OccurredAt < dayEnd)
                .Select(e => new { e.Journal, e.ObjectId, e.StaffId, e.HasDeviation })
                .ToListAsync();
            var objects = await _context.HaccpObjects.AsNoTracking().Where(o => o.IsActive)
                .OrderBy(o => o.SortOrder).ThenBy(o => o.Name).ToListAsync();
            var staff = await _context.HaccpStaff.AsNoTracking().Where(s => s.IsActive).OrderBy(s => s.FullName).ToListAsync();
            var schedule = await HaccpSchedule.LoadAsync(_context);

            var tasks = new List<TodayTask>();
            foreach (var j in HaccpJournals.All)
            {
                var times = schedule.GetValueOrDefault(j.Key) ?? Array.Empty<string>();
                if (times.Length == 0 || j.Frequency == "per_receipt") continue; // журнал вне графика — без напоминаний
                var due = HaccpSchedule.DueCount(times, nowUtc);
                var next = HaccpSchedule.Next(times, nowUtc);
                var jEntries = entries.Where(e => e.Journal == j.Key).ToList();
                switch (j.Frequency)
                {
                    case "per_object":
                        foreach (var o in objects.Where(o => o.Kind == j.ObjectKind))
                        {
                            var done = jEntries.Count(e => e.ObjectId == o.Id);
                            tasks.Add(new TodayTask(j.Key, j.Short, o.Id, o.Name, times.Length, due, done, TaskStatus(done, due, times.Length), next,
                                o.MinValue.HasValue || o.MaxValue.HasValue ? $"норма {HaccpJournals.Range(o.MinValue, o.MaxValue, "°C")}" : null));
                        }
                        break;
                    case "per_staff":
                        if (staff.Count > 0)
                        {
                            var marked = jEntries.Select(e => e.StaffId).Distinct().Count();
                            // Не все выходят каждый день: достаточно отметить тех, кто на смене
                            var done = marked > 0 ? 1 : 0;
                            tasks.Add(new TodayTask(j.Key, j.Short, null, null, 1, Math.Min(due, 1), done, TaskStatus(done, Math.Min(due, 1), 1), next,
                                $"отмечено {marked} из {staff.Count} — отметьте всех, кто вышел на смену"));
                        }
                        break;
                    case "daily":
                    {
                        var done = jEntries.Count;
                        tasks.Add(new TodayTask(j.Key, j.Short, null, null, times.Length, due, done, TaskStatus(done, due, times.Length), next, null));
                        break;
                    }
                }
            }

            var today = HaccpJournals.Moscow(nowUtc).Date;
            var alerts = new List<StaffAlert>();
            foreach (var s in staff)
            {
                void Check(DateTime? until, string what)
                {
                    if (until is { } u && u.Date <= today.AddDays(30))
                        alerts.Add(new StaffAlert(s.Id, s.FullName, what, u, u.Date < today));
                }
                Check(s.MedicalExamUntil, "Медосмотр");
                Check(s.HygieneTrainingUntil, "Гигиеническая аттестация");
                if (s.MedicalExamUntil == null) alerts.Add(new StaffAlert(s.Id, s.FullName, "Не указан срок медосмотра", today, true));
            }

            // Приходы за неделю без бракеража сырья
            var weekAgo = dayStart.AddDays(-7);
            var checkedDocs = await _context.HaccpEntries.AsNoTracking()
                .Where(e => e.Journal == HaccpJournals.Perishable && e.StockDocumentId != null && !e.IsAnnulled)
                .Select(e => e.StockDocumentId!.Value).Distinct().ToListAsync();
            var receipts = await _context.StockDocuments.AsNoTracking()
                .Where(d => d.Type == StockDocumentTypes.Receipt && d.Status == StockDocumentStatuses.Posted && d.Date >= weekAgo && !checkedDocs.Contains(d.Id))
                .OrderByDescending(d => d.Date)
                .Select(d => new ReceiptTask(d.Id, d.Number, d.Date, d.Supplier != null ? d.Supplier.Name : null, d.Lines.Count))
                .Take(20)
                .ToListAsync();

            return new Today(today, tasks, alerts.OrderBy(a => a.Until).ToList(), receipts, entries.Count(e => e.HasDeviation));
        }

        // ================= Выгрузка =================

        /// <summary>Колонки для печати и Excel: дата/время, объект или сотрудник, графы журнала, отклонение, кто внёс</summary>
        public static (List<string> Header, List<(List<string> Cells, bool Highlight)> Rows) Table(
            HaccpJournal journal, IEnumerable<HaccpEntry> entries, IReadOnlyDictionary<int, HaccpObject> objects, IReadOnlyDictionary<int, HaccpStaff> staff)
        {
            var header = new List<string> { "№", "Дата и время" };
            if (journal.ObjectKind != null) header.Add(journal.ObjectKind == HaccpObjectKinds.Ccp ? "Контрольная точка (критический предел)" : "Оборудование / помещение (норма)");
            if (journal.UsesStaff) header.AddRange(new[] { "ФИО работника", "Должность" });
            header.AddRange(journal.Fields.Select(f => f.Unit != null ? $"{f.Label}, {f.Unit}" : f.Label));
            header.AddRange(new[] { "Отклонение", "Корректирующее действие", "Внёс (подпись)", "Внесено" });

            var rows = new List<(List<string>, bool)>();
            var n = 0;
            foreach (var e in entries.OrderBy(e => e.OccurredAt))
            {
                var values = JsonNode.Parse(e.Data) as JsonObject ?? new JsonObject();
                var cells = new List<string> { (++n).ToString(), HaccpJournals.Moscow(e.OccurredAt).ToString("dd.MM.yyyy HH:mm") };
                if (journal.ObjectKind != null)
                {
                    var o = e.ObjectId.HasValue ? objects.GetValueOrDefault(e.ObjectId.Value) : null;
                    cells.Add(o == null ? "" : $"{(o.Location != null ? o.Location + ", " : "")}{o.Name} ({HaccpJournals.Range(o.MinValue, o.MaxValue, "°C")})");
                }
                if (journal.UsesStaff)
                {
                    var s = e.StaffId.HasValue ? staff.GetValueOrDefault(e.StaffId.Value) : null;
                    cells.Add(s?.FullName ?? "");
                    cells.Add(s?.Position ?? "");
                }
                cells.AddRange(journal.Fields.Select(f => HaccpJournals.Display(f, values[f.Key])));
                cells.Add(e.IsAnnulled ? $"АННУЛИРОВАНО: {e.AnnulReason}" : e.Deviation ?? "");
                cells.Add(e.CorrectiveAction ?? "");
                cells.Add(e.CreatedByName ?? "");
                var late = e.CreatedAt - e.OccurredAt > LateThreshold ? " (внесено позже)" : "";
                var edited = e.Version > 1 ? $", ред. {e.Version}" : "";
                cells.Add(HaccpJournals.Moscow(e.CreatedAt).ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture) + late + edited);
                rows.Add((cells, e.HasDeviation || e.IsAnnulled));
            }
            return (header, rows);
        }

        private static string Trunc(string s, int max) => s.Length <= max ? s : s[..max];
    }
}
