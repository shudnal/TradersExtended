using System;
using System.Collections.Generic;
using System.Text;

namespace TradersExtended
{
    internal static class CsvRecords
    {
        internal static List<List<string>> Parse(string content)
        {
            var rows = new List<List<string>>();
            var row = new List<string>();
            var field = new StringBuilder();
            bool quoted = false;
            bool closedQuote = false;
            content = (content ?? string.Empty).TrimStart('\uFEFF');
            for (int index = 0; index < content.Length; index++)
            {
                char current = content[index];
                if (quoted)
                {
                    if (current != '"')
                        field.Append(current);
                    else if (index + 1 < content.Length && content[index + 1] == '"')
                    {
                        field.Append('"');
                        index++;
                    }
                    else
                    {
                        quoted = false;
                        closedQuote = true;
                    }
                    continue;
                }
                if (current == ',' || current == '\r' || current == '\n')
                {
                    row.Add(field.ToString());
                    field.Clear();
                    closedQuote = false;
                    if (current != ',')
                    {
                        if (current == '\r' && index + 1 < content.Length && content[index + 1] == '\n')
                            index++;
                        rows.Add(row);
                        row = new List<string>();
                    }
                }
                else if (current == '"' && field.Length == 0 && !closedQuote)
                    quoted = true;
                else if (closedQuote || current == '"')
                    throw new FormatException("CSV contains unexpected text outside a quoted field.");
                else
                    field.Append(current);
            }
            if (quoted)
                throw new FormatException("CSV contains an unterminated quoted field.");
            if (field.Length > 0 || closedQuote || row.Count > 0)
            {
                row.Add(field.ToString());
                rows.Add(row);
            }
            return rows;
        }
    }
}
