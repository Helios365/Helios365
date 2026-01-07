using Helios365.Core.Models;
using Helios365.Core.Repositories;
using Helios365.Core.Contracts;

namespace Helios365.Core.Services;

public interface IDirectorySyncService
{
    Task<User> EnsureProfileAsync(DirectoryUser user, CancellationToken cancellationToken = default);
}

public class DirectorySyncService : IDirectorySyncService
{
    private readonly IUserRepository repository;

    public DirectorySyncService(IUserRepository repository)
    {
        this.repository = repository;
    }

    public async Task<User> EnsureProfileAsync(DirectoryUser user, CancellationToken cancellationToken = default)
    {
        if (user == null) throw new ArgumentNullException(nameof(user));
        if (string.IsNullOrWhiteSpace(user.Id)) throw new ArgumentException("User id is required", nameof(user));

        var existing = await repository.GetAsync(user.Id, cancellationToken).ConfigureAwait(false);

        var profile = existing ?? new User { Id = user.Id };

        profile.DisplayName = user.DisplayName;
        profile.UserPrincipalName = user.UserPrincipalName;

        if (!string.IsNullOrWhiteSpace(user.Mail))
        {
            profile.Mail = user.Mail;
        }

        if (!string.IsNullOrWhiteSpace(user.MobilePhone))
        {
            profile.MobilePhone = user.MobilePhone;
        }

        // Preserve user choices (e.g., policy acceptance) from existing profile
        if (existing != null)
        {
            profile.PoliciesAccepted = existing.PoliciesAccepted;
        }

        profile.LastSyncedUtc = DateTimeOffset.UtcNow;

        await repository.UpsertAsync(profile, cancellationToken).ConfigureAwait(false);

        return profile;
    }
}
