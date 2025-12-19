using System.Security.Claims;
using Helios365.Core.Models;
using Helios365.Core.Repositories;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Logging;

namespace Helios365.Web.Services;

public interface IProfileService
{
    Task<User?> GetCurrentAsync(CancellationToken cancellationToken = default);
    Task<User> UpdateContactAsync(string mail, string mobilePhone, bool notificationConsentGranted, CancellationToken cancellationToken = default);
}

public class ProfileService : IProfileService
{
    private readonly AuthenticationStateProvider authStateProvider;
    private readonly IUserRepository userRepository;
    private readonly ILogger<ProfileService> logger;

    public ProfileService(
        AuthenticationStateProvider authStateProvider,
        IUserRepository userRepository,
        ILogger<ProfileService> logger)
    {
        this.authStateProvider = authStateProvider;
        this.userRepository = userRepository;
        this.logger = logger;
    }

    public async Task<User?> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        var userId = await ResolveObjectIdAsync().ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return null;
        }

        return await userRepository.GetAsync(userId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<User> UpdateContactAsync(string mail, string mobilePhone, bool notificationConsentGranted, CancellationToken cancellationToken = default)
    {
        var authState = await authStateProvider.GetAuthenticationStateAsync().ConfigureAwait(false);
        var principal = authState.User;

        var userId = principal.FindFirstValue("http://schemas.microsoft.com/identity/claims/objectidentifier");
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new InvalidOperationException("User id not found in claims.");
        }

        var profile = await userRepository.GetAsync(userId, cancellationToken).ConfigureAwait(false)
            ?? new User { Id = userId };

        profile.DisplayName = principal.Identity?.Name ?? profile.DisplayName;
        profile.UserPrincipalName = principal.FindFirstValue("preferred_username") ?? profile.UserPrincipalName;

        if (!string.IsNullOrWhiteSpace(mail))
        {
            profile.Mail = mail.Trim();
        }

        if (!string.IsNullOrWhiteSpace(mobilePhone))
        {
            profile.MobilePhone = mobilePhone.Trim();
        }

        profile.NotificationConsentGranted = notificationConsentGranted;
        profile.LastSyncedUtc = DateTimeOffset.UtcNow;

        try
        {
            await userRepository.UpsertAsync(profile, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to update contact information for user {UserId}", userId);
            throw;
        }

        return profile;
    }

    private async Task<string?> ResolveObjectIdAsync()
    {
        var authState = await authStateProvider.GetAuthenticationStateAsync().ConfigureAwait(false);
        var principal = authState.User;
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        return principal.FindFirstValue("http://schemas.microsoft.com/identity/claims/objectidentifier");
    }
}
