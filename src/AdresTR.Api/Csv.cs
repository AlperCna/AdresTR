using System.Text;

namespace AdresTR.Api;

/// <summary>Minimal RFC 4180 CSV reading and writing.</summary>
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
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    quoted = false;
                }
                else
                {
                    field.Append(c);
                }
            }
            else if (c == '"')
            {
                quoted = true;
            }
            else if (c == ',')
            {
                row.Add(field.ToString());
                field.Clear();
            }
            else if (c == '\n')
            {
                row.Add(field.ToString());
                field.Clear();
                rows.Add(row);
                row = [];
            }
            else if (c != '\r' && c != '\uFEFF')
            {
                field.Append(c);
            }
        }

        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row);
        }

        return rows;
    }

    public static string Line(IEnumerable<string> fields) =>
        string.Join(',', fields.Select(f => f.AsSpan().IndexOfAny(",\"\n\r") >= 0 ? $"\"{f.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : f));
}
