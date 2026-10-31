using System.IO.Compression;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Cafe.API.Data;
using Cafe.API.Models;
using Cafe.API.Services.Haccp;
using Xunit;

namespace Cafe.Tests
{
    public class HaccpTests
    {
        private static readonly DateTime Now = new(2026, 10, 28, 9, 0, 0, DateTimeKind.Utc); // 12:00 по Москве

        private static ApplicationDbContext NewContext() =>
            new(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"haccp-{Guid.NewGuid()}")
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options);

        private static readonly HaccpObject Fridge = new() { Id = 1, Kind = HaccpObjectKinds.Fridge, Name = "Холодильник мясной", MinValue = 2, MaxValue = 6, ChecksPerDay = 2 };

        [Fact]
        public void FridgeTemperatureOutOfRange_IsDeviation()
        {
            var j = HaccpJournals.Find(HaccpJournals.Fridge)!;
            var ok = HaccpJournals.Check(j, new JsonObject { ["temperature"] = "4,5" }, Fridge, null, Now);
            Assert.Empty(ok.Errors);
            Assert.Empty(ok.Deviations);
            Assert.Equal(4.5m, ok.Values["temperature"]!.GetValue<decimal>());

            var warm = HaccpJournals.Check(j, new JsonObject { ["temperature"] = 9 }, Fridge, null, Now);
            var d = Assert.Single(warm.Deviations);
            Assert.Contains("+2…+6 °C", d);

            var empty = HaccpJournals.Check(j, new JsonObject(), Fridge, null, Now);
            Assert.Contains(empty.Errors, e => e.Contains("Температура"));
        }

        [Fact]
        public void HealthJournal_FlagsSuspendedAndExpiredMedicalExam()
        {
            var j = HaccpJournals.Find(HaccpJournals.Health)!;
            var staff = new HaccpStaff { Id = 1, FullName = "Иванов И.И.", MedicalExamUntil = new DateTime(2026, 10, 1) };
            var r = HaccpJournals.Check(j, new JsonObject { ["noInfection"] = true, ["noSkin"] = false, ["result"] = "Отстранён" }, null, staff, Now);
            Assert.Empty(r.Errors);
            Assert.Equal(3, r.Deviations.Count);
            Assert.Contains(r.Deviations, x => x.Contains("Медосмотр просрочен"));

            var bad = HaccpJournals.Check(j, new JsonObject { ["noInfection"] = "true", ["noSkin"] = "true", ["result"] = "Может быть" }, null, null, Now);
            Assert.Contains(bad.Errors, x => x.Contains("выберите из списка"));
        }

        [Fact]
        public void Perishable_ExpiredOnArrival_IsDeviation()
        {
            var j = HaccpJournals.Find(HaccpJournals.Perishable)!;
            var r = HaccpJournals.Check(j, new JsonObject
            {
                ["product"] = "Молоко 3,2%", ["supplier"] = "ООО Молочник", ["quantity"] = "12 л", ["safetyDoc"] = "ВСД 123",
                ["organoleptic"] = "Соответствует", ["sellBy"] = "2026-10-27"
            }, null, null, Now);
            Assert.Empty(r.Errors);
            Assert.Contains("Срок реализации истёк", r.Deviations);
        }

        private static HaccpActor Cook => new(10, "Повар Петров", false);
        private static HaccpActor Chef => new(11, "Шеф Сидоров", true);

        [Fact]
        public async Task Deviation_RequiresCorrectiveAction_AndEditsKeepHistory()
        {
            using var db = NewContext();
            db.HaccpObjects.Add(new HaccpObject { Kind = HaccpObjectKinds.Fridge, Name = "Холодильник", MinValue = 2, MaxValue = 6, ChecksPerDay = 2 });
            await db.SaveChangesAsync();
            var fridgeId = db.HaccpObjects.Single().Id;
            var svc = new HaccpService(db);

            var warm = new HaccpEntryInput(HaccpJournals.Fridge, fridgeId, null, null, null, new JsonObject { ["temperature"] = 9 }, null);
            var ex = await Assert.ThrowsAsync<HaccpException>(() => svc.CreateAsync(new[] { warm }, Cook));
            Assert.Contains("корректирующее действие", ex.Message);

            var created = await svc.CreateAsync(new[] { warm with { CorrectiveAction = "Продукты перенесены, вызван мастер" } }, Cook);
            var entry = Assert.Single(created);
            Assert.True(entry.HasDeviation);

            // Без причины не исправить
            await Assert.ThrowsAsync<HaccpException>(() => svc.UpdateAsync(entry.Id, warm with { Values = new JsonObject { ["temperature"] = 5 } }, null, Cook));
            // Чужую запись повар исправить не может
            await Assert.ThrowsAsync<HaccpException>(() =>
                svc.UpdateAsync(entry.Id, warm with { Values = new JsonObject { ["temperature"] = 5 } }, "опечатка", Cook with { UserId = 99 }));

            var fixedEntry = await svc.UpdateAsync(entry.Id, warm with { Values = new JsonObject { ["temperature"] = 5 } }, "опечатка: было 5, записал 9", Cook);
            Assert.False(fixedEntry.HasDeviation);
            Assert.Equal(2, fixedEntry.Version);
            var rev = Assert.Single(db.HaccpEntryRevisions);
            Assert.Contains("9", rev.Data);
            Assert.Equal("опечатка: было 5, записал 9", rev.Reason);

            var annulled = await svc.AnnulAsync(entry.Id, "запись не по тому холодильнику", Chef);
            Assert.True(annulled.IsAnnulled);
            Assert.Equal(2, db.HaccpEntryRevisions.Count());
            await Assert.ThrowsAsync<HaccpException>(() => svc.UpdateAsync(entry.Id, warm, "ещё раз", Chef));
        }

        [Fact]
        public async Task OldEntries_OnlyByManager()
        {
            using var db = NewContext();
            var svc = new HaccpService(db);
            var input = new HaccpEntryInput(HaccpJournals.Cleaning, null, null, null, DateTime.UtcNow.AddDays(-3),
                new JsonObject { ["kind"] = "Генеральная уборка", ["area"] = "Кухня", ["doneBy"] = "Петров" }, null);
            await Assert.ThrowsAsync<HaccpException>(() => svc.CreateAsync(new[] { input }, Cook));
            Assert.Single(await svc.CreateAsync(new[] { input }, Chef));
            await Assert.ThrowsAsync<HaccpException>(() =>
                svc.CreateAsync(new[] { input with { OccurredAt = DateTime.UtcNow.AddHours(2) } }, Chef));
        }

        [Fact]
        public async Task Today_FollowsScheduleAndShowsExpiringDocs()
        {
            using var db = NewContext();
            var now = DateTime.UtcNow;
            db.HaccpObjects.Add(new HaccpObject { Kind = HaccpObjectKinds.Fridge, Name = "Холодильник", MinValue = 2, MaxValue = 6 });
            db.HaccpStaff.Add(new HaccpStaff { FullName = "Иванов", MedicalExamUntil = HaccpJournals.Moscow(now).Date.AddDays(10) });
            await db.SaveChangesAsync();
            var svc = new HaccpService(db);
            // Холодильник: проверки в 00:00 и 23:59 по Москве — первая уже наступила, вторая ещё нет
            await HaccpSchedule.SaveAsync(db, new Dictionary<string, string[]> { ["fridge"] = new[] { "00:00", "23:59" }, ["health"] = new[] { "00:00" } }, null);

            var before = await svc.TodayAsync(now);
            var fridge = Assert.Single(before.Tasks, t => t.Journal == HaccpJournals.Fridge);
            Assert.Equal((2, 1, 0, "todo"), (fridge.Expected, fridge.Due, fridge.Done, fridge.Status));

            await svc.CreateAsync(new[]
            {
                new HaccpEntryInput(HaccpJournals.Fridge, db.HaccpObjects.Single().Id, null, null, null, new JsonObject { ["temperature"] = 4 }, null)
            }, Cook);
            var after = await svc.TodayAsync(now);
            fridge = Assert.Single(after.Tasks, t => t.Journal == HaccpJournals.Fridge);
            Assert.Equal("ok", fridge.Status);   // сделано всё, что уже наступило, — напоминания нет
            Assert.Equal("23:59", fridge.Next);
            Assert.Contains(after.Tasks, t => t.Journal == HaccpJournals.Health && t.Status == "todo");
            var alert = Assert.Single(after.Staff);
            Assert.False(alert.Expired);
        }

        [Theory]
        [InlineData(0, 0, 2, "upcoming")]
        [InlineData(1, 1, 2, "ok")]
        [InlineData(0, 1, 2, "todo")]
        [InlineData(1, 2, 2, "partial")]
        [InlineData(2, 2, 2, "done")]
        public void TaskStatus_ByDueSlots(int done, int due, int expected, string status) =>
            Assert.Equal(status, HaccpService.TaskStatus(done, due, expected));

        [Fact]
        public void Schedule_NormalizesAndRejectsBadTimes()
        {
            var (ok, error) = HaccpSchedule.Validate(new Dictionary<string, string[]> { ["fridge"] = new[] { "18:00", "9:00", "09:00" } });
            Assert.Null(error);
            Assert.Equal(new[] { "09:00", "18:00" }, ok["fridge"]);
            Assert.NotNull(HaccpSchedule.Validate(new Dictionary<string, string[]> { ["fridge"] = new[] { "25:00" } }).Error);
        }

        [Fact]
        public void Export_BuildsValidXlsx()
        {
            var j = HaccpJournals.Find(HaccpJournals.Fridge)!;
            var entries = new[]
            {
                new HaccpEntry { Journal = j.Key, ObjectId = 1, OccurredAt = Now, CreatedAt = Now, Data = "{\"temperature\":9}", HasDeviation = true, Deviation = "Температура 9 °C — норма +2…+6 °C", CorrectiveAction = "Вызван мастер", CreatedByName = "Петров" },
                new HaccpEntry { Journal = j.Key, ObjectId = 1, OccurredAt = Now.AddHours(-5), CreatedAt = Now, Data = "{\"temperature\":4}", CreatedByName = "Петров" },
            };
            var (header, rows) = HaccpService.Table(j, entries, new Dictionary<int, HaccpObject> { [1] = Fridge }, new Dictionary<int, HaccpStaff>());
            Assert.Contains("Температура, °C", header);
            Assert.Equal("4 °C", rows[0].Cells[3]);
            Assert.EndsWith("(внесено позже)", rows[0].Cells[^1]);
            Assert.True(rows[1].Highlight);

            var bytes = XlsxWriter.Build(new XlsxWriter.Sheet("Холодильники", new[] { j.Title }, header,
                rows.Select(r => ((IReadOnlyList<string>)r.Cells, r.Highlight)).ToList()));
            using var zip = new ZipArchive(new MemoryStream(bytes));
            var sheet = new StreamReader(zip.GetEntry("xl/worksheets/sheet1.xml")!.Open()).ReadToEnd();
            Assert.Contains("Вызван мастер", sheet);
            Assert.NotNull(zip.GetEntry("xl/workbook.xml"));
            Assert.Equal("AA", XlsxWriter.Col(26));
        }
    }
}
