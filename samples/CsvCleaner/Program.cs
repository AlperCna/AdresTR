// Cleans the address column of a CSV file and appends structured columns.
//
//   dotnet run --project samples/CsvCleaner -- samples/CsvCleaner/orders.csv adres > cleaned.csv
//
// Rows with confidence below 0.8 are worth a human look (the "check" column says so).

using System.Globalization;
using System.Text;
using AdresTR;
using AdresTR.Data;

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: CsvCleaner <input.csv> <address-column>");
    return 2;
}

List<List<string>> rows = Csv.Read(File.ReadAllText(args[0], Encoding.UTF8));
int column = rows[0].IndexOf(args[1]);
if (column < 0)
{
    Console.Error.WriteLine($"Column '{args[1]}' not found. Columns: {string.Join(", ", rows[0])}");
    return 2;
}

string[] extra = ["il", "ilce", "mahalle", "posta_kodu", "adres_kanonik", "guven", "kontrol"];
Console.OutputEncoding = Encoding.UTF8;
Console.WriteLine(Csv.Line([.. rows[0], .. extra]));

foreach (List<string> row in rows.Skip(1))
{
    ParseResult r = TurkishGazetteer.Parser.Parse(row[column]);
    Console.WriteLine(Csv.Line(
    [
        .. row,
        r.Province?.Name ?? "",
        r.District?.Name ?? "",
        r.Unit?.Name ?? r.Mahalle?.Value ?? "",
        r.PostalCode?.Value ?? r.Unit?.PostalCode ?? "",
        r.ToCanonicalString(),
        r.Confidence.ToString("0.00", CultureInfo.InvariantCulture),
        r.Unit is null || r.Confidence < 0.8 ? "kontrol et" : "",
    ]));
}

return 0;

/// <summary>Minimal RFC 4180 CSV reading and writing for the sample.</summary>
internal static class Csv
{
    public static List<List<string>> Read(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                else if (c == '"') { quoted = false; }
                else { field.Append(c); }
            }
            else if (c == '"') { quoted = true; }
            else if (c == ',') { row.Add(field.ToString()); field.Clear(); }
            else if (c == '\n') { row.Add(field.ToString()); field.Clear(); rows.Add(row); row = []; }
            else if (c != '\r') { field.Append(c); }
        }

        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row);
        }

        return rows;
    }

    public static string Line(IEnumerable<string> fields) =>
        string.Join(',', fields.Select(f => f.AsSpan().IndexOfAny(",\"\n") >= 0 ? $"\"{f.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : f));
}
