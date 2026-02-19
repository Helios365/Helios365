using Helios365.Core.Models;
using Helios365.Core.Repositories;
using Microsoft.Extensions.Logging;

namespace Helios365.Core.Services;

public interface ICustomerService
{
    // Customer operations
    Task<Customer?> GetCustomerAsync(string id, CancellationToken cancellationToken = default);
    Task<IEnumerable<Customer>> ListCustomersAsync(int limit = 100, int offset = 0, CancellationToken cancellationToken = default);
    Task<IEnumerable<Customer>> ListActiveCustomersAsync(int limit = 100, CancellationToken cancellationToken = default);
    Task<Customer> CreateCustomerAsync(Customer customer, CancellationToken cancellationToken = default);
    Task<Customer> UpdateCustomerAsync(string id, Customer customer, CancellationToken cancellationToken = default);
    Task<bool> DeleteCustomerAsync(string id, CancellationToken cancellationToken = default);

    // Service principal operations
    Task<ServicePrincipal?> GetServicePrincipalAsync(string id, CancellationToken cancellationToken = default);
    Task<IEnumerable<ServicePrincipal>> ListServicePrincipalsAsync(string customerId, int limit = 100, CancellationToken cancellationToken = default);
    Task<IEnumerable<ServicePrincipal>> ListAllServicePrincipalsAsync(int limit = 100, int offset = 0, CancellationToken cancellationToken = default);
    Task<ServicePrincipal> CreateServicePrincipalAsync(ServicePrincipal servicePrincipal, string? clientSecret = null, CancellationToken cancellationToken = default);
    Task<ServicePrincipal> UpdateServicePrincipalAsync(string id, ServicePrincipal servicePrincipal, string? newClientSecret = null, CancellationToken cancellationToken = default);
    Task<bool> DeleteServicePrincipalAsync(string id, CancellationToken cancellationToken = default);
}

public class CustomerService : ICustomerService
{
    private readonly ICustomerRepository _customerRepository;
    private readonly IServicePrincipalRepository _servicePrincipalRepository;
    private readonly ISecretRepository _secretRepository;
    private readonly ILogger<CustomerService> _logger;

    public CustomerService(
        ICustomerRepository customerRepository,
        IServicePrincipalRepository servicePrincipalRepository,
        ISecretRepository secretRepository,
        ILogger<CustomerService> logger)
    {
        _customerRepository = customerRepository;
        _servicePrincipalRepository = servicePrincipalRepository;
        _secretRepository = secretRepository;
        _logger = logger;
    }

    // Customer operations

    public async Task<Customer?> GetCustomerAsync(string id, CancellationToken cancellationToken = default)
    {
        return await _customerRepository.GetAsync(id, cancellationToken);
    }

    public async Task<IEnumerable<Customer>> ListCustomersAsync(int limit = 100, int offset = 0, CancellationToken cancellationToken = default)
    {
        return await _customerRepository.ListAsync(limit, offset, cancellationToken);
    }

    public async Task<IEnumerable<Customer>> ListActiveCustomersAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        return await _customerRepository.ListActiveAsync(limit, cancellationToken);
    }

    public async Task<Customer> CreateCustomerAsync(Customer customer, CancellationToken cancellationToken = default)
    {
        customer.CreatedAt = DateTime.UtcNow;
        customer.UpdatedAt = DateTime.UtcNow;
        return await _customerRepository.CreateAsync(customer, cancellationToken);
    }

    public async Task<Customer> UpdateCustomerAsync(string id, Customer customer, CancellationToken cancellationToken = default)
    {
        customer.UpdatedAt = DateTime.UtcNow;
        return await _customerRepository.UpdateAsync(id, customer, cancellationToken);
    }

    public async Task<bool> DeleteCustomerAsync(string id, CancellationToken cancellationToken = default)
    {
        return await _customerRepository.DeleteAsync(id, cancellationToken);
    }

    // Service principal operations

    public async Task<ServicePrincipal?> GetServicePrincipalAsync(string id, CancellationToken cancellationToken = default)
    {
        return await _servicePrincipalRepository.GetAsync(id, cancellationToken);
    }

    public async Task<IEnumerable<ServicePrincipal>> ListServicePrincipalsAsync(string customerId, int limit = 100, CancellationToken cancellationToken = default)
    {
        return await _servicePrincipalRepository.ListByCustomerAsync(customerId, limit, cancellationToken);
    }

    public async Task<IEnumerable<ServicePrincipal>> ListAllServicePrincipalsAsync(int limit = 100, int offset = 0, CancellationToken cancellationToken = default)
    {
        return await _servicePrincipalRepository.ListAsync(limit, offset, cancellationToken);
    }

    public async Task<ServicePrincipal> CreateServicePrincipalAsync(ServicePrincipal servicePrincipal, string? clientSecret = null, CancellationToken cancellationToken = default)
    {
        servicePrincipal.CreatedAt = DateTime.UtcNow;
        servicePrincipal.UpdatedAt = DateTime.UtcNow;

        if (!string.IsNullOrWhiteSpace(clientSecret))
        {
            var reference = await _secretRepository.SetServicePrincipalSecretAsync(servicePrincipal, clientSecret, cancellationToken);
            servicePrincipal.ClientSecretKeyVaultReference = reference;
        }
        else if (string.IsNullOrWhiteSpace(servicePrincipal.ClientSecretKeyVaultReference))
        {
            throw new InvalidOperationException("Client secret is required for new service principals");
        }

        var created = await _servicePrincipalRepository.CreateAsync(servicePrincipal, cancellationToken);
        _logger.LogInformation("Created service principal {Name} for customer {CustomerId}", created.Name, created.CustomerId);
        return created;
    }

    public async Task<ServicePrincipal> UpdateServicePrincipalAsync(string id, ServicePrincipal servicePrincipal, string? newClientSecret = null, CancellationToken cancellationToken = default)
    {
        servicePrincipal.UpdatedAt = DateTime.UtcNow;

        if (!string.IsNullOrWhiteSpace(newClientSecret))
        {
            var reference = await _secretRepository.SetServicePrincipalSecretAsync(servicePrincipal, newClientSecret, cancellationToken);
            servicePrincipal.ClientSecretKeyVaultReference = reference;
        }

        var updated = await _servicePrincipalRepository.UpdateAsync(id, servicePrincipal, cancellationToken);
        _logger.LogInformation("Updated service principal {Name} for customer {CustomerId}", updated.Name, updated.CustomerId);
        return updated;
    }

    public async Task<bool> DeleteServicePrincipalAsync(string id, CancellationToken cancellationToken = default)
    {
        return await _servicePrincipalRepository.DeleteAsync(id, cancellationToken);
    }
}
