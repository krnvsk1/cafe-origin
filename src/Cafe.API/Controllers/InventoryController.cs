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
    /// Склад: номенклатура, поставщики, остатки и журнал движений
    /// </summary>
    [ApiController]
    [Route("api/inventory")]
    [HasPermission(Permissions.InventoryView, Permissions.TechCards)]
    public class InventoryController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IInventoryService _inventory;

        public InventoryController(ApplicationDbContext context, IInventoryService inventory)
        {
            _context = context;
            _inventory = inventory;
        }

        public class StockItemRequest
        {
            public string Name { get; set; } = string.Empty;
            public string Unit { get; set; } = StockUnits.Kg;
            public string? Category { get; set; }
            public string Kind { get; set; } = StockItemKinds.Raw;
            public decimal MinQuantity { get; set; }
            public decimal? LastPrice { get; set; }
            public bool IsArchived { get; set; }
        }

        public class SupplierRequest
        {
            public string Name { get; set; } = string.Empty;
            public string? Inn { get; set; }
            public string? Phone { get; set; }
            public string? ContactPerson { get; set; }
            public string? Notes { get; set; }
            public bool IsArchived { get; set; }
        }

        // ================= Сводка =================

        // GET: api/inventory/summary
        [HttpGet("summary")]
        public async Task<IActionResult> Summary()
        {
            var raw = await _context.StockItems.AsNoTracking()
                .Where(i => !i.IsArchived && i.Kind == StockItemKinds.Raw)
                .Select(i => new { i.Quantity, i.AvgCost, i.LastPrice, i.MinQuantity })
                .ToListAsync();

            var drafts = await _context.StockDocuments.CountAsync(d => d.Status == StockDocumentStatuses.Draft);
            var dishes = await _context.Dishes.CountAsync(s => s.IsAvailable && !s.Children.Any());
            var dishCards = await _context.TechCards.CountAsync(c => c.DishId != null);

            return Ok(new
            {
                itemsCount = raw.Count,
                stockValue = Math.Round(raw.Sum(i => Math.Max(i.Quantity, 0) * (i.AvgCost > 0 ? i.AvgCost : i.LastPrice)), 2),
                lowCount = raw.Count(i => i.MinQuantity > 0 && i.Quantity <= i.MinQuantity),
                negativeCount = raw.Count(i => i.Quantity < 0),
                draftsCount = drafts,
                dishesCount = dishes,
                dishCardsCount = dishCards
            });
        }

        // ================= Номенклатура и остатки =================

        // GET: api/inventory/items?search=&kind=&category=&lowOnly=&includeArchived=
        [HttpGet("items")]
        public async Task<IActionResult> GetItems(
            [FromQuery] string? search,
            [FromQuery] string? kind,
            [FromQuery] string? category,
            [FromQuery] bool lowOnly = false,
            [FromQuery] bool includeArchived = false)
        {
            var query = _context.StockItems.AsNoTracking().AsQueryable();
            if (!includeArchived) query = query.Where(i => !i.IsArchived);
            if (!string.IsNullOrWhiteSpace(kind)) query = query.Where(i => i.Kind == kind);
            if (!string.IsNullOrWhiteSpace(category)) query = query.Where(i => i.Category == category);
            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim().ToLower();
                query = query.Where(i => i.Name.ToLower().Contains(s) || (i.Category != null && i.Category.ToLower().Contains(s)));
            }
            if (lowOnly) query = query.Where(i => i.Kind == StockItemKinds.Raw && i.Quantity <= i.MinQuantity && (i.MinQuantity > 0 || i.Quantity < 0));

            var items = await query.OrderBy(i => i.Category).ThenBy(i => i.Name).ToListAsync();
            var ids = items.Select(i => i.Id).ToList();
            var semiCards = await _context.TechCards.AsNoTracking()
                .Where(c => c.StockItemId != null && ids.Contains(c.StockItemId.Value))
                .Select(c => c.StockItemId!.Value)
                .ToListAsync();

            var calc = items.Any(i => i.Kind == StockItemKinds.Semi) ? await _inventory.LoadCalculatorAsync() : null;

            return Ok(items.Select(i => ToDto(i, semiCards.Contains(i.Id), calc)));
        }

        // GET: api/inventory/items/categories
        [HttpGet("items/categories")]
        public async Task<IActionResult> GetCategories()
        {
            var cats = await _context.StockItems.AsNoTracking()
                .Where(i => i.Category != null && i.Category != "")
                .Select(i => i.Category!)
                .Distinct()
                .OrderBy(c => c)
                .ToListAsync();
            return Ok(cats);
        }

        // GET: api/inventory/items/{id}
        [HttpGet("items/{id:int}")]
        public async Task<IActionResult> GetItem(int id)
        {
            var item = await _context.StockItems.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id);
            if (item == null) return NotFound(new { message = "Позиция не найдена" });
            var hasCard = await _context.TechCards.AnyAsync(c => c.StockItemId == id);
            var calc = item.Kind == StockItemKinds.Semi ? await _inventory.LoadCalculatorAsync() : null;
            return Ok(ToDto(item, hasCard, calc));
        }

        // POST: api/inventory/items
        [HasPermission(Permissions.InventoryManage, Permissions.TechCards)]
        [HttpPost("items")]
        public async Task<IActionResult> CreateItem([FromBody] StockItemRequest request)
        {
            var error = await ValidateItemAsync(request, null);
            if (error != null) return BadRequest(new { message = error });

            var item = new StockItem();
            Fill(item, request);
            _context.StockItems.Add(item);
            await _context.SaveChangesAsync();
            return Ok(ToDto(item, false, null));
        }

        // PUT: api/inventory/items/{id}
        [HasPermission(Permissions.InventoryManage)]
        [HttpPut("items/{id:int}")]
        public async Task<IActionResult> UpdateItem(int id, [FromBody] StockItemRequest request)
        {
            var item = await _context.StockItems.FindAsync(id);
            if (item == null) return NotFound(new { message = "Позиция не найдена" });

            var error = await ValidateItemAsync(request, item);
            if (error != null) return BadRequest(new { message = error });

            Fill(item, request);
            await _context.SaveChangesAsync();
            return Ok(ToDto(item, await _context.TechCards.AnyAsync(c => c.StockItemId == id), null));
        }

        // DELETE: api/inventory/items/{id} — удаляет неиспользованную позицию, иначе отправляет в архив
        [HasPermission(Permissions.InventoryManage)]
        [HttpDelete("items/{id:int}")]
        public async Task<IActionResult> DeleteItem(int id)
        {
            var item = await _context.StockItems.FindAsync(id);
            if (item == null) return NotFound(new { message = "Позиция не найдена" });

            var used = await _context.StockMovements.AnyAsync(m => m.StockItemId == id)
                       || await _context.StockDocumentLines.AnyAsync(l => l.StockItemId == id)
                       || await _context.TechCardLines.AnyAsync(l => l.StockItemId == id);

            if (used)
            {
                item.IsArchived = true;
                await _context.SaveChangesAsync();
                return Ok(new { archived = true, message = "Позиция используется в документах или техкартах — перенесена в архив" });
            }

            _context.StockItems.Remove(item);
            await _context.SaveChangesAsync();
            return Ok(new { archived = false });
        }

        // GET: api/inventory/items/{id}/movements?limit=100
        [HttpGet("items/{id:int}/movements")]
        public async Task<IActionResult> GetMovements(int id, [FromQuery] int limit = 100)
        {
            limit = Math.Clamp(limit, 1, 500);
            var movements = await _context.StockMovements.AsNoTracking()
                .Where(m => m.StockItemId == id)
                .OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id)
                .Take(limit)
                .Select(m => new
                {
                    m.Id,
                    m.Type,
                    m.Quantity,
                    m.UnitCost,
                    m.BalanceAfter,
                    m.DocumentId,
                    m.OrderId,
                    m.Comment,
                    m.CreatedAt
                })
                .ToListAsync();
            return Ok(movements);
        }

        // POST: api/inventory/recalculate — пересчитать остатки по журналу
        [HasPermission(Permissions.InventoryManage)]
        [HttpPost("recalculate")]
        public async Task<IActionResult> Recalculate()
        {
            var changed = await _inventory.RecalculateBalancesAsync();
            await _context.SaveChangesAsync();
            return Ok(new { changed });
        }

        // ================= Поставщики =================

        // GET: api/inventory/suppliers?includeArchived=
        [HttpGet("suppliers")]
        public async Task<IActionResult> GetSuppliers([FromQuery] bool includeArchived = false)
        {
            var query = _context.Suppliers.AsNoTracking().AsQueryable();
            if (!includeArchived) query = query.Where(s => !s.IsArchived);

            var suppliers = await query.OrderBy(s => s.Name).ToListAsync();
            var stats = await _context.StockDocuments.AsNoTracking()
                .Where(d => d.SupplierId != null && d.Type == StockDocumentTypes.Receipt && d.Status == StockDocumentStatuses.Posted)
                .GroupBy(d => d.SupplierId!.Value)
                .Select(g => new { Id = g.Key, Count = g.Count(), Total = g.Sum(d => d.Total), Last = g.Max(d => d.Date) })
                .ToDictionaryAsync(x => x.Id);

            return Ok(suppliers.Select(s =>
            {
                stats.TryGetValue(s.Id, out var st);
                return new
                {
                s.Id,
                s.Name,
                s.Inn,
                s.Phone,
                s.ContactPerson,
                s.Notes,
                s.IsArchived,
                receiptsCount = st?.Count ?? 0,
                receiptsTotal = st?.Total ?? 0m,
                lastReceiptAt = st?.Last
                };
            }));
        }

        // POST: api/inventory/suppliers
        [HasPermission(Permissions.InventoryManage)]
        [HttpPost("suppliers")]
        public async Task<IActionResult> CreateSupplier([FromBody] SupplierRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Name)) return BadRequest(new { message = "Введите название поставщика" });
            var supplier = new Supplier();
            Fill(supplier, request);
            _context.Suppliers.Add(supplier);
            await _context.SaveChangesAsync();
            return Ok(supplier);
        }

        // PUT: api/inventory/suppliers/{id}
        [HasPermission(Permissions.InventoryManage)]
        [HttpPut("suppliers/{id:int}")]
        public async Task<IActionResult> UpdateSupplier(int id, [FromBody] SupplierRequest request)
        {
            var supplier = await _context.Suppliers.FindAsync(id);
            if (supplier == null) return NotFound(new { message = "Поставщик не найден" });
            if (string.IsNullOrWhiteSpace(request.Name)) return BadRequest(new { message = "Введите название поставщика" });
            Fill(supplier, request);
            await _context.SaveChangesAsync();
            return Ok(supplier);
        }

        // DELETE: api/inventory/suppliers/{id} — с накладными уходит в архив
        [HasPermission(Permissions.InventoryManage)]
        [HttpDelete("suppliers/{id:int}")]
        public async Task<IActionResult> DeleteSupplier(int id)
        {
            var supplier = await _context.Suppliers.FindAsync(id);
            if (supplier == null) return NotFound(new { message = "Поставщик не найден" });

            if (await _context.StockDocuments.AnyAsync(d => d.SupplierId == id))
            {
                supplier.IsArchived = true;
                await _context.SaveChangesAsync();
                return Ok(new { archived = true, message = "По поставщику есть накладные — он перенесён в архив" });
            }

            _context.Suppliers.Remove(supplier);
            await _context.SaveChangesAsync();
            return Ok(new { archived = false });
        }

        // ================= Вспомогательное =================

        private async Task<string?> ValidateItemAsync(StockItemRequest r, StockItem? existing)
        {
            if (string.IsNullOrWhiteSpace(r.Name)) return "Введите название";
            if (!StockUnits.All.Contains(r.Unit)) return "Неизвестная единица измерения";
            if (r.Kind != StockItemKinds.Raw && r.Kind != StockItemKinds.Semi) return "Неизвестный тип позиции";
            if (r.MinQuantity < 0) return "Минимальный остаток не может быть отрицательным";

            var name = r.Name.Trim().ToLower();
            var duplicate = await _context.StockItems.AnyAsync(i =>
                i.Name.ToLower() == name && i.Unit == r.Unit && (existing == null || i.Id != existing.Id));
            if (duplicate) return "Позиция с таким названием и единицей уже есть";

            if (existing != null && existing.Kind != r.Kind)
            {
                if (existing.Kind == StockItemKinds.Raw && await _context.StockMovements.AnyAsync(m => m.StockItemId == existing.Id))
                    return "По позиции уже были движения — сделать её полуфабрикатом нельзя. Создайте новую позицию.";
                if (existing.Kind == StockItemKinds.Semi && await _context.TechCards.AnyAsync(c => c.StockItemId == existing.Id))
                    return "Сначала удалите техкарту полуфабриката";
            }
            return null;
        }

        private static void Fill(StockItem item, StockItemRequest r)
        {
            item.Name = r.Name.Trim();
            item.Unit = r.Unit;
            item.Category = string.IsNullOrWhiteSpace(r.Category) ? null : r.Category.Trim();
            item.Kind = r.Kind;
            item.MinQuantity = r.MinQuantity;
            item.IsArchived = r.IsArchived;
            if (r.LastPrice.HasValue && r.LastPrice.Value >= 0) item.LastPrice = r.LastPrice.Value;
        }

        private static void Fill(Supplier s, SupplierRequest r)
        {
            s.Name = r.Name.Trim();
            s.Inn = string.IsNullOrWhiteSpace(r.Inn) ? null : r.Inn.Trim();
            s.Phone = string.IsNullOrWhiteSpace(r.Phone) ? null : r.Phone.Trim();
            s.ContactPerson = string.IsNullOrWhiteSpace(r.ContactPerson) ? null : r.ContactPerson.Trim();
            s.Notes = string.IsNullOrWhiteSpace(r.Notes) ? null : r.Notes.Trim();
            s.IsArchived = r.IsArchived;
        }

        private static object ToDto(StockItem i, bool hasCard, TechCardCalculator? calc)
        {
            var cost = i.Kind == StockItemKinds.Semi && calc != null ? calc.UnitCost(i.Id) : TechCardCalculator.CostOf(i);
            return new
            {
                i.Id,
                i.Name,
                i.Unit,
                i.Category,
                i.Kind,
                i.Quantity,
                i.AvgCost,
                i.LastPrice,
                i.MinQuantity,
                i.IsArchived,
                unitCost = Math.Round(cost, 2),
                value = i.Kind == StockItemKinds.Raw ? Math.Round(Math.Max(i.Quantity, 0) * cost, 2) : 0,
                isLow = i.Kind == StockItemKinds.Raw && (i.Quantity < 0 || (i.MinQuantity > 0 && i.Quantity <= i.MinQuantity)),
                hasTechCard = hasCard
            };
        }
    }
}
