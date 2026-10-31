using System.Text;

namespace ShadowForge.GameData.Mods.Merge;

/// <summary>
/// Splits one line of a game table into fields. A field wrapped in double quotes may hold commas,
/// and a doubled quote inside it is one quote.
/// </summary>
internal static class CsvFields
{
    public static List<string> Split(string line)
    {
        line = line.TrimEnd('\r', '\n');
        var fields = new List<string>();
        var field = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (quoted)
            {
                if (c != '"') field.Append(c);
                else if (i + 1 < line.Length && line[i + 1] == '"') { field.Append('"'); i++; }
                else quoted = false;
            }
            else if (c == '"') quoted = true;
            else if (c == ',') { fields.Add(field.ToString()); field.Clear(); }
            else field.Append(c);
        }
        fields.Add(field.ToString());
        return fields;
    }
}
