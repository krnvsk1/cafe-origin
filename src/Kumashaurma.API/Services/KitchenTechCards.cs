using Kumashaurma.API.Models;

namespace Kumashaurma.API.Services
{
    /// <param name="Unit">kg / l / pcs, для блюда в составе — portion (Gross — доля порции)</param>
    public record KitchenTechCardLine(int? StockItemId, string Name, string Unit, bool IsSemi, decimal Gross, decimal Net, decimal LossPercent,
        int? DishId = null, bool IsDish = false);

    public record KitchenTechCard(string Title, decimal? OutputWeight, decimal YieldQuantity, string? Instructions, IReadOnlyList<KitchenTechCardLine> Lines);

    /// <summary>
    /// Техкарта для экрана кухни: состав, брутто/нетто, выход и технология — без себестоимости и наценки
    /// (их видят только те, у кого есть права на склад и техкарты).
    /// </summary>
    public static class KitchenTechCards
    {
        public static KitchenTechCard? ToView(TechCard? card, string title, TechCardCalculator calc)
        {
            if (card == null) return null;
            var lines = card.Lines
                .OrderBy(l => l.SortOrder)
                .Select(l =>
                {
                    if (l.SubDishId is int dishId)
                        return new KitchenTechCardLine(null, calc.DishName(dishId) ?? "Удалённое блюдо", "portion", false,
                            l.Gross, l.Gross, 0, dishId, true);
                    var item = calc.Item(l.StockItemId);
                    return new KitchenTechCardLine(
                        l.StockItemId,
                        item?.Name ?? "Удалённая позиция",
                        item?.Unit ?? string.Empty,
                        item?.Kind == StockItemKinds.Semi,
                        l.Gross,
                        l.Net,
                        l.LossPercent);
                })
                .ToList();
            var instructions = string.IsNullOrWhiteSpace(card.Instructions) ? null : card.Instructions.Trim();
            return new KitchenTechCard(title, card.OutputWeight, card.YieldQuantity, instructions, lines);
        }

        /// <summary>Карты полуфабрикатов и блюд из состава (соус, заготовка, половина пиццы) — один уровень, без повторов</summary>
        public static List<KitchenTechCard> SemiCards(IEnumerable<KitchenTechCard> cards, TechCardCalculator calc)
        {
            var lines = cards.SelectMany(c => c.Lines).ToList();
            var semis = lines.Where(l => l.IsSemi && l.StockItemId.HasValue)
                .GroupBy(l => l.StockItemId!.Value)
                .Select(g => ToView(calc.ForSemi(g.Key), g.First().Name, calc));
            var dishes = lines.Where(l => l.IsDish && l.DishId.HasValue)
                .GroupBy(l => l.DishId!.Value)
                .Select(g => ToView(calc.ForDish(g.Key), g.First().Name, calc));
            return semis.Concat(dishes).OfType<KitchenTechCard>().ToList();
        }
    }
}
