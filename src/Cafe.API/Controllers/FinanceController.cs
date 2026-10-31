using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Cafe.API.Authorization;
using Cafe.API.Data;
using Cafe.API.Extensions;
using Cafe.API.Models;
using Cafe.API.Services.Edo;
using Cafe.API.Services.Finance;

namespace Cafe.API.Controllers
{
    /// <summary>
    /// Финансы: предстоящие платежи поставщикам по входящим накладным СБИС — суммы, сроки, реквизиты, отметка об оплате
    /// </summary>
    [ApiController]
    [Route("api/finance")]
    [HasPermission(Permissions.Finance)]
    public class FinanceController : ControllerBase
    {
        private const int MaxPaymentDelayDays = 365;

        private readonly ApplicationDbContext _context;
        private readonly EdoService _edo;

        public FinanceController(ApplicationDbContext context, EdoService edo)
        {
            _context = context;
            _edo = edo;
        }

        public class PaidRequest
        {
            public bool Paid { get; set; }
        }

        public class SupplierTermsRequest
        {
            [Required, MaxLength(12)]
            public string Inn { get; set; } = string.Empty;
            [MaxLength(300)]
            public string? Name { get; set; }
            /// <summary>Отсрочка, дней; null — убрать</summary>
            [Range(0, MaxPaymentDelayDays)]
            public int? PaymentDelayDays { get; set; }
        }

        public class SupplierRequisitesRequest
        {
            [Required, MaxLength(12)]
            public string Inn { get; set; } = string.Empty;
            [MaxLength(300)]
            public string? Name { get; set; }
            [RegularExpression(@"^\d{20}$", ErrorMessage = "Расчётный счёт — 20 цифр")]
            public string? BankAccount { get; set; }
            [RegularExpression(@"^\d{9}$", ErrorMessage = "БИК — 9 цифр")]
            public string? Bik { get; set; }
            [RegularExpression(@"^\d{20}$", ErrorMessage = "Корр. счёт — 20 цифр")]
            public string? CorrAccount { get; set; }
            [MaxLength(300)]
            public string? BankName { get; set; }
        }

        private record SupplierTerms(int? PaymentDelayDays, bool HasBank);

        private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        // Платёж — любая накладная СБИС, кроме скрытых и тех, что не удалось разобрать
        private IQueryable<EdoDocument> Payables() =>
            _context.EdoDocuments.Where(d => d.Status != EdoStatuses.Ignored && d.Status != EdoStatuses.Error);

        private async Task<Dictionary<string, SupplierTerms>> SupplierTermsByInnAsync(IEnumerable<string?> inns)
        {
            var list = inns.Where(i => !string.IsNullOrEmpty(i)).Distinct().ToList();
            var rows = await _context.Suppliers.AsNoTracking()
                .Where(s => s.Inn != null && list.Contains(s.Inn))
                .Select(s => new { s.Inn, s.PaymentDelayDays, HasBank = s.BankAccount != null })
                .ToListAsync();
            return rows.GroupBy(s => s.Inn!).ToDictionary(g => g.Key, g => new SupplierTerms(g.First().PaymentDelayDays, g.First().HasBank));
        }

        // GET: api/finance/payments?filter=unpaid|paid|all
        [HttpGet("payments")]
        public async Task<IActionResult> Payments([FromQuery] string filter = "unpaid")
        {
            await _edo.BackfillAsync();

            var docs = await Payables().AsNoTracking()
                .Select(d => new
                {
                    d.Id, d.Number, d.Date, d.SupplierInn, d.SupplierName, d.Total, d.VatTotal, d.WithoutVat,
                    d.UpdDueDate, d.PaidAt, d.Status, d.StockDocumentId
                })
                .ToListAsync();
            var terms = await SupplierTermsByInnAsync(docs.Select(d => d.SupplierInn));
            var today = PaymentSchedule.TodayMoscow(DateTime.UtcNow);

            var items = docs.Select(d =>
            {
                var supplierTerms = d.SupplierInn != null && terms.TryGetValue(d.SupplierInn, out var t) ? t : null;
                var due = PaymentSchedule.DueDate(d.UpdDueDate, d.Date, supplierTerms?.PaymentDelayDays);
                return new
                {
                    d.Id, d.Number, d.Date, d.SupplierInn, d.SupplierName, d.Total, d.VatTotal, d.WithoutVat,
                    dueDate = due,
                    dueFromUpd = d.UpdDueDate.HasValue,
                    daysLeft = PaymentSchedule.DaysLeft(due, today),
                    bucket = PaymentSchedule.Bucket(due, d.PaidAt.HasValue, today),
                    d.PaidAt,
                    edoStatus = d.Status,
                    d.StockDocumentId
                };
            }).ToList();

            var unpaid = items.Where(i => i.bucket != PaymentBuckets.Paid).ToList();
            object Sum(string bucket) => new
            {
                count = unpaid.Count(i => i.bucket == bucket),
                total = unpaid.Where(i => i.bucket == bucket).Sum(i => i.Total)
            };

            var shown = filter switch
            {
                "paid" => items.Where(i => i.bucket == PaymentBuckets.Paid).OrderByDescending(i => i.PaidAt),
                "all" => items.OrderBy(i => i.bucket == PaymentBuckets.Paid).ThenBy(i => i.dueDate ?? DateTime.MaxValue),
                // К оплате: сначала с ближайшим сроком, без срока — в конце
                _ => unpaid.OrderBy(i => i.dueDate ?? DateTime.MaxValue).ThenBy(i => i.Date)
            };

            return Ok(new
            {
                today,
                summary = new
                {
                    overdue = Sum(PaymentBuckets.Overdue),
                    week = Sum(PaymentBuckets.ThisWeek),
                    later = Sum(PaymentBuckets.Later),
                    noDue = Sum(PaymentBuckets.NoDueDate),
                    unpaidTotal = unpaid.Sum(i => i.Total)
                },
                items = shown
            });
        }

        // GET: api/finance/payments/{id} — карточка платежа: реквизиты, назначение, срок
        [HttpGet("payments/{id:int}")]
        public async Task<IActionResult> Payment(int id)
        {
            var d = await Payables().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (d == null) return NotFound(new { message = "Платёж не найден" });

            var supplier = d.SupplierInn != null
                ? await _context.Suppliers.AsNoTracking().FirstOrDefaultAsync(s => s.Inn == d.SupplierInn)
                : null;

            UpdDocument? upd = null;
            string? parseError = null;
            try { upd = _edo.Parse(d); }
            catch (Exception ex) when (ex is EdoException or UpdParseException) { parseError = ex.Message; }

            // Реквизиты из этой накладной, иначе запомненные у поставщика из прошлых УПД
            var bank = upd?.SellerBank;
            var bankFromSupplier = false;
            if (bank == null && supplier?.BankAccount != null)
            {
                bank = new UpdBank(supplier.BankAccount, supplier.Bik, supplier.CorrAccount, supplier.BankName);
                bankFromSupplier = true;
            }

            var today = PaymentSchedule.TodayMoscow(DateTime.UtcNow);
            var due = PaymentSchedule.DueDate(d.UpdDueDate, d.Date, supplier?.PaymentDelayDays);

            string? paidBy = null;
            if (d.PaidByUserId.HasValue)
                paidBy = await _context.Users.AsNoTracking().Where(u => u.Id == d.PaidByUserId)
                    .Select(u => ((u.FirstName ?? "") + " " + (u.LastName ?? "")).Trim()).FirstOrDefaultAsync();

            return Ok(new
            {
                d.Id, d.Number, d.Date, d.SupplierInn, supplierKpp = d.SupplierKpp, d.SupplierName, d.Total, d.VatTotal, d.WithoutVat,
                dueDate = due,
                dueFromUpd = d.UpdDueDate.HasValue,
                updDelayDays = upd?.PaymentDelayDays,
                supplierDelayDays = supplier?.PaymentDelayDays,
                daysLeft = PaymentSchedule.DaysLeft(due, today),
                bucket = PaymentSchedule.Bucket(due, d.PaidAt.HasValue, today),
                bank = bank == null ? null : new { account = bank.Account, bik = bank.Bik, corrAccount = bank.CorrAccount, bankName = bank.BankName },
                bankFromSupplier,
                basis = upd?.Basis == null ? null : new { name = upd.Basis.Name, number = upd.Basis.Number, date = upd.Basis.Date },
                vatPhrase = upd != null ? UpdParser.VatPhrase(upd) : null,
                paymentPurpose = upd != null ? UpdParser.PaymentPurpose(upd) : null,
                d.PaidAt,
                paidBy = string.IsNullOrEmpty(paidBy) ? null : paidBy,
                edoStatus = d.Status,
                d.StockDocumentId,
                error = parseError
            });
        }

        // POST: api/finance/payments/{id}/paid — отметить оплату или снять отметку
        [HttpPost("payments/{id:int}/paid")]
        public async Task<IActionResult> MarkPaid(int id, [FromBody] PaidRequest request)
        {
            var d = await Payables().FirstOrDefaultAsync(x => x.Id == id);
            if (d == null) return NotFound(new { message = "Платёж не найден" });

            d.PaidAt = request.Paid ? DateTime.UtcNow : null;
            d.PaidByUserId = request.Paid ? User.GetUserId() : null;
            await _context.SaveChangesAsync();
            return Ok(new { d.Id, d.PaidAt });
        }

        // GET: api/finance/suppliers — поставщики с долгом: к оплате, просрочено, ближайший срок, условия
        [HttpGet("suppliers")]
        public async Task<IActionResult> Suppliers()
        {
            await _edo.BackfillAsync();
            var today = PaymentSchedule.TodayMoscow(DateTime.UtcNow);

            var docs = await Payables().AsNoTracking()
                .Where(d => d.SupplierInn != null)
                .Select(d => new { d.SupplierInn, d.SupplierName, d.Total, d.Date, d.UpdDueDate, d.PaidAt })
                .ToListAsync();
            var suppliers = await _context.Suppliers.AsNoTracking()
                .Where(s => !s.IsArchived && s.Inn != null)
                .Select(s => new { s.Id, s.Inn, s.Name, s.PaymentDelayDays, HasBank = s.BankAccount != null })
                .ToListAsync();
            var byInn = suppliers.GroupBy(s => s.Inn!).ToDictionary(g => g.Key, g => g.First());
            var docsByInn = docs.GroupBy(d => d.SupplierInn!).ToDictionary(g => g.Key, g => g.ToList());

            var items = byInn.Keys.Union(docsByInn.Keys).Select(inn =>
            {
                byInn.TryGetValue(inn, out var supplier);
                var list = docsByInn.TryGetValue(inn, out var l) ? l : new();
                var unpaid = list.Where(d => d.PaidAt == null)
                    .Select(d => (d.Total, Due: PaymentSchedule.DueDate(d.UpdDueDate, d.Date, supplier?.PaymentDelayDays)))
                    .ToList();
                return new
                {
                    inn,
                    supplierId = supplier?.Id,
                    name = supplier?.Name ?? list.Select(d => d.SupplierName).FirstOrDefault(n => n != null) ?? $"ИНН {inn}",
                    paymentDelayDays = supplier?.PaymentDelayDays,
                    hasBank = supplier?.HasBank ?? false,
                    invoices = list.Count,
                    unpaidCount = unpaid.Count,
                    unpaidTotal = unpaid.Sum(u => u.Total),
                    overdueTotal = unpaid.Where(u => u.Due.HasValue && u.Due.Value < today).Sum(u => u.Total),
                    noDueCount = unpaid.Count(u => !u.Due.HasValue),
                    nextDueDate = unpaid.Where(u => u.Due.HasValue && u.Due.Value >= today).Select(u => u.Due).Min()
                };
            })
            // Сначала те, кому мы должны больше всего (просрочка — выше всего)
            .OrderByDescending(x => x.overdueTotal).ThenByDescending(x => x.unpaidTotal).ThenBy(x => x.name)
            .ToList();

            return Ok(items);
        }

        // GET: api/finance/suppliers/{inn} — карточка поставщика: условия, реквизиты, неоплаченные накладные
        [HttpGet("suppliers/{inn}")]
        public async Task<IActionResult> Supplier(string inn)
        {
            var supplier = await _context.Suppliers.AsNoTracking().FirstOrDefaultAsync(s => s.Inn == inn);
            var docs = await Payables().AsNoTracking()
                .Where(d => d.SupplierInn == inn)
                .OrderByDescending(d => d.Date)
                .Select(d => new { d.Id, d.Number, d.Date, d.SupplierName, d.SupplierKpp, d.Total, d.UpdDueDate, d.PaidAt })
                .ToListAsync();
            if (supplier == null && docs.Count == 0) return NotFound(new { message = "Поставщик не найден" });

            var today = PaymentSchedule.TodayMoscow(DateTime.UtcNow);
            var invoices = docs.Select(d =>
            {
                var due = PaymentSchedule.DueDate(d.UpdDueDate, d.Date, supplier?.PaymentDelayDays);
                return new
                {
                    d.Id, d.Number, d.Date, d.Total,
                    dueDate = due,
                    daysLeft = PaymentSchedule.DaysLeft(due, today),
                    bucket = PaymentSchedule.Bucket(due, d.PaidAt.HasValue, today),
                    d.PaidAt
                };
            }).ToList();

            return Ok(new
            {
                inn,
                supplierId = supplier?.Id,
                name = supplier?.Name ?? docs.Select(d => d.SupplierName).FirstOrDefault(n => n != null) ?? $"ИНН {inn}",
                kpp = docs.Select(d => d.SupplierKpp).FirstOrDefault(k => k != null),
                paymentDelayDays = supplier?.PaymentDelayDays,
                bank = new { account = supplier?.BankAccount, bik = supplier?.Bik, corrAccount = supplier?.CorrAccount, bankName = supplier?.BankName },
                unpaidTotal = invoices.Where(i => i.PaidAt == null).Sum(i => i.Total),
                invoices
            });
        }

        // PUT: api/finance/suppliers/requisites — реквизиты вручную (из договора или счёта); пустое поле — очистить
        [HttpPut("suppliers/requisites")]
        public async Task<IActionResult> SaveSupplierRequisites([FromBody] SupplierRequisitesRequest request)
        {
            var supplier = await _edo.FindOrCreateSupplierAsync(request.Inn.Trim(), Clean(request.Name));
            if (supplier == null) return BadRequest(new { message = "Не указан поставщик" });

            supplier.BankAccount = Clean(request.BankAccount);
            supplier.Bik = Clean(request.Bik);
            supplier.CorrAccount = Clean(request.CorrAccount);
            supplier.BankName = Clean(request.BankName);
            await _context.SaveChangesAsync();
            return Ok(new { supplier.Id, supplier.Inn });
        }

        // PUT: api/finance/suppliers/terms — отсрочка поставщика (для накладных, где срок не указан)
        [HttpPut("suppliers/terms")]
        public async Task<IActionResult> SaveSupplierTerms([FromBody] SupplierTermsRequest request)
        {
            var supplier = await _edo.FindOrCreateSupplierAsync(request.Inn.Trim(), request.Name);
            if (supplier == null) return BadRequest(new { message = "Не указан поставщик" });

            supplier.PaymentDelayDays = request.PaymentDelayDays;
            await _context.SaveChangesAsync();
            return Ok(new { supplier.Id, supplier.Inn, supplier.PaymentDelayDays });
        }
    }
}
