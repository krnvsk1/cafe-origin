using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Cafe.API.Authorization;
using Cafe.API.Data;
using Cafe.API.Extensions;
using Cafe.API.Models;
using Cafe.API.Services.Fiscal;

namespace Cafe.API.Controllers
{
    /// <summary>Онлайн-касса АТОЛ: настройки, смена, чеки по заказам кассы</summary>
    [ApiController]
    [Route("api/fiscal")]
    [HasPermission(Permissions.Pos, Permissions.Settings)]
    public class FiscalController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly FiscalService _fiscal;
        private readonly FiscalSettingsProvider _settings;

        public FiscalController(ApplicationDbContext context, FiscalService fiscal, FiscalSettingsProvider settings)
        {
            _context = context;
            _fiscal = fiscal;
            _settings = settings;
        }

        public class SettingsRequest
        {
            public bool Enabled { get; set; }
            public string Url { get; set; } = string.Empty;
            public string TaxationType { get; set; } = "usnIncome";
            public string Vat { get; set; } = "none";
            public string PaymentObject { get; set; } = "commodity";
            public string? CashierInn { get; set; }
            public bool SendToCustomer { get; set; } = true;
        }

        // GET: api/fiscal/status — настройки; с ?shift=true ещё и состояние смены на кассе
        [HttpGet("status")]
        public async Task<IActionResult> Status([FromQuery] bool shift = false)
        {
            var o = await _settings.GetAsync();
            string? shiftState = null, shiftError = null;
            int? shiftNumber = null;
            if (shift && o.Enabled)
            {
                try { (shiftState, shiftNumber) = await _fiscal.ShiftStatusAsync(o, HttpContext.RequestAborted); }
                catch (FiscalException ex) { shiftError = ex.Message; }
            }
            var since = DateTime.UtcNow.AddDays(-2);
            var failed = await _context.FiscalReceipts.CountAsync(r => r.CreatedAt >= since && r.Status != FiscalStatuses.Done
                && !_context.FiscalReceipts.Any(x => x.OrderId == r.OrderId && x.Kind == r.Kind && x.Status == FiscalStatuses.Done));
            return Ok(new
            {
                o.Enabled, o.Url, o.TaxationType, o.Vat, o.PaymentObject, o.CashierInn, o.SendToCustomer,
                layout = o.Layout ?? ReceiptLayout.Default(),
                shiftState, shiftNumber, shiftError,
                failedReceipts = failed
            });
        }

        // PUT: api/fiscal/settings
        [HttpPut("settings")]
        [HasPermission(Permissions.Settings)]
        public async Task<IActionResult> Save([FromBody] SettingsRequest r)
        {
            var o = Map(r);
            var error = Validate(o);
            if (error != null) return BadRequest(new { message = error });
            o.Layout = (await _settings.GetAsync()).Layout ?? ReceiptLayout.Default();
            await _settings.SaveAsync(o, User.GetUserId());
            return await Status();
        }

        // PUT: api/fiscal/layout — свой текст и QR-код в чеке
        [HttpPut("layout")]
        [HasPermission(Permissions.Settings)]
        public async Task<IActionResult> SaveLayout([FromBody] ReceiptLayout layout)
        {
            var l = layout.Normalized();
            var error = l.Validate();
            if (error != null) return BadRequest(new { message = error });
            var cur = await _settings.GetAsync();
            var o = new FiscalOptions
            {
                Enabled = cur.Enabled, Url = cur.Url, TaxationType = cur.TaxationType, Vat = cur.Vat,
                PaymentObject = cur.PaymentObject, CashierInn = cur.CashierInn, SendToCustomer = cur.SendToCustomer, Layout = l
            };
            await _settings.SaveAsync(o, User.GetUserId());
            return await Status();
        }

        // POST: api/fiscal/layout/sample — напечатать образец (нефискальный документ, в ФН не попадает)
        [HttpPost("layout/sample")]
        [HasPermission(Permissions.Settings)]
        public async Task<IActionResult> Sample([FromBody] ReceiptLayout layout)
        {
            var l = layout.Normalized();
            var error = l.Validate();
            if (error != null) return BadRequest(new { message = error });
            try
            {
                await _fiscal.PrintSampleAsync(l, User.GetUserId(), HttpContext.RequestAborted);
                return Ok(new { ok = true });
            }
            catch (FiscalException ex) { return BadRequest(new { message = ex.Message }); }
        }

        // POST: api/fiscal/settings/test — спросить у кассы состояние смены (без сохранения)
        [HttpPost("settings/test")]
        [HasPermission(Permissions.Settings)]
        public async Task<IActionResult> Test([FromBody] SettingsRequest r)
        {
            var o = Map(r);
            var error = Validate(o);
            if (error != null) return Ok(new { ok = false, message = error });
            try
            {
                var (state, number) = await _fiscal.ShiftStatusAsync(o, HttpContext.RequestAborted);
                return Ok(new { ok = true, message = $"Касса на связи · смена {ShiftText(state, number)}" });
            }
            catch (FiscalException ex) { return Ok(new { ok = false, message = ex.Message }); }
        }

        // POST: api/fiscal/shift/open | close | x
        [HttpPost("shift/{op}")]
        [HasPermission(Permissions.Pos)]
        public async Task<IActionResult> Shift(string op)
        {
            var type = op switch { "open" => "openShift", "close" => "closeShift", "x" => "reportX", _ => null };
            if (type == null) return NotFound();
            try
            {
                await _fiscal.ShiftAsync(type, User.GetUserId(), HttpContext.RequestAborted);
                return await Status(true);
            }
            catch (FiscalException ex) { return BadRequest(new { message = ex.Message }); }
        }

        // POST: api/fiscal/orders/{id}/sell — пробить (или повторить) чек прихода
        [HttpPost("orders/{id:int}/sell")]
        [HasPermission(Permissions.Pos)]
        public Task<IActionResult> Sell(int id) => Print(id, FiscalKinds.Sell);

        // POST: api/fiscal/orders/{id}/return — чек возврата прихода
        [HttpPost("orders/{id:int}/return")]
        [HasPermission(Permissions.Pos)]
        public Task<IActionResult> Return(int id) => Print(id, FiscalKinds.Return);

        private async Task<IActionResult> Print(int id, string kind)
        {
            try
            {
                var r = await _fiscal.PrintAsync(id, kind, User.GetUserId(), HttpContext.RequestAborted);
                return Ok(View(r));
            }
            catch (FiscalException ex) { return BadRequest(new { message = ex.Message }); }
        }

        public static object View(FiscalReceipt r) => new
        {
            r.Id, r.OrderId, r.Kind, r.Status, r.Total, r.Error, r.FiscalDocumentNumber, r.ShiftNumber, r.ReceiptNumber, r.FiscalDateTime, r.ClientContact
        };

        private static FiscalOptions Map(SettingsRequest r) => new()
        {
            Enabled = r.Enabled,
            Url = (r.Url ?? "").Trim(),
            TaxationType = r.TaxationType,
            Vat = r.Vat,
            PaymentObject = r.PaymentObject,
            CashierInn = string.IsNullOrWhiteSpace(r.CashierInn) ? null : r.CashierInn.Trim(),
            SendToCustomer = r.SendToCustomer
        };

        private static string? Validate(FiscalOptions o)
        {
            if (!Uri.TryCreate(o.Url, UriKind.Absolute, out var u) || u.Scheme is not ("http" or "https"))
                return "Адрес кассы — вида http://192.168.1.86:16732 (АТОЛ Web Server)";
            if (!FiscalTasks.TaxationTypes.Contains(o.TaxationType)) return "Выберите систему налогообложения";
            if (!FiscalTasks.VatTypes.Contains(o.Vat)) return "Выберите ставку НДС";
            if (!FiscalTasks.PaymentObjects.Contains(o.PaymentObject)) return "Выберите признак предмета расчёта";
            if (o.CashierInn != null && !(o.CashierInn.All(char.IsDigit) && o.CashierInn.Length == 12)) return "ИНН кассира — 12 цифр";
            return null;
        }

        private static string ShiftText(string state, int? number) => state switch
        {
            "opened" => $"открыта{(number != null ? $" (№ {number})" : "")}",
            "closed" => "закрыта",
            "expired" => "открыта больше 24 часов — закройте её",
            _ => state
        };
    }
}
