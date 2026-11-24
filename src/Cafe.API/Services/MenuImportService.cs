using System.Net;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Cafe.API.Data;
using Cafe.API.Models;

namespace Cafe.API.Services
{
    /// <summary>
    /// Импорт меню с сайта заведения на платформе FoodPicasso (адрес страницы меню задаёт администратор):
    /// разделы → категории, блюда → карточки, размеры → варианты, модификаторы → добавки, фото → uploads.
    /// Повторный запуск обновляет цены и описания и добавляет новое, ничего не удаляя.
    /// </summary>
    public class MenuImportService
    {
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

        public async Task<ImportResult> ImportAsync(string sourceUrl, bool downloadImages, CancellationToken ct)
        {
            var html = await _http.GetStringAsync(sourceUrl, ct);
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

            var existingCards = await _context.Dishes
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

                    if (card == null)
                    {
                        card = new Dish { CreatedAt = DateTime.UtcNow, IsAvailable = true };
                        _context.Dishes.Add(card);
                        existingCards.Add(card);
                        created++;
                    }
                    else
                    {
                        updated++;
                    }

                    card.Name = name;
                    card.Category = Truncate(section.Title, 50);
                    if (!string.IsNullOrWhiteSpace(dish.Description))
                        card.Description = Truncate(dish.Description, 500);
                    card.Badge = dish.Badge;
                    card.IsPromo = dish.Badge == "promo";
                    card.IsSpicy = spicy;
                    card.SortOrder = sortOrder;
                    if (dish.Sizes.Count > 0)
                        card.Price = dish.Sizes.Min(s => s.Price);
                    card.UpdatedAt = DateTime.UtcNow;
                    await _context.SaveChangesAsync(ct);

                    // Варианты (размеры). У блюда без размеров — один вариант «Стандарт»
                    var sizes = dish.Sizes.Count > 0 ? dish.Sizes : new List<SourceOption> { new("Стандарт", card.Price) };
                    var childOrder = 0;
                    var children = new List<Dish>();
                    foreach (var size in sizes)
                    {
                        var childName = Truncate(string.IsNullOrWhiteSpace(size.Name) ? "Стандарт" : size.Name, 100);
                        // Одинаковые названия размеров внутри карточки (бывает в исходных данных)
                        if (children.Any(c => c.Name == childName))
                            childName = Truncate($"{childName} ({size.Price:0} ₽)", 100);

                        var child = card.Children.FirstOrDefault(c => c.Name == childName);
                        if (child == null)
                        {
                            child = new Dish { ParentId = card.Id, CreatedAt = DateTime.UtcNow, IsAvailable = true };
                            _context.Dishes.Add(child);
                            card.Children.Add(child);
                        }
                        child.Name = childName;
                        child.Price = size.Price;
                        child.Category = card.Category;
                        child.Description = card.Description;
                        child.IsSpicy = spicy;
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
                            var linked = await _context.DishAddons
                                .Where(sa => sa.DishId == child.Id)
                                .Select(sa => sa.AddonId)
                                .ToListAsync(ct);
                            foreach (var (addon, _) in addons.Where(a => !linked.Contains(a.Addon.Id)))
                            {
                                _context.DishAddons.Add(new DishAddon
                                {
                                    DishId = child.Id,
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
                        var image = new DishImage
                        {
                            DishId = card.Id,
                            FileName = Truncate(Path.GetFileName(path.Split('?')[0]), 255),
                            FilePath = path,
                            IsPrimary = true,
                            CreatedAt = DateTime.UtcNow
                        };
                        _context.DishImages.Add(image);
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
        private static readonly Regex ModifierDictionary = new("<script\\b[^>]*\\bdata-modifier-dictionary[^>]*>(.*?)</script\\s*>", RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);

        public static List<SourceSection> Parse(string html)
        {
            var result = new List<SourceSection>();
            using var modifierDictionary = ParseModifierDictionary(html);
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
                    var dish = ParseDish(chunk[dishMatches[j].Index..dEnd], int.Parse(dishMatches[j].Groups[1].Value), modifierDictionary?.RootElement ?? default);
                    if (dish != null) dishes.Add(dish);
                }
                if (dishes.Count > 0) result.Add(new SourceSection(title, dishes));
            }
            return result;
        }

        private static SourceDish? ParseDish(string chunk, int id, JsonElement modifierDictionary)
        {
            var name = Clean(DishName.Match(chunk).Groups[1].Value);
            if (string.IsNullOrEmpty(name)) return null;

            var descriptionMatch = DishIngredients.Match(chunk);
            var description = Clean(descriptionMatch.Success ? descriptionMatch.Groups[1].Value : ElementContentByClass(chunk, "ddish__ingredients"));
            var badges = DishBadge.Matches(chunk).Select(m => m.Groups[1].Value).ToList();
            var badge = new[] { "promo", "hit", "new", "lean" }.FirstOrDefault(b => badges.Contains(b));

            var sizes = new List<SourceOption>();
            var groups = new List<SourceAddonGroup>();
            var hasLegacyModifiers = false;
            void AddModifier(JsonElement modifier)
            {
                var type = Str(modifier, "type");
                var options = modifier.TryGetProperty("options", out var opts) && opts.ValueKind == JsonValueKind.Array
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
                        Clean(Str(modifier, "title") ?? "Добавки"),
                        type == "one_one",
                        Bool(modifier, "required"),
                        Int(modifier, "min"),
                        Int(modifier, "max"),
                        options));
                }
            }

            // Старый шаблон помещал модификаторы в data-modifiers.
            using (var modifiers = ParseAttr(chunk, "data-modifiers"))
            {
                if (modifiers?.RootElement.ValueKind == JsonValueKind.Array)
                {
                    hasLegacyModifiers = true;
                    foreach (var m in modifiers.RootElement.EnumerateArray())
                        AddModifier(m);
                }
            }

            // В текущем шаблоне карточки содержат ссылки на общий JSON-словарь страницы.
            if (!hasLegacyModifiers && modifierDictionary.ValueKind == JsonValueKind.Object)
            {
                var refs = ReadAttribute(chunk, "data-modifier-refs")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? Array.Empty<string>();
                foreach (var reference in refs)
                    if (modifierDictionary.TryGetProperty(reference, out var modifier) && modifier.ValueKind == JsonValueKind.Object)
                        AddModifier(modifier);
            }

            // Если сайт не публикует группу размеров, берём базовую цену из карточки.
            if (sizes.Count == 0 && decimal.TryParse(ReadAttribute(chunk, "data-price"), NumberStyles.Number, CultureInfo.InvariantCulture, out var basePrice))
                sizes.Add(new SourceOption("Стандарт", basePrice));

            string? image = null;
            using (var media = ParseAttr(chunk, "data-media"))
            {
                if (media?.RootElement.ValueKind == JsonValueKind.Array)
                {
                    var photo = media.RootElement.EnumerateArray().FirstOrDefault(x => Str(x, "type") == "photo");
                    if (photo.ValueKind == JsonValueKind.Object)
                        image = Str(photo, "high") ?? Str(photo, "src");
                }
                image ??= ExtractImageUrl(chunk);
            }

            return new SourceDish(id, name, description, badge, sizes, groups, image);
        }

        private static JsonDocument? ParseModifierDictionary(string html)
        {
            var match = ModifierDictionary.Match(html);
            if (!match.Success) return null;
            try { return JsonDocument.Parse(WebUtility.HtmlDecode(match.Groups[1].Value)); }
            catch (JsonException) { return null; }
        }

        private static string? ExtractImageUrl(string chunk)
        {
            foreach (Match image in Regex.Matches(chunk, "<img\\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Singleline))
            {
                var url = ReadAttribute(image.Value, "data-full") ?? ReadAttribute(image.Value, "data-src") ?? ReadAttribute(image.Value, "src");
                if (!string.IsNullOrWhiteSpace(url) && !url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                    return url;
            }
            return null;
        }

        private static string ElementContentByClass(string html, string className)
        {
            var escapedClass = Regex.Escape(className);
            var pattern = "<[a-z][^>]*\\bclass\\s*=\\s*(?:\"[^\"]*\\b" + escapedClass + "\\b[^\"]*\"|'[^']*\\b" + escapedClass + "\\b[^']*')[^>]*>(.*?)</[a-z][^>]*>";
            return Regex.Match(html, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline).Groups[1].Value;
        }

        private static string? ReadAttribute(string html, string attribute)
        {
            var pattern = @"\b" + Regex.Escape(attribute) + @"\s*=\s*(?:""(?<value>[^""]*)""|'(?<value>[^']*)')";
            var match = Regex.Match(html, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
            return match.Success ? WebUtility.HtmlDecode(match.Groups["value"].Value) : null;
        }

        private static JsonDocument? ParseAttr(string chunk, string attr)
        {
            var value = ReadAttribute(chunk, attr);
            if (value == null) return null;
            try { return JsonDocument.Parse(value); }
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
