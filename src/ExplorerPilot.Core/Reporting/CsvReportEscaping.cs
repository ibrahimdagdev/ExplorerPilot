using System.Text;

namespace ExplorerPilot.Core.Reporting;

/// <summary>Excel-compatible CSV escaping with formula-injection hardening.</summary>
public static class CsvReportEscaping
{
    /// <summary>
    /// Escapes a field for CSV. Prefixes formula-like values with a single quote,
    /// then applies RFC-style quoting when needed.
    /// </summary>
    public static string Escape(string? value)
    {
        var text = value ?? string.Empty;

        if (IsFormulaInjectionRisk(text))
        {
            text = "'" + text;
        }

        var needsQuotes = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c is ',' or '"' or '\r' or '\n')
            {
                needsQuotes = true;
                break;
            }
        }

        if (!needsQuotes)
        {
            return text;
        }

        var sb = new StringBuilder(text.Length + 8);
        sb.Append('"');
        foreach (var c in text)
        {
            if (c == '"')
            {
                sb.Append('"').Append('"');
            }
            else
            {
                sb.Append(c);
            }
        }

        sb.Append('"');
        return sb.ToString();
    }

    public static bool IsFormulaInjectionRisk(string value)
    {
        if (value.Length == 0)
        {
            return false;
        }

        var first = value[0];
        return first is '=' or '+' or '-' or '@' or '\t' or '\r';
    }
}
