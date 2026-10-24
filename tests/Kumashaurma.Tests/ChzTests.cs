using System.Text;
using System.Text.Json;
using Kumashaurma.API.Services.Chz;
using Xunit;

namespace Kumashaurma.Tests
{
    public class ChzTests
    {
        [Fact]
        public void Normalize_CutsCryptoTailAtGroupSeparator()
        {
            var raw = "0104607009471233215Abc12\u001d93dGVz";
            Assert.Equal("0104607009471233215Abc12", ChzCodes.Normalize(raw, "milk"));
        }

        [Fact]
        public void Normalize_CutsCryptoTailByLength_WhenScannerDroppedSeparator()
        {
            // Молоко: (01)14 цифр (21)6 символов (93)4 символа
            Assert.Equal("010460700947123321aB3dEf", ChzCodes.Normalize("010460700947123321aB3dEf93AbCd", "milk"));
            // Пиво: серийный 7 символов
            Assert.Equal("010460700947123321aB3dEfG", ChzCodes.Normalize("010460700947123321aB3dEfG93AbCd", "beer"));
        }

        [Fact]
        public void Parse_SplitsLinesAndDropsDuplicates()
        {
            var codes = ChzCodes.Parse("010460700947123321aB3dEf93AbCd\n010460700947123321aB3dEf93AbCd\n\n  short  \n", "milk");
            Assert.Single(codes);
        }

        [Fact]
        public void BuildsReceiptDocument()
        {
            var b64 = ChzService.BuildReceiptDocument("7700000000", "PRODUCTION_USE", new DateTime(2026, 10, 25), "KS-000001",
                new[] { "010460700947123321aB3dEf" });
            using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(b64)));
            var root = doc.RootElement;
            Assert.Equal("7700000000", root.GetProperty("inn").GetString());
            Assert.Equal("PRODUCTION_USE", root.GetProperty("action").GetString());
            Assert.Equal("2026-10-25", root.GetProperty("action_date").GetString());
            Assert.Equal("OTHER", root.GetProperty("document_type").GetString());
            Assert.Equal("010460700947123321aB3dEf", root.GetProperty("products")[0].GetProperty("cis").GetString());
            Assert.Contains("Акт", root.GetProperty("primary_document_custom_name").GetString());
        }

        [Fact]
        public void ParsesDocumentStatus()
        {
            Assert.Equal("CHECKED_OK", ChzClient.ParseStatus("[{\"number\":\"x\",\"status\":\"CHECKED_OK\"}]").Status);
            var (status, errors) = ChzClient.ParseStatus("{\"status\":\"CHECKED_NOT_OK\",\"errors\":[\"Код не найден\"]}");
            Assert.Equal("CHECKED_NOT_OK", status);
            Assert.Equal("Код не найден", errors);
        }
    }
}
