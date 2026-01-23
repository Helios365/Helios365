namespace Helios365.Core.Templates.Models;

/// <summary>
/// Result of rendering an email template, containing both HTML and plain-text versions.
/// </summary>
public class EmailTemplateResult
{
    public required string Subject { get; init; }
    public required string HtmlBody { get; init; }
    public required string PlainTextBody { get; init; }
}
