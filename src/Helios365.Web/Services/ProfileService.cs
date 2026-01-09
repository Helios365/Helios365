using System.Security.Claims;
using Helios365.Core.Models;
using Helios365.Core.Repositories;
using Helios365.Core.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Logging;

namespace Helios365.Web.Services;

public interface IProfileService
{
    Task<User?> GetCurrentAsync(CancellationToken cancellationToken = default);
    Task<User> UpdateContactAsync(string mail, string mobilePhone, bool policiesAccepted, CancellationToken cancellationToken = default);
}

public class ProfileService : IProfileService
{
    private readonly AuthenticationStateProvider authStateProvider;
    private readonly IUserRepository userRepository;
    private readonly IDirectoryService directoryService;
    private readonly ILogger<ProfileService> logger;

    public ProfileService(
        AuthenticationStateProvider authStateProvider,
        IUserRepository userRepository,
        IDirectoryService directoryService,
        ILogger<ProfileService> logger)
    {
        this.authStateProvider = authStateProvider;
        this.userRepository = userRepository;
        this.directoryService = directoryService;
        this.logger = logger;
    }

    public async Task<User?> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        var userId = await ResolveObjectIdAsync().ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return null;
        }

        var user = await userRepository.GetAsync(userId, cancellationToken).ConfigureAwait(false);
        if (user != null)
        {
            return user;
        }

        // User not in repository yet - fetch from Entra to prefill form
        var directoryUser = await directoryService.GetUserAsync(userId, cancellationToken).ConfigureAwait(false);
        if (directoryUser == null)
        {
            return null;
        }

        return new User
        {
            Id = directoryUser.Id,
            DisplayName = directoryUser.DisplayName,
            UserPrincipalName = directoryUser.UserPrincipalName,
            Mail = directoryUser.Mail,
            MobilePhone = directoryUser.MobilePhone
        };
    }

    public async Task<User> UpdateContactAsync(string mail, string mobilePhone, bool policiesAccepted, CancellationToken cancellationToken = default)
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

        profile.PoliciesAccepted = policiesAccepted;
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
