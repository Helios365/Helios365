using System.Collections.ObjectModel;

namespace Helios365.Core.Contracts;

public sealed class CommunicationSendResult
{
    public bool Succeeded { get; init; }

    public IReadOnlyList<string> Recipients { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> MessageIds { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> FailedRecipients { get; init; } = Array.Empty<string>();

    public string? Error { get; init; }

    public static CommunicationSendResult Success(IEnumerable<string> recipients, IEnumerable<string> messageIds) =>
        new()
        {
            Succeeded = true,
            Recipients = Normalize(recipients),
            MessageIds = Normalize(messageIds)
        };

    public static CommunicationSendResult Failure(
        IEnumerable<string> recipients,
        string error,
        IEnumerable<string>? failedRecipients = null,
        IEnumerable<string>? messageIds = null) =>
        new()
        {
            Succeeded = false,
            Recipients = Normalize(recipients),
            FailedRecipients = Normalize(failedRecipients ?? Enumerable.Empty<string>()),
            MessageIds = Normalize(messageIds ?? Enumerable.Empty<string>()),
            Error = error
        };

    private static IReadOnlyList<string> Normalize(IEnumerable<string> values) =>
        new ReadOnlyCollection<string>(
            values
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList());
}
