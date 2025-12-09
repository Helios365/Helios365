using Helios365.Core.Models;
using Helios365.Core.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using GraphSdk = Microsoft.Graph;

namespace Helios365.Core.Services;

public interface IDirectoryService
{
    Task<DirectoryUser?> GetUserAsync(string userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DirectoryUser>> GetUsersAsync(IEnumerable<DirectoryRole> roles, CancellationToken cancellationToken = default);
}

public class DirectoryService : IDirectoryService
{
    private static readonly string[] UserSelect = new[]
    {
        "id",
        "displayName",
        "userPrincipalName",
        "mail",
        "mobilePhone",
        "businessPhones",
        "accountEnabled"
    };

    private readonly GraphSdk.GraphServiceClient graphClient;
    private readonly ILogger<DirectoryService> logger;
    private readonly DirectoryServiceOptions options;

    public DirectoryService(
        GraphSdk.GraphServiceClient graphClient,
        IOptions<DirectoryServiceOptions> options,
        ILogger<DirectoryService> logger)
    {
        this.graphClient = graphClient;
        this.logger = logger;
        this.options = options.Value;
    }

    public async Task<DirectoryUser?> GetUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("User id is required", nameof(userId));
        }

        try
        {
            var user = await graphClient.Users[userId]
                .GetAsync(request =>
                {
                    request.QueryParameters.Select = UserSelect;
                }, cancellationToken)
                .ConfigureAwait(false);

            if (user == null || (user.AccountEnabled.HasValue && !user.AccountEnabled.Value))
            {
                return null;
            }

            return Map(user);
        }
        catch (GraphSdk.Models.ODataErrors.ODataError ex)
        {
            logger.LogWarning(ex, "Graph GetUserAsync failed for user {UserId}", userId);
            return null;
        }
    }

    public async Task<IReadOnlyList<DirectoryUser>> GetUsersAsync(IEnumerable<DirectoryRole> roles, CancellationToken cancellationToken = default)
    {
        var groupIds = ResolveGroupIds(roles);

        if (groupIds.Count == 0)
        {
            return Array.Empty<DirectoryUser>();
        }

        var results = new List<DirectoryUser>();

        foreach (var groupId in groupIds)
        {
            try
            {
                var members = await GetGroupMembersAsync(groupId, cancellationToken).ConfigureAwait(false);
                results.AddRange(members);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to fetch members for group {GroupId}", groupId);
            }
        }

        return results
            .GroupBy(u => u.Id, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(u => u.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private List<string> ResolveGroupIds(IEnumerable<DirectoryRole> roles)
    {
        var ids = new List<string>();

        foreach (var role in roles)
        {
            var id = role switch
            {
                DirectoryRole.Admin => options.Groups.Admin,
                DirectoryRole.Operator => options.Groups.Operator,
                DirectoryRole.Reader => options.Groups.Reader,
                _ => null
            };

            if (!string.IsNullOrWhiteSpace(id))
            {
                ids.Add(id);
            }
        }

        return ids
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<IReadOnlyList<DirectoryUser>> GetGroupMembersAsync(string groupId, CancellationToken cancellationToken)
    {
        var results = new List<DirectoryUser>();

            var response = await graphClient.Groups[groupId].Members.GraphUser
                .GetAsync(request =>
                {
                    request.QueryParameters.Select = UserSelect;
                    request.QueryParameters.Top = 999;
                }, cancellationToken)
            .ConfigureAwait(false);

        while (response != null)
        {
            if (response.Value != null)
            {
                foreach (var user in response.Value)
                {
                    if (user == null)
                    {
                        continue;
                    }

                    if (user.AccountEnabled.HasValue && !user.AccountEnabled.Value)
                    {
                        continue;
                    }

                    results.Add(Map(user));
                }
            }

            if (string.IsNullOrEmpty(response.OdataNextLink))
            {
                break;
            }

            response = await graphClient.Groups[groupId].Members.GraphUser
                .WithUrl(response.OdataNextLink)
                .GetAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        return results;
    }

    private static DirectoryUser Map(GraphSdk.Models.User user) => new()
    {
        Id = user.Id ?? string.Empty,
        DisplayName = user.DisplayName ?? string.Empty,
        UserPrincipalName = user.UserPrincipalName ?? string.Empty,
        Mail = user.Mail,
        MobilePhone = user.MobilePhone,
        BusinessPhones = user.BusinessPhones?.Where(p => !string.IsNullOrWhiteSpace(p)).ToArray() ?? Array.Empty<string>()
    };
}
