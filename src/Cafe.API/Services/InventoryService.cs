using Microsoft.EntityFrameworkCore;
using Cafe.API.Data;
using Cafe.API.Models;

namespace Cafe.API.Services
{
    public interface IInventoryService
    {
        /// <summary>Проводит черновик: двигает остатки и пишет журнал. Сохранение — на вызывающем.</summary>
        Task PostDocumentAsync(StockDocument document);

        /// <summary>Отменяет проведённый документ обратными движениями</summary>
        Task CancelDocumentAsync(StockDocument document);

        /// <summary>Реакция на смену статуса заказа: списание по техкартам или возврат</summary>
        Task OnOrderStatusChangedAsync(Order order, string oldStatus, string newStatus);

        /// <summary>
        /// Склад при отмене блюда из заказа: если по заказу уже списано — вернуть продукты блюда;
        /// writeOff — списать их как порчу (блюдо приготовлено или начато). Сохранение — на вызывающем.
        /// </summary>
        Task VoidItemStockAsync(Order order, OrderItem item, int quantity, bool writeOff, string comment);

        /// <summary>Вес весовой позиции поправили после списания: дописать или вернуть разницу. Сохранение — на вызывающем.</summary>
        Task AdjustWeightStockAsync(Order order, OrderItem item, int oldGrams, int newGrams);

        Task<TechCardCalculator> LoadCalculatorAsync(bool tracking = false);

        /// <summary>Пересчитывает остатки по журналу (на случай рассинхронизации)</summary>
        Task<int> RecalculateBalancesAsync();
    }

    public class InventoryService : IInventoryService
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<InventoryService> _logger;

        public InventoryService(ApplicationDbContext context, ILogger<InventoryService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<TechCardCalculator> LoadCalculatorAsync(bool tracking = false)
        {
            IQueryable<StockItem> items = _context.StockItems;
            IQueryable<TechCard> cards = _context.TechCards.Include(c => c.Lines);
            if (!tracking)
            {
                items = items.AsNoTracking();
                cards = cards.AsNoTracking();
            }
            var dishes = await _context.Dishes.AsNoTracking()
                .Select(d => new { d.Id, d.ParentId, d.Name, ParentName = d.Parent != null ? d.Parent.Name : null })
                .ToListAsync();
            return new TechCardCalculator(await items.ToListAsync(), await cards.ToListAsync(),
                dishes.Select(d => (d.Id, d.ParentId, d.ParentName != null ? $"{d.ParentName} · {d.Name}" : d.Name)));
        }

        // ================= Документы =================

        public async Task PostDocumentAsync(StockDocument document)
        {
            if (document.Status != StockDocumentStatuses.Draft)
                throw new InventoryException("Провести можно только черновик");
            if (document.Lines.Count == 0)
                throw new InventoryException("В документе нет строк");

            var itemIds = document.Lines.Select(l => l.StockItemId).Distinct().ToList();
            var items = await _context.StockItems.Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id);

            foreach (var line in document.Lines)
            {
                if (!items.TryGetValue(line.StockItemId, out var item))
                    throw new InventoryException($"Позиция #{line.StockItemId} не найдена");
                if (item.Kind == StockItemKinds.Semi)
                    throw new InventoryException($"«{item.Name}» — полуфабрикат: он не хранится на складе, учитывается сырьё из его техкарты");
            }

            var comment = DocumentLabel(document);
            decimal total = 0;

            switch (document.Type)
            {
                case StockDocumentTypes.Receipt:
                    foreach (var line in document.Lines)
                    {
                        if (line.Quantity <= 0) throw new InventoryException("Количество в приходе должно быть больше нуля");
                        var item = items[line.StockItemId];
                        Apply(item, line.Quantity, line.Price, StockMovementTypes.Receipt, document.Id, null, comment, affectsAvg: true);
                        if (line.Price > 0) item.LastPrice = line.Price;
                        total += line.Sum;
                    }
                    break;

                case StockDocumentTypes.WriteOff:
                    foreach (var line in document.Lines)
                    {
                        if (line.Quantity <= 0) throw new InventoryException("Количество в списании должно быть больше нуля");
                        var item = items[line.StockItemId];
                        line.Price = Math.Round(TechCardCalculator.CostOf(item), 4);
                        line.Sum = Math.Round(line.Quantity * line.Price, 2);
                        Apply(item, -line.Quantity, line.Price, StockMovementTypes.WriteOff, document.Id, null, comment, affectsAvg: false);
                        total += line.Sum;
                    }
                    break;

                case StockDocumentTypes.Inventory:
                    foreach (var line in document.Lines)
                    {
                        if (line.Quantity < 0) throw new InventoryException("Фактический остаток не может быть отрицательным");
                        var item = items[line.StockItemId];
                        line.ExpectedQuantity = item.Quantity;
                        line.Price = Math.Round(TechCardCalculator.CostOf(item), 4);
                        var diff = line.Quantity - item.Quantity;
                        line.Sum = Math.Round(diff * line.Price, 2);
                        if (diff != 0)
                            Apply(item, diff, line.Price, StockMovementTypes.Inventory, document.Id, null, comment, affectsAvg: false);
                        total += line.Sum;
                    }
                    break;

                default:
                    throw new InventoryException("Неизвестный тип документа");
            }

            document.Total = Math.Round(total, 2);
            document.Status = StockDocumentStatuses.Posted;
            document.PostedAt = DateTime.UtcNow;
        }

        public async Task CancelDocumentAsync(StockDocument document)
        {
            if (document.Status != StockDocumentStatuses.Posted)
                throw new InventoryException("Отменить можно только проведённый документ");

            var movements = await _context.StockMovements
                .Where(m => m.DocumentId == document.Id && m.Type != StockMovementTypes.Reversal)
                .ToListAsync();

            var itemIds = movements.Select(m => m.StockItemId).Distinct().ToList();
            var items = await _context.StockItems.Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id);
            var comment = $"Отмена: {DocumentLabel(document)}";

            foreach (var m in movements)
            {
                var item = items[m.StockItemId];
                // Отмена прихода убирает и его вклад в среднюю себестоимость
                Apply(item, -m.Quantity, m.UnitCost, StockMovementTypes.Reversal, document.Id, null, comment,
                    affectsAvg: m.Type == StockMovementTypes.Receipt || m.Quantity < 0);
            }

            document.Status = StockDocumentStatuses.Cancelled;
            document.CancelledAt = DateTime.UtcNow;
        }

        // ================= Заказы =================

        public async Task OnOrderStatusChangedAsync(Order order, string oldStatus, string newStatus)
        {
            var shouldBeDeducted = !OrderStatuses.IsNotStarted(newStatus) && newStatus != OrderStatuses.Cancelled;

            if (shouldBeDeducted && order.StockDeductedAt == null)
                await DeductForOrderAsync(order);
            else if (!shouldBeDeducted && order.StockDeductedAt != null)
                await ReturnForOrderAsync(order);
        }

        private async Task DeductForOrderAsync(Order order)
        {
            var lines = await _context.OrderItems.AsNoTracking()
                .Where(i => i.OrderId == order.Id)
                .Select(i => new
                {
                    i.DishId,
                    i.Quantity,
                    i.Weight,
                    ParentId = _context.Dishes.Where(s => s.Id == i.DishId).Select(s => s.ParentId).FirstOrDefault(),
                    Addons = i.SelectedAddons.Select(a => new { a.AddonId, a.Quantity }).ToList()
                })
                .ToListAsync();

            var calc = await LoadCalculatorAsync(tracking: true);
            var consumption = new Dictionary<int, decimal>();
            var missing = 0;

            foreach (var line in lines)
            {
                var card = calc.ForDish(line.DishId, line.ParentId);
                // Техкарта весового блюда — на 100 г
                if (card != null) calc.AddToRaw(card, Weighing.Portions(line.Quantity, line.Weight), consumption);
                else missing++;

                foreach (var addon in line.Addons)
                {
                    var addonCard = calc.ForAddon(addon.AddonId);
                    if (addonCard != null) calc.AddToRaw(addonCard, addon.Quantity * line.Quantity, consumption);
                }
            }

            var comment = $"Заказ #{order.Id}";
            foreach (var (itemId, qty) in consumption)
            {
                var item = calc.Item(itemId);
                if (item == null || qty == 0) continue;
                Apply(item, -Math.Round(qty, 3), Math.Round(TechCardCalculator.CostOf(item), 4),
                    StockMovementTypes.Sale, null, order.Id, comment, affectsAvg: false);
            }

            order.StockDeductedAt = DateTime.UtcNow;
            if (missing > 0)
                _logger.LogInformation("📦 Заказ #{OrderId}: {Missing} поз. без техкарты — по ним ничего не списано", order.Id, missing);
        }

        public async Task VoidItemStockAsync(Order order, OrderItem item, int quantity, bool writeOff, string comment)
        {
            if (quantity <= 0) return;
            var parentId = await _context.Dishes.Where(s => s.Id == item.DishId).Select(s => s.ParentId).FirstOrDefaultAsync();
            List<(int AddonId, int Quantity)> addons;
            if (item.SelectedAddons.Count > 0)
                addons = item.SelectedAddons.Select(a => (a.AddonId, a.Quantity)).ToList();
            else
                addons = (await _context.OrderItemAddons.Where(a => a.OrderItemId == item.Id).Select(a => new { a.AddonId, a.Quantity }).ToListAsync())
                    .Select(a => (a.AddonId, a.Quantity)).ToList();

            var calc = await LoadCalculatorAsync(tracking: true);
            var consumption = new Dictionary<int, decimal>();
            var card = calc.ForDish(item.DishId, parentId);
            if (card != null) calc.AddToRaw(card, Weighing.Portions(quantity, item.Weight), consumption);
            foreach (var (addonId, addonQty) in addons)
            {
                var addonCard = calc.ForAddon(addonId);
                if (addonCard != null) calc.AddToRaw(addonCard, addonQty * quantity, consumption);
            }

            foreach (var (itemId, qty) in consumption)
            {
                var stock = calc.Item(itemId);
                if (stock == null || qty == 0) continue;
                var cost = Math.Round(TechCardCalculator.CostOf(stock), 4);
                // Уже списано с заказом — возвращаем (так отмена всего заказа не вернёт это второй раз)
                if (order.StockDeductedAt != null)
                    Apply(stock, Math.Round(qty, 3), cost, StockMovementTypes.SaleReturn, null, order.Id, $"Отмена блюда: {comment}", affectsAvg: false);
                // Приготовлено или начато — списываем как порчу, без привязки к заказу
                if (writeOff)
                    Apply(stock, -Math.Round(qty, 3), cost, StockMovementTypes.WriteOff, null, null, $"Списание отменённого блюда: {comment}", affectsAvg: false);
            }
        }

        public async Task AdjustWeightStockAsync(Order order, OrderItem item, int oldGrams, int newGrams)
        {
            if (order.StockDeductedAt == null || oldGrams == newGrams) return;
            var parentId = await _context.Dishes.Where(s => s.Id == item.DishId).Select(s => s.ParentId).FirstOrDefaultAsync();
            var calc = await LoadCalculatorAsync(tracking: true);
            var card = calc.ForDish(item.DishId, parentId);
            if (card == null) return;
            var consumption = new Dictionary<int, decimal>();
            calc.AddToRaw(card, Weighing.Portions(item.Quantity, newGrams) - Weighing.Portions(item.Quantity, oldGrams), consumption);
            var comment = $"Вес «{Weighing.StripWeight(item.Name)}»: {Weighing.Label(oldGrams)} → {Weighing.Label(newGrams)}, заказ #{order.Id}";
            foreach (var (itemId, qty) in consumption)
            {
                var stock = calc.Item(itemId);
                if (stock == null || qty == 0) continue;
                var cost = Math.Round(TechCardCalculator.CostOf(stock), 4);
                // Больше веса — досписываем как продажу, меньше — возвращаем; оба движения привязаны к заказу
                Apply(stock, -Math.Round(qty, 3), cost, qty > 0 ? StockMovementTypes.Sale : StockMovementTypes.SaleReturn,
                    null, order.Id, comment, affectsAvg: false);
            }
        }

        private async Task ReturnForOrderAsync(Order order)
        {
            var net = await _context.StockMovements
                .Where(m => m.OrderId == order.Id &&
                            (m.Type == StockMovementTypes.Sale || m.Type == StockMovementTypes.SaleReturn))
                .GroupBy(m => m.StockItemId)
                .Select(g => new
                {
                    StockItemId = g.Key,
                    Quantity = g.Sum(m => m.Quantity),
                    Cost = g.Sum(m => m.Quantity * m.UnitCost)
                })
                .ToListAsync();

            var ids = net.Select(n => n.StockItemId).ToList();
            var items = await _context.StockItems.Where(i => ids.Contains(i.Id)).ToDictionaryAsync(i => i.Id);
            var comment = $"Отмена заказа #{order.Id}";

            foreach (var n in net.Where(n => n.Quantity < 0))
            {
                var unitCost = n.Quantity != 0 ? Math.Round(n.Cost / n.Quantity, 4) : 0;
                Apply(items[n.StockItemId], -n.Quantity, unitCost, StockMovementTypes.SaleReturn, null, order.Id, comment, affectsAvg: true);
            }

            order.StockDeductedAt = null;
        }

        // ================= Общее =================

        public async Task<int> RecalculateBalancesAsync()
        {
            var sums = await _context.StockMovements
                .GroupBy(m => m.StockItemId)
                .Select(g => new { Id = g.Key, Quantity = g.Sum(m => m.Quantity) })
                .ToDictionaryAsync(x => x.Id, x => x.Quantity);

            var changed = 0;
            foreach (var item in await _context.StockItems.Where(i => i.Kind == StockItemKinds.Raw).ToListAsync())
            {
                var actual = sums.GetValueOrDefault(item.Id);
                if (item.Quantity != actual)
                {
                    item.Quantity = actual;
                    changed++;
                }
            }
            return changed;
        }

        /// <summary>
        /// Меняет остаток позиции и пишет движение в журнал.
        /// affectsAvg: приход по цене пересчитывает среднюю себестоимость, отмена прихода — убирает его вклад.
        /// </summary>
        public void Apply(StockItem item, decimal delta, decimal unitCost, string type, int? documentId, int? orderId,
            string? comment, bool affectsAvg)
        {
            if (delta == 0) return;

            item.AvgCost = NewAverageCost(item.Quantity, item.AvgCost, delta, unitCost, affectsAvg);
            item.Quantity += delta;

            _context.StockMovements.Add(new StockMovement
            {
                StockItemId = item.Id,
                Type = type,
                Quantity = delta,
                UnitCost = unitCost,
                BalanceAfter = item.Quantity,
                DocumentId = documentId,
                OrderId = orderId,
                Comment = comment,
                CreatedAt = DateTime.UtcNow
            });
        }

        /// <summary>Средневзвешенная себестоимость после движения</summary>
        public static decimal NewAverageCost(decimal quantity, decimal avgCost, decimal delta, decimal unitCost, bool affectsAvg)
        {
            if (!affectsAvg) return avgCost;

            if (delta > 0)
            {
                // Если остаток был нулевым или ушёл в минус — берём цену прихода
                if (quantity <= 0 || avgCost <= 0) return Math.Round(unitCost, 4);
                return Math.Round((quantity * avgCost + delta * unitCost) / (quantity + delta), 4);
            }

            // Отмена прихода: убираем его вклад, если после этого что-то остаётся
            var rest = quantity + delta;
            if (rest <= 0) return avgCost;
            var value = quantity * avgCost + delta * unitCost;
            return value > 0 ? Math.Round(value / rest, 4) : avgCost;
        }

        private static string DocumentLabel(StockDocument d)
        {
            var kind = d.Type switch
            {
                StockDocumentTypes.Receipt => "Приход",
                StockDocumentTypes.WriteOff => "Списание",
                StockDocumentTypes.Inventory => "Инвентаризация",
                _ => "Документ"
            };
            return string.IsNullOrWhiteSpace(d.Number) ? $"{kind} #{d.Id}" : $"{kind} № {d.Number}";
        }
    }

    /// <summary>Ошибка складской операции — текст показывается пользователю</summary>
    public class InventoryException : Exception
    {
        public InventoryException(string message) : base(message) { }
    }
}
