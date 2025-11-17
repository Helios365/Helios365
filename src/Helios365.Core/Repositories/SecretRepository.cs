using System.Text.RegularExpressions;
using Azure;
using Azure.Security.KeyVault.Secrets;
using Helios365.Core.Models;
using Microsoft.Extensions.Logging;

namespace Helios365.Core.Repositories;

public class SecretRepository : ISecretRepository
{
    private readonly SecretClient _secretClient;
    private readonly ILogger<SecretRepository> _logger;

    public SecretRepository(SecretClient secretClient, ILogger<SecretRepository> logger)
    {
        _secretClient = secretClient;
        _logger = logger;
    }

    public async Task<string> SetServicePrincipalSecretAsync(ServicePrincipal sp, string plaintextSecret, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(plaintextSecret))
            throw new ArgumentException("Secret cannot be empty", nameof(plaintextSecret));

        var name = BuildSecretName(sp);

        try
        {
            var kvSecret = new KeyVaultSecret(name, plaintextSecret);
            kvSecret.Properties.Tags["app"] = "helios365";
            kvSecret.Properties.Tags["type"] = "serviceprincipal";
            kvSecret.Properties.Tags["customerId"] = sp.CustomerId ?? string.Empty;
            kvSecret.Properties.Tags["servicePrincipalId"] = sp.Id ?? string.Empty;

            await _secretClient.SetSecretAsync(kvSecret, cancellationToken);

            var reference = new Uri(_secretClient.VaultUri, $"secrets/{name}").ToString();
            return reference;
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, "Failed to set secret {SecretName} in Key Vault", name);
            throw;
        }
    }

    private static string BuildSecretName(ServicePrincipal sp)
    {
        var customer = SanitizeSegment(sp.CustomerId);
        var id = SanitizeSegment(sp.Id);
        var name = $"sp-{customer}-{id}".ToLowerInvariant();
        return name.Length <= 127 ? name : name[^127..];
    }

    private static string SanitizeSegment(string? value)
    {
        var v = value ?? string.Empty;
        v = Regex.Replace(v, "[^a-zA-Z0-9-]", "-");
        v = Regex.Replace(v, "-+", "-");
        v = v.Trim('-');
        if (string.IsNullOrEmpty(v)) v = "na";
        return v;
    }
}
