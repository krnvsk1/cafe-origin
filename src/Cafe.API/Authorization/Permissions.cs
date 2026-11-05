namespace Cafe.API.Authorization
{
    /// <summary>
    /// Каталог разрешений: окна и функции, доступ к которым админ раздаёт ролям.
    /// Ключи хранятся в базе (role_claims), поэтому их нельзя переименовывать.
    /// </summary>
    public static class Permissions
    {
        public const string ClaimType = "permission";

        public const string Dashboard = "dashboard";
        public const string Analytics = "analytics";

        public const string OrdersView = "orders.view";
        public const string OrdersManage = "orders.manage";
        public const string Kitchen = "kitchen";
        public const string Pos = "pos";
        public const string Waiter = "waiter";
        public const string Voids = "voids";
        public const string TablesManage = "tables";
        public const string Shift = "shift";
        public const string ChecksAll = "checks.all";
        public const string ShiftApprove = "shift.approve";
        public const string ShiftSelf = "shift.self";
        public const string Delivery = "delivery";
        public const string CouriersAssign = "couriers.assign";

        public const string Menu = "menu";
        public const string MenuImport = "menu.import";
        public const string StopList = "stoplist";
        public const string Zones = "zones";

        public const string PromoCodes = "promocodes";
        public const string Reviews = "reviews";
        public const string Points = "points";

        public const string InventoryView = "inventory.view";
        public const string InventoryManage = "inventory.manage";
        public const string TechCards = "techcards";
        public const string Chz = "chz";

        public const string Finance = "finance";

        public const string Haccp = "haccp";
        public const string HaccpManage = "haccp.manage";

        public const string UsersView = "users.view";
        public const string UsersRoles = "users.roles";
        public const string Roles = "roles";
        public const string Settings = "settings";

        public record Definition(string Key, string Group, string Title, string Description);

        public static readonly IReadOnlyList<Definition> All = new List<Definition>
        {
            new(Dashboard, "Обзор", "Дашборд", "Сводка за день: выручка, заказы, средний чек"),
            new(Analytics, "Обзор", "Аналитика", "Отчёты по продажам, популярные блюда, клиенты"),

            new(OrdersView, "Заказы", "Все заказы", "Видеть заказы всех клиентов и получать уведомления о новых"),
            new(OrdersManage, "Заказы", "Управление заказами", "Менять статус, редактировать и удалять заказы"),
            new(Pos, "Заказы", "Касса", "Принимать заказы у стойки — в зале и с собой, отмечать оплату и выдачу"),
            new(Waiter, "Заказы", "Официант", "Открывать чеки на столы с телефона, отправлять заказы на кухню, закрывать чек при оплате"),
            new(Voids, "Заказы", "Отменённые блюда", "Видеть, какие блюда официанты убрали из чеков: кто, когда, почему, списано ли со склада"),
            new(Shift, "Заказы", "Смена заведения", "Открывать и закрывать смену заведения на день — без неё официанты не начинают работу"),
            new(ShiftApprove, "Заказы", "Подтверждение смен официантов", "Подтверждать или отклонять открытие смены официанта — пока смена не подтверждена, официант не может открывать счета"),
            new(ShiftSelf, "Заказы", "Смена официанта без подтверждения", "Открывать свою смену официанта сразу, без подтверждения менеджера или администратора"),
            new(ChecksAll, "Заказы", "Все чеки и смены", "Видеть чеки всех официантов и все смены, открывать чужие счета и передавать их другому официанту"),
            new(TablesManage, "Заказы", "Зал и столы", "Расставлять столы и перегородки на схеме зала, добавлять и переименовывать столы"),
            new(Kitchen, "Заказы", "Кухня", "Экран кухни: принимать заказы и отмечать готовность"),
            new(Delivery, "Заказы", "Доставка", "Экран курьера: брать заказы и отмечать доставку"),
            new(CouriersAssign, "Заказы", "Назначение курьеров", "Назначать курьера на заказ"),

            new(Menu, "Меню", "Товары и добавки", "Создавать и редактировать позиции, фото и добавки"),
            new(MenuImport, "Меню", "Импорт меню", "Загружать меню с сайта заведения (платформа FoodPicasso)"),
            new(StopList, "Меню", "Стоп-лист", "Временно снимать позиции и добавки с продажи"),
            new(Zones, "Меню", "Зоны доставки", "Границы зон, цены и время доставки"),

            new(PromoCodes, "Маркетинг", "Промокоды", "Создавать и отключать промокоды"),
            new(Reviews, "Маркетинг", "Отзывы", "Читать отзывы и отвечать на них"),
            new(Points, "Маркетинг", "Баллы клиентов", "Начислять и списывать бонусные баллы вручную"),

            new(InventoryView, "Склад", "Склад: просмотр", "Остатки, документы, блюда и отчёты по себестоимости"),
            new(InventoryManage, "Склад", "Склад: документы", "Проводить приходы, списания и инвентаризации, вести номенклатуру и поставщиков"),
            new(TechCards, "Склад", "Техкарты", "Создавать и менять технологические карты"),
            new(Chz, "Склад", "Честный знак", "Выводить из оборота маркированный товар (подпись своим сертификатом КЭП)"),

            new(Haccp, "Журналы ХАССП", "Журналы ХАССП", "Заполнять и смотреть журналы: здоровье сотрудников, температура холодильников, бракераж, фритюр, уборка, контрольные точки; печать и выгрузка"),
            new(HaccpManage, "Журналы ХАССП", "Журналы ХАССП: управление", "Оборудование и нормы, контрольные точки, сотрудники и медкнижки, исправление чужих и старых записей"),

            new(Finance, "Финансы", "Платежи поставщикам", "Предстоящие платежи по накладным СБИС: суммы, сроки, реквизиты, отметка об оплате"),

            new(UsersView, "Сотрудники и доступ", "Пользователи", "Список пользователей и их данные"),
            new(UsersRoles, "Сотрудники и доступ", "Назначение ролей", "Выдавать и снимать роли у пользователей"),
            new(Roles, "Сотрудники и доступ", "Роли и доступы", "Создавать роли и настраивать их права"),
            new(Settings, "Сотрудники и доступ", "Настройка интеграций", "Подключение ЕГАИС, Честного знака, СБИС и других интеграций"),
        };

        public static readonly HashSet<string> Keys = All.Select(p => p.Key).ToHashSet();

        /// <summary>Права встроенных ролей при первом запуске (дальше их меняет админ)</summary>
        public static readonly IReadOnlyDictionary<string, string[]> Defaults = new Dictionary<string, string[]>
        {
            ["manager"] = new[]
            {
                Dashboard, Analytics, OrdersView, OrdersManage, Pos, Waiter, Voids, TablesManage, Shift, ShiftApprove, ShiftSelf, ChecksAll, Kitchen, Delivery, CouriersAssign,
                Menu, StopList, Zones, PromoCodes, Reviews, Points, UsersView, InventoryView, Haccp, HaccpManage
            },
            ["courier"] = new[] { OrdersView, Delivery },
            ["user"] = Array.Empty<string>(),
        };
    }
}
