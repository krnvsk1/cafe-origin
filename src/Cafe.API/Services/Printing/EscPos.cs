using System.Text;

namespace Cafe.API.Services.Printing
{
    /// <summary>
    /// Документ для чекового принтера (ESC/POS: RONGTA, Xprinter и т.п.): текст, жирный, крупный шрифт, обрезка.
    /// Кириллица — в кодировке, которую выбрали в настройках (обычно PC866, на принтере — таблица ESC t 17).
    /// </summary>
    public class EscPos
    {
        static EscPos() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        private readonly List<byte> _bytes = new();
        private readonly Encoding _encoding;
        public int Width { get; }

        public EscPos(int width, int codeTable, int encoding)
        {
            Width = Math.Clamp(width, 24, 64);
            _encoding = Encoding.GetEncoding(encoding);
            _bytes.AddRange(new byte[] { 0x1B, 0x40 });                         // ESC @ — сброс
            _bytes.AddRange(new byte[] { 0x1B, 0x74, (byte)Math.Clamp(codeTable, 0, 255) }); // ESC t n — кодовая таблица
        }

        public enum Align { Left = 0, Center = 1, Right = 2 }

        private void Raw(params byte[] b) => _bytes.AddRange(b);

        /// <summary>Строка текста; длинная переносится по словам</summary>
        public EscPos Line(string text, Align align = Align.Left, bool bold = false, bool big = false)
        {
            Raw(0x1B, 0x61, (byte)align);                 // ESC a — выравнивание
            Raw(0x1B, 0x45, (byte)(bold ? 1 : 0));        // ESC E — жирный
            Raw(0x1D, 0x21, (byte)(big ? 0x11 : 0x00));   // GS ! — двойная ширина и высота
            var width = big ? Width / 2 : Width;
            foreach (var part in Wrap(text, width)) Text(part + "\n");
            Raw(0x1D, 0x21, 0x00);
            Raw(0x1B, 0x45, 0x00);
            Raw(0x1B, 0x61, 0x00);
            return this;
        }

        /// <summary>Слева название, справа сумма — в одну строку, если влезает</summary>
        public EscPos Pair(string left, string right, bool bold = false)
        {
            Raw(0x1B, 0x45, (byte)(bold ? 1 : 0));
            var room = Width - right.Length - 1;
            if (left.Length <= room) Text(left + new string(' ', Width - left.Length - right.Length) + right + "\n");
            else
            {
                foreach (var part in Wrap(left, Width)) Text(part + "\n");
                Text(new string(' ', Math.Max(0, Width - right.Length)) + right + "\n");
            }
            Raw(0x1B, 0x45, 0x00);
            return this;
        }

        public EscPos Separator(char c = '-') { Text(new string(c, Width) + "\n"); return this; }

        public EscPos Feed(int lines = 1) { Raw(0x1B, 0x64, (byte)Math.Clamp(lines, 0, 10)); return this; }

        /// <summary>Отступ и обрезка бумаги (если у принтера есть резак)</summary>
        public EscPos Cut() { Feed(4); Raw(0x1D, 0x56, 0x42, 0x00); return this; }

        public byte[] ToArray() => _bytes.ToArray();

        private void Text(string s) => _bytes.AddRange(_encoding.GetBytes(s.Replace('ё', 'е').Replace('Ё', 'Е')));

        public static IEnumerable<string> Wrap(string text, int width)
        {
            text = (text ?? string.Empty).Replace('\n', ' ').Trim();
            if (text.Length == 0) { yield return string.Empty; yield break; }
            var line = new StringBuilder();
            foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var w = word;
                while (w.Length > width)
                {
                    if (line.Length > 0) { yield return line.ToString(); line.Clear(); }
                    yield return w[..width];
                    w = w[width..];
                }
                if (line.Length > 0 && line.Length + 1 + w.Length > width) { yield return line.ToString(); line.Clear(); }
                if (line.Length > 0) line.Append(' ');
                line.Append(w);
            }
            if (line.Length > 0) yield return line.ToString();
        }
    }
}
