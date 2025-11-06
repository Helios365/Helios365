using Helios365.Core.Models;
using Helios365.Core.Repositories;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Helios365.Platform.Pages;

public class IndexModel : PageModel
{
    private readonly IAlertRepository _alertRepository;
    private readonly ICustomerRepository _customerRepository;
    private readonly IResourceRepository _resourceRepository;
    private readonly ILogger<IndexModel> _logger;

    public IndexModel(
        IAlertRepository alertRepository,
        ICustomerRepository customerRepository,
        IResourceRepository resourceRepository,
        ILogger<IndexModel> logger)
    {
        _alertRepository = alertRepository;
        _customerRepository = customerRepository;
        _resourceRepository = resourceRepository;
        _logger = logger;
    }

    public int ActiveAlertsCount { get; set; }
    public int ResolvedTodayCount { get; set; }
    public int EscalatedCount { get; set; }
    public int CustomersCount { get; set; }
    public int ResourcesCount { get; set; }
    public List<Alert> RecentAlerts { get; set; } = [];
    public Dictionary<string, Customer> Customers { get; set; } = [];

    public async Task OnGetAsync()
    {
        try
        {
            // Get recent alerts
            RecentAlerts = (await _alertRepository.ListAsync(limit: 20)).ToList();

            // Load customers for display
            var customerIds = RecentAlerts.Select(a => a.CustomerId).Distinct();
            foreach (var customerId in customerIds)
            {
                var customer = await _customerRepository.GetAsync(customerId);
                if (customer != null)
                {
                    Customers[customerId] = customer;
                }
            }

            // Count active alerts (not resolved, healthy, or failed)
            var allAlerts = await _alertRepository.ListAsync(limit: 1000);
            ActiveAlertsCount = allAlerts.Count(a => 
                a.Status != AlertStatus.Resolved && 
                a.Status != AlertStatus.Healthy && 
                a.Status != AlertStatus.Failed);

            // Count resolved today
            ResolvedTodayCount = allAlerts.Count(a =>
                (a.Status == AlertStatus.Resolved || a.Status == AlertStatus.Healthy) &&
                a.ResolvedAt.HasValue &&
                a.ResolvedAt.Value.Date == DateTime.UtcNow.Date);

            // Count escalated
            EscalatedCount = allAlerts.Count(a => a.Status == AlertStatus.Escalated);

            // Count customers
            var customers = await _customerRepository.ListActiveAsync(limit: 1000);
            CustomersCount = customers.Count();

            // Count resources
            var resources = await _resourceRepository.ListAsync(limit: 1000);
            ResourcesCount = resources.Count();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading dashboard data");
        }
    }
}
