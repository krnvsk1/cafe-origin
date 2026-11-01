using System.Text;
using Cafe.API.Services.Printing;
using Xunit;

namespace Cafe.Tests
{
    public class PrintingTests
    {
        static PrintingTests() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        private static string Text(byte[] bytes, int encoding = 866) => Encoding.GetEncoding(encoding).GetString(bytes);

        [Fact]
        public void Wrap_SplitsByWords_AndLongWords()
        {
            Assert.Equal(new[] { "Свиная шея", "на мангале" }, EscPos.Wrap("Свиная шея на мангале", 12));
            Assert.Equal(new[] { "abcde", "fgh" }, EscPos.Wrap("abcdefgh", 5));
        }

        [Fact]
        public void Precheck_HasLinesTotalAndCut_InCp866()
        {
            var s = new PrintSettings { Width = 48, CodeTable = 17, Encoding = 866, Footer = "Спасибо!" };
            var data = new PrecheckData("Кафе", "5", "счёт №12", "Никита", 2, DateTime.UtcNow, DateTime.UtcNow,
                new[] { new PrecheckLine("Свиная шея · 350 г", 1, 1050, 1050), new PrecheckLine("Пиво", 2, 200, 400) }, s.Footer);
            var bytes = PrintService.Precheck(s, data);

            Assert.Equal(new byte[] { 0x1B, 0x40, 0x1B, 0x74, 17 }, bytes[..5]);   // сброс и кодовая таблица
            Assert.Equal(new byte[] { 0x1D, 0x56, 0x42, 0x00 }, bytes[^4..]);       // обрезка
            var text = Text(bytes);
            Assert.Contains("СЧЕТ", text);
            Assert.Contains("Стол 5", text);
            Assert.Contains("Свиная шея · 350 г".Replace("·", "?"), text.Replace("·", "?"));
            Assert.Contains("1 450,00", text);   // итог 1050 + 400
            Assert.Contains("Это не кассовый чек", text);
            // Ни одна строка не длиннее ширины принтера
            foreach (var line in text.Split('\n'))
                Assert.True(new string(line.Where(ch => ch >= ' ').ToArray()).Length <= 48 + 8);
        }

        [Fact]
        public void Pair_FitsWidth()
        {
            var p = new EscPos(32, 17, 866).Pair("ИТОГО", "1 450,00");
            var text = Text(p.ToArray()[5..]);
            Assert.Contains("ИТОГО" + new string(' ', 32 - 5 - 8) + "1 450,00", text);
        }

        [Fact]
        public void AgentScript_HasServerAndToken()
        {
            var script = PrintAgentScript.Build("http://192.168.1.10:5173", "abc123");
            Assert.Contains("$Base  = 'http://192.168.1.10:5173'", script);
            Assert.Contains("$Token = 'abc123'", script);
            Assert.Contains("/api/print-agent/jobs", script);
        }
    }
}
