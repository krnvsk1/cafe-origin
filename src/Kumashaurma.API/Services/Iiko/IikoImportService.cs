using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Kumashaurma.API.Data;
using Kumashaurma.API.Models;

namespace Kumashaurma.API.Services.Iiko
{
    /// <summary>
    /// Перенос техкарт из iiko: блюда → техкарты блюд/добавок, заготовки → полуфабрикаты со своими картами,
    /// товары → позиции склада (находим по названию или создаём).
    /// </summary>
    public class IikoImportService
    {
        // Скачанные из iiko данные держим 30 минут: предпросмотр → импорт без повторного входа
        private static readonly ConcurrentDictionary<Guid, (IikoSnapshot Snapshot, DateTime Expires)> Snapshots = new();

        private readonly ApplicationDbContext _context;
        private readonly IikoClient _client;

        public IikoImportService(ApplicationDbContext context, IikoClient client)
        {
            _context = context;
            _client = client;
        }

        public record Target(string Kind, int Id, string Name, bool HasCard);

        public record PreviewIngredient(string Name, string Type, decimal Gross, string? Unit);

        public record PreviewDish(string IikoId, string Name, string Type, decimal Yield, string? Unit,
            List<PreviewIngredient> Ingredients, Target? Match);

        public record Preview(Guid SnapshotId, List<PreviewDish> Dishes, int Prepared, int Goods, int GoodsMatched, List<Target> Targets);

        public record ImportChoice(string IikoId, string Kind, int Id);

        public record ImportResult(int Cards, int SemiCards, int CreatedItems, int Skipped, List<string> Warnings);

        public static string Normalize(string s) =>
            Regex.Replace(Regex.Replace(s.ToLowerInvariant().Replace('ё', 'е'), @"[^\p{L}\p{N}]+", " "), @"\s+", " ").Trim();

        public async Task<Preview> PreviewAsync(string url, string login, string password, CancellationToken ct)
        {
            var snapshot = await _client.FetchAsync(url, login, password, ct);
            foreach (var key in Snapshots.Where(s => s.Value.Expires < DateTime.UtcNow).Select(s => s.Key).ToList())
                Snapshots.TryRemove(key, out _);
            var id = Guid.NewGuid();
            Snapshots[id] = (snapshot, DateTime.UtcNow.AddMinutes(30));

            var targets = await TargetsAsync();
            var byName = new Dictionary<string, Target>();
            foreach (var t in targets)
                foreach (var n in NameVariants(t.Name))
                    byName.TryAdd(n, t);

            var dishes = new List<PreviewDish>();
            foreach (var (pid, chart) in snapshot.Charts)
            {
                if (!snapshot.Products.TryGetValue(pid, out var p) || p.Type is not ("DISH" or "MODIFIER")) continue;
                var match = NameVariants(p.Name).Select(n => byName.GetValueOrDefault(n)).FirstOrDefault(t => t != null);
                dishes.Add(new PreviewDish(pid, p.Name, p.Type, chart.AssembledAmount, p.Unit,
                    chart.Items.OrderBy(i => i.SortWeight).Select(i =>
                    {
                        var ip = snapshot.Products.GetValueOrDefault(i.ProductId);
                        return new PreviewIngredient(ip?.Name ?? "?", ip?.Type ?? "?", i.AmountIn, ip?.Unit);
                    }).ToList(),
                    match));
            }

            var used = snapshot.Charts.Values.SelectMany(c => c.Items).Select(i => i.ProductId).Distinct()
                .Select(snapshot.Products.GetValueOrDefault).Where(p => p != null).ToList();
            var stockNames = (await _context.StockItems.AsNoTracking().Where(i => !i.IsArchived).Select(i => i.Name).ToListAsync())
                .Select(Normalize).ToHashSet();
            var goods = used.Where(p => p!.Type is not ("PREPARED" or "DISH")).ToList();

            return new Preview(id,
                dishes.OrderBy(d => d.Match == null).ThenBy(d => d.Name).ToList(),
                used.Count(p => p!.Type is "PREPARED" or "DISH"),
                goods.Count,
                goods.Count(p => stockNames.Contains(Normalize(p!.Name))),
                targets);
        }

        /// <summary>Варианты названия для сравнения: целиком и без размеров/граммовок в конце</summary>
        private static IEnumerable<string> NameVariants(string name)
        {
            var n = Normalize(name);
            yield return n;
            var trimmed = Regex.Replace(n, @"\s(\d+([.,]\d+)?\s?(г|гр|кг|мл|л|см|шт))$", "").Trim();
            if (trimmed != n) yield return trimmed;
        }

        private async Task<List<Target>> TargetsAsync()
        {
            var cards = await _context.TechCards.AsNoTracking().Select(c => new { c.ShawarmaId, c.AddonId }).ToListAsync();
            var dishCards = cards.Where(c => c.ShawarmaId != null).Select(c => c.ShawarmaId!.Value).ToHashSet();
            var addonCards = cards.Where(c => c.AddonId != null).Select(c => c.AddonId!.Value).ToHashSet();

            var all = await _context.Shawarmas.AsNoTracking().Select(s => new { s.Id, s.Name, s.ParentId, s.SortOrder }).ToListAsync();
            var parents = all.Where(s => s.ParentId == null).ToDictionary(s => s.Id);
            var list = new List<Target>();
            foreach (var p in parents.Values.OrderBy(p => p.SortOrder).ThenBy(p => p.Name))
            {
                list.Add(new Target("dish", p.Id, p.Name, dishCards.Contains(p.Id)));
                foreach (var c in all.Where(c => c.ParentId == p.Id).OrderBy(c => c.SortOrder))
                    list.Add(new Target("dish", c.Id, $"{p.Name} {c.Name}", dishCards.Contains(c.Id)));
            }
            var addons = await _context.Addons.AsNoTracking().OrderBy(a => a.Name).Select(a => new { a.Id, a.Name }).ToListAsync();
            list.AddRange(addons.Select(a => new Target("addon", a.Id, a.Name, addonCards.Contains(a.Id))));
            return list;
        }

        public async Task<ImportResult> ImportAsync(Guid snapshotId, IReadOnlyCollection<ImportChoice> choices, bool overwrite)
        {
            if (!Snapshots.TryGetValue(snapshotId, out var entry) || entry.Expires < DateTime.UtcNow)
                throw new IikoException("Данные из iiko устарели — загрузите их ещё раз");
            var s = entry.Snapshot;
            var warnings = new List<string>();
            int cards = 0, semiCards = 0, created = 0, skipped = 0;

            var stock = await _context.StockItems.Where(i => !i.IsArchived).ToListAsync();
            var byName = new Dictionary<string, StockItem>();
            foreach (var i in stock) byName.TryAdd(Normalize(i.Name), i);
            var semiDone = new Dictionary<string, StockItem>();
            // Блюда iiko, которые переносятся в наши блюда: если такое блюдо входит в состав другого — ссылаемся на него, а не делаем п/ф
            var dishMap = choices.Where(c => c.Kind == "dish").GroupBy(c => c.IikoId).ToDictionary(g => g.Key, g => g.First().Id);

            // Позиция склада для ингредиента iiko (создаём, если нет)
            StockItem Item(IikoProduct p, bool semi)
            {
                var key = Normalize(p.Name);
                if (byName.TryGetValue(key, out var item)) return item;
                item = new StockItem
                {
                    Name = p.Name.Length > 200 ? p.Name[..200] : p.Name,
                    Unit = UnitOf(p.Unit).Unit,
                    Kind = semi ? StockItemKinds.Semi : StockItemKinds.Raw,
                    Category = semi ? "Полуфабрикаты" : "Из iiko",
                    CreatedAt = DateTime.UtcNow
                };
                _context.StockItems.Add(item);
                byName[key] = item;
                created++;
                return item;
            }

            await using var tx = await _context.Database.BeginTransactionAsync();

            // Сначала полуфабрикаты (рекурсивно), чтобы ингредиенты блюд ссылались на них
            async Task<StockItem> Semi(IikoProduct p, HashSet<string> path)
            {
                if (semiDone.TryGetValue(p.Id, out var done)) return done;
                var item = Item(p, semi: true);
                if (item.Kind != StockItemKinds.Semi && s.Charts.ContainsKey(p.Id))
                    warnings.Add($"«{p.Name}»: на складе есть такая позиция-сырьё — карта заготовки не перенесена, ингредиент спишется как сырьё");
                semiDone[p.Id] = item;
                if (item.Kind == StockItemKinds.Semi && s.Charts.TryGetValue(p.Id, out var chart) && path.Add(p.Id))
                {
                    await _context.SaveChangesAsync();
                    var lines = await LinesAsync(chart, path, allowDishes: false);
                    if (lines.Count > 0 && await SaveCardAsync(c => c.StockItemId == item.Id, () => new TechCard { StockItemId = item.Id },
                            lines, chart, semi: true, overwrite, unitFactor: UnitOf(p.Unit).Factor))
                        semiCards++;
                    path.Remove(p.Id);
                }
                return item;
            }

            async Task<List<(StockItem? Item, int? DishId, decimal Gross, decimal Loss)>> LinesAsync(IikoChart chart, HashSet<string> path, bool allowDishes)
            {
                var result = new List<(StockItem?, int?, decimal, decimal)>();
                foreach (var i in chart.Items.OrderBy(i => i.SortWeight))
                {
                    if (i.AmountIn <= 0) continue;
                    if (!s.Products.TryGetValue(i.ProductId, out var p))
                    {
                        warnings.Add($"Ингредиент {i.ProductId} не найден в номенклатуре iiko — пропущен");
                        continue;
                    }
                    if (path.Contains(p.Id))
                    {
                        warnings.Add($"«{p.Name}» входит сам в себя — строка пропущена");
                        continue;
                    }
                    // Блюдо в составе (сет, «½ пиццы»), которое тоже есть в нашем меню: строка-ссылка на долю порции
                    if (allowDishes && p.Type == "DISH" && dishMap.TryGetValue(p.Id, out var ourDishId) && UnitOf(p.Unit).Unit == StockUnits.Piece)
                    {
                        result.Add((null, ourDishId, Math.Round(i.AmountIn, 4), 0));
                        continue;
                    }
                    var isSemi = p.Type is "PREPARED" or "DISH" && s.Charts.ContainsKey(p.Id);
                    var item = isSemi ? await Semi(p, path) : Item(p, semi: false);
                    var (unit, factor) = UnitOf(p.Unit);
                    // Для штучных ингредиентов iiko пишет нетто в килограммах (1 шт → 0,089 кг) — потерь тут нет
                    var output = i.AmountOut > 0 ? i.AmountOut : i.AmountMiddle > 0 ? i.AmountMiddle : i.AmountIn;
                    var loss = unit == StockUnits.Piece ? 0 : Math.Clamp(Math.Round((1 - output / i.AmountIn) * 100, 2), 0, 99);
                    result.Add((item, null, Math.Round(i.AmountIn * factor, 4), loss));
                }
                return result;
            }

            foreach (var choice in choices)
            {
                if (!s.Charts.TryGetValue(choice.IikoId, out var chart) || !s.Products.TryGetValue(choice.IikoId, out var dish))
                {
                    skipped++;
                    continue;
                }
                var path = new HashSet<string> { dish.Id };
                var lines = await LinesAsync(chart, path, allowDishes: true);
                if (lines.Count == 0)
                {
                    warnings.Add($"«{dish.Name}»: в карте iiko нет ингредиентов с весом — пропущено");
                    skipped++;
                    continue;
                }
                bool saved = choice.Kind switch
                {
                    "dish" => await _context.Shawarmas.AnyAsync(x => x.Id == choice.Id)
                              && await SaveCardAsync(c => c.ShawarmaId == choice.Id, () => new TechCard { ShawarmaId = choice.Id }, lines, chart, false, overwrite, 1, OutputGrams(dish, chart, s)),
                    "addon" => await _context.Addons.AnyAsync(x => x.Id == choice.Id)
                               && await SaveCardAsync(c => c.AddonId == choice.Id, () => new TechCard { AddonId = choice.Id }, lines, chart, false, overwrite, 1, OutputGrams(dish, chart, s)),
                    _ => false
                };
                if (saved) cards++; else skipped++;
            }

            await _context.SaveChangesAsync();
            await tx.CommitAsync();
            return new ImportResult(cards, semiCards, created, skipped, warnings.Distinct().Take(50).ToList());
        }

        /// <summary>Создать или заменить техкарту. false — карта уже есть и перезапись выключена</summary>
        private async Task<bool> SaveCardAsync(System.Linq.Expressions.Expression<Func<TechCard, bool>> find, Func<TechCard> create,
            List<(StockItem? Item, int? DishId, decimal Gross, decimal Loss)> lines, IikoChart chart, bool semi, bool overwrite, decimal unitFactor,
            decimal? outputGrams = null)
        {
            await _context.SaveChangesAsync(); // новые позиции склада получают Id
            var card = await _context.TechCards.Include(c => c.Lines).FirstOrDefaultAsync(find);
            if (card != null && !overwrite) return false;
            if (card == null)
            {
                card = create();
                _context.TechCards.Add(card);
            }
            else
            {
                _context.TechCardLines.RemoveRange(card.Lines);
                card.Lines.Clear();
            }

            card.YieldQuantity = semi ? Math.Round(chart.AssembledAmount * unitFactor, 3) : 1;
            card.OutputWeight = !semi && outputGrams is > 0 ? Math.Round(outputGrams.Value, 1) : null;
            var tech = chart.Technology?.Trim();
            card.Instructions = string.IsNullOrEmpty(tech) ? card.Instructions : tech.Length > 4000 ? tech[..4000] : tech;
            card.UpdatedAt = DateTime.UtcNow;

            // Одинаковые ингредиенты в карте iiko складываем
            var order = 0;
            foreach (var g in lines.GroupBy(l => (l.Item, l.DishId)))
            {
                var gross = g.Sum(x => x.Gross);
                var net = g.Sum(x => x.Gross * (1 - x.Loss / 100m));
                card.Lines.Add(new TechCardLine
                {
                    StockItem = g.Key.Item,
                    SubDishId = g.Key.DishId,
                    Gross = Math.Round(gross / (semi ? 1 : Math.Max(chart.AssembledAmount, 1)), 4),
                    LossPercent = gross > 0 ? Math.Clamp(Math.Round((1 - net / gross) * 100, 2), 0, 99) : 0,
                    SortOrder = order++
                });
            }
            return true;
        }

        /// <summary>
        /// Выход порции, г. Берём вес порции из карточки блюда iiko (UNIT_WEIGHT); если его нет — считаем по карте:
        /// весовые ингредиенты и полуфабрикаты — по выходу, штучные — по весу нетто из карты (iiko пишет его в кг) или весу штуки.
        /// </summary>
        public static decimal? OutputGrams(IikoProduct dish, IikoChart chart, IikoSnapshot s)
        {
            if (dish.UnitWeight > 0 && UnitOf(dish.Unit).Unit == StockUnits.Piece) return dish.UnitWeight * 1000m;

            decimal kg = 0;
            foreach (var i in chart.Items)
            {
                var p = s.Products.GetValueOrDefault(i.ProductId);
                var (unit, factor) = UnitOf(p?.Unit);
                var output = i.AmountOut > 0 ? i.AmountOut : i.AmountMiddle;
                kg += unit == StockUnits.Piece
                    ? output > 0 ? output : (p?.UnitWeight ?? 0) * i.AmountIn
                    : (output > 0 ? output : i.AmountIn) * factor;
            }
            return kg > 0 ? kg * 1000m / Math.Max(chart.AssembledAmount, 1) : null;
        }

        /// <summary>Единица iiko → единица склада и множитель (г → кг × 0,001)</summary>
        public static (string Unit, decimal Factor) UnitOf(string? iikoUnit)
        {
            var u = Normalize(iikoUnit ?? "");
            return u switch
            {
                "кг" or "kg" or "килограмм" => (StockUnits.Kg, 1m),
                "г" or "гр" or "g" or "грамм" => (StockUnits.Kg, 0.001m),
                "л" or "l" or "литр" => (StockUnits.Liter, 1m),
                "мл" or "ml" => (StockUnits.Liter, 0.001m),
                "шт" or "pcs" or "порц" or "порция" or "пор" or "уп" or "упак" or "бут" => (StockUnits.Piece, 1m),
                _ => (StockUnits.Kg, 1m)
            };
        }
    }
}
