using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Kumashaurma.API.Services.Iiko
{
    public class IikoException : Exception
    {
        public IikoException(string message) : base(message) { }
    }

    public class IikoProduct
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        /// <summary>DISH — блюдо, GOODS — товар, PREPARED — заготовка (полуфабрикат), MODIFIER — модификатор, SERVICE, OUTER</summary>
        public string Type { get; set; } = string.Empty;
        public string? Num { get; set; }
        public string? MainUnitId { get; set; }
        /// <summary>Название единицы (кг, л, шт, порц)</summary>
        public string? Unit { get; set; }
    }

    public class IikoChartItem
    {
        public string ProductId { get; set; } = string.Empty;
        /// <summary>Брутто, в основной единице ингредиента</summary>
        public decimal AmountIn { get; set; }
        /// <summary>Нетто после холодной обработки</summary>
        public decimal AmountMiddle { get; set; }
        /// <summary>Выход после горячей обработки</summary>
        public decimal AmountOut { get; set; }
        public decimal SortWeight { get; set; }
    }

    public class IikoChart
    {
        public string AssembledProductId { get; set; } = string.Empty;
        public DateTime? DateFrom { get; set; }
        public DateTime? DateTo { get; set; }
        /// <summary>Выход: сколько единиц блюда/заготовки получается по карте</summary>
        public decimal AssembledAmount { get; set; } = 1;
        public string? Technology { get; set; }
        public List<IikoChartItem> Items { get; set; } = new();
    }

    public class IikoSnapshot
    {
        public Dictionary<string, IikoProduct> Products { get; set; } = new();
        /// <summary>Действующая сегодня карта по каждому продукту</summary>
        public Dictionary<string, IikoChart> Charts { get; set; } = new();
    }

    /// <summary>
    /// API iikoServer (/resto/api): вход по логину и SHA1 пароля → ключ сессии (занимает лицензию — обязательно выходим),
    /// номенклатура, единицы измерения, технологические карты.
    /// </summary>
    public class IikoClient
    {
        private readonly HttpClient _http;
        public IikoClient(HttpClient http) => _http = http;

        /// <summary>«192.168.1.10:8080» / «http://…:8080/resto/» → «http://…:8080/resto»</summary>
        public static string NormalizeBaseUrl(string url)
        {
            var u = (url ?? "").Trim().TrimEnd('/');
            if (u.Length == 0) throw new IikoException("Укажите адрес сервера iiko, например http://192.168.1.10:8080/resto");
            if (!u.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !u.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                u = "http://" + u;
            var resto = u.IndexOf("/resto", StringComparison.OrdinalIgnoreCase);
            u = resto >= 0 ? u[..(resto + "/resto".Length)] : u + "/resto";
            if (!Uri.TryCreate(u, UriKind.Absolute, out _)) throw new IikoException("Не похоже на адрес сервера iiko");
            return u;
        }

        public static string Sha1(string password) =>
            Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(password ?? ""))).ToLowerInvariant();

        /// <summary>Скачать номенклатуру и действующие техкарты (с выходом из сессии в конце)</summary>
        public async Task<IikoSnapshot> FetchAsync(string url, string login, string password, CancellationToken ct = default)
        {
            var baseUrl = NormalizeBaseUrl(url);
            var key = await LoginAsync(baseUrl, login, password, ct);
            try
            {
                var snapshot = new IikoSnapshot();

                var units = new Dictionary<string, string>();
                try
                {
                    var unitsJson = await GetAsync(baseUrl, "/api/v2/entities/list?rootType=MeasureUnit&includeDeleted=false", key, ct);
                    units = ParseUnits(unitsJson);
                }
                catch (IikoException) { /* не критично — единицы угадаем по названию */ }

                var productsJson = await GetAsync(baseUrl, "/api/v2/entities/products/list?includeDeleted=false", key, ct);
                foreach (var p in ParseProducts(productsJson))
                {
                    if (p.MainUnitId != null && units.TryGetValue(p.MainUnitId, out var unit)) p.Unit = unit;
                    snapshot.Products[p.Id] = p;
                }

                var today = DateTime.Today;
                var chartsJson = await GetAsync(baseUrl,
                    $"/api/v2/assemblyCharts/getAll?dateFrom={today:yyyy-MM-dd}&dateTo={today.AddDays(1):yyyy-MM-dd}&includeDeletedProducts=false&includePreparedCharts=false",
                    key, ct);
                snapshot.Charts = ActiveCharts(ParseCharts(chartsJson), today);
                return snapshot;
            }
            finally
            {
                try { await GetRawAsync($"{baseUrl}/api/logout?key={Uri.EscapeDataString(key)}", CancellationToken.None); } catch { /* ignore */ }
            }
        }

        public async Task<string> LoginAsync(string baseUrl, string login, string password, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(login)) throw new IikoException("Укажите логин iiko");
            var (status, body) = await GetRawAsync($"{baseUrl}/api/auth?login={Uri.EscapeDataString(login.Trim())}&pass={Sha1(password)}", ct);
            if (status == HttpStatusCode.OK && body.Trim().Length is > 10 and < 200 && !body.TrimStart().StartsWith('<'))
                return body.Trim();
            if (status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden || body.Contains("password", StringComparison.OrdinalIgnoreCase))
                throw new IikoException($"iiko не пустил: неверный логин или пароль ({Short(body)})");
            if (body.Contains("licen", StringComparison.OrdinalIgnoreCase) || body.Contains("лиценз", StringComparison.OrdinalIgnoreCase))
                throw new IikoException($"iiko: нет свободной лицензии на подключение — закройте лишний iikoOffice и повторите ({Short(body)})");
            throw new IikoException($"iiko ответил {(int)status}: {Short(body)}");
        }

        private async Task<string> GetAsync(string baseUrl, string path, string key, CancellationToken ct)
        {
            var sep = path.Contains('?') ? '&' : '?';
            var (status, body) = await GetRawAsync($"{baseUrl}{path}{sep}key={Uri.EscapeDataString(key)}", ct);
            if (status != HttpStatusCode.OK) throw new IikoException($"iiko ответил {(int)status} на {path.Split('?')[0]}: {Short(body)}");
            return body;
        }

        private async Task<(HttpStatusCode Status, string Body)> GetRawAsync(string url, CancellationToken ct)
        {
            try
            {
                using var res = await _http.GetAsync(url, ct);
                return (res.StatusCode, await res.Content.ReadAsStringAsync(ct));
            }
            catch (HttpRequestException ex)
            {
                throw new IikoException($"Нет связи с сервером iiko: {ex.Message}. Проверьте адрес и что порт открыт в брандмауэре");
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new IikoException("Сервер iiko не ответил вовремя");
            }
        }

        private static string Short(string s)
        {
            s = s.Trim().Replace('\n', ' ').Replace('\r', ' ');
            return s.Length > 300 ? s[..300] + "…" : s;
        }

        // ---------- Разбор ответов ----------

        private static JsonElement Root(string json, string what)
        {
            try { return JsonDocument.Parse(json).RootElement.Clone(); }
            catch (JsonException) { throw new IikoException($"iiko вернул не JSON ({what}): {Short(json)}"); }
        }

        private static string? Str(JsonElement e, string name) =>
            e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v)
                ? v.ValueKind switch { JsonValueKind.String => v.GetString(), JsonValueKind.Number => v.GetRawText(), _ => null }
                : null;

        private static decimal Dec(JsonElement e, string name) =>
            e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v)
                ? v.ValueKind == JsonValueKind.Number ? v.GetDecimal()
                  : v.ValueKind == JsonValueKind.String && decimal.TryParse(v.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : 0
                : 0;

        private static bool Bool(JsonElement e, string name) =>
            e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

        public static Dictionary<string, string> ParseUnits(string json)
        {
            var root = Root(json, "единицы");
            var result = new Dictionary<string, string>();
            if (root.ValueKind != JsonValueKind.Array) return result;
            foreach (var u in root.EnumerateArray())
            {
                var id = Str(u, "id");
                var name = Str(u, "name") ?? Str(u, "code");
                if (id != null && name != null) result[id] = name;
            }
            return result;
        }

        public static List<IikoProduct> ParseProducts(string json)
        {
            var root = Root(json, "номенклатура");
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("products", out var inner)) root = inner;
            if (root.ValueKind != JsonValueKind.Array) throw new IikoException("iiko вернул номенклатуру в неожиданном формате");
            var list = new List<IikoProduct>();
            foreach (var p in root.EnumerateArray())
            {
                if (Bool(p, "deleted")) continue;
                var id = Str(p, "id");
                var name = Str(p, "name");
                if (id == null || string.IsNullOrWhiteSpace(name)) continue;
                list.Add(new IikoProduct
                {
                    Id = id,
                    Name = name.Trim(),
                    Type = (Str(p, "type") ?? Str(p, "productType") ?? "").ToUpperInvariant(),
                    Num = Str(p, "num"),
                    MainUnitId = Str(p, "mainUnit") ?? Str(p, "measureUnit"),
                });
            }
            return list;
        }

        public static List<IikoChart> ParseCharts(string json)
        {
            var root = Root(json, "техкарты");
            var arrays = new List<JsonElement>();
            if (root.ValueKind == JsonValueKind.Array) arrays.Add(root);
            else
                foreach (var name in new[] { "assemblyCharts", "preparedCharts" })
                    if (root.TryGetProperty(name, out var a) && a.ValueKind == JsonValueKind.Array) arrays.Add(a);
            if (arrays.Count == 0) throw new IikoException("iiko вернул техкарты в неожиданном формате");

            var list = new List<IikoChart>();
            foreach (var arr in arrays)
                foreach (var c in arr.EnumerateArray())
                {
                    var productId = Str(c, "assembledProductId") ?? Str(c, "productId");
                    if (productId == null) continue;
                    var chart = new IikoChart
                    {
                        AssembledProductId = productId,
                        DateFrom = Date(Str(c, "dateFrom")),
                        DateTo = Date(Str(c, "dateTo")),
                        AssembledAmount = Dec(c, "assembledAmount") is > 0 and var a ? a : 1,
                        Technology = Str(c, "technologyDescription"),
                    };
                    if (c.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
                        foreach (var i in items.EnumerateArray())
                        {
                            var pid = Str(i, "productId");
                            if (pid == null) continue;
                            chart.Items.Add(new IikoChartItem
                            {
                                ProductId = pid,
                                AmountIn = Dec(i, "amountIn"),
                                AmountMiddle = Dec(i, "amountMiddle"),
                                AmountOut = Dec(i, "amountOut"),
                                SortWeight = Dec(i, "sortWeight"),
                            });
                        }
                    list.Add(chart);
                }
            return list;
        }

        /// <summary>По каждому продукту — карта, действующая на дату (самая поздняя из подходящих)</summary>
        public static Dictionary<string, IikoChart> ActiveCharts(IEnumerable<IikoChart> charts, DateTime today) =>
            charts
                .Where(c => (c.DateFrom == null || c.DateFrom.Value.Date <= today) && (c.DateTo == null || c.DateTo.Value.Date > today))
                .GroupBy(c => c.AssembledProductId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(c => c.DateFrom ?? DateTime.MinValue).First());

        private static DateTime? Date(string? s) =>
            DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
    }
}
