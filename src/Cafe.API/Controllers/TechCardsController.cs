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
    /// Технологические карты блюд, добавок и полуфабрикатов, себестоимость и отчёты
    /// </summary>
    [ApiController]
    [Route("api/inventory")]
    [HasPermission(Permissions.InventoryView, Permissions.TechCards)]
    public class TechCardsController : ControllerBase
    {
        private const string DishTarget = "dish";
        private const string AddonTarget = "addon";
        private const string Semi = "semi";

        private readonly ApplicationDbContext _context;
        private readonly IInventoryService _inventory;

        public TechCardsController(ApplicationDbContext context, IInventoryService inventory)
        {
            _context = context;
            _inventory = inventory;
        }

        public class TechCardLineRequest
        {
            /// <summary>Товар или полуфабрикат</summary>
            public int? StockItemId { get; set; }
            /// <summary>…или другое блюдо меню (только в картах блюд и добавок); Gross — доля порции</summary>
            public int? SubDishId { get; set; }
            public decimal Gross { get; set; }
            public decimal LossPercent { get; set; }
        }

        public class TechCardRequest
        {
            public decimal? OutputWeight { get; set; }
            public decimal YieldQuantity { get; set; } = 1;
            public string? Instructions { get; set; }
            public List<TechCardLineRequest> Lines { get; set; } = new();
        }

        // ================= Блюда =================

        // GET: api/inventory/dishes — все продаваемые позиции и добавки с себестоимостью
        [HttpGet("dishes")]
        public async Task<IActionResult> GetDishes()
        {
            var calc = await _inventory.LoadCalculatorAsync();

            var all = await _context.Dishes.AsNoTracking()
                .Select(s => new { s.Id, s.Name, s.Price, s.ParentId, s.Category, s.IsAvailable, s.SortOrder })
                .ToListAsync();
            var byParent = all.Where(s => s.ParentId != null).GroupBy(s => s.ParentId!.Value).ToDictionary(g => g.Key, g => g.ToList());

            var dishes = new List<object>();
            foreach (var card in all.Where(s => s.ParentId == null).OrderBy(s => s.Category).ThenBy(s => s.SortOrder).ThenBy(s => s.Name))
            {
                var parentCard = calc.ForDish(card.Id);
                var children = byParent.GetValueOrDefault(card.Id) ?? new();

                // Продаётся сама карточка (без вариантов) или каждый вариант
                var sellable = children.Count == 0
                    ? new[] { (Id: card.Id, Name: card.Name, Price: card.Price, IsAvailable: card.IsAvailable, IsVariant: false) }
                    : children.OrderBy(c => c.SortOrder).ThenBy(c => c.Price)
                        .Select(c => (c.Id, c.Name, c.Price, c.IsAvailable, IsVariant: true)).ToArray();

                foreach (var s in sellable)
                {
                    var own = calc.ForDish(s.Id);
                    var effective = own ?? (s.IsVariant ? parentCard : null);
                    var cost = effective != null ? calc.CardCost(effective) : (decimal?)null;
                    dishes.Add(new
                    {
                        id = s.Id,
                        cardId = card.Id,
                        name = s.IsVariant ? $"{card.Name} · {s.Name}" : s.Name,
                        dishName = card.Name,
                        variantName = s.IsVariant ? s.Name : null,
                        category = card.Category,
                        price = s.Price,
                        isAvailable = s.IsAvailable && card.IsAvailable,
                        // own — своя карта, parent — общая карта карточки, none — нет
                        techCard = own != null ? "own" : effective != null ? "parent" : "none",
                        cost,
                        margin = cost.HasValue ? s.Price - cost.Value : (decimal?)null,
                        marginPercent = cost.HasValue && s.Price > 0 ? Math.Round((s.Price - cost.Value) / s.Price * 100, 1) : (decimal?)null,
                        foodCostPercent = cost.HasValue && s.Price > 0 ? Math.Round(cost.Value / s.Price * 100, 1) : (decimal?)null
                    });
                }
            }

            var addons = await _context.Addons.AsNoTracking()
                .Include(a => a.Category)
                .OrderBy(a => a.Category.DisplayOrder).ThenBy(a => a.DisplayOrder).ThenBy(a => a.Name)
                .Select(a => new { a.Id, a.Name, a.Price, a.IsAvailable, CategoryName = a.Category.Name })
                .ToListAsync();

            var addonDtos = addons.Select(a =>
            {
                var card = calc.ForAddon(a.Id);
                var cost = card != null ? calc.CardCost(card) : (decimal?)null;
                return new
                {
                    a.Id,
                    a.Name,
                    category = a.CategoryName,
                    a.Price,
                    a.IsAvailable,
                    techCard = card != null ? "own" : "none",
                    cost,
                    margin = cost.HasValue ? a.Price - cost.Value : (decimal?)null,
                    foodCostPercent = cost.HasValue && a.Price > 0 ? Math.Round(cost.Value / a.Price * 100, 1) : (decimal?)null
                };
            });

            return Ok(new { dishes, addons = addonDtos });
        }

        // ================= Техкарты =================

        // GET: api/inventory/techcards/{target}/{id}   target: dish | addon | semi
        [HttpGet("techcards/{target}/{id:int}")]
        public async Task<IActionResult> GetCard(string target, int id)
        {
            var owner = await ResolveOwnerAsync(target, id);
            if (owner == null) return NotFound(new { message = "Позиция не найдена" });

            var calc = await _inventory.LoadCalculatorAsync();
            var card = target switch
            {
                DishTarget => calc.ForDish(id),
                AddonTarget => calc.ForAddon(id),
                _ => calc.ForSemi(id)
            };

            // У варианта без своей карты показываем карту карточки как подсказку
            TechCard? inherited = null;
            if (card == null && target == DishTarget && owner.ParentId.HasValue)
                inherited = calc.ForDish(owner.ParentId.Value);

            return Ok(new
            {
                target,
                ownerId = id,
                ownerName = owner.Name,
                parentId = owner.ParentId,
                price = owner.Price,
                unit = owner.Unit,
                exists = card != null,
                card = card != null ? CardDto(card, calc, owner.Price) : null,
                inherited = inherited != null ? CardDto(inherited, calc, owner.Price) : null
            });
        }

        // PUT: api/inventory/techcards/{target}/{id} — создать или заменить
        [HasPermission(Permissions.TechCards)]
        [HttpPut("techcards/{target}/{id:int}")]
        public async Task<IActionResult> SaveCard(string target, int id, [FromBody] TechCardRequest request)
        {
            var owner = await ResolveOwnerAsync(target, id);
            if (owner == null) return NotFound(new { message = "Позиция не найдена" });

            if (request.Lines.Count == 0) return BadRequest(new { message = "Добавьте хотя бы один ингредиент" });
            if (request.Lines.Count > 100) return BadRequest(new { message = "Слишком много ингредиентов" });
            if (request.Lines.Any(l => l.StockItemId.HasValue == l.SubDishId.HasValue))
                return BadRequest(new { message = "В каждой строке выберите либо товар/полуфабрикат, либо блюдо" });
            if (request.Lines.Any(l => l.Gross <= 0)) return BadRequest(new { message = "Укажите вес брутто у всех ингредиентов" });
            if (request.Lines.Any(l => l.LossPercent < 0 || l.LossPercent >= 100)) return BadRequest(new { message = "Потери должны быть от 0 до 99 %" });
            if (target == Semi && request.YieldQuantity <= 0) return BadRequest(new { message = "Укажите выход полуфабриката" });
            if (target == Semi && request.Lines.Any(l => l.SubDishId.HasValue))
                return BadRequest(new { message = "Полуфабрикат собирается из товаров и других полуфабрикатов — блюдо в него добавить нельзя" });

            var ids = request.Lines.Where(l => l.StockItemId.HasValue).Select(l => l.StockItemId!.Value).Distinct().ToList();
            var known = await _context.StockItems.AsNoTracking().Where(i => ids.Contains(i.Id)).Select(i => i.Id).ToListAsync();
            if (known.Count != ids.Count) return BadRequest(new { message = "Некоторые ингредиенты не найдены — обновите страницу" });

            var dishIds = request.Lines.Where(l => l.SubDishId.HasValue).Select(l => l.SubDishId!.Value).Distinct().ToList();
            var knownDishes = await _context.Dishes.AsNoTracking().Where(d => dishIds.Contains(d.Id)).Select(d => d.Id).ToListAsync();
            if (knownDishes.Count != dishIds.Count) return BadRequest(new { message = "Некоторые блюда из состава не найдены — обновите страницу" });

            var calc = await _inventory.LoadCalculatorAsync();
            if (target == Semi)
            {
                if (ids.Contains(id)) return BadRequest(new { message = "Полуфабрикат не может входить сам в себя" });
                if (calc.CreatesCycle(id, ids))
                    return BadRequest(new { message = "Получается замкнутый круг: один из полуфабрикатов уже содержит этот" });
            }
            if (target == DishTarget && dishIds.Count > 0)
            {
                if (dishIds.Contains(id)) return BadRequest(new { message = "Блюдо не может входить само в себя" });
                if (calc.CreatesDishCycle(id, dishIds))
                    return BadRequest(new { message = "Получается замкнутый круг: одно из блюд в составе уже содержит это блюдо" });
            }

            var card = await _context.TechCards.Include(c => c.Lines).FirstOrDefaultAsync(c =>
                target == DishTarget ? c.DishId == id : target == AddonTarget ? c.AddonId == id : c.StockItemId == id);

            if (card == null)
            {
                card = new TechCard
                {
                    DishId = target == DishTarget ? id : null,
                    AddonId = target == AddonTarget ? id : null,
                    StockItemId = target == Semi ? id : null
                };
                _context.TechCards.Add(card);
            }
            else
            {
                _context.TechCardLines.RemoveRange(card.Lines);
                card.Lines.Clear();
            }

            card.OutputWeight = request.OutputWeight is > 0 ? request.OutputWeight : null;
            card.YieldQuantity = target == Semi ? request.YieldQuantity : 1;
            card.Instructions = string.IsNullOrWhiteSpace(request.Instructions) ? null : request.Instructions.Trim();
            card.UpdatedAt = DateTime.UtcNow;

            var order = 0;
            foreach (var l in request.Lines)
            {
                card.Lines.Add(new TechCardLine
                {
                    StockItemId = l.StockItemId,
                    SubDishId = l.SubDishId,
                    Gross = Math.Round(l.Gross, 4),
                    LossPercent = l.SubDishId.HasValue ? 0 : Math.Round(l.LossPercent, 2),
                    SortOrder = order++
                });
            }

            await _context.SaveChangesAsync();
            return await GetCard(target, id);
        }

        // DELETE: api/inventory/techcards/{target}/{id}
        [HasPermission(Permissions.TechCards)]
        [HttpDelete("techcards/{target}/{id:int}")]
        public async Task<IActionResult> DeleteCard(string target, int id)
        {
            if (target != DishTarget && target != AddonTarget && target != Semi) return NotFound();
            var card = await _context.TechCards.FirstOrDefaultAsync(c =>
                target == DishTarget ? c.DishId == id : target == AddonTarget ? c.AddonId == id : c.StockItemId == id);
            if (card == null) return NotFound(new { message = "Техкарты нет" });

            if (target == Semi && await _context.TechCardLines.AnyAsync(l => l.StockItemId == id))
                return BadRequest(new { message = "Полуфабрикат используется в других техкартах — сначала уберите его оттуда" });

            _context.TechCards.Remove(card);
            await _context.SaveChangesAsync();
            return NoContent();
        }

        // ================= Отчёты =================

        // GET: api/inventory/reports/sales?from=&to= — продажи, себестоимость и маржа по блюдам
        [HttpGet("reports/sales")]
        public async Task<IActionResult> SalesReport([FromQuery] DateTime? from, [FromQuery] DateTime? to)
        {
            var (start, end) = Period(from, to);
            var calc = await _inventory.LoadCalculatorAsync();

            var items = await _context.OrderItems.AsNoTracking()
                .Where(i => i.Order!.CreatedAt >= start && i.Order.CreatedAt < end && i.Order.Status != OrderStatuses.Cancelled)
                .Select(i => new
                {
                    i.DishId,
                    i.Name,
                    i.Quantity,
                    i.Weight,
                    i.Price,
                    ParentId = _context.Dishes.Where(s => s.Id == i.DishId).Select(s => s.ParentId).FirstOrDefault(),
                    Addons = i.SelectedAddons.Select(a => new { a.AddonId, a.Quantity, a.Price }).ToList()
                })
                .ToListAsync();

            // Весовые позиции разного веса — одна строка отчёта
            var rows = items
                .GroupBy(i => new { i.DishId, Name = i.Weight.HasValue ? Weighing.StripWeight(i.Name) : i.Name })
                .Select(g =>
                {
                    var first = g.First();
                    var card = calc.ForDish(first.DishId, first.ParentId);
                    var unitCost = card != null ? calc.CardCost(card) : (decimal?)null;
                    var qty = g.Sum(i => i.Quantity);
                    var revenue = g.Sum(i => i.Price * i.Quantity);
                    var addonsRevenue = g.Sum(i => i.Addons.Sum(a => a.Price * a.Quantity) * i.Quantity);
                    var addonsCost = g.Sum(i => i.Addons.Sum(a =>
                    {
                        var ac = calc.ForAddon(a.AddonId);
                        return ac != null ? calc.CardCost(ac) * a.Quantity : 0;
                    }) * i.Quantity);
                    var portions = g.Sum(i => Weighing.Portions(i.Quantity, i.Weight));
                    var cost = unitCost.HasValue ? unitCost.Value * portions + addonsCost : (decimal?)null;
                    var totalRevenue = revenue + addonsRevenue;
                    return new
                    {
                        dishId = first.DishId,
                        name = g.Key.Name,
                        quantity = qty,
                        revenue = Math.Round(totalRevenue, 2),
                        cost = cost.HasValue ? Math.Round(cost.Value, 2) : (decimal?)null,
                        margin = cost.HasValue ? Math.Round(totalRevenue - cost.Value, 2) : (decimal?)null,
                        foodCostPercent = cost.HasValue && totalRevenue > 0 ? Math.Round(cost.Value / totalRevenue * 100, 1) : (decimal?)null,
                        hasTechCard = card != null
                    };
                })
                .OrderByDescending(r => r.revenue)
                .ToList();

            var withCard = rows.Where(r => r.cost.HasValue).ToList();
            return Ok(new
            {
                from = start,
                to = end.AddDays(-1),
                rows,
                totals = new
                {
                    revenue = rows.Sum(r => r.revenue),
                    revenueWithCards = withCard.Sum(r => r.revenue),
                    cost = withCard.Sum(r => r.cost ?? 0),
                    margin = withCard.Sum(r => r.margin ?? 0),
                    foodCostPercent = withCard.Sum(r => r.revenue) > 0
                        ? Math.Round(withCard.Sum(r => r.cost ?? 0) / withCard.Sum(r => r.revenue) * 100, 1)
                        : (decimal?)null,
                    withoutCardCount = rows.Count(r => !r.hasTechCard),
                    withoutCardQuantity = rows.Where(r => !r.hasTechCard).Sum(r => r.quantity)
                }
            });
        }

        // GET: api/inventory/reports/consumption?from=&to= — движение по каждой позиции за период:
        // приход, расход по продажам (по техкартам), списания и расхождения инвентаризации
        [HttpGet("reports/consumption")]
        public async Task<IActionResult> ConsumptionReport([FromQuery] DateTime? from, [FromQuery] DateTime? to)
        {
            var (start, end) = Period(from, to);

            var grouped = await _context.StockMovements.AsNoTracking()
                .Where(m => m.CreatedAt >= start && m.CreatedAt < end)
                .GroupBy(m => new { m.StockItemId, m.Type })
                .Select(g => new
                {
                    g.Key.StockItemId,
                    g.Key.Type,
                    Quantity = g.Sum(m => m.Quantity),
                    Value = g.Sum(m => m.Quantity * m.UnitCost)
                })
                .ToListAsync();

            var ids = grouped.Select(g => g.StockItemId).Distinct().ToList();
            var items = await _context.StockItems.AsNoTracking().Where(i => ids.Contains(i.Id)).ToDictionaryAsync(i => i.Id);

            var rows = ids
                .Where(items.ContainsKey)
                .Select(id =>
                {
                    var g = grouped.Where(x => x.StockItemId == id).ToList();
                    decimal Q(params string[] types) => g.Where(x => types.Contains(x.Type)).Sum(x => x.Quantity);
                    decimal V(params string[] types) => g.Where(x => types.Contains(x.Type)).Sum(x => x.Value);
                    var item = items[id];
                    // Отмены проведённых документов показываем отдельной колонкой
                    return new
                    {
                        stockItemId = id,
                        name = item.Name,
                        unit = item.Unit,
                        category = item.Category,
                        received = Math.Round(Q(StockMovementTypes.Receipt), 3),
                        sold = Math.Round(-Q(StockMovementTypes.Sale, StockMovementTypes.SaleReturn), 3),
                        soldValue = Math.Round(-V(StockMovementTypes.Sale, StockMovementTypes.SaleReturn), 2),
                        writtenOff = Math.Round(-Q(StockMovementTypes.WriteOff), 3),
                        writtenOffValue = Math.Round(-V(StockMovementTypes.WriteOff), 2),
                        inventoryDiff = Math.Round(Q(StockMovementTypes.Inventory), 3),
                        inventoryDiffValue = Math.Round(V(StockMovementTypes.Inventory), 2),
                        reversals = Math.Round(Q(StockMovementTypes.Reversal), 3),
                        balance = item.Quantity
                    };
                })
                .OrderBy(r => r.category).ThenBy(r => r.name)
                .ToList();

            return Ok(new
            {
                from = start,
                to = end.AddDays(-1),
                rows,
                totals = new
                {
                    soldValue = rows.Sum(r => r.soldValue),
                    writtenOffValue = rows.Sum(r => r.writtenOffValue),
                    inventoryDiffValue = rows.Sum(r => r.inventoryDiffValue)
                }
            });
        }

        // ================= Вспомогательное =================

        private record Owner(string Name, decimal? Price, int? ParentId, string? Unit);

        private async Task<Owner?> ResolveOwnerAsync(string target, int id) => target switch
        {
            DishTarget => await _context.Dishes.AsNoTracking().Where(s => s.Id == id)
                .Select(s => new Owner(s.Parent != null ? s.Parent.Name + " · " + s.Name : s.Name, s.Price, s.ParentId, null))
                .FirstOrDefaultAsync(),
            AddonTarget => await _context.Addons.AsNoTracking().Where(a => a.Id == id)
                .Select(a => new Owner(a.Name, a.Price, null, null))
                .FirstOrDefaultAsync(),
            Semi => await _context.StockItems.AsNoTracking().Where(i => i.Id == id && i.Kind == StockItemKinds.Semi)
                .Select(i => new Owner(i.Name, null, null, i.Unit))
                .FirstOrDefaultAsync(),
            _ => null
        };

        private static object CardDto(TechCard card, TechCardCalculator calc, decimal? price)
        {
            var cost = calc.CardCost(card);
            return new
            {
                card.Id,
                card.OutputWeight,
                card.YieldQuantity,
                card.Instructions,
                card.UpdatedAt,
                cost,
                unitCost = card.StockItemId.HasValue && card.YieldQuantity > 0 ? Math.Round(cost / card.YieldQuantity, 2) : cost,
                margin = price.HasValue ? price.Value - cost : (decimal?)null,
                foodCostPercent = price is > 0 ? Math.Round(cost / price.Value * 100, 1) : (decimal?)null,
                lines = card.Lines.OrderBy(l => l.SortOrder).Select(l =>
                {
                    var item = calc.Item(l.StockItemId);
                    return new
                    {
                        l.StockItemId,
                        l.SubDishId,
                        stockItemName = l.SubDishId is int dishId ? calc.DishName(dishId) : item?.Name,
                        // Блюдо в составе считается порциями
                        unit = l.SubDishId.HasValue ? "portion" : item?.Unit,
                        kind = l.SubDishId.HasValue ? "dish" : item?.Kind,
                        l.Gross,
                        l.LossPercent,
                        net = l.Net,
                        unitCost = Math.Round(calc.ComponentUnitCost(l), 2),
                        cost = calc.LineCost(l)
                    };
                })
            };
        }

        private static (DateTime start, DateTime end) Period(DateTime? from, DateTime? to)
        {
            var end = (to ?? DateTime.UtcNow).Date.AddDays(1);
            var start = (from ?? end.AddDays(-30)).Date;
            if (start >= end) start = end.AddDays(-1);
            return (start, end);
        }
    }
}
