using System.IO.Compression;
using System.Security;
using System.Text;

namespace Cafe.API.Services.Haccp
{
    /// <summary>
    /// Минимальный .xlsx без сторонних библиотек: один лист, строка-заголовок жирным,
    /// строки с отклонением — красной заливкой, ширина колонок по содержимому.
    /// </summary>
    public static class XlsxWriter
    {
        public record Sheet(string Name, IReadOnlyList<string> Title, IReadOnlyList<string> Header, IReadOnlyList<(IReadOnlyList<string> Cells, bool Highlight)> Rows);

        public static byte[] Build(Sheet sheet)
        {
            using var ms = new MemoryStream();
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
            {
                Add(zip, "[Content_Types].xml",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                    "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
                    "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                    "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
                    "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
                    "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>" +
                    "</Types>");
                Add(zip, "_rels/.rels",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                    "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
                    "</Relationships>");
                Add(zip, "xl/workbook.xml",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
                    $"<sheets><sheet name=\"{Esc(SheetName(sheet.Name))}\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
                Add(zip, "xl/_rels/workbook.xml.rels",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                    "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
                    "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>" +
                    "</Relationships>");
                // Стили: 0 — обычный, 1 — заголовок (жирный, серый фон, перенос), 2 — отклонение (красный фон), 3 — название (жирный крупнее); у всех перенос текста
                Add(zip, "xl/styles.xml",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
                    "<fonts count=\"3\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font><font><b/><sz val=\"11\"/><name val=\"Calibri\"/></font><font><b/><sz val=\"13\"/><name val=\"Calibri\"/></font></fonts>" +
                    "<fills count=\"4\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill>" +
                    "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFEFEFEF\"/></patternFill></fill>" +
                    "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFFDE2DE\"/></patternFill></fill></fills>" +
                    "<borders count=\"2\"><border/><border><left style=\"thin\"/><right style=\"thin\"/><top style=\"thin\"/><bottom style=\"thin\"/></border></borders>" +
                    "<cellStyleXfs count=\"1\"><xf/></cellStyleXfs>" +
                    "<cellXfs count=\"4\">" +
                    "<xf fontId=\"0\" fillId=\"0\" borderId=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment vertical=\"top\" wrapText=\"1\"/></xf>" +
                    "<xf fontId=\"1\" fillId=\"2\" borderId=\"1\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment vertical=\"center\" wrapText=\"1\"/></xf>" +
                    "<xf fontId=\"0\" fillId=\"3\" borderId=\"1\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment vertical=\"top\" wrapText=\"1\"/></xf>" +
                    "<xf fontId=\"2\" fillId=\"0\" borderId=\"0\" applyFont=\"1\"/>" +
                    "</cellXfs></styleSheet>");

                var sb = new StringBuilder();
                sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
                sb.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
                sb.Append("<cols>");
                for (var c = 0; c < sheet.Header.Count; c++)
                {
                    var longest = Math.Max(sheet.Header[c].Length / 2, sheet.Rows.Select(r => c < r.Cells.Count ? r.Cells[c].Length : 0).DefaultIfEmpty(0).Max());
                    var width = Math.Clamp(longest + 2, 8, 45);
                    sb.Append($"<col min=\"{c + 1}\" max=\"{c + 1}\" width=\"{width}\" customWidth=\"1\"/>");
                }
                sb.Append("</cols><sheetData>");
                var row = 1;
                foreach (var line in sheet.Title)
                    AppendRow(sb, row++, new[] { line }, 3);
                if (sheet.Title.Count > 0) row++;
                AppendRow(sb, row++, sheet.Header, 1);
                foreach (var (cells, highlight) in sheet.Rows)
                    AppendRow(sb, row++, cells, highlight ? 2 : 0);
                sb.Append("</sheetData></worksheet>");
                Add(zip, "xl/worksheets/sheet1.xml", sb.ToString());
            }
            return ms.ToArray();
        }

        private static void AppendRow(StringBuilder sb, int row, IReadOnlyList<string> cells, int style)
        {
            sb.Append($"<row r=\"{row}\">");
            for (var c = 0; c < cells.Count; c++)
                sb.Append($"<c r=\"{Col(c)}{row}\" t=\"inlineStr\" s=\"{style}\"><is><t xml:space=\"preserve\">{Esc(cells[c] ?? "")}</t></is></c>");
            sb.Append("</row>");
        }

        public static string Col(int index)
        {
            var s = "";
            index++;
            while (index > 0)
            {
                var m = (index - 1) % 26;
                s = (char)('A' + m) + s;
                index = (index - 1) / 26;
            }
            return s;
        }

        private static string SheetName(string name)
        {
            var clean = new string(name.Where(ch => !"[]:*?/\\".Contains(ch)).ToArray());
            return clean.Length > 31 ? clean[..31] : clean.Length == 0 ? "Журнал" : clean;
        }

        // Управляющие символы в XML недопустимы — выкидываем
        private static string Esc(string s) =>
            SecurityElement.Escape(new string(s.Where(ch => ch == '\n' || ch == '\t' || ch >= ' ').ToArray())) ?? "";

        private static void Add(ZipArchive zip, string name, string content)
        {
            var entry = zip.CreateEntry(name, CompressionLevel.Fastest);
            using var w = new StreamWriter(entry.Open(), new UTF8Encoding(false));
            w.Write(content);
        }
    }
}
