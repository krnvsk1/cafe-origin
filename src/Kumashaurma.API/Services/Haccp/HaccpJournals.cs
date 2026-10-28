using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kumashaurma.API.Models;

namespace Kumashaurma.API.Services.Haccp
{
    /// <summary>
    /// Типы граф: text, textarea, number, temperature, humidity, bool, select, datetime, date.
    /// temperature/humidity без своих границ берут норму у объекта (холодильника, склада, ККТ).
    /// </summary>
    public record HaccpField(
        string Key,
        string Label,
        string Type,
        bool Required = false,
        string[]? Options = null,
        string? Unit = null,
        decimal? Min = null,
        decimal? Max = null,
        // Значения select, которые считаются отклонением
        string[]? BadOptions = null,
        // Для bool: какое значение — отклонение
        bool? BadIf = null,
        string? Default = null,
        string? Hint = null,
        // Подставлять значение из прошлой записи журнала (вид жира, средство, комиссия…)
        bool Remember = false);

    /// <summary>
    /// Журнал. Frequency: per_object — ChecksPerDay раз в день по каждому объекту вида ObjectKind;
    /// daily — хотя бы одна запись в день; per_staff — по каждому сотруднику, вышедшему на смену; per_receipt — по приходам.
    /// </summary>
    public record HaccpJournal(
        string Key,
        string Title,
        string Short,
        string Basis,
        string Frequency,
        string? ObjectKind,
        bool UsesStaff,
        IReadOnlyList<HaccpField> Fields,
        string? Description = null);

    public static class HaccpJournals
    {
        public const string Health = "health";
        public const string Fridge = "fridge";
        public const string Storage = "storage";
        public const string Finished = "finished";
        public const string Perishable = "perishable";
        public const string Fryer = "fryer";
        public const string Cleaning = "cleaning";
        public const string Ccp = "ccp";

        private static readonly string[] Organoleptic = { "Соответствует", "Не соответствует" };
        private static readonly string[] FatQuality = { "Отлично", "Хорошо", "Удовлетворительно", "Неудовлетворительно" };

        public static readonly IReadOnlyList<HaccpJournal> All = new List<HaccpJournal>
        {
            new(Health, "Гигиенический журнал (сотрудники)", "Здоровье сотрудников",
                "СанПиН 2.3/2.4.4282-26, прил. 1 (рекомендуемый образец)", "per_staff", null, true,
                new HaccpField[]
                {
                    new("noInfection", "Нет признаков инфекционных заболеваний у сотрудника и членов семьи", "bool", Required: true, BadIf: false, Default: "true"),
                    new("noSkin", "Нет заболеваний верхних дыхательных путей и гнойничковых заболеваний кожи рук и открытых поверхностей тела", "bool", Required: true, BadIf: false, Default: "true"),
                    new("result", "Результат осмотра", "select", Required: true, Options: new[] { "Допущен", "Отстранён" }, BadOptions: new[] { "Отстранён" }, Default: "Допущен"),
                    new("note", "Примечание", "text"),
                },
                "Перед сменой: ответственный осматривает каждого работника, работник подтверждает отсутствие заболеваний."),

            new(Fridge, "Журнал учёта температурного режима холодильного оборудования", "Холодильники",
                "СанПиН 2.3/2.4.4282-26, прил. 2 (рекомендуемый образец)", "per_object", HaccpObjectKinds.Fridge, false,
                new HaccpField[]
                {
                    new("temperature", "Температура", "temperature", Required: true, Unit: "°C"),
                    new("note", "Примечание", "text"),
                }),

            new(Storage, "Журнал учёта температуры и влажности в складских помещениях", "Склад: t° и влажность",
                "СанПиН 2.3/2.4.4282-26, прил. 3 (рекомендуемый образец)", "per_object", HaccpObjectKinds.Storage, false,
                new HaccpField[]
                {
                    new("temperature", "Температура", "temperature", Required: true, Unit: "°C"),
                    new("humidity", "Влажность", "humidity", Required: true, Unit: "%"),
                    new("note", "Примечание", "text"),
                }),

            new(Finished, "Журнал бракеража готовой пищевой продукции", "Бракераж готовой продукции",
                "СанПиН 2.3/2.4.4282-26, прил. 4 (рекомендуемый образец)", "daily", null, false,
                new HaccpField[]
                {
                    new("dish", "Наименование готового блюда", "dish", Required: true),
                    new("madeAt", "Дата и час изготовления блюда", "datetime", Required: true),
                    new("organoleptic", "Результаты органолептической оценки качества", "select", Required: true, Options: Organoleptic, BadOptions: new[] { "Не соответствует" }, Default: "Соответствует"),
                    new("organolepticNote", "Комментарий к оценке (вкус, запах, вид)", "text"),
                    new("allowed", "Разрешение к реализации", "bool", Required: true, BadIf: false, Default: "true"),
                    new("weight", "Результаты взвешивания порционных блюд", "number", Unit: "г"),
                    new("commission", "Члены бракеражной комиссии", "text", Required: true, Hint: "ФИО через запятую", Remember: true),
                    new("note", "Примечание", "text"),
                }),

            new(Perishable, "Журнал бракеража скоропортящейся пищевой продукции", "Бракераж сырья",
                "СанПиН 2.3/2.4.4282-26, прил. 5 (рекомендуемый образец)", "per_receipt", null, false,
                new HaccpField[]
                {
                    new("product", "Наименование пищевой продукции", "text", Required: true),
                    new("packing", "Фасовка", "text"),
                    new("productionDate", "Дата выработки", "date"),
                    new("manufacturer", "Изготовитель", "text"),
                    new("supplier", "Поставщик", "text", Required: true),
                    new("quantity", "Количество поступившей продукции", "text", Required: true, Hint: "например 12 кг, 30 шт"),
                    new("safetyDoc", "Номер документа, подтверждающего безопасность", "text", Required: true, Hint: "декларация, ветсертификат (Меркурий), УПД"),
                    new("organoleptic", "Результаты органолептической оценки", "select", Required: true, Options: Organoleptic, BadOptions: new[] { "Не соответствует" }, Default: "Соответствует"),
                    new("storage", "Условия хранения", "text", Hint: "например +2…+6 °C", Remember: true),
                    new("sellBy", "Конечный срок реализации", "date", Required: true),
                    new("soldAt", "Дата и час фактической реализации", "datetime"),
                    new("note", "Примечание", "text"),
                },
                "При приёмке скоропортящегося сырья. Можно заполнить из прихода на склад."),

            new(Fryer, "Журнал учёта использования фритюрных жиров", "Фритюрные жиры",
                "СанПиН 2.3/2.4.4282-26, п. 44 (контроль качества фритюрных жиров)", "per_object", HaccpObjectKinds.Fryer, false,
                new HaccpField[]
                {
                    new("fatType", "Вид фритюрного жира", "text", Required: true, Remember: true),
                    new("qualityStart", "Органолептическая оценка жира на начало жарки", "select", Required: true, Options: FatQuality, BadOptions: new[] { "Неудовлетворительно" }, Default: "Отлично"),
                    new("product", "Вид продукции", "text", Required: true, Remember: true),
                    new("endedAt", "Время окончания фритюрной жарки", "datetime"),
                    new("qualityEnd", "Органолептическая оценка жира по окончании жарки", "select", Options: FatQuality, BadOptions: new[] { "Неудовлетворительно" }),
                    new("carryOver", "Переходящий остаток", "number", Unit: "кг"),
                    new("disposed", "Утилизированный жир", "number", Unit: "кг"),
                }),

            new(Cleaning, "Журнал уборки и дезинфекции", "Уборка и дезинфекция",
                "Программа производственного контроля (ХАССП)", "daily", null, false,
                new HaccpField[]
                {
                    new("kind", "Вид обработки", "select", Required: true, Options: new[] { "Текущая уборка", "Генеральная уборка", "Дезинфекция" }, Default: "Текущая уборка"),
                    new("area", "Помещение / оборудование", "text", Required: true, Remember: true),
                    new("agent", "Моющее / дезинфицирующее средство и концентрация", "text", Remember: true),
                    new("doneBy", "Кто проводил", "text", Required: true),
                    new("note", "Примечание", "text"),
                }),

            new(Ccp, "Журнал контроля критических контрольных точек (ХАССП)", "Контрольные точки ХАССП",
                "Программа производственного контроля на принципах ХАССП (СанПиН 2.3/2.4.4282-26, п. 4)", "per_object", HaccpObjectKinds.Ccp, false,
                new HaccpField[]
                {
                    new("product", "Продукция / партия", "text", Required: true),
                    new("temperature", "Измеренное значение", "temperature", Required: true, Unit: "°C"),
                    new("note", "Примечание", "text"),
                }),
        };

        public static HaccpJournal? Find(string? key) => All.FirstOrDefault(j => j.Key == key);

        public record CheckResult(JsonObject Values, List<string> Errors, List<string> Deviations);

        /// <summary>
        /// Привести значения к типам граф, проверить обязательные и найти отклонения
        /// (по границам граф, норме объекта и «плохим» значениям).
        /// </summary>
        public static CheckResult Check(HaccpJournal journal, JsonObject? input, HaccpObject? obj, HaccpStaff? staff, DateTime nowUtc)
        {
            var values = new JsonObject();
            var errors = new List<string>();
            var deviations = new List<string>();
            input ??= new JsonObject();

            foreach (var f in journal.Fields)
            {
                var raw = input[f.Key];
                var text = raw switch
                {
                    null => null,
                    JsonValue v when v.TryGetValue<string>(out var s) => s,
                    JsonValue v => v.ToJsonString(),
                    _ => null
                };
                text = text?.Trim();
                if (string.IsNullOrEmpty(text))
                {
                    if (f.Required) errors.Add($"Заполните «{f.Label}»");
                    continue;
                }

                switch (f.Type)
                {
                    case "number":
                    case "temperature":
                    case "humidity":
                    {
                        if (!decimal.TryParse(text.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var n))
                        {
                            errors.Add($"«{f.Label}»: нужно число");
                            continue;
                        }
                        if (f.Type == "humidity" && (n < 0 || n > 100)) { errors.Add($"«{f.Label}»: от 0 до 100 %"); continue; }
                        if (f.Type == "temperature" && (n < -60 || n > 400)) { errors.Add($"«{f.Label}»: проверьте значение"); continue; }
                        if (f.Type == "number" && n < 0) { errors.Add($"«{f.Label}»: не может быть меньше нуля"); continue; }
                        values[f.Key] = n;

                        var (min, max) = f.Type switch
                        {
                            "temperature" => (f.Min ?? obj?.MinValue, f.Max ?? obj?.MaxValue),
                            "humidity" => (f.Min ?? obj?.MinHumidity, f.Max ?? obj?.MaxHumidity),
                            _ => (f.Min, f.Max)
                        };
                        if ((min.HasValue && n < min) || (max.HasValue && n > max))
                            deviations.Add($"{f.Label} {Fmt(n)}{UnitSuffix(f.Unit)} — норма {Range(min, max, f.Unit)}");
                        break;
                    }
                    case "bool":
                    {
                        var b = text is "true" or "1" or "да" or "Да";
                        if (text is not ("true" or "false" or "1" or "0" or "да" or "нет" or "Да" or "Нет")) { errors.Add($"«{f.Label}»: да или нет"); continue; }
                        values[f.Key] = b;
                        if (f.BadIf.HasValue && b == f.BadIf.Value) deviations.Add($"{f.Label}: {(b ? "да" : "нет")}");
                        break;
                    }
                    case "select":
                    {
                        if (f.Options != null && !f.Options.Contains(text)) { errors.Add($"«{f.Label}»: выберите из списка"); continue; }
                        values[f.Key] = text;
                        if (f.BadOptions?.Contains(text) == true) deviations.Add($"{f.Label}: {text}");
                        break;
                    }
                    case "date":
                    {
                        if (!DateTime.TryParseExact(text[..Math.Min(10, text.Length)], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                        {
                            errors.Add($"«{f.Label}»: дата в формате ГГГГ-ММ-ДД");
                            continue;
                        }
                        values[f.Key] = d.ToString("yyyy-MM-dd");
                        break;
                    }
                    case "datetime":
                    {
                        if (!DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var dt))
                        {
                            errors.Add($"«{f.Label}»: дата и время");
                            continue;
                        }
                        values[f.Key] = DateTime.SpecifyKind(dt, DateTimeKind.Utc).ToString("yyyy-MM-ddTHH:mm:ssZ");
                        break;
                    }
                    default:
                        values[f.Key] = text.Length > 1000 ? text[..1000] : text;
                        break;
                }
            }

            // Срок годности скоропортящейся продукции уже истёк при приёмке
            if (journal.Key == Perishable && values["sellBy"] is JsonValue sellBy
                && DateTime.TryParse(sellBy.GetValue<string>(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var until)
                && until.Date < Moscow(nowUtc).Date)
                deviations.Add("Срок реализации истёк");

            // Медкнижка просрочена — к работе не допускается
            if (journal.UsesStaff && staff != null)
            {
                var today = Moscow(nowUtc).Date;
                if (staff.MedicalExamUntil.HasValue && staff.MedicalExamUntil.Value.Date < today)
                    deviations.Add($"Медосмотр просрочен ({staff.MedicalExamUntil.Value:dd.MM.yyyy})");
                if (staff.HygieneTrainingUntil.HasValue && staff.HygieneTrainingUntil.Value.Date < today)
                    deviations.Add($"Гигиеническая аттестация просрочена ({staff.HygieneTrainingUntil.Value:dd.MM.yyyy})");
            }

            return new CheckResult(values, errors, deviations);
        }

        /// <summary>Значение графы для печати и выгрузки</summary>
        public static string Display(HaccpField f, JsonNode? v)
        {
            if (v == null) return "";
            switch (f.Type)
            {
                case "bool":
                    return v.GetValueKind() == JsonValueKind.True ? "Да" : v.GetValueKind() == JsonValueKind.False ? "Нет" : v.ToString();
                case "number":
                case "temperature":
                case "humidity":
                    return v.GetValueKind() == JsonValueKind.Number ? Fmt(v.GetValue<decimal>()) + UnitSuffix(f.Unit) : v.ToString();
                case "date":
                    return DateTime.TryParse(v.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d.ToString("dd.MM.yyyy") : v.ToString();
                case "datetime":
                    return DateTime.TryParse(v.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var dt)
                        ? Moscow(DateTime.SpecifyKind(dt, DateTimeKind.Utc)).ToString("dd.MM.yyyy HH:mm") : v.ToString();
                default:
                    return v.ToString();
            }
        }

        public static string Range(decimal? min, decimal? max, string? unit)
        {
            var u = UnitSuffix(unit);
            return (min, max) switch
            {
                ({ } a, { } b) => $"{Signed(a)}…{Signed(b)}{u}",
                ({ } a, null) => $"не ниже {Signed(a)}{u}",
                (null, { } b) => $"не выше {Signed(b)}{u}",
                _ => "не задана"
            };
        }

        private static string Signed(decimal v) => v > 0 ? "+" + Fmt(v) : Fmt(v);
        private static string Fmt(decimal v) => v.ToString("0.#", CultureInfo.GetCultureInfo("ru-RU"));
        private static string UnitSuffix(string? unit) => string.IsNullOrEmpty(unit) ? "" : unit == "%" ? " %" : $" {unit}";

        /// <summary>Москва без перехода на летнее время</summary>
        public static DateTime Moscow(DateTime utc) => utc.AddHours(3);
        public static DateTime MoscowDayStartUtc(DateTime utc) => Moscow(utc).Date.AddHours(-3);

        public static string Serialize(JsonObject o) => o.ToJsonString(new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }
}
