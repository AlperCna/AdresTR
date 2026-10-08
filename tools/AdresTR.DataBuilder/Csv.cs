using System.Text;

namespace AdresTR.DataBuilder;

/// <summary>Minimal RFC 4180 CSV reader (UTF-8, header row, quoted fields with embedded commas, quotes and newlines).</summary>
internal static class Csv
{
    public static IEnumerable<IReadOnlyDictionary<string, string>> ReadFile(string path)
    {
        using var reader = new StreamReader(path, Encoding.UTF8);
        string[]? header = null;
        int line = 0;

        foreach (List<string> fields in ReadRecords(reader))
        {
            line++;
            if (header is null)
            {
                header = [.. fields];
                continue;
            }

            if (fields.Count == 1 && fields[0].Length == 0)
            {
                continue; // blank line
            }

            if (fields.Count != header.Length)
            {
                throw new InvalidDataException($"{Path.GetFileName(path)} record {line}: expected {header.Length} fields, found {fields.Count}.");
            }

            var record = new Dictionary<string, string>(header.Length, StringComparer.Ordinal);
            for (int i = 0; i < header.Length; i++)
            {
                record[header[i]] = fields[i];
            }

            yield return record;
        }
    }

    internal static IEnumerable<List<string>> ReadRecords(TextReader reader)
    {
        var fields = new List<string>();
        var field = new StringBuilder();
        bool inQuotes = false;
        bool any = false;
        int c;

        while ((c = reader.Read()) != -1)
        {
            any = true;
            char ch = (char)c;

            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (reader.Peek() == '"')
                    {
                        field.Append('"');
                        reader.Read();
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(ch);
                }

                continue;
            }

            switch (ch)
            {
                case '"':
                    inQuotes = true;
                    break;
                case ',':
                    fields.Add(field.ToString());
                    field.Clear();
                    break;
                case '\r':
                    break;
                case '\n':
                    fields.Add(field.ToString());
                    field.Clear();
                    yield return fields;
                    fields = [];
                    any = false;
                    break;
                default:
                    field.Append(ch);
                    break;
            }
        }

        if (inQuotes)
        {
            throw new InvalidDataException("Unterminated quoted field at end of file.");
        }

        if (any)
        {
            fields.Add(field.ToString());
            yield return fields;
        }
    }
}
