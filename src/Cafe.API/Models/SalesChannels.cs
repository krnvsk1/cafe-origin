namespace Cafe.API.Models
{
    /// <summary>Способы получения заказа и ограничения позиций по ним (например, пиво — только в зале)</summary>
    public static class SalesChannels
    {
        public const string Delivery = "Доставка";
        public const string Pickup = "Самовывоз";
        public const string DineIn = "В зале";

        public static readonly string[] All = { Delivery, Pickup, DineIn };

        public static string[]? Parse(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var list = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(All.Contains).Distinct().ToArray();
            return list.Length == 0 || list.Length == All.Length ? null : list;
        }

        /// <summary>null или все способы — без ограничений</summary>
        public static string? Format(IEnumerable<string>? values)
        {
            if (values == null) return null;
            var list = All.Where(values.Contains).ToArray();
            return list.Length == 0 || list.Length == All.Length ? null : string.Join(",", list);
        }

        /// <summary>Можно ли заказать позицию этим способом. Ограничение карточки действует и на её варианты.</summary>
        public static bool Allows(string? itemTypes, string? parentTypes, string orderType)
        {
            var own = Parse(itemTypes);
            var parent = Parse(parentTypes);
            return (own == null || own.Contains(orderType)) && (parent == null || parent.Contains(orderType));
        }

        public static string Describe(string[] types) => types.Length switch
        {
            1 when types[0] == DineIn => "только в зале",
            1 when types[0] == Pickup => "только самовывоз",
            1 when types[0] == Delivery => "только доставка",
            _ => string.Join(" и ", types.Select(t => t.ToLowerInvariant()))
        };
    }
}
