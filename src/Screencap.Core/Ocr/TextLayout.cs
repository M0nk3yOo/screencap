using System.Text;
using Screencap.Core.Platform;

namespace Screencap.Core.Ocr;

public static class TextLayout
{
    /// <summary>
    /// Turns OCR lines into plain text in reading order. Engines report lines in their own
    /// order and split one visual line into several pieces (columns, table cells), so pieces
    /// whose vertical centers are close are merged into one line, left to right.
    /// </summary>
    public static string ToPlainText(IEnumerable<RecognizedLine> lines)
    {
        var rows = new List<List<RecognizedLine>>();
        foreach (var line in lines.Where(l => !string.IsNullOrWhiteSpace(l.Text)).OrderBy(l => l.Box.Y))
        {
            var center = line.Box.Y + line.Box.Height / 2;
            var row = rows.LastOrDefault();
            if (row is not null)
            {
                var reference = row[0].Box;
                if (Math.Abs(center - (reference.Y + reference.Height / 2)) < Math.Min(reference.Height, line.Box.Height) / 2)
                {
                    row.Add(line);
                    continue;
                }
            }
            rows.Add([line]);
        }

        var sb = new StringBuilder();
        foreach (var row in rows)
        {
            if (sb.Length > 0) sb.Append('\n');
            sb.AppendJoin("  ", row.OrderBy(l => l.Box.X).Select(l => l.Text.Trim()));
        }
        return sb.ToString();
    }
}
