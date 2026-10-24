using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Kumashaurma.API.Data;
using Kumashaurma.API.Models;

namespace Kumashaurma.API.Services
{
    /// <summary>
    /// Импорт меню с сайта kumashaurma.ru (платформа foodpicasso):
    /// разделы → категории, блюда → карточки, размеры → варианты, модификаторы → добавки, фото → uploads.
    /// Повторный запуск обновляет цены и описания и добавляет новое, ничего не удаляя.
    /// </summary>
    public class MenuImportService
    {
        public const string DefaultSourceUrl = "https://kumashaurma.ru/";

        private readonly ApplicationDbContext _context;
        private readonly HttpClient _http;
        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<MenuImportService> _logger;

        public MenuImportService(ApplicationDbContext context, HttpClient http, IWebHostEnvironment environment, ILogger<MenuImportService> logger)
        {
            _context = context;
            _http = http;
            _environment = environment;
            _logger = logger;
        }

        public record ImportResult(int Categories, int CardsCreated, int CardsUpdated, int Variants, int Addons, int Images, List<string> Warnings);

        // ---------- Модель исходных данных ----------

        public record SourceOption(string Name, decimal Price);
        public record SourceAddonGroup(string Title, bool SingleChoice, bool Required, int Min, int Max, List<SourceOption> Options);
        public record SourceDish(int Id, string Name, string Description, string? Badge, List<SourceOption> Sizes, List<SourceAddonGroup> AddonGroups, string? ImageUrl);
        public record SourceSection(string Title, List<SourceDish> Dishes);

        public async Task<ImportResult> ImportAsync(string? sourceUrl, bool downloadImages, CancellationToken ct)
        {
            var url = string.IsNullOrWhiteSpace(sourceUrl) ? DefaultSourceUrl : sourceUrl;
            var html = await _http.GetStringAsync(url, ct);
            var sections = Parse(html);
            if (sections.Count == 0)
                throw new InvalidOperationException("На странице не найдено меню (разметка сайта могла измениться)");

            var warnings = new List<string>();
            int created = 0, updated = 0, variants = 0, images = 0;

            // Добавки: группа → категория добавок, опция → добавка
            var addonCategories = await _context.AddonCategories.Include(c => c.Addons).ToListAsync(ct);
            var addonCache = new Dictionary<(string Group, string Name), Addon>();
            int addonsCreated = 0;

            async Task<List<(Addon Addon, AddonCategory Category)>> EnsureAddonsAsync(SourceAddonGroup group)
            {
                var category = addonCategories.FirstOrDefault(c => string.Equals(c.Name, group.Title, StringComparison.OrdinalIgnoreCase));
                if (category == null)
                {
                    category = new AddonCategory
                    {
                        Name = Truncate(group.Title, 100),
                        IsRequired = group.Required,
                        MinSelections = group.Min,
                        MaxSelections = group.SingleChoice ? 1 : group.Max,
                        IsActive = true,
                        DisplayOrder = addonCategories.Count,
                        CreatedAt = DateTime.UtcNow
                    };
                    _context.AddonCategories.Add(category);
                    await _context.SaveChangesAsync(ct);
                    addonCategories.Add(category);
                }

                var result = new List<(Addon, AddonCategory)>();
                var order = 0;
                foreach (var option in group.Options)
                {
                    var key = (category.Name.ToLowerInvariant(), option.Name.ToLowerInvariant());
                    if (!addonCache.TryGetValue(key, out var addon))
                    {
                        addon = category.Addons.FirstOrDefault(a => string.Equals(a.Name, option.Name, StringComparison.OrdinalIgnoreCase));
                        if (addon == null)
                        {
                            addon = new Addon
                            {
                                Name = Truncate(option.Name, 100),
                                Price = option.Price,
                                AddonCategoryId = category.Id,
                                IsAvailable = true,
                                DisplayOrder = order,
                                CreatedAt = DateTime.UtcNow
                            };
                            _context.Addons.Add(addon);
                            category.Addons.Add(addon);
                            addonsCreated++;
                        }
                        else
                        {
                            addon.Price = option.Price;
                        }
                        addonCache[key] = addon;
                    }
                    order++;
                    result.Add((addon, category));
                }
                await _context.SaveChangesAsync(ct);
                return result;
            }

            var existingCards = await _context.Shawarmas
                .Where(s => s.ParentId == null)
                .Include(s => s.Children)
                .Include(s => s.Images)
                .ToListAsync(ct);

            var sortOrder = 0;
            foreach (var section in sections)
            {
                foreach (var dish in section.Dishes)
                {
                    sortOrder++;
                    var name = Truncate(dish.Name, 100);
                    var card = existingCards.FirstOrDefault(c => c.Name == name);
                    if (card != null && !string.IsNullOrEmpty(card.Category) && card.Category != section.Title)
                    {
                        // Такое же название в другом разделе — отдельная карточка
                        name = Truncate($"{dish.Name} ({section.Title})", 100);
                        card = existingCards.FirstOrDefault(c => c.Name == name);
                    }

                    var spicy = Regex.IsMatch($"{dish.Name} {dish.Description}", "остр|халапеньо|🔥|чили", RegexOptions.IgnoreCase);
                    var cheesy = Regex.IsMatch(dish.Name, "сыр|чиз", RegexOptions.IgnoreCase);

                    if (card == null)
                    {
                        card = new Shawarma { CreatedAt = DateTime.UtcNow, IsAvailable = true };
                        _context.Shawarmas.Add(card);
                        existingCards.Add(card);
                        created++;
                    }
                    else
                    {
                        updated++;
                    }

                    card.Name = name;
                    card.Category = Truncate(section.Title, 50);
                    card.Description = Truncate(dish.Description, 500);
                    card.Badge = dish.Badge;
                    card.IsPromo = dish.Badge == "promo";
                    card.IsSpicy = spicy;
                    card.HasCheese = cheesy;
                    card.SortOrder = sortOrder;
                    card.Price = dish.Sizes.Count > 0 ? dish.Sizes.Min(s => s.Price) : 0;
                    card.UpdatedAt = DateTime.UtcNow;
                    await _context.SaveChangesAsync(ct);

                    // Варианты (размеры). У блюда без размеров — один вариант «Стандарт»
                    var sizes = dish.Sizes.Count > 0 ? dish.Sizes : new List<SourceOption> { new("Стандарт", card.Price) };
                    var childOrder = 0;
                    var children = new List<Shawarma>();
                    foreach (var size in sizes)
                    {
                        var childName = Truncate(string.IsNullOrWhiteSpace(size.Name) ? "Стандарт" : size.Name, 100);
                        // Одинаковые названия размеров внутри карточки (бывает в исходных данных)
                        if (children.Any(c => c.Name == childName))
                            childName = Truncate($"{childName} ({size.Price:0} ₽)", 100);

                        var child = card.Children.FirstOrDefault(c => c.Name == childName);
                        if (child == null)
                        {
                            child = new Shawarma { ParentId = card.Id, CreatedAt = DateTime.UtcNow, IsAvailable = true };
                            _context.Shawarmas.Add(child);
                            card.Children.Add(child);
                        }
                        child.Name = childName;
                        child.Price = size.Price;
                        child.Category = card.Category;
                        child.Description = card.Description;
                        child.IsSpicy = spicy;
                        child.HasCheese = cheesy;
                        child.SortOrder = childOrder++;
                        child.UpdatedAt = DateTime.UtcNow;
                        children.Add(child);
                        variants++;
                    }
                    await _context.SaveChangesAsync(ct);

                    // Добавки привязываются к каждому варианту
                    foreach (var group in dish.AddonGroups.Where(g => g.Options.Count > 0))
                    {
                        var addons = await EnsureAddonsAsync(group);
                        foreach (var child in children)
                        {
                            var linked = await _context.ShawarmaAddons
                                .Where(sa => sa.ShawarmaId == child.Id)
                                .Select(sa => sa.AddonId)
                                .ToListAsync(ct);
                            foreach (var (addon, _) in addons.Where(a => !linked.Contains(a.Addon.Id)))
                            {
                                _context.ShawarmaAddons.Add(new ShawarmaAddon
                                {
                                    ShawarmaId = child.Id,
                                    AddonId = addon.Id,
                                    MaxQuantity = group.SingleChoice ? 1 : 5
                                });
                            }
                        }
                        await _context.SaveChangesAsync(ct);
                    }

                    // Фото: только если у карточки ещё нет своего
                    if (!string.IsNullOrEmpty(dish.ImageUrl) && !card.Images.Any())
                    {
                        var path = downloadImages ? await DownloadImageAsync(dish.ImageUrl, ct) : dish.ImageUrl;
                        if (path == null)
                        {
                            warnings.Add($"Не удалось скачать фото «{dish.Name}» — сохранена ссылка");
                            path = dish.ImageUrl;
                        }
                        var image = new ShawarmaImage
                        {
                            ShawarmaId = card.Id,
                            FileName = Truncate(Path.GetFileName(path.Split('?')[0]), 255),
                            FilePath = path,
                            IsPrimary = true,
                            CreatedAt = DateTime.UtcNow
                        };
                        _context.ShawarmaImages.Add(image);
                        card.Images.Add(image);
                        await _context.SaveChangesAsync(ct);
                        images++;
                    }
                }
            }

            _logger.LogInformation("📥 Импорт меню: {Created} новых, {Updated} обновлено, {Variants} вариантов, {Addons} добавок, {Images} фото",
                created, updated, variants, addonsCreated, images);

            return new ImportResult(sections.Count, created, updated, variants, addonsCreated, images, warnings);
        }

        private async Task<string?> DownloadImageAsync(string url, CancellationToken ct)
        {
            try
            {
                using var response = await _http.GetAsync(url, ct);
                if (!response.IsSuccessStatusCode) return null;
                var bytes = await response.Content.ReadAsByteArrayAsync(ct);
                if (bytes.Length == 0 || bytes.Length > 8 * 1024 * 1024) return null;

                var ext = response.Content.Headers.ContentType?.MediaType switch
                {
                    "image/webp" => ".webp",
                    "image/png" => ".png",
                    "image/jpeg" => ".jpg",
                    _ => Path.GetExtension(new Uri(url).AbsolutePath) is { Length: > 0 and <= 5 } e ? e : ".webp"
                };

                var folder = Path.Combine(_environment.WebRootPath ?? Path.Combine(_environment.ContentRootPath, "wwwroot"), "uploads");
                Directory.CreateDirectory(folder);
                var fileName = $"{Guid.NewGuid()}{ext}";
                await File.WriteAllBytesAsync(Path.Combine(folder, fileName), bytes, ct);
                return $"/uploads/{fileName}";
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Не удалось скачать {Url}", url);
                return null;
            }
        }

        // ---------- Разбор HTML ----------

        private static readonly Regex SectionStart = new("<div class=\"sect nav__anchor\"", RegexOptions.Compiled);
        private static readonly Regex SectionTitle = new("<h2 class=\"sect__title\">(.*?)</h2>", RegexOptions.Compiled | RegexOptions.Singleline);
        private static readonly Regex DishStart = new("<div class=\"ddish(?:\\s[^\"]*)?\"\\s+data-id=\"(\\d+)\"", RegexOptions.Compiled);
        private static readonly Regex DishName = new("class=\"ddish__link\"[^>]*>(.*?)</a>", RegexOptions.Compiled | RegexOptions.Singleline);
        private static readonly Regex DishIngredients = new("<div class=\"ddish__ingredients\">(.*?)</div>", RegexOptions.Compiled | RegexOptions.Singleline);
        private static readonly Regex DishBadge = new("ddish__badge--([a-z]+)", RegexOptions.Compiled);

        public static List<SourceSection> Parse(string html)
        {
            var result = new List<SourceSection>();
            var starts = SectionStart.Matches(html).Select(m => m.Index).ToList();
            for (var i = 0; i < starts.Count; i++)
            {
                var end = i + 1 < starts.Count ? starts[i + 1] : html.Length;
                var chunk = html[starts[i]..end];
                var title = Clean(SectionTitle.Match(chunk).Groups[1].Value);
                if (string.IsNullOrEmpty(title)) continue;

                var dishes = new List<SourceDish>();
                var dishMatches = DishStart.Matches(chunk).ToList();
                for (var j = 0; j < dishMatches.Count; j++)
                {
                    var dEnd = j + 1 < dishMatches.Count ? dishMatches[j + 1].Index : chunk.Length;
                    var dish = ParseDish(chunk[dishMatches[j].Index..dEnd], int.Parse(dishMatches[j].Groups[1].Value));
                    if (dish != null) dishes.Add(dish);
                }
                if (dishes.Count > 0) result.Add(new SourceSection(title, dishes));
            }
            return result;
        }

        private static SourceDish? ParseDish(string chunk, int id)
        {
            var name = Clean(DishName.Match(chunk).Groups[1].Value);
            if (string.IsNullOrEmpty(name)) return null;

            var description = Clean(DishIngredients.Match(chunk).Groups[1].Value);
            var badges = DishBadge.Matches(chunk).Select(m => m.Groups[1].Value).ToList();
            var badge = new[] { "promo", "hit", "new", "lean" }.FirstOrDefault(b => badges.Contains(b));

            var sizes = new List<SourceOption>();
            var groups = new List<SourceAddonGroup>();
            using (var modifiers = ParseAttr(chunk, "data-modifiers"))
            {
                if (modifiers?.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var m in modifiers.RootElement.EnumerateArray())
                    {
                        var type = Str(m, "type");
                        var options = m.TryGetProperty("options", out var opts) && opts.ValueKind == JsonValueKind.Array
                            ? opts.EnumerateArray()
                                .Select(o => new SourceOption(Clean(Str(o, "name") ?? Str(o, "size") ?? ""), Money(o)))
                                .Where(o => o.Name.Length > 0)
                                .ToList()
                            : new List<SourceOption>();

                        if (type == "sizes")
                        {
                            sizes.AddRange(options);
                        }
                        else
                        {
                            groups.Add(new SourceAddonGroup(
                                Clean(Str(m, "title") ?? "Добавки"),
                                type == "one_one",
                                Bool(m, "required"),
                                Int(m, "min"),
                                Int(m, "max"),
                                options));
                        }
                    }
                }
            }

            string? image = null;
            using (var media = ParseAttr(chunk, "data-media"))
            {
                if (media?.RootElement.ValueKind == JsonValueKind.Array)
                {
                    var photo = media.RootElement.EnumerateArray().FirstOrDefault(x => Str(x, "type") == "photo");
                    if (photo.ValueKind == JsonValueKind.Object)
                        image = Str(photo, "high") ?? Str(photo, "src");
                }
            }

            return new SourceDish(id, name, description, badge, sizes, groups, image);
        }

        private static JsonDocument? ParseAttr(string chunk, string attr)
        {
            var m = Regex.Match(chunk, attr + "='([^']*)'");
            if (!m.Success) m = Regex.Match(chunk, attr + "=\"([^\"]*)\"");
            if (!m.Success) return null;
            try { return JsonDocument.Parse(WebUtility.HtmlDecode(m.Groups[1].Value)); }
            catch (JsonException) { return null; }
        }

        private static string? Str(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        private static bool Bool(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && (v.ValueKind == JsonValueKind.True || (v.ValueKind == JsonValueKind.Number && v.GetDouble() != 0));

        private static int Int(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? (int)v.GetDouble() : 0;

        // Цены на сайте хранятся в десятитысячных долях рубля
        private static decimal Money(JsonElement e) =>
            e.TryGetProperty("price", out var v) && v.ValueKind == JsonValueKind.Number ? Math.Round(v.GetDecimal() / 10000m, 2) : 0;

        private static string Clean(string? s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var text = Regex.Replace(WebUtility.HtmlDecode(s), "<[^>]+>", " ");
            text = Regex.Replace(text, @"\s+", " ").Trim();
            text = Regex.Replace(text, @"\s+([,.;:])", "$1");   // «моцарелла , курица» → «моцарелла, курица»
            text = Regex.Replace(text, @",(?=[^\s\d])", ", "); // но «0,33» не трогаем
            return text;
        }

        private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
    }
}
