using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using Tsg.Application;
using Tsg.Domain;

namespace Tsg.Infrastructure;

/// <summary>Небольшой PDF-экспорт без внешних пакетов. Шрифт Arial встраивается для кириллицы.</summary>
public sealed class PdfReceiptExporter(string? fontPath = null) : IReceiptExporter
{
    public void Export(ReceiptData receipt, string path)
    {
        var resolvedFont = fontPath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "arial.ttf");
        if (!File.Exists(resolvedFont)) throw new AppException("Для экспорта PDF нужен системный шрифт Arial (arial.ttf).");
        try
        {
            var font = new TrueTypeFont(File.ReadAllBytes(resolvedFont));
            var pdf = Build(receipt, font);
            var fullPath = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllBytes(fullPath, pdf);
        }
        catch (IOException) { throw new AppException("Не удалось сохранить PDF. Проверьте папку и закройте уже открытый файл."); }
        catch (UnauthorizedAccessException) { throw new AppException("Нет прав на сохранение PDF в выбранной папке."); }
    }

    private static byte[] Build(ReceiptData receipt, TrueTypeFont font)
    {
        var invoice = receipt.Invoice;
        var content = new StringBuilder();
        var used = new HashSet<char>();
        var y = 793m;
        void Text(string value, decimal x, decimal atY, int size = 11)
        {
            foreach (var character in value) used.Add(character);
            var hex = Convert.ToHexString(Encoding.BigEndianUnicode.GetBytes(value));
            content.AppendLine($"BT /F1 {size} Tf 1 0 0 1 {N(x)} {N(atY)} Tm <{hex}> Tj ET");
        }
        void Paragraph(string value, int size = 11)
        {
            var line = new StringBuilder();
            decimal width = 0;
            foreach (var character in value)
            {
                var advance = font.Width(character) * size / 1000m;
                if (width + advance > 503 && line.Length > 0)
                { Text(line.ToString(), 46, y, size); y -= size + 6; line.Clear(); width = 0; }
                line.Append(character); width += advance;
            }
            Text(line.ToString(), 46, y, size); y -= size + 6;
        }
        void Cell(string value, decimal left, decimal width, bool rightAligned)
        {
            var glyphWidth = value.Sum(character => font.Width(character));
            var size = glyphWidth == 0 ? 11 : Math.Min(11, (int)decimal.Floor(width * 1000m / glyphWidth));
            if (size < 7) throw new AppException("Значение слишком длинное для колонки PDF.");
            var actualWidth = glyphWidth * size / 1000m;
            Text(value, rightAligned ? left + width - actualWidth : left, y, size);
        }
        Paragraph("Счёт-квитанция ТСЖ", 21); y -= 5;
        Paragraph($"Номер {invoice.Number}    Период {invoice.Period}", 13);
        Paragraph($"Лицевой счёт {invoice.AccountNumber}    Площадь {invoice.Area:N4} м²");
        Paragraph($"Плательщик: {invoice.ResidentName}");
        Paragraph($"Адрес: {invoice.Address}");
        Paragraph($"Сформирована {invoice.CreatedAt:dd.MM.yyyy}    Срок оплаты {invoice.DueDate:dd.MM.yyyy}");
        y -= 18;
        Text("Услуга", 46, y); Text("Объём", 278, y); Text("Ед.", 348, y);
        Text("Тариф", 400, y); Text("Сумма, ₽", 470, y);
        y -= 10;
        content.AppendLine($"0.7 w 46 {N(y)} m 549 {N(y)} l S");
        y -= 21;
        foreach (var line in invoice.Lines)
        {
            Cell(line.ServiceName, 46, 225, rightAligned: false);
            Cell(line.Quantity.ToString("0.####", CultureInfo.GetCultureInfo("ru-RU")), 278, 62, rightAligned: true);
            Cell(line.Unit, 348, 44, rightAligned: false);
            Cell(line.Price.ToString("0.00", CultureInfo.GetCultureInfo("ru-RU")), 400, 62, rightAligned: true);
            Cell(line.Amount.ToString("0.00", CultureInfo.GetCultureInfo("ru-RU")), 470, 79, rightAligned: true);
            y -= 25;
        }
        y -= 10;
        Paragraph($"Начислено за месяц: {invoice.TotalCharges:N2} руб.", 13);
        Paragraph($"Сальдо на начало: {invoice.BeginningBalance:N2} руб.");
        Paragraph($"Сальдо по квитанции: {invoice.ClosingBalance:N2} руб.");
        Paragraph($"К оплате при формировании: {invoice.AmountDue:N2} руб.", 14);
        Paragraph($"Учебные платежи по этой квитанции: {receipt.Paid:N2} руб.");
        Paragraph($"Текущий остаток по последней квитанции: {receipt.Remaining:N2} руб.");
        Paragraph($"Состояние: {StatusText(receipt.Status)}");
        y -= 15;
        Paragraph("Отрицательное сальдо означает переплату. Перенесённый долг оплачивается по последней квитанции.", 10);
        Paragraph("Учебная демонстрация. Этот документ не является основанием для реального платежа.", 10);
        if (y < 42) throw new AppException("Данные квитанции слишком длинные для одной страницы PDF.");

        var characters = used.Order().ToList();
        var cidMap = new byte[131072];
        foreach (var character in characters)
            BinaryPrimitives.WriteUInt16BigEndian(cidMap.AsSpan(character * 2, 2), font.Glyph(character));
        var widths = string.Join(" ", characters.Select(c => $"{(int)c} [{font.Width(c)}]"));
        var unicode = new StringBuilder("/CIDInit /ProcSet findresource begin\n12 dict begin\nbegincmap\n/CIDSystemInfo << /Registry (Adobe) /Ordering (UCS) /Supplement 0 >> def\n/CMapName /ArialUnicode def\n/CMapType 2 def\n1 begincodespacerange\n<0000> <FFFF>\nendcodespacerange\n");
        foreach (var batch in characters.Chunk(90))
        {
            unicode.AppendLine($"{batch.Length} beginbfchar");
            foreach (var c in batch) unicode.AppendLine($"<{(int)c:X4}> <{(int)c:X4}>");
            unicode.AppendLine("endbfchar");
        }
        unicode.AppendLine("endcmap\nCMapName currentdict /CMap defineresource pop\nend\nend");
        var objects = new List<byte[]>
        {
            Bytes("<< /Type /Catalog /Pages 2 0 R >>"),
            Bytes("<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            Bytes("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>"),
            Stream(Bytes(content.ToString())),
            Bytes("<< /Type /Font /Subtype /Type0 /BaseFont /Arial /Encoding /Identity-H /DescendantFonts [6 0 R] /ToUnicode 9 0 R >>"),
            Bytes($"<< /Type /Font /Subtype /CIDFontType2 /BaseFont /Arial /CIDSystemInfo << /Registry (Adobe) /Ordering (Identity) /Supplement 0 >> /FontDescriptor 7 0 R /CIDToGIDMap 10 0 R /DW 600 /W [{widths}] >>"),
            Bytes("<< /Type /FontDescriptor /FontName /Arial /Flags 32 /FontBBox [-1000 -400 2000 1200] /ItalicAngle 0 /Ascent 950 /Descent -250 /CapHeight 720 /StemV 80 /FontFile2 8 0 R >>"),
            Stream(font.Bytes, $"/Length1 {font.Bytes.Length}"),
            Stream(Bytes(unicode.ToString())),
            Stream(cidMap)
        };
        using var output = new MemoryStream();
        output.Write(Bytes("%PDF-1.4\n%Tsg\n"));
        var offsets = new List<long> { 0 };
        for (var index = 0; index < objects.Count; index++)
        {
            offsets.Add(output.Position);
            output.Write(Bytes($"{index + 1} 0 obj\n")); output.Write(objects[index]); output.Write(Bytes("\nendobj\n"));
        }
        var xref = output.Position;
        output.Write(Bytes($"xref\n0 {offsets.Count}\n0000000000 65535 f \n"));
        foreach (var offset in offsets.Skip(1)) output.Write(Bytes($"{offset:0000000000} 00000 n \n"));
        output.Write(Bytes($"trailer\n<< /Size {offsets.Count} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n"));
        return output.ToArray();
    }

    public static string StatusText(InvoiceStatus status) => status switch
    {
        InvoiceStatus.Paid => "Оплачена", InvoiceStatus.PartiallyPaid => "Частично оплачена",
        InvoiceStatus.CarriedForward => "Долг перенесён", _ => "Не оплачена"
    };
    private static string N(decimal value) => value.ToString(CultureInfo.InvariantCulture);
    private static byte[] Bytes(string text) => Encoding.ASCII.GetBytes(text);
    private static byte[] Stream(byte[] bytes, string extras = "")
    {
        using var stream = new MemoryStream();
        stream.Write(Bytes($"<< /Length {bytes.Length} {extras} >>\nstream\n")); stream.Write(bytes);
        stream.Write(Bytes("\nendstream")); return stream.ToArray();
    }

    private sealed class TrueTypeFont
    {
        public byte[] Bytes { get; }
        private readonly int cmap;
        private readonly int hmtx;
        private readonly int metricCount;
        private readonly int units;
        public TrueTypeFont(byte[] bytes)
        {
            Bytes = bytes;
            var tables = new Dictionary<string, int>();
            var count = U16(4);
            for (var i = 0; i < count; i++)
            {
                var offset = 12 + i * 16;
                tables[Encoding.ASCII.GetString(bytes, offset, 4)] = checked((int)U32(offset + 8));
            }
            units = U16(tables["head"] + 18);
            hmtx = tables["hmtx"]; metricCount = U16(tables["hhea"] + 34);
            var baseCmap = tables["cmap"];
            var cmapCount = U16(baseCmap + 2);
            for (var i = 0; i < cmapCount; i++)
            {
                var offset = baseCmap + 4 + i * 8;
                var candidate = baseCmap + checked((int)U32(offset + 4));
                if (U16(candidate) == 4 && (U16(offset) == 0 || (U16(offset) == 3 && U16(offset + 2) == 1)))
                    cmap = candidate;
            }
            if (cmap == 0) throw new AppException("Шрифт не содержит таблицы символов Unicode.");
        }
        public ushort Glyph(char character)
        {
            var segments = U16(cmap + 6) / 2;
            var ends = cmap + 14;
            var starts = ends + segments * 2 + 2;
            var deltas = starts + segments * 2;
            var ranges = deltas + segments * 2;
            for (var i = 0; i < segments; i++)
            {
                if (character < U16(starts + i * 2) || character > U16(ends + i * 2)) continue;
                var delta = U16(deltas + i * 2);
                var range = U16(ranges + i * 2);
                if (range == 0) return unchecked((ushort)(character + delta));
                var glyph = U16(ranges + i * 2 + range + (character - U16(starts + i * 2)) * 2);
                return glyph == 0 ? (ushort)0 : unchecked((ushort)(glyph + delta));
            }
            return 0;
        }
        public int Width(char character) => (int)Math.Round(U16(hmtx + Math.Min(Glyph(character), metricCount - 1) * 4) * 1000d / units);
        private ushort U16(int offset) => BinaryPrimitives.ReadUInt16BigEndian(Bytes.AsSpan(offset, 2));
        private uint U32(int offset) => BinaryPrimitives.ReadUInt32BigEndian(Bytes.AsSpan(offset, 4));
    }
}
