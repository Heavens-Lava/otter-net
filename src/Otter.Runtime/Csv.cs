using System.Text;
using System.Text.RegularExpressions;

namespace Otter.Runtime;

// CSV (D95), as otterPS's ConvertFrom-OtterCsvText and ConvertTo-OtterCsvText:
// the first row names the columns, each further row becomes a thing whose
// properties are text, and writing quotes a cell only when it must.
public sealed class CsvProblem(string message) : Exception(message);

public static class OtterCsv
{
    const string NoHeader = "Otter needs a header row to read CSV into things.";

    public static OtterList Read(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new CsvProblem(NoHeader);

        var rows = new List<List<string>>();
        var row = new List<string>();
        var cell = new StringBuilder();
        bool inQuotes = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (inQuotes)
            {
                if (c != '"') cell.Append(c);
                else if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                else inQuotes = false;
                continue;
            }
            switch (c)
            {
                case '"': inQuotes = true; break;
                case ',': row.Add(cell.ToString()); cell.Clear(); break;
                case '\r' or '\n':
                    if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                    row.Add(cell.ToString());
                    cell.Clear();
                    rows.Add(row);
                    row = new List<string>();
                    break;
                default: cell.Append(c); break;
            }
        }
        if (inQuotes) throw new CsvProblem("This is not valid CSV, so Otter could not read it.");
        if (cell.Length > 0 || row.Count > 0)
        {
            row.Add(cell.ToString());
            rows.Add(row);
        }
        // Trailing empty lines are not rows (RFC 4180).
        while (rows.Count > 0 && rows[^1].Count == 1 && rows[^1][0] == "") rows.RemoveAt(rows.Count - 1);
        if (rows.Count == 0 || rows[0].Count == 0) throw new CsvProblem(NoHeader);

        List<string> headers = rows[0];
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string header in headers)
        {
            if (string.IsNullOrWhiteSpace(header)) throw new CsvProblem("CSV headers cannot be empty.");
            if (!seen.Add(header)) throw new CsvProblem($"CSV header \"{header}\" appears more than once.");
        }

        var result = new OtterList();
        for (int r = 1; r < rows.Count; r++)
        {
            if (rows[r].Count != headers.Count)
            {
                throw new CsvProblem($"Row {r + 1} has {rows[r].Count} fields, but the CSV header defines {headers.Count} columns.");
            }
            var thing = new OtterThing("thing");
            for (int c = 0; c < headers.Count; c++) thing.WriteProperty(headers[c], rows[r][c]);
            result.Add(thing);
        }
        return result;
    }

    const string OnlyThings = "Otter can only write a list of things to CSV.";

    // Lines end with "\r\n", the header first.
    public static string Write(object? rows)
    {
        if (rows is not OtterList list) throw new CsvProblem(OnlyThings);
        if (list.Count == 0) throw new CsvProblem("Otter cannot write an empty list to CSV because there are no column headers.");
        if (list[0] is not OtterThing first) throw new CsvProblem(OnlyThings);
        var columns = first.PropertyNames.ToList();
        if (columns.Count == 0) throw new CsvProblem("Otter cannot write things with no properties to CSV.");
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] is not OtterThing item) throw new CsvProblem(OnlyThings);
            if (item.PropertyNames.Count != columns.Count || !columns.All(item.HasProperty))
            {
                throw new CsvProblem($"Row {i + 2} properties do not match the columns defined by the first row.");
            }
        }

        var sb = new StringBuilder();
        sb.Append(string.Join(",", columns.Select(c => Cell(c)))).Append("\r\n");
        foreach (OtterThing item in list.Cast<OtterThing>())
        {
            sb.Append(string.Join(",", columns.Select(c => Cell(item.ReadProperty(c))))).Append("\r\n");
        }
        return sb.ToString();
    }

    static readonly Regex NeedsQuotes = new("[\",\r\n]");

    static string Cell(object? value)
    {
        if (value is null) return "";
        string text = Values.Format(value);
        return NeedsQuotes.IsMatch(text) ? "\"" + text.Replace("\"", "\"\"") + "\"" : text;
    }
}
