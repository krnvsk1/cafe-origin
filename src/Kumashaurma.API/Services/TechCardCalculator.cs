using Kumashaurma.API.Models;

namespace Kumashaurma.API.Services
{
    /// <summary>
    /// Расчёты по техкартам без обращения к базе: раскладка до сырья
    /// (полуфабрикаты раскрываются по своим картам) и себестоимость.
    /// </summary>
    public class TechCardCalculator
    {
        private const int MaxDepth = 8;

        private readonly Dictionary<int, StockItem> _items;
        private readonly Dictionary<int, TechCard> _byDish;
        private readonly Dictionary<int, TechCard> _byAddon;
        private readonly Dictionary<int, TechCard> _bySemi;

        public TechCardCalculator(IEnumerable<StockItem> items, IEnumerable<TechCard> cards)
        {
            _items = items.ToDictionary(i => i.Id);
            var list = cards.ToList();
            _byDish = list.Where(c => c.ShawarmaId.HasValue).ToDictionary(c => c.ShawarmaId!.Value);
            _byAddon = list.Where(c => c.AddonId.HasValue).ToDictionary(c => c.AddonId!.Value);
            _bySemi = list.Where(c => c.StockItemId.HasValue).ToDictionary(c => c.StockItemId!.Value);
        }

        public StockItem? Item(int id) => _items.TryGetValue(id, out var item) ? item : null;

        /// <summary>Карта позиции меню; у варианта без своей карты — карта родителя</summary>
        public TechCard? ForDish(int shawarmaId, int? parentId = null)
        {
            if (_byDish.TryGetValue(shawarmaId, out var own)) return own;
            if (parentId.HasValue && _byDish.TryGetValue(parentId.Value, out var parent)) return parent;
            return null;
        }

        public TechCard? ForAddon(int addonId) => _byAddon.TryGetValue(addonId, out var c) ? c : null;

        public TechCard? ForSemi(int stockItemId) => _bySemi.TryGetValue(stockItemId, out var c) ? c : null;

        /// <summary>Сколько сырья (по id позиции) уходит на <paramref name="portions"/> порций по карте</summary>
        public Dictionary<int, decimal> ExpandToRaw(TechCard card, decimal portions = 1)
        {
            var result = new Dictionary<int, decimal>();
            Expand(card, portions, result, new HashSet<int>(), 0);
            return result;
        }

        /// <summary>Добавляет раскладку в общий словарь (для заказа из нескольких позиций)</summary>
        public void AddToRaw(TechCard card, decimal portions, Dictionary<int, decimal> into) =>
            Expand(card, portions, into, new HashSet<int>(), 0);

        private void Expand(TechCard card, decimal portions, Dictionary<int, decimal> into, HashSet<int> path, int depth)
        {
            if (depth > MaxDepth)
                throw new InvalidOperationException("Слишком глубокая вложенность полуфабрикатов");

            foreach (var line in card.Lines)
            {
                var qty = line.Gross * portions;
                if (qty == 0) continue;

                var item = Item(line.StockItemId);
                var semiCard = item?.Kind == StockItemKinds.Semi ? ForSemi(line.StockItemId) : null;

                if (semiCard != null && semiCard.Lines.Count > 0)
                {
                    if (!path.Add(line.StockItemId))
                        throw new InvalidOperationException($"Полуфабрикат «{item!.Name}» входит сам в себя");
                    var yield = semiCard.YieldQuantity > 0 ? semiCard.YieldQuantity : 1;
                    Expand(semiCard, qty / yield, into, path, depth + 1);
                    path.Remove(line.StockItemId);
                }
                else
                {
                    into[line.StockItemId] = into.GetValueOrDefault(line.StockItemId) + qty;
                }
            }
        }

        /// <summary>Себестоимость единицы складской позиции</summary>
        public decimal UnitCost(int stockItemId) => UnitCost(stockItemId, new HashSet<int>(), 0);

        private decimal UnitCost(int stockItemId, HashSet<int> path, int depth)
        {
            var item = Item(stockItemId);
            if (item == null) return 0;

            if (item.Kind == StockItemKinds.Semi)
            {
                var card = ForSemi(stockItemId);
                if (card == null || depth > MaxDepth || !path.Add(stockItemId)) return 0;
                var yield = card.YieldQuantity > 0 ? card.YieldQuantity : 1;
                var cost = card.Lines.Sum(l => l.Gross * UnitCost(l.StockItemId, path, depth + 1)) / yield;
                path.Remove(stockItemId);
                return cost;
            }

            return CostOf(item);
        }

        /// <summary>Себестоимость одной порции по карте</summary>
        public decimal CardCost(TechCard card) =>
            Math.Round(card.Lines.Sum(l => l.Gross * UnitCost(l.StockItemId)), 2);

        public decimal LineCost(TechCardLine line) => Math.Round(line.Gross * UnitCost(line.StockItemId), 2);

        /// <summary>Средняя себестоимость, а если приходов ещё не было — последняя цена</summary>
        public static decimal CostOf(StockItem item) => item.AvgCost > 0 ? item.AvgCost : item.LastPrice;

        /// <summary>
        /// Проверка перед сохранением карты полуфабриката: он не должен (даже через другие полуфабрикаты)
        /// содержать сам себя.
        /// </summary>
        public bool CreatesCycle(int semiItemId, IEnumerable<int> lineItemIds)
        {
            var stack = new Stack<int>(lineItemIds);
            var seen = new HashSet<int>();
            while (stack.Count > 0)
            {
                var id = stack.Pop();
                if (id == semiItemId) return true;
                if (!seen.Add(id)) continue;
                var card = ForSemi(id);
                if (card == null) continue;
                foreach (var l in card.Lines) stack.Push(l.StockItemId);
            }
            return false;
        }
    }
}
