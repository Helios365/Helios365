using System.Collections.Concurrent;
using System.Reflection;
using System.Text.RegularExpressions;
using Fluid;
using Fluid.Values;
using Helios365.Core.Templates.Models;
using Microsoft.Extensions.Logging;

namespace Helios365.Core.Services;

/// <summary>
/// Service for rendering email templates.
/// </summary>
public interface IEmailTemplateService
{
    /// <summary>
    /// Renders the alert notification email template.
    /// </summary>
    Task<EmailTemplateResult> RenderAlertNotificationAsync(AlertEmailModel model, CancellationToken cancellationToken = default);

    /// <summary>
    /// Renders a named template with the provided model.
    /// </summary>
    Task<EmailTemplateResult> RenderTemplateAsync<TModel>(string templateName, TModel model, CancellationToken cancellationToken = default);
}

/// <summary>
/// Renders email templates using Fluid (Liquid) templating engine.
/// Templates are loaded from embedded resources.
/// </summary>
public partial class EmailTemplateService : IEmailTemplateService
{
    private readonly FluidParser _parser;
    private readonly TemplateOptions _templateOptions;
    private readonly ConcurrentDictionary<string, IFluidTemplate> _templateCache = new();
    private readonly ILogger<EmailTemplateService> _logger;
    private readonly Assembly _assembly;

    private const string TemplateNamespace = "Helios365.Core.Templates.Email";
    private const string LayoutTemplateName = "Layouts._BaseLayout";

    public EmailTemplateService(ILogger<EmailTemplateService> logger)
    {
        _logger = logger;
        _parser = new FluidParser();
        _assembly = typeof(EmailTemplateService).Assembly;

        _templateOptions = new TemplateOptions();
        _templateOptions.MemberAccessStrategy = new UnsafeMemberAccessStrategy();
        _templateOptions.ValueConverters.Add(o => o is DateTime dt ? new StringValue(dt.ToString("u")) : null);
    }

    public async Task<EmailTemplateResult> RenderAlertNotificationAsync(AlertEmailModel model, CancellationToken cancellationToken = default)
    {
        return await RenderTemplateAsync("AlertNotification", model, cancellationToken);
    }

    public async Task<EmailTemplateResult> RenderTemplateAsync<TModel>(string templateName, TModel model, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateName);
        ArgumentNullException.ThrowIfNull(model);

        _logger.LogDebug("Rendering email template: {TemplateName}", templateName);

        var htmlTemplate = await GetOrLoadTemplateAsync(templateName);
        var layoutTemplate = await GetOrLoadTemplateAsync(LayoutTemplateName);

        var context = new TemplateContext(model, _templateOptions);

        // Render the content template first
        var contentHtml = await htmlTemplate.RenderAsync(context);

        // Then render the layout with the content
        context.SetValue("content", contentHtml);
        context.SetValue("model", model);
        var fullHtml = await layoutTemplate.RenderAsync(context);

        // Generate plain-text version from HTML
        var plainText = ConvertHtmlToPlainText(fullHtml);

        // Extract subject from model if it has a Title property, otherwise use template name
        var subject = GetSubjectFromModel(model, templateName);

        _logger.LogDebug("Successfully rendered template: {TemplateName}", templateName);

        return new EmailTemplateResult
        {
            Subject = subject,
            HtmlBody = fullHtml,
            PlainTextBody = plainText
        };
    }

    private async Task<IFluidTemplate> GetOrLoadTemplateAsync(string templateName)
    {
        if (_templateCache.TryGetValue(templateName, out var cachedTemplate))
        {
            return cachedTemplate;
        }

        var resourceName = $"{TemplateNamespace}.{templateName}.liquid";
        var templateContent = await LoadEmbeddedResourceAsync(resourceName);

        if (!_parser.TryParse(templateContent, out var template, out var error))
        {
            _logger.LogError("Failed to parse template {TemplateName}: {Error}", templateName, error);
            throw new InvalidOperationException($"Failed to parse email template '{templateName}': {error}");
        }

        _templateCache.TryAdd(templateName, template);
        return template;
    }

    private async Task<string> LoadEmbeddedResourceAsync(string resourceName)
    {
        await using var stream = _assembly.GetManifestResourceStream(resourceName);

        if (stream is null)
        {
            var availableResources = _assembly.GetManifestResourceNames();
            _logger.LogError(
                "Template resource not found: {ResourceName}. Available: {Available}",
                resourceName,
                string.Join(", ", availableResources));
            throw new FileNotFoundException($"Email template resource not found: {resourceName}");
        }

        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }

    private static string GetSubjectFromModel<TModel>(TModel model, string templateName)
    {
        // Try to get subject from model properties
        var modelType = typeof(TModel);

        // Check for AlertEmailModel specifically
        if (model is AlertEmailModel alertModel)
        {
            return $"[Helios365] {alertModel.Severity}: {alertModel.Title}";
        }

        // Try to get Title property
        var titleProp = modelType.GetProperty("Title");
        if (titleProp?.GetValue(model) is string title)
        {
            return $"[Helios365] {title}";
        }

        // Try to get Subject property
        var subjectProp = modelType.GetProperty("Subject");
        if (subjectProp?.GetValue(model) is string subject)
        {
            return subject;
        }

        // Fallback to template name
        return $"[Helios365] {templateName}";
    }

    private static string ConvertHtmlToPlainText(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        // Remove style and script blocks
        var result = StyleScriptRegex().Replace(html, string.Empty);

        // Replace common block elements with newlines
        result = BlockElementRegex().Replace(result, "\n");

        // Replace <br> tags with newlines
        result = BrTagRegex().Replace(result, "\n");

        // Replace <li> with bullet points
        result = ListItemRegex().Replace(result, "\n• ");

        // Remove remaining HTML tags
        result = HtmlTagRegex().Replace(result, string.Empty);

        // Decode HTML entities
        result = System.Net.WebUtility.HtmlDecode(result);

        // Normalize whitespace
        result = MultipleSpacesRegex().Replace(result, " ");
        result = MultipleNewlinesRegex().Replace(result, "\n\n");

        return result.Trim();
    }

    [GeneratedRegex(@"<(style|script)[^>]*>.*?</\1>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex StyleScriptRegex();

    [GeneratedRegex(@"</(div|p|h[1-6]|tr|table)>", RegexOptions.IgnoreCase)]
    private static partial Regex BlockElementRegex();

    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex BrTagRegex();

    [GeneratedRegex(@"<li[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex ListItemRegex();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex(@"[ \t]+")]
    private static partial Regex MultipleSpacesRegex();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex MultipleNewlinesRegex();
}
