using System.Net;
using System.Text;

namespace ExplorerPilot.Core.Reporting;

/// <summary>HTML escaping helpers for offline report documents.</summary>
public static class HtmlReportEscaping
{
    public static string Escape(string? value) =>
        WebUtility.HtmlEncode(value ?? string.Empty);

    public static void AppendEscaped(StringBuilder sb, string? value) =>
        sb.Append(Escape(value));
}
