using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Cafe.API.Data;
using Cafe.API.Models;

namespace Cafe.API.Services.Printing
{
    /// <summary>
    /// Запросы к устройствам в сети кафе (АТОЛ Web Server, УТМ ЕГАИС) через программу на кассовом ПК.
    /// Сервер в интернете не видит компьютеры внутри кафе, поэтому запрос кладётся в очередь,
    /// программа на ПК забирает его, выполняет у себя и возвращает ответ.
    /// Подключается к HttpClient кассы и УТМ: пока «через программу» выключено — запросы идут напрямую.
    /// </summary>
    public class DeviceRelayHandler : DelegatingHandler
    {
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
        private readonly IServiceScopeFactory _scopes;
        private readonly PrintSettingsProvider _settings;

        public DeviceRelayHandler(IServiceScopeFactory scopes, PrintSettingsProvider settings)
        {
            _scopes = scopes;
            _settings = settings;
        }

        public record RelayRequest(string Method, string Url, string? ContentType, string? Body);
        public record RelayResponse(int Status, string? ContentType, string? Body);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var s = await _settings.GetAsync();
            if (!s.RelayDevices) return await base.SendAsync(request, ct);
            if (!PrintSettingsProvider.AgentOnline)
                throw new HttpRequestException("программа связи на кассовом ПК не запущена (или ПК выключен)");

            var body = request.Content == null ? null : Convert.ToBase64String(await request.Content.ReadAsByteArrayAsync(ct));
            var contentType = request.Content?.Headers.ContentType?.ToString();
            var payload = JsonSerializer.Serialize(new RelayRequest(request.Method.Method, request.RequestUri!.ToString(), contentType, body), Json);

            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var job = new PrintJob
            {
                Kind = "http",
                Title = $"{request.Method.Method} {request.RequestUri!.AbsolutePath}"[..Math.Min(100, $"{request.Method.Method} {request.RequestUri!.AbsolutePath}".Length)],
                Payload = payload,
            };
            db.PrintJobs.Add(job);
            await db.SaveChangesAsync(ct);

            // Ждём ответ: программа забирает задания сразу, ответ устройства — за секунды
            var started = DateTime.UtcNow;
            while (true)
            {
                await Task.Delay(200, ct);
                var cur = await db.PrintJobs.AsNoTracking().Where(j => j.Id == job.Id)
                    .Select(j => new { j.Status, j.Error, j.Result }).FirstAsync(ct);
                if (cur.Status == PrintJobStatuses.Done && cur.Result != null)
                {
                    var r = JsonSerializer.Deserialize<RelayResponse>(cur.Result, Json)!;
                    var res = new HttpResponseMessage((HttpStatusCode)r.Status) { RequestMessage = request };
                    res.Content = new ByteArrayContent(r.Body == null ? Array.Empty<byte>() : Convert.FromBase64String(r.Body));
                    if (!string.IsNullOrEmpty(r.ContentType) && MediaTypeHeaderValue.TryParse(r.ContentType, out var mt))
                        res.Content.Headers.ContentType = mt;
                    return res;
                }
                if (cur.Status == PrintJobStatuses.Error)
                    throw new HttpRequestException(cur.Error ?? "устройство не ответило");
                if (cur.Status == PrintJobStatuses.Pending && DateTime.UtcNow - started > TimeSpan.FromSeconds(10))
                {
                    await db.PrintJobs.Where(j => j.Id == job.Id && j.Status == PrintJobStatuses.Pending)
                        .ExecuteUpdateAsync(u => u.SetProperty(j => j.Status, PrintJobStatuses.Error)
                            .SetProperty(j => j.Error, "Программа на кассовом ПК не забрала запрос")
                            .SetProperty(j => j.DoneAt, DateTime.UtcNow), ct);
                    throw new HttpRequestException("программа связи на кассовом ПК не отвечает");
                }
            }
        }
    }
}
