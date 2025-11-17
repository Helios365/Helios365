using Helios365.Core.Models;

namespace Helios365.Core.Repositories;

public interface ISecretRepository
{
    /// <summary>
    /// Stores the plaintext secret for the given Service Principal in the backing secret store
    /// and returns a stable reference (e.g., Key Vault secret URL without version).
    /// </summary>
    Task<string> SetServicePrincipalSecretAsync(ServicePrincipal sp, string plaintextSecret, CancellationToken cancellationToken = default);
}

