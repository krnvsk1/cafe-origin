using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Kumashaurma.API.Data;
using Kumashaurma.API.Models;

namespace Kumashaurma.API.Services.Chz
{
    public class ChzService
    {
        /// <summary>Причины вывода из оборота, понятные общепиту</summary>
        public static readonly IReadOnlyDictionary<string, string> Actions = new Dictionary<string, string>
        {
            ["PRODUCTION_USE"] = "Использование для производственных целей",
            ["OWN_USE"] = "Использование для собственных нужд",
            ["EXPIRATION"] = "Истечение срока годности",
            ["DESTRUCTION"] = "Уничтожение (порча, брак)",
            ["LOSS"] = "Утрата",
        };

        public static readonly IReadOnlyDictionary<string, string> Groups = new Dictionary<string, string>
        {
            ["milk"] = "Молочная продукция",
            ["water"] = "Упакованная вода",
            ["beer"] = "Пиво и слабоалкогольные напитки",
            ["nabeer"] = "Безалкогольное пиво",
            ["softdrinks"] = "Соки и безалкогольные напитки",
            ["grocery"] = "Бакалея",
            ["vegetableoil"] = "Растительные масла",
            ["sweets"] = "Кондитерские изделия",
            ["conserve"] = "Консервы",
        };

        // Токен по ИНН и контуру; живёт ~10 часов, храним в памяти сервера
        private static readonly ConcurrentDictionary<string, (string Token, DateTime Expires)> Tokens = new();

        private readonly ApplicationDbContext _context;
        private readonly ChzClient _client;
        private readonly ChzSettingsProvider _settings;
        private readonly ILogger<ChzService> _logger;

        public ChzService(ApplicationDbContext context, ChzClient client, ChzSettingsProvider settings, ILogger<ChzService> logger)
        {
            _context = context;
            _client = client;
            _settings = settings;
            _logger = logger;
        }

        private static string TokenKey(ChzOptions o) => $"{o.Inn}|{o.Sandbox}";

        public async Task<(bool SignedIn, DateTime? Expires)> SessionAsync()
        {
            var o = await _settings.GetAsync();
            return Tokens.TryGetValue(TokenKey(o), out var t) && t.Expires > DateTime.UtcNow ? (true, t.Expires) : (false, null);
        }

        private async Task<(ChzOptions Options, string Token)> RequireTokenAsync()
        {
            var o = await _settings.GetAsync();
            if (!o.Enabled) throw new ChzApiException("Честный знак не подключён — заполните настройки");
            if (!Tokens.TryGetValue(TokenKey(o), out var t) || t.Expires <= DateTime.UtcNow)
                throw new ChzApiException("Нужно войти в Честный знак с сертификатом КЭП", 401);
            return (o, t.Token);
        }

        public async Task<ChzClient.AuthKey> StartSignInAsync(CancellationToken ct)
        {
            var o = await _settings.GetAsync();
            if (!o.Enabled) throw new ChzApiException("Честный знак не подключён — заполните настройки");
            return await _client.GetAuthKeyAsync(o, ct);
        }

        public async Task<DateTime> CompleteSignInAsync(string uuid, string signature, CancellationToken ct)
        {
            var o = await _settings.GetAsync();
            var token = await _client.SignInAsync(o, uuid, signature, ct);
            var expires = DateTime.UtcNow.AddHours(9.5);
            Tokens[TokenKey(o)] = (token, expires);
            return expires;
        }

        public void SignOut(ChzOptions o) => Tokens.TryRemove(TokenKey(o), out _);

        /// <summary>
        /// Собрать документ вывода из оборота (LK_RECEIPT). Возвращает черновик — его JSON (base64) подписывается в браузере.
        /// </summary>
        public async Task<ChzDocument> PrepareWithdrawalAsync(string productGroup, string action, DateTime date, string codesText,
            string? comment, int? stockDocumentId, int? userId)
        {
            var o = await _settings.GetAsync();
            if (!o.Enabled) throw new ChzApiException("Честный знак не подключён — заполните настройки");
            if (!System.Text.RegularExpressions.Regex.IsMatch(o.Inn ?? "", @"^\d{10}(\d{2})?$"))
                throw new ChzApiException("Укажите ИНН организации в настройках Честного знака");
            if (!Groups.ContainsKey(productGroup)) throw new ChzApiException("Неизвестная товарная группа");
            if (!Actions.ContainsKey(action)) throw new ChzApiException("Неизвестная причина вывода");

            var codes = ChzCodes.Parse(codesText, productGroup);
            if (codes.Count == 0) throw new ChzApiException("Отсканируйте хотя бы один код маркировки");
            if (codes.Count > 5000) throw new ChzApiException("Не больше 5000 кодов в одном документе");

            var actionDate = date.Date > DateTime.UtcNow.AddHours(3).Date ? DateTime.UtcNow.AddHours(3).Date : date.Date;
            var doc = new ChzDocument
            {
                ProductGroup = productGroup,
                Action = action,
                ActionDate = actionDate,
                Codes = string.Join('\n', codes),
                CodesCount = codes.Count,
                Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim()[..Math.Min(comment.Trim().Length, 300)],
                StockDocumentId = stockDocumentId,
                CreatedByUserId = userId,
                Status = ChzStatuses.Draft,
                CreatedAt = DateTime.UtcNow
            };
            _context.ChzDocuments.Add(doc);
            await _context.SaveChangesAsync();

            doc.DocumentNumber = $"KS-{doc.Id:D6}";
            doc.ProductDocument = BuildReceiptDocument(o.Inn!, action, actionDate, doc.DocumentNumber, codes);
            await _context.SaveChangesAsync();
            return doc;
        }

        /// <summary>JSON документа LK_RECEIPT в base64 (подписывается откреплённой подписью)</summary>
        public static string BuildReceiptDocument(string inn, string action, DateTime date, string number, IEnumerable<string> codes)
        {
            var day = date.ToString("yyyy-MM-dd");
            var json = JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["inn"] = inn,
                ["action"] = action,
                ["action_date"] = day,
                ["document_type"] = "OTHER",
                ["document_number"] = number,
                ["document_date"] = day,
                ["primary_document_custom_name"] = action == "PRODUCTION_USE"
                    ? "Акт использования для производственных целей"
                    : "Акт вывода из оборота",
                ["products"] = codes.Select(c => new Dictionary<string, string> { ["cis"] = c }).ToList()
            }, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
        }

        /// <summary>Отправить подписанный документ в Честный знак</summary>
        public async Task<ChzDocument> SubmitAsync(int id, string signature, CancellationToken ct)
        {
            var doc = await _context.ChzDocuments.FirstOrDefaultAsync(d => d.Id == id, ct)
                      ?? throw new ChzApiException("Документ не найден");
            if (doc.Status is not (ChzStatuses.Draft or ChzStatuses.Error))
                throw new ChzApiException("Документ уже отправлен");

            var (o, token) = await RequireTokenAsync();
            try
            {
                var chzId = await _client.CreateDocumentAsync(o, token, doc.ProductGroup, "LK_RECEIPT", doc.ProductDocument, signature, ct);
                doc.ChzDocumentId = chzId;
                doc.Status = ChzStatuses.Sent;
                doc.SentAt = DateTime.UtcNow;
                doc.Result = null;
                _logger.LogInformation("Честный знак: вывод из оборота {Number}, кодов {Count}, документ {Id}", doc.DocumentNumber, doc.CodesCount, chzId);
            }
            catch (ChzApiException ex) when (ex.StatusCode is 400 or 422)
            {
                doc.Status = ChzStatuses.Error;
                doc.Result = Trunc(ex.Message, 2000);
            }
            catch (ChzApiException ex) when (ex.StatusCode == 401)
            {
                SignOut(o);
                throw new ChzApiException("Сессия Честного знака истекла — войдите заново", 401);
            }
            await _context.SaveChangesAsync(ct);
            return doc;
        }

        /// <summary>Узнать результат проверки документа</summary>
        public async Task<ChzDocument> RefreshAsync(int id, CancellationToken ct)
        {
            var doc = await _context.ChzDocuments.FirstOrDefaultAsync(d => d.Id == id, ct)
                      ?? throw new ChzApiException("Документ не найден");
            if (doc.ChzDocumentId == null) return doc;

            var (o, token) = await RequireTokenAsync();
            var (status, errors) = await _client.GetDocumentStatusAsync(o, token, doc.ProductGroup, doc.ChzDocumentId, ct);
            doc.CheckedAt = DateTime.UtcNow;
            switch (status)
            {
                case "CHECKED_OK":
                    doc.Status = ChzStatuses.Ok;
                    doc.Result = "Коды выведены из оборота";
                    break;
                case "CHECKED_NOT_OK":
                case "PARSE_ERROR":
                case "CANCELLED":
                    doc.Status = ChzStatuses.Error;
                    doc.Result = Trunc(errors ?? $"Документ не прошёл проверку ({status})", 2000);
                    break;
                default:
                    doc.Result = $"Проверяется ({status})";
                    break;
            }
            await _context.SaveChangesAsync(ct);
            return doc;
        }

        private static string Trunc(string value, int max) => value.Length <= max ? value : value[..max];
    }
}
