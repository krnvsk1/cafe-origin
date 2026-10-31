using Cafe.API.Models;

namespace Cafe.API.Services
{
    /// <summary>
    /// Расчёты по техкартам без обращения к базе.
    /// Товары — то, что приходует склад. Полуфабрикаты собираются из товаров (и других п/ф) с потерями и выходом.
    /// Блюда — из товаров, полуфабрикатов и других блюд (целиком или долей: ½, ⅓ порции).
    /// Раскладка до товаров идёт рекурсивно, себестоимость считается так же.
    /// </summary>
    public class TechCardCalculator
    {
        private const int MaxDepth = 10;

        private readonly Dictionary<int, StockItem> _items;
        private readonly Dictionary<int, TechCard> _byDish;
        private readonly Dictionary<int, TechCard> _byAddon;
        private readonly Dictionary<int, TechCard> _bySemi;
        private readonly Dictionary<int, int?> _dishParents;
        private readonly Dictionary<int, string> _dishNames;

        public TechCardCalculator(IEnumerable<StockItem> items, IEnumerable<TechCard> cards,
            IEnumerable<(int Id, int? ParentId, string Name)>? dishes = null)
        {
            _items = items.ToDictionary(i => i.Id);
            var list = cards.ToList();
            _byDish = list.Where(c => c.DishId.HasValue).ToDictionary(c => c.DishId!.Value);
            _byAddon = list.Where(c => c.AddonId.HasValue).ToDictionary(c => c.AddonId!.Value);
            _bySemi = list.Where(c => c.StockItemId.HasValue).ToDictionary(c => c.StockItemId!.Value);
            var d = dishes?.ToList() ?? new();
            _dishParents = d.ToDictionary(x => x.Id, x => x.ParentId);
            _dishNames = d.ToDictionary(x => x.Id, x => x.Name);
        }

        public StockItem? Item(int id) => _items.TryGetValue(id, out var item) ? item : null;
        public StockItem? Item(int? id) => id.HasValue ? Item(id.Value) : null;

        /// <summary>Название блюда меню (вариант — «Карточка · вариант»)</summary>
        public string? DishName(int id) => _dishNames.TryGetValue(id, out var n) ? n : null;

        /// <summary>Карта позиции меню; у варианта без своей карты — карта родителя</summary>
        public TechCard? ForDish(int dishId, int? parentId = null)
        {
            if (_byDish.TryGetValue(dishId, out var own)) return own;
            parentId ??= _dishParents.GetValueOrDefault(dishId);
            if (parentId.HasValue && _byDish.TryGetValue(parentId.Value, out var parent)) return parent;
            return null;
        }

        public TechCard? ForAddon(int addonId) => _byAddon.TryGetValue(addonId, out var c) ? c : null;

        public TechCard? ForSemi(int stockItemId) => _bySemi.TryGetValue(stockItemId, out var c) ? c : null;

        /// <summary>Сколько товаров (по id позиции) уходит на <paramref name="portions"/> порций по карте</summary>
        public Dictionary<int, decimal> ExpandToRaw(TechCard card, decimal portions = 1)
        {
            var result = new Dictionary<int, decimal>();
            Expand(card, portions, result, new HashSet<string>(), 0);
            return result;
        }

        /// <summary>Добавляет раскладку в общий словарь (для заказа из нескольких позиций)</summary>
        public void AddToRaw(TechCard card, decimal portions, Dictionary<int, decimal> into) =>
            Expand(card, portions, into, new HashSet<string>(), 0);

        private void Expand(TechCard card, decimal portions, Dictionary<int, decimal> into, HashSet<string> path, int depth)
        {
            if (depth > MaxDepth)
                throw new InvalidOperationException("Слишком глубокая вложенность техкарт");

            foreach (var line in card.Lines)
            {
                var qty = line.Gross * portions;
                if (qty == 0) continue;

                if (line.SubDishId is int dishId)
                {
                    // Блюдо в составе: раскладываем его карту на долю порции
                    var dishCard = ForDish(dishId);
                    if (dishCard == null || dishCard.Lines.Count == 0) continue;
                    if (!path.Add($"d{dishId}"))
                        throw new InvalidOperationException($"Блюдо «{DishName(dishId)}» входит само в себя");
                    Expand(dishCard, qty, into, path, depth + 1);
                    path.Remove($"d{dishId}");
                    continue;
                }

                if (line.StockItemId is not int itemId) continue;
                var item = Item(itemId);
                var semiCard = item?.Kind == StockItemKinds.Semi ? ForSemi(itemId) : null;

                if (semiCard != null && semiCard.Lines.Count > 0)
                {
                    if (!path.Add($"s{itemId}"))
                        throw new InvalidOperationException($"Полуфабрикат «{item!.Name}» входит сам в себя");
                    var yield = semiCard.YieldQuantity > 0 ? semiCard.YieldQuantity : 1;
                    Expand(semiCard, qty / yield, into, path, depth + 1);
                    path.Remove($"s{itemId}");
                }
                else
                {
                    into[itemId] = into.GetValueOrDefault(itemId) + qty;
                }
            }
        }

        /// <summary>Себестоимость единицы складской позиции</summary>
        public decimal UnitCost(int stockItemId) => UnitCost(stockItemId, new HashSet<string>(), 0);

        public decimal UnitCost(int? stockItemId) => stockItemId.HasValue ? UnitCost(stockItemId.Value) : 0;

        private decimal UnitCost(int stockItemId, HashSet<string> path, int depth)
        {
            var item = Item(stockItemId);
            if (item == null) return 0;

            if (item.Kind == StockItemKinds.Semi)
            {
                var card = ForSemi(stockItemId);
                if (card == null || depth > MaxDepth || !path.Add($"s{stockItemId}")) return 0;
                var yield = card.YieldQuantity > 0 ? card.YieldQuantity : 1;
                var cost = card.Lines.Sum(l => LineCostRaw(l, path, depth + 1)) / yield;
                path.Remove($"s{stockItemId}");
                return cost;
            }

            return CostOf(item);
        }

        /// <summary>Себестоимость одной порции блюда меню (по своей карте или карте родителя)</summary>
        public decimal DishCost(int dishId) => DishCost(dishId, new HashSet<string>(), 0);

        private decimal DishCost(int dishId, HashSet<string> path, int depth)
        {
            var card = ForDish(dishId);
            if (card == null || depth > MaxDepth || !path.Add($"d{dishId}")) return 0;
            var cost = card.Lines.Sum(l => LineCostRaw(l, path, depth + 1));
            path.Remove($"d{dishId}");
            return cost;
        }

        private decimal LineCostRaw(TechCardLine l, HashSet<string> path, int depth) =>
            l.SubDishId is int dishId ? l.Gross * DishCost(dishId, path, depth)
            : l.StockItemId is int itemId ? l.Gross * UnitCost(itemId, path, depth)
            : 0;

        /// <summary>Себестоимость одной порции по карте</summary>
        public decimal CardCost(TechCard card) =>
            Math.Round(card.Lines.Sum(l => LineCostRaw(l, new HashSet<string>(), 0)), 2);

        public decimal LineCost(TechCardLine line) => Math.Round(LineCostRaw(line, new HashSet<string>(), 0), 2);

        /// <summary>Себестоимость единицы компонента строки: товара/п/ф — за единицу, блюда — за порцию</summary>
        public decimal ComponentUnitCost(TechCardLine line) =>
            line.SubDishId is int dishId ? DishCost(dishId) : UnitCost(line.StockItemId);

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
                foreach (var l in card.Lines)
                    if (l.StockItemId is int sid) stack.Push(sid);
            }
            return false;
        }

        /// <summary>
        /// Проверка перед сохранением карты блюда: вложенные блюда не должны (через свои составы) содержать его самого.
        /// Вариант без своей карты считается через карту карточки — поэтому карточка не может входить в свой вариант и наоборот.
        /// </summary>
        public bool CreatesDishCycle(int dishId, IEnumerable<int> subDishIds)
        {
            var self = new HashSet<int> { dishId };
            var stack = new Stack<int>(subDishIds);
            var seen = new HashSet<int>();
            while (stack.Count > 0)
            {
                var id = stack.Pop();
                if (self.Contains(id)) return true;
                if (!seen.Add(id)) continue;
                // Карта, которая реально используется для этого блюда (своя или родителя)
                if (!_byDish.ContainsKey(id) && _dishParents.GetValueOrDefault(id) is int p && self.Contains(p)) return true;
                var card = ForDish(id);
                if (card == null) continue;
                foreach (var l in card.Lines)
                    if (l.SubDishId is int sub) stack.Push(sub);
            }
            return false;
        }
    }
}
