using Kumashaurma.API.Models;

namespace Kumashaurma.API.Services
{
    public record KitchenTechCardLine(int StockItemId, string Name, string Unit, bool IsSemi, decimal Gross, decimal Net, decimal LossPercent);

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

        /// <summary>Карты полуфабрикатов из состава (соус, заготовка) — один уровень, без повторов</summary>
        public static List<KitchenTechCard> SemiCards(IEnumerable<KitchenTechCard> cards, TechCardCalculator calc) =>
            cards
                .SelectMany(c => c.Lines)
                .Where(l => l.IsSemi)
                .GroupBy(l => l.StockItemId)
                .Select(g => ToView(calc.ForSemi(g.Key), g.First().Name, calc))
                .OfType<KitchenTechCard>()
                .ToList();
    }
}
