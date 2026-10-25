using System.Globalization;
using System.Net;
using System.Text;

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
    /// Выгрузки iikoServer: вход как на веб-страницу iiko (/resto, форма j_spring_security_check — лицензия REST API не нужна),
    /// затем CSV номенклатуры (goods.csv) и техкарт (assemblyCharts.csv) из /resto/service/export/csv/.
    /// </summary>
    public class IikoClient
    {
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

        /// <summary>Скачать номенклатуру и действующие техкарты</summary>
        public async Task<IikoSnapshot> FetchAsync(string url, string login, string password, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(login)) throw new IikoException("Укажите логин iiko");
            var baseUrl = NormalizeBaseUrl(url);

            // Своя «сессия браузера» с cookie на каждый импорт
            using var handler = new HttpClientHandler { CookieContainer = new CookieContainer(), AllowAutoRedirect = true };
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(120) };
            try
            {
                using (var loginRes = await http.PostAsync($"{baseUrl}/j_spring_security_check", new FormUrlEncodedContent(new Dictionary<string, string>
                       {
                           ["j_username"] = login.Trim(),
                           ["j_password"] = password ?? ""
                       }), ct))
                {
                    var final = loginRes.RequestMessage?.RequestUri?.ToString() ?? "";
                    if (final.Contains("error", StringComparison.OrdinalIgnoreCase))
                        throw new IikoException("iiko не пустил: неверный логин или пароль");
                }

                var goods = await CsvAsync(http, $"{baseUrl}/service/export/csv/goods.csv", "номенклатура", ct);
                var charts = await CsvAsync(http, $"{baseUrl}/service/export/csv/assemblyCharts.csv", "техкарты", ct);

                var snapshot = new IikoSnapshot();
                foreach (var p in ParseGoodsCsv(goods)) snapshot.Products[p.Id] = p;
                snapshot.Charts = ActiveCharts(ParseChartsCsv(charts), DateTime.Today);
                return snapshot;
            }
            catch (HttpRequestException ex)
            {
                throw new IikoException($"Нет связи с сервером iiko: {ex.Message}. Проверьте адрес и что порт открыт в брандмауэре");
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new IikoException("Сервер iiko не ответил вовремя");
            }
            finally
            {
                try { using var _ = await http.GetAsync($"{baseUrl}/j_spring_security_logout", CancellationToken.None); } catch { /* ignore */ }
            }
        }

        private static async Task<string> CsvAsync(HttpClient http, string url, string what, CancellationToken ct)
        {
            using var res = await http.GetAsync(url, ct);
            var text = await res.Content.ReadAsStringAsync(ct);
            var type = res.Content.Headers.ContentType?.MediaType ?? "";
            if (res.StatusCode != HttpStatusCode.OK)
                throw new IikoException($"iiko не отдал {what}: HTTP {(int)res.StatusCode}");
            if (type.Contains("html", StringComparison.OrdinalIgnoreCase) || text.TrimStart().StartsWith('<'))
                throw new IikoException("iiko не пустил: неверный логин или пароль (вместо выгрузки — страница входа)");
            return text;
        }

        // ---------- Разбор CSV (разделитель «;», первая строка — заголовки) ----------

        public static List<Dictionary<string, string>> ParseCsv(string text)
        {
            var lines = text.TrimStart('\uFEFF').Split('\n');
            var rows = new List<Dictionary<string, string>>();
            if (lines.Length == 0) return rows;
            var header = SplitLine(lines[0].TrimEnd('\r')).Select(h => h.Trim().ToUpperInvariant()).ToList();
            foreach (var raw in lines.Skip(1))
            {
                var line = raw.TrimEnd('\r');
                if (line.Trim().Length == 0) continue;
                var cells = SplitLine(line);
                var row = new Dictionary<string, string>();
                for (var i = 0; i < header.Count && i < cells.Count; i++)
                    if (header[i].Length > 0) row[header[i]] = cells[i];
                rows.Add(row);
            }
            return rows;
        }

        /// <summary>Строка CSV с «;»: кавычки учитываем, только если поле начинается с кавычки</summary>
        private static List<string> SplitLine(string line)
        {
            var cells = new List<string>();
            var i = 0;
            while (i <= line.Length)
            {
                if (i < line.Length && line[i] == '"')
                {
                    var sb = new StringBuilder();
                    i++;
                    while (i < line.Length)
                    {
                        if (line[i] == '"' && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i += 2; }
                        else if (line[i] == '"') { i++; break; }
                        else sb.Append(line[i++]);
                    }
                    cells.Add(sb.ToString());
                    while (i < line.Length && line[i] != ';') i++;
                    i++;
                }
                else
                {
                    var end = line.IndexOf(';', i);
                    if (end < 0) end = line.Length;
                    cells.Add(line[i..end]);
                    i = end + 1;
                }
            }
            return cells;
        }

        private static decimal Dec(string? s) =>
            decimal.TryParse((s ?? "").Trim().Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : 0;

        public static List<IikoProduct> ParseGoodsCsv(string text)
        {
            var rows = ParseCsv(text);
            if (rows.Count > 0 && !rows[0].ContainsKey("NUM"))
                throw new IikoException("goods.csv: нет колонки NUM — формат выгрузки iiko отличается от ожидаемого");
            return rows
                .Where(r => !string.IsNullOrWhiteSpace(r.GetValueOrDefault("NUM")) && !string.IsNullOrWhiteSpace(r.GetValueOrDefault("NAME")))
                .Select(r => new IikoProduct
                {
                    Id = r["NUM"].Trim(),
                    Num = r["NUM"].Trim(),
                    Name = r["NAME"].Trim(),
                    Type = (r.GetValueOrDefault("TYPE") ?? "").Trim().ToUpperInvariant(),
                    Unit = r.GetValueOrDefault("MEASURE_UNIT")?.Trim(),
                })
                .ToList();
        }

        public static List<IikoChart> ParseChartsCsv(string text)
        {
            var rows = ParseCsv(text);
            if (rows.Count > 0 && !rows[0].ContainsKey("PRODUCT_CODE"))
                throw new IikoException("assemblyCharts.csv: нет колонки PRODUCT_CODE — формат выгрузки iiko отличается от ожидаемого");
            // Одна карта = блюдо + дата начала действия
            return rows
                .Where(r => !string.IsNullOrWhiteSpace(r.GetValueOrDefault("PRODUCT_CODE")) && !string.IsNullOrWhiteSpace(r.GetValueOrDefault("ITEM_CODE")))
                .GroupBy(r => (Product: r["PRODUCT_CODE"].Trim(), From: r.GetValueOrDefault("DATE_FROM")?.Trim() ?? ""))
                .Select(g =>
                {
                    var first = g.First();
                    return new IikoChart
                    {
                        AssembledProductId = g.Key.Product,
                        DateFrom = DateTime.TryParseExact(g.Key.From, "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null,
                        AssembledAmount = Dec(first.GetValueOrDefault("AMOUNT")) is > 0 and var a ? a : 1,
                        Technology = g.Select(r => r.GetValueOrDefault("TECHNOLOGY")?.Trim()).FirstOrDefault(t => !string.IsNullOrEmpty(t)),
                        Items = g.Select((r, i) => new IikoChartItem
                        {
                            ProductId = r["ITEM_CODE"].Trim(),
                            AmountIn = Dec(r.GetValueOrDefault("ITEM_GROSS_WEIGHT")),
                            AmountMiddle = Dec(r.GetValueOrDefault("ITEM_NET_WEIGHT")),
                            AmountOut = Dec(r.GetValueOrDefault("ITEM_OUT_WEIGHT")),
                            SortWeight = i
                        }).ToList()
                    };
                })
                .ToList();
        }

        /// <summary>По каждому продукту — карта, действующая на дату (самая поздняя из подходящих)</summary>
        public static Dictionary<string, IikoChart> ActiveCharts(IEnumerable<IikoChart> charts, DateTime today) =>
            charts
                .Where(c => (c.DateFrom == null || c.DateFrom.Value.Date <= today) && (c.DateTo == null || c.DateTo.Value.Date > today))
                .GroupBy(c => c.AssembledProductId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(c => c.DateFrom ?? DateTime.MinValue).First());
    }
}
