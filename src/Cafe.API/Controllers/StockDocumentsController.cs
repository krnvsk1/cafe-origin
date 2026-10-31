using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Cafe.API.Data;
using Cafe.API.Models;
using Cafe.API.Services;
using Cafe.API.Authorization;

namespace Cafe.API.Controllers
{
    /// <summary>
    /// Складские документы: приходы по накладным, списания, инвентаризации.
    /// Черновик можно править; остатки меняются только при проведении; проведённый документ отменяется обратными движениями.
    /// </summary>
    [ApiController]
    [Route("api/inventory/documents")]
    [HasPermission(Permissions.InventoryView)]
    public class StockDocumentsController : ControllerBase
    {
        private static readonly string[] WriteOffReasons = { "spoilage", "expired", "staff", "damage", "other" };
        private static readonly string[] AttachmentExtensions = { ".jpg", ".jpeg", ".png", ".webp", ".heic", ".pdf" };

        private readonly ApplicationDbContext _context;
        private readonly IInventoryService _inventory;
        private readonly IWebHostEnvironment _environment;

        public StockDocumentsController(ApplicationDbContext context, IInventoryService inventory, IWebHostEnvironment environment)
        {
            _context = context;
            _inventory = inventory;
            _environment = environment;
        }

        public class LineRequest
        {
            public int StockItemId { get; set; }
            public decimal Quantity { get; set; }
            public decimal Price { get; set; }
            /// <summary>Сумма строки из накладной; если цена не указана — цена = сумма / количество</summary>
            public decimal? Sum { get; set; }
        }

        public class DocumentRequest
        {
            public string Type { get; set; } = StockDocumentTypes.Receipt;
            public string? Number { get; set; }
            public DateTime? Date { get; set; }
            public int? SupplierId { get; set; }
            public string? Reason { get; set; }
            public string? Comment { get; set; }
            public List<LineRequest> Lines { get; set; } = new();
            /// <summary>Сразу провести после сохранения</summary>
            public bool Post { get; set; }
        }

        // GET: api/inventory/documents?type=&status=&supplierId=&from=&to=&search=&page=1&pageSize=30
        [HttpGet]
        public async Task<IActionResult> GetList(
            [FromQuery] string? type,
            [FromQuery] string? status,
            [FromQuery] int? supplierId,
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to,
            [FromQuery] string? search,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 30)
        {
            pageSize = Math.Clamp(pageSize, 1, 100);
            page = Math.Max(page, 1);

            var query = _context.StockDocuments.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(type)) query = query.Where(d => d.Type == type);
            if (!string.IsNullOrWhiteSpace(status)) query = query.Where(d => d.Status == status);
            if (supplierId.HasValue) query = query.Where(d => d.SupplierId == supplierId);
            if (from.HasValue) query = query.Where(d => d.Date >= from.Value.Date);
            if (to.HasValue) query = query.Where(d => d.Date < to.Value.Date.AddDays(1));
            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim().ToLower();
                query = query.Where(d => (d.Number != null && d.Number.ToLower().Contains(s))
                                         || (d.Supplier != null && d.Supplier.Name.ToLower().Contains(s))
                                         || (d.Comment != null && d.Comment.ToLower().Contains(s)));
            }

            var total = await query.CountAsync();
            var docs = await query
                .OrderByDescending(d => d.Date).ThenByDescending(d => d.Id)
                .Skip((page - 1) * pageSize).Take(pageSize)
                .Select(d => new
                {
                    d.Id,
                    d.Type,
                    d.Status,
                    d.Number,
                    d.Date,
                    d.SupplierId,
                    supplierName = d.Supplier != null ? d.Supplier.Name : null,
                    d.Reason,
                    d.Comment,
                    // У черновика сумма считается по строкам
                    total = d.Status == StockDocumentStatuses.Draft && d.Type == StockDocumentTypes.Receipt
                        ? d.Lines.Sum(l => l.Sum)
                        : d.Total,
                    linesCount = d.Lines.Count,
                    d.AttachmentPath,
                    d.CreatedAt,
                    d.PostedAt
                })
                .ToListAsync();

            return Ok(new { items = docs, total, page, pageSize });
        }

        // GET: api/inventory/documents/{id}
        [HttpGet("{id:int}")]
        public async Task<IActionResult> Get(int id)
        {
            var doc = await LoadAsync(id, tracking: false);
            if (doc == null) return NotFound(new { message = "Документ не найден" });
            return Ok(ToDto(doc));
        }

        // GET: api/inventory/documents/inventory-sheet — все позиции с учётными остатками для бланка инвентаризации
        [HttpGet("inventory-sheet")]
        public async Task<IActionResult> InventorySheet([FromQuery] string? category)
        {
            var query = _context.StockItems.AsNoTracking()
                .Where(i => !i.IsArchived && i.Kind == StockItemKinds.Raw);
            if (!string.IsNullOrWhiteSpace(category)) query = query.Where(i => i.Category == category);

            var items = await query
                .OrderBy(i => i.Category).ThenBy(i => i.Name)
                .Select(i => new { i.Id, i.Name, i.Unit, i.Category, i.Quantity, unitCost = i.AvgCost > 0 ? i.AvgCost : i.LastPrice })
                .ToListAsync();
            return Ok(items);
        }

        // POST: api/inventory/documents
        [HasPermission(Permissions.InventoryManage)]
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] DocumentRequest request)
        {
            if (!StockDocumentTypes.All.Contains(request.Type))
                return BadRequest(new { message = "Неизвестный тип документа" });

            var doc = new StockDocument
            {
                Type = request.Type,
                Status = StockDocumentStatuses.Draft,
                CreatedByUserId = CurrentUserId(),
                CreatedAt = DateTime.UtcNow
            };

            var error = await FillAsync(doc, request);
            if (error != null) return BadRequest(new { message = error });

            _context.StockDocuments.Add(doc);
            await _context.SaveChangesAsync();

            if (request.Post)
            {
                var postError = await TryPostAsync(doc);
                if (postError != null)
                    return BadRequest(new { message = postError, id = doc.Id });
            }

            return Ok(ToDto((await LoadAsync(doc.Id, tracking: false))!));
        }

        // PUT: api/inventory/documents/{id} — только черновик
        [HasPermission(Permissions.InventoryManage)]
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] DocumentRequest request)
        {
            var doc = await LoadAsync(id, tracking: true);
            if (doc == null) return NotFound(new { message = "Документ не найден" });
            if (doc.Status != StockDocumentStatuses.Draft)
                return BadRequest(new { message = "Проведённый документ нельзя изменить — отмените его и создайте новый" });

            _context.StockDocumentLines.RemoveRange(doc.Lines);
            doc.Lines.Clear();

            var error = await FillAsync(doc, request);
            if (error != null) return BadRequest(new { message = error });
            await _context.SaveChangesAsync();

            if (request.Post)
            {
                var postError = await TryPostAsync(doc);
                if (postError != null) return BadRequest(new { message = postError, id = doc.Id });
            }

            return Ok(ToDto((await LoadAsync(doc.Id, tracking: false))!));
        }

        // POST: api/inventory/documents/{id}/post — провести
        [HasPermission(Permissions.InventoryManage)]
        [HttpPost("{id:int}/post")]
        public async Task<IActionResult> Post(int id)
        {
            var doc = await LoadAsync(id, tracking: true);
            if (doc == null) return NotFound(new { message = "Документ не найден" });

            var error = await TryPostAsync(doc);
            if (error != null) return BadRequest(new { message = error });
            return Ok(ToDto((await LoadAsync(id, tracking: false))!));
        }

        // POST: api/inventory/documents/{id}/cancel — отменить проведённый
        [HasPermission(Permissions.InventoryManage)]
        [HttpPost("{id:int}/cancel")]
        public async Task<IActionResult> Cancel(int id)
        {
            var doc = await LoadAsync(id, tracking: true);
            if (doc == null) return NotFound(new { message = "Документ не найден" });

            await using var tx = await BeginTransactionAsync();
            try
            {
                await _inventory.CancelDocumentAsync(doc);
                await _context.SaveChangesAsync();
                if (tx != null) await tx.CommitAsync();
            }
            catch (InventoryException ex)
            {
                return BadRequest(new { message = ex.Message });
            }

            return Ok(ToDto((await LoadAsync(id, tracking: false))!));
        }

        // DELETE: api/inventory/documents/{id} — только черновик
        [HasPermission(Permissions.InventoryManage)]
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            var doc = await _context.StockDocuments.FindAsync(id);
            if (doc == null) return NotFound(new { message = "Документ не найден" });
            if (doc.Status != StockDocumentStatuses.Draft)
                return BadRequest(new { message = "Удалить можно только черновик. Проведённый документ — отмените." });

            DeleteAttachmentFile(doc.AttachmentPath);
            _context.StockDocuments.Remove(doc);
            await _context.SaveChangesAsync();
            return NoContent();
        }

        // POST: api/inventory/documents/{id}/attachment — фото или скан накладной
        [HasPermission(Permissions.InventoryManage)]
        [HttpPost("{id:int}/attachment")]
        [RequestSizeLimit(15 * 1024 * 1024)]
        public async Task<IActionResult> UploadAttachment(int id, IFormFile file)
        {
            var doc = await _context.StockDocuments.FindAsync(id);
            if (doc == null) return NotFound(new { message = "Документ не найден" });
            if (file == null || file.Length == 0) return BadRequest(new { message = "Файл не выбран" });

            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!AttachmentExtensions.Contains(ext))
                return BadRequest(new { message = "Поддерживаются фото (JPG, PNG, WEBP, HEIC) и PDF" });

            var folder = Path.Combine(_environment.WebRootPath ?? "wwwroot", "uploads", "invoices");
            Directory.CreateDirectory(folder);
            var fileName = $"{Guid.NewGuid()}{ext}";
            await using (var stream = new FileStream(Path.Combine(folder, fileName), FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            DeleteAttachmentFile(doc.AttachmentPath);
            doc.AttachmentPath = $"/uploads/invoices/{fileName}";
            await _context.SaveChangesAsync();
            return Ok(new { attachmentPath = doc.AttachmentPath });
        }

        // DELETE: api/inventory/documents/{id}/attachment
        [HasPermission(Permissions.InventoryManage)]
        [HttpDelete("{id:int}/attachment")]
        public async Task<IActionResult> DeleteAttachment(int id)
        {
            var doc = await _context.StockDocuments.FindAsync(id);
            if (doc == null) return NotFound(new { message = "Документ не найден" });
            DeleteAttachmentFile(doc.AttachmentPath);
            doc.AttachmentPath = null;
            await _context.SaveChangesAsync();
            return NoContent();
        }

        // ================= Вспомогательное =================

        private async Task<string?> TryPostAsync(StockDocument doc)
        {
            await using var tx = await BeginTransactionAsync();
            try
            {
                await _inventory.PostDocumentAsync(doc);
                await _context.SaveChangesAsync();
                if (tx != null) await tx.CommitAsync();
                return null;
            }
            catch (InventoryException ex)
            {
                // Откатываем изменения в памяти, чтобы черновик остался как был
                foreach (var entry in _context.ChangeTracker.Entries().Where(e => e.State != EntityState.Unchanged).ToList())
                {
                    if (entry.State == EntityState.Added) entry.State = EntityState.Detached;
                    else entry.Reload();
                }
                return ex.Message;
            }
        }

        private async Task<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction?> BeginTransactionAsync() =>
            _context.Database.IsRelational() ? await _context.Database.BeginTransactionAsync() : null;

        private async Task<string?> FillAsync(StockDocument doc, DocumentRequest r)
        {
            if (r.Lines.Count == 0) return "Добавьте хотя бы одну позицию";
            if (r.Lines.Count > 500) return "Слишком много строк в одном документе";

            if (doc.Type == StockDocumentTypes.Receipt && r.SupplierId.HasValue
                && !await _context.Suppliers.AnyAsync(s => s.Id == r.SupplierId))
                return "Поставщик не найден";

            if (doc.Type == StockDocumentTypes.WriteOff && !string.IsNullOrWhiteSpace(r.Reason) && !WriteOffReasons.Contains(r.Reason))
                return "Неизвестная причина списания";

            var ids = r.Lines.Select(l => l.StockItemId).Distinct().ToList();
            var items = await _context.StockItems.AsNoTracking().Where(i => ids.Contains(i.Id)).ToDictionaryAsync(i => i.Id);
            if (items.Count != ids.Count) return "Некоторые позиции не найдены — обновите страницу";

            var semi = items.Values.FirstOrDefault(i => i.Kind == StockItemKinds.Semi);
            if (semi != null)
                return $"«{semi.Name}» — полуфабрикат и не хранится на складе. Укажите сырьё.";

            if (doc.Type == StockDocumentTypes.Inventory && ids.Count != r.Lines.Count)
                return "В инвентаризации каждая позиция должна встречаться один раз";

            doc.Number = string.IsNullOrWhiteSpace(r.Number) ? null : r.Number.Trim();
            doc.Date = (r.Date ?? DateTime.UtcNow);
            doc.SupplierId = doc.Type == StockDocumentTypes.Receipt ? r.SupplierId : null;
            doc.Reason = doc.Type == StockDocumentTypes.WriteOff ? r.Reason : null;
            doc.Comment = string.IsNullOrWhiteSpace(r.Comment) ? null : r.Comment.Trim();

            var order = 0;
            decimal total = 0;
            foreach (var l in r.Lines)
            {
                if (doc.Type == StockDocumentTypes.Inventory ? l.Quantity < 0 : l.Quantity <= 0)
                    return $"Проверьте количество у «{items[l.StockItemId].Name}»";

                var price = l.Price;
                decimal sum;
                if (doc.Type == StockDocumentTypes.Receipt)
                {
                    if (price < 0 || (l.Sum ?? 0) < 0) return $"Проверьте цену у «{items[l.StockItemId].Name}»";
                    if (price == 0 && l.Sum is > 0) price = Math.Round(l.Sum.Value / l.Quantity, 4);
                    sum = l.Sum is > 0 ? Math.Round(l.Sum.Value, 2) : Math.Round(l.Quantity * price, 2);
                    total += sum;
                }
                else
                {
                    // Себестоимость для списания и инвентаризации фиксируется при проведении
                    price = 0;
                    sum = 0;
                }

                doc.Lines.Add(new StockDocumentLine
                {
                    StockItemId = l.StockItemId,
                    Quantity = Math.Round(l.Quantity, 3),
                    Price = price,
                    Sum = sum,
                    SortOrder = order++
                });
            }

            doc.Total = total;
            return null;
        }

        private async Task<StockDocument?> LoadAsync(int id, bool tracking)
        {
            IQueryable<StockDocument> q = _context.StockDocuments
                .Include(d => d.Supplier)
                .Include(d => d.Lines.OrderBy(l => l.SortOrder)).ThenInclude(l => l.StockItem);
            if (!tracking) q = q.AsNoTracking();
            return await q.FirstOrDefaultAsync(d => d.Id == id);
        }

        private int? CurrentUserId() =>
            int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

        private void DeleteAttachmentFile(string? path)
        {
            if (string.IsNullOrEmpty(path) || !path.StartsWith("/uploads/invoices/")) return;
            var full = Path.Combine(_environment.WebRootPath ?? "wwwroot", path.TrimStart('/'));
            try { if (System.IO.File.Exists(full)) System.IO.File.Delete(full); } catch (IOException) { }
        }

        private static object ToDto(StockDocument d) => new
        {
            d.Id,
            d.Type,
            d.Status,
            d.Number,
            d.Date,
            d.SupplierId,
            supplierName = d.Supplier?.Name,
            d.Reason,
            d.Comment,
            total = d.Status == StockDocumentStatuses.Draft && d.Type == StockDocumentTypes.Receipt
                ? d.Lines.Sum(l => l.Sum)
                : d.Total,
            d.AttachmentPath,
            d.CreatedAt,
            d.PostedAt,
            d.CancelledAt,
            lines = d.Lines.OrderBy(l => l.SortOrder).Select(l => new
            {
                l.Id,
                l.StockItemId,
                stockItemName = l.StockItem?.Name,
                unit = l.StockItem?.Unit,
                // Для черновика инвентаризации — текущий учётный остаток
                currentQuantity = l.StockItem?.Quantity,
                l.Quantity,
                l.Price,
                l.Sum,
                l.ExpectedQuantity,
                difference = l.ExpectedQuantity.HasValue ? l.Quantity - l.ExpectedQuantity.Value : (decimal?)null
            })
        };
    }
}
