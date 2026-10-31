namespace Kumashaurma.API.Services
{
    /// <summary>
    /// Раздельные кухни: каждая кухня ведёт свою часть заказа (принят → готовится → готов),
    /// а общий статус заказа складывается из них.
    /// </summary>
    public static class KitchenFlow
    {
        public static readonly string[] StationStatuses = { OrderStatuses.New, OrderStatuses.Accepted, OrderStatuses.Preparing, OrderStatuses.Ready };

        public static int Rank(string status) => status switch
        {
            OrderStatuses.New => 0,
            OrderStatuses.Accepted => 1,
            OrderStatuses.Preparing => 2,
            OrderStatuses.Ready or OrderStatuses.OnTheWay or OrderStatuses.Delivered or OrderStatuses.Completed => 3,
            _ => -1
        };

        private static string FromRank(int rank) => rank switch
        {
            <= 0 => OrderStatuses.New,
            1 => OrderStatuses.Accepted,
            2 => OrderStatuses.Preparing,
            _ => OrderStatuses.Ready
        };

        /// <summary>
        /// Статус части заказа на кухне. Пока ни одна кухня не отмечала заказ — все части идут за общим статусом
        /// (заказ двигают целиком). Как только кухни отмечают свои части — у каждой свой статус,
        /// и общий статус (он складывается из них) другие кухни не двигает; только готовый целиком заказ готов везде.
        /// </summary>
        public static string Effective(string? stationStatus, string orderStatus, bool orderHasMarks = false)
        {
            var order = Rank(orderStatus);
            var lift = !orderHasMarks ? Math.Max(0, order) : order >= 3 ? 3 : 0;
            return FromRank(Math.Max(stationStatus == null ? 0 : Rank(stationStatus), lift));
        }

        /// <summary>
        /// Общий статус заказа по кухням: все готовы — «Готов»; хоть одна готовит — «Готовится»;
        /// хоть одна приняла — «Принят». Никогда не откатывает заказ назад.
        /// </summary>
        public static string Overall(IReadOnlyCollection<string> stationStatuses, string orderStatus)
        {
            if (stationStatuses.Count == 0) return orderStatus;
            var ranks = stationStatuses.Select(Rank).ToList();
            var target = ranks.All(r => r >= 3) ? 3 : ranks.Any(r => r >= 2) ? 2 : ranks.Any(r => r >= 1) ? 1 : 0;
            var current = Rank(orderStatus);
            return current < 0 || target <= current ? orderStatus : FromRank(target);
        }

        /// <summary>
        /// Статус части заказа на кухне с учётом готовности блюд: все блюда кухни готовы — «Готов»,
        /// часть готова — как минимум «Готовится».
        /// </summary>
        public static string Effective(string? stationStatus, string orderStatus, int itemsTotal, int itemsReady, bool orderHasMarks = false)
        {
            var baseStatus = Effective(stationStatus, orderStatus, orderHasMarks);
            if (itemsTotal > 0 && itemsReady >= itemsTotal) return OrderStatuses.Ready;
            if (itemsReady > 0 && Rank(baseStatus) < 2) return OrderStatuses.Preparing;
            return baseStatus;
        }

        /// <summary>Сколько можно отметить: готово — от 0 до количества, отдано — от 0 до готового</summary>
        public static (int Ready, int Served) Clamp(int quantity, int ready, int served)
        {
            ready = Math.Clamp(ready, 0, quantity);
            served = Math.Clamp(served, 0, ready);
            return (ready, served);
        }

        /// <summary>Кухня позиции: своя у блюда, иначе у карточки, иначе кухня по умолчанию</summary>
        public static int StationFor(int? own, int? parent, int defaultStation) => own ?? parent ?? defaultStation;
    }
}
