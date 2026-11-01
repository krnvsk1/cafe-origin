namespace Cafe.API.Services
{
    /// <summary>
    /// Весовые блюда: цена в меню — за 100 г, при заказе указывают вес в граммах.
    /// Цена позиции заказа хранится уже за этот вес, поэтому суммы чеков, оплаты и аналитика считаются как обычно.
    /// </summary>
    public static class Weighing
    {
        public const int MinGrams = 10;
        public const int MaxGrams = 10000;

        /// <summary>Цена за указанный вес при цене за 100 г</summary>
        public static decimal PriceFor(decimal pricePer100, int grams) =>
            Math.Round(pricePer100 * grams / 100m, 2, MidpointRounding.AwayFromZero);

        /// <summary>«350 г», «1,2 кг»</summary>
        public static string Label(int grams) =>
            grams >= 1000 && grams % 100 == 0
                ? $"{grams / 1000m:0.#} кг".Replace('.', ',')
                : $"{grams} г";

        /// <summary>Название позиции с весом: «Свиная шея · 350 г»</summary>
        public static string NameWith(string name, int grams, int max = 100)
        {
            var suffix = $" · {Label(grams)}";
            var basePart = name.Length + suffix.Length > max ? name[..Math.Max(0, max - suffix.Length)] : name;
            return basePart + suffix;
        }

        /// <summary>Убрать из названия прежний вес (при правке веса)</summary>
        public static string StripWeight(string name)
        {
            var i = name.LastIndexOf(" · ", StringComparison.Ordinal);
            if (i < 0) return name;
            var tail = name[(i + 3)..];
            return tail.EndsWith(" г") || tail.EndsWith(" кг") ? name[..i] : name;
        }

        /// <summary>Ошибка для пользователя или null, если вес подходит</summary>
        public static string? Validate(bool weighted, int? grams, string dishName)
        {
            if (!weighted) return grams is null or 0 ? null : $"«{dishName}» продаётся поштучно — вес не нужен";
            if (grams is null) return $"Укажите вес для «{dishName}»";
            if (grams < MinGrams || grams > MaxGrams) return $"Вес «{dishName}» — от {MinGrams} г до {MaxGrams / 1000} кг";
            return null;
        }

        /// <summary>Во сколько «порций техкарты» обходится позиция: техкарта весового блюда — на 100 г</summary>
        public static decimal Portions(int quantity, int? grams) =>
            grams is > 0 ? quantity * grams.Value / 100m : quantity;
    }
}
