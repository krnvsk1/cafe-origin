using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Kumashaurma.API.Authorization;
using Kumashaurma.API.Data;
using Kumashaurma.API.Extensions;
using Kumashaurma.API.Models;
using Kumashaurma.API.Services.Haccp;

namespace Kumashaurma.API.Controllers
{
    /// <summary>Электронные журналы по ХАССП и СанПиН 2.3/2.4.4282-26</summary>
    [ApiController]
    [Route("api/haccp")]
    [HasPermission(Permissions.Haccp, Permissions.HaccpManage)]
    public class HaccpController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly HaccpService _haccp;

        public HaccpController(ApplicationDbContext context, HaccpService haccp)
        {
            _context = context;
            _haccp = haccp;
        }

        public class EntryRequest
        {
            public string Journal { get; set; } = string.Empty;
            public int? ObjectId { get; set; }
            public int? StaffId { get; set; }
            public int? StockDocumentId { get; set; }
            public DateTime? OccurredAt { get; set; }
            public JsonObject? Values { get; set; }
            public string? CorrectiveAction { get; set; }
            /// <summary>Причина исправления (для PUT)</summary>
            public string? Reason { get; set; }

            public HaccpEntryInput ToInput() => new(Journal, ObjectId, StaffId, StockDocumentId, OccurredAt, Values, CorrectiveAction);
        }

        public class ReasonRequest
        {
            public string? Reason { get; set; }
        }

        public class ObjectRequest
        {
            public string Kind { get; set; } = HaccpObjectKinds.Fridge;
            public string Name { get; set; } = string.Empty;
            public string? Location { get; set; }
            public decimal? MinValue { get; set; }
            public decimal? MaxValue { get; set; }
            public decimal? MinHumidity { get; set; }
            public decimal? MaxHumidity { get; set; }
            public int ChecksPerDay { get; set; } = 1;
            public string? Note { get; set; }
            public bool IsActive { get; set; } = true;
            public int SortOrder { get; set; }
        }

        public class StaffRequest
        {
            public string FullName { get; set; } = string.Empty;
            public string? Position { get; set; }
            public string? MedicalBookNumber { get; set; }
            public DateTime? MedicalExamUntil { get; set; }
            public DateTime? HygieneTrainingUntil { get; set; }
            public bool IsActive { get; set; } = true;
            public string? Note { get; set; }
        }

        private async Task<HaccpActor> ActorAsync() =>
            new(User.GetUserId(), await _haccp.UserNameAsync(User.GetUserId()), User.HasPermission(Permissions.HaccpManage));

        // GET: api/haccp/meta — журналы и их графы, оборудование, сотрудники, блюда меню
        [HttpGet("meta")]
        public async Task<IActionResult> Meta()
        {
            var dishes = await _context.Shawarmas.AsNoTracking()
                .Where(s => !_context.Shawarmas.Any(c => c.ParentId == s.Id))
                .Select(s => s.Parent != null ? s.Parent.Name + " · " + s.Name : s.Name)
                .OrderBy(n => n)
                .ToListAsync();
            return Ok(new
            {
                canManage = User.HasPermission(Permissions.HaccpManage),
                journals = HaccpJournals.All,
                objects = await _context.HaccpObjects.AsNoTracking().OrderBy(o => o.Kind).ThenBy(o => o.SortOrder).ThenBy(o => o.Name).ToListAsync(),
                staff = await _context.HaccpStaff.AsNoTracking().OrderByDescending(s => s.IsActive).ThenBy(s => s.FullName).ToListAsync(),
                dishes,
                editWindowHours = HaccpService.OwnEditWindow.TotalHours
            });
        }

        // GET: api/haccp/today — что нужно заполнить сегодня
        [HttpGet("today")]
        public async Task<IActionResult> Today() => Ok(await _haccp.TodayAsync(DateTime.UtcNow));

        // GET: api/haccp/entries?journal=fridge&from=2026-10-01&to=2026-10-28
        [HttpGet("entries")]
        public async Task<IActionResult> Entries([FromQuery] string journal, [FromQuery] DateTime? from, [FromQuery] DateTime? to,
            [FromQuery] int? objectId, [FromQuery] bool deviationsOnly = false)
        {
            if (HaccpJournals.Find(journal) == null) return BadRequest(new { message = "Неизвестный журнал" });
            var (start, end) = Period(from, to);
            var q = _context.HaccpEntries.AsNoTracking().Where(e => e.Journal == journal && e.OccurredAt >= start && e.OccurredAt < end);
            if (objectId.HasValue) q = q.Where(e => e.ObjectId == objectId);
            if (deviationsOnly) q = q.Where(e => e.HasDeviation);
            var list = await q.OrderByDescending(e => e.OccurredAt).Take(1000).ToListAsync();
            var me = User.GetUserId();
            var canManage = User.HasPermission(Permissions.HaccpManage);
            var now = DateTime.UtcNow;
            return Ok(list.Select(e => Dto(e, me, canManage, now)));
        }

        private static object Dto(HaccpEntry e, int? me, bool canManage, DateTime now) => new
        {
            e.Id,
            e.Journal,
            e.ObjectId,
            e.StaffId,
            e.StockDocumentId,
            e.OccurredAt,
            values = JsonNode.Parse(e.Data),
            e.HasDeviation,
            e.Deviation,
            e.CorrectiveAction,
            e.CreatedByName,
            e.CreatedAt,
            e.UpdatedAt,
            e.Version,
            e.IsAnnulled,
            e.AnnulReason,
            late = e.CreatedAt - e.OccurredAt > HaccpService.LateThreshold,
            canEdit = !e.IsAnnulled && (canManage || (e.CreatedByUserId == me && now - e.CreatedAt <= HaccpService.OwnEditWindow))
        };

        // GET: api/haccp/entries/{id}/history — прежние редакции
        [HttpGet("entries/{id:int}/history")]
        public async Task<IActionResult> History(int id)
        {
            var revisions = await _context.HaccpEntryRevisions.AsNoTracking().Where(r => r.EntryId == id).OrderBy(r => r.Version).ToListAsync();
            return Ok(revisions.Select(r => new
            {
                r.Version, r.Action, r.OccurredAt, values = JsonNode.Parse(r.Data), r.CorrectiveAction, r.Reason, r.ChangedByName, r.ChangedAt
            }));
        }

        // POST: api/haccp/entries — одна или несколько записей (например, бракераж всего прихода)
        [HttpPost("entries")]
        public async Task<IActionResult> Create([FromBody] List<EntryRequest> requests)
        {
            try
            {
                var created = await _haccp.CreateAsync(requests.Select(r => r.ToInput()).ToList(), await ActorAsync());
                var now = DateTime.UtcNow;
                return Ok(created.Select(e => Dto(e, User.GetUserId(), User.HasPermission(Permissions.HaccpManage), now)));
            }
            catch (HaccpException ex) { return BadRequest(new { message = ex.Message }); }
        }

        // PUT: api/haccp/entries/{id} — исправить с указанием причины
        [HttpPut("entries/{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] EntryRequest r)
        {
            try
            {
                var e = await _haccp.UpdateAsync(id, r.ToInput(), r.Reason, await ActorAsync());
                return Ok(Dto(e, User.GetUserId(), User.HasPermission(Permissions.HaccpManage), DateTime.UtcNow));
            }
            catch (HaccpException ex) { return BadRequest(new { message = ex.Message }); }
        }

        // POST: api/haccp/entries/{id}/annul — аннулировать (запись остаётся в журнале зачёркнутой)
        [HttpPost("entries/{id:int}/annul")]
        public async Task<IActionResult> Annul(int id, [FromBody] ReasonRequest r)
        {
            try
            {
                var e = await _haccp.AnnulAsync(id, r.Reason, await ActorAsync());
                return Ok(Dto(e, User.GetUserId(), User.HasPermission(Permissions.HaccpManage), DateTime.UtcNow));
            }
            catch (HaccpException ex) { return BadRequest(new { message = ex.Message }); }
        }

        // GET: api/haccp/export?journal=fridge&from=…&to=… — Excel для проверки
        [HttpGet("export")]
        public async Task<IActionResult> Export([FromQuery] string journal, [FromQuery] DateTime? from, [FromQuery] DateTime? to)
        {
            var j = HaccpJournals.Find(journal);
            if (j == null) return BadRequest(new { message = "Неизвестный журнал" });
            var (start, end) = Period(from, to);
            var entries = await _context.HaccpEntries.AsNoTracking()
                .Where(e => e.Journal == journal && e.OccurredAt >= start && e.OccurredAt < end).ToListAsync();
            var objects = await _context.HaccpObjects.AsNoTracking().ToDictionaryAsync(o => o.Id);
            var staff = await _context.HaccpStaff.AsNoTracking().ToDictionaryAsync(s => s.Id);
            var (header, rows) = HaccpService.Table(j, entries, objects, staff);
            var title = new List<string>
            {
                j.Title,
                $"Период: {HaccpJournals.Moscow(start):dd.MM.yyyy} — {HaccpJournals.Moscow(end).AddDays(-1):dd.MM.yyyy}",
                $"Основание: {j.Basis}. Электронный журнал, выгружен {HaccpJournals.Moscow(DateTime.UtcNow):dd.MM.yyyy HH:mm}"
            };
            var bytes = XlsxWriter.Build(new XlsxWriter.Sheet(j.Short, title, header,
                rows.Select(r => ((IReadOnlyList<string>)r.Cells, r.Highlight)).ToList()));
            var file = $"{j.Short} {HaccpJournals.Moscow(start):yyyy-MM-dd}_{HaccpJournals.Moscow(end).AddDays(-1):yyyy-MM-dd}.xlsx";
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", file);
        }

        // GET: api/haccp/receipts?days=14 — приходы со склада для бракеража сырья
        [HttpGet("receipts")]
        public async Task<IActionResult> Receipts([FromQuery] int days = 14)
        {
            var since = HaccpJournals.MoscowDayStartUtc(DateTime.UtcNow).AddDays(-Math.Clamp(days, 1, 90));
            var checkedDocs = await _context.HaccpEntries.AsNoTracking()
                .Where(e => e.Journal == HaccpJournals.Perishable && e.StockDocumentId != null && !e.IsAnnulled)
                .Select(e => e.StockDocumentId!.Value).Distinct().ToListAsync();
            var docs = await _context.StockDocuments.AsNoTracking()
                .Where(d => d.Type == StockDocumentTypes.Receipt && d.Status == StockDocumentStatuses.Posted && d.Date >= since)
                .OrderByDescending(d => d.Date)
                .Select(d => new
                {
                    d.Id,
                    d.Number,
                    d.Date,
                    supplier = d.Supplier != null ? d.Supplier.Name : null,
                    lines = d.Lines.OrderBy(l => l.SortOrder).Select(l => new
                    {
                        name = l.StockItem != null ? l.StockItem.Name : "",
                        unit = l.StockItem != null ? l.StockItem.Unit : "",
                        l.Quantity
                    }).ToList()
                })
                .Take(50)
                .ToListAsync();
            return Ok(docs.Select(d => new { d.Id, d.Number, d.Date, d.supplier, d.lines, isChecked = checkedDocs.Contains(d.Id) }));
        }

        // ================= Оборудование и контрольные точки =================

        [HttpPost("objects")]
        [HasPermission(Permissions.HaccpManage)]
        public async Task<IActionResult> CreateObject([FromBody] ObjectRequest r)
        {
            var o = new HaccpObject();
            var error = FillObject(o, r);
            if (error != null) return BadRequest(new { message = error });
            _context.HaccpObjects.Add(o);
            await _context.SaveChangesAsync();
            return Ok(o);
        }

        [HttpPut("objects/{id:int}")]
        [HasPermission(Permissions.HaccpManage)]
        public async Task<IActionResult> UpdateObject(int id, [FromBody] ObjectRequest r)
        {
            var o = await _context.HaccpObjects.FirstOrDefaultAsync(x => x.Id == id);
            if (o == null) return NotFound(new { message = "Не найдено" });
            if (o.Kind != r.Kind && await _context.HaccpEntries.AnyAsync(e => e.ObjectId == id))
                return BadRequest(new { message = "По этому объекту уже есть записи — тип менять нельзя" });
            var error = FillObject(o, r);
            if (error != null) return BadRequest(new { message = error });
            await _context.SaveChangesAsync();
            return Ok(o);
        }

        [HttpDelete("objects/{id:int}")]
        [HasPermission(Permissions.HaccpManage)]
        public async Task<IActionResult> DeleteObject(int id)
        {
            var o = await _context.HaccpObjects.FirstOrDefaultAsync(x => x.Id == id);
            if (o == null) return NotFound();
            // Записи журнала хранятся — объект с записями только выключаем
            if (await _context.HaccpEntries.AnyAsync(e => e.ObjectId == id)) o.IsActive = false;
            else _context.HaccpObjects.Remove(o);
            await _context.SaveChangesAsync();
            return NoContent();
        }

        private static string? FillObject(HaccpObject o, ObjectRequest r)
        {
            if (!HaccpObjectKinds.All.Contains(r.Kind)) return "Неизвестный тип";
            if (string.IsNullOrWhiteSpace(r.Name)) return "Укажите название";
            if (r.MinValue.HasValue && r.MaxValue.HasValue && r.MinValue > r.MaxValue) return "Нижняя граница больше верхней";
            if (r.MinHumidity.HasValue && r.MaxHumidity.HasValue && r.MinHumidity > r.MaxHumidity) return "Нижняя граница влажности больше верхней";
            if (r.ChecksPerDay is < 1 or > 24) return "Проверок в день — от 1 до 24";
            o.Kind = r.Kind;
            o.Name = r.Name.Trim();
            o.Location = string.IsNullOrWhiteSpace(r.Location) ? null : r.Location.Trim();
            o.MinValue = r.MinValue;
            o.MaxValue = r.MaxValue;
            o.MinHumidity = r.Kind == HaccpObjectKinds.Storage ? r.MinHumidity : null;
            o.MaxHumidity = r.Kind == HaccpObjectKinds.Storage ? r.MaxHumidity : null;
            o.ChecksPerDay = r.ChecksPerDay;
            o.Note = string.IsNullOrWhiteSpace(r.Note) ? null : r.Note.Trim();
            o.IsActive = r.IsActive;
            o.SortOrder = r.SortOrder;
            return null;
        }

        // ================= Сотрудники =================

        [HttpPost("staff")]
        [HasPermission(Permissions.HaccpManage)]
        public async Task<IActionResult> CreateStaff([FromBody] StaffRequest r)
        {
            if (string.IsNullOrWhiteSpace(r.FullName)) return BadRequest(new { message = "Укажите ФИО" });
            var s = new HaccpStaff();
            FillStaff(s, r);
            _context.HaccpStaff.Add(s);
            await _context.SaveChangesAsync();
            return Ok(s);
        }

        [HttpPut("staff/{id:int}")]
        [HasPermission(Permissions.HaccpManage)]
        public async Task<IActionResult> UpdateStaff(int id, [FromBody] StaffRequest r)
        {
            var s = await _context.HaccpStaff.FirstOrDefaultAsync(x => x.Id == id);
            if (s == null) return NotFound(new { message = "Не найдено" });
            if (string.IsNullOrWhiteSpace(r.FullName)) return BadRequest(new { message = "Укажите ФИО" });
            FillStaff(s, r);
            await _context.SaveChangesAsync();
            return Ok(s);
        }

        [HttpDelete("staff/{id:int}")]
        [HasPermission(Permissions.HaccpManage)]
        public async Task<IActionResult> DeleteStaff(int id)
        {
            var s = await _context.HaccpStaff.FirstOrDefaultAsync(x => x.Id == id);
            if (s == null) return NotFound();
            if (await _context.HaccpEntries.AnyAsync(e => e.StaffId == id)) s.IsActive = false;
            else _context.HaccpStaff.Remove(s);
            await _context.SaveChangesAsync();
            return NoContent();
        }

        private static void FillStaff(HaccpStaff s, StaffRequest r)
        {
            s.FullName = r.FullName.Trim();
            s.Position = string.IsNullOrWhiteSpace(r.Position) ? null : r.Position.Trim();
            s.MedicalBookNumber = string.IsNullOrWhiteSpace(r.MedicalBookNumber) ? null : r.MedicalBookNumber.Trim();
            s.MedicalExamUntil = r.MedicalExamUntil.HasValue ? DateTime.SpecifyKind(r.MedicalExamUntil.Value.Date, DateTimeKind.Utc) : null;
            s.HygieneTrainingUntil = r.HygieneTrainingUntil.HasValue ? DateTime.SpecifyKind(r.HygieneTrainingUntil.Value.Date, DateTimeKind.Utc) : null;
            s.IsActive = r.IsActive;
            s.Note = string.IsNullOrWhiteSpace(r.Note) ? null : r.Note.Trim();
        }

        /// <summary>Период в днях по Москве → границы в UTC (по умолчанию — текущий месяц)</summary>
        private static (DateTime Start, DateTime End) Period(DateTime? from, DateTime? to)
        {
            var today = HaccpJournals.Moscow(DateTime.UtcNow).Date;
            var f = (from ?? new DateTime(today.Year, today.Month, 1)).Date;
            var t = (to ?? today).Date;
            if (t < f) (f, t) = (t, f);
            if ((t - f).TotalDays > 400) f = t.AddDays(-400);
            return (DateTime.SpecifyKind(f.AddHours(-3), DateTimeKind.Utc), DateTime.SpecifyKind(t.AddDays(1).AddHours(-3), DateTimeKind.Utc));
        }
    }
}
