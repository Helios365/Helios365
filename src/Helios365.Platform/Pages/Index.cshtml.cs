using Helios365.Core.Models;
using Helios365.Core.Repositories;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Helios365.Platform.Pages;

public class IndexModel : PageModel
{
    private readonly IAlertRepository _alertRepository;
    private readonly ICustomerRepository _customerRepository;
    private readonly ILogger<IndexModel> _logger;

    public IndexModel(
        IAlertRepository alertRepository,
        ICustomerRepository customerRepository,
        ILogger<IndexModel> logger)
    {
        _alertRepository = alertRepository;
        _customerRepository = customerRepository;
        _logger = logger;
    }

    public int ActiveAlertsCount { get; set; }
    public int ResolvedTodayCount { get; set; }
    public int EscalatedCount { get; set; }
    public int CustomersCount { get; set; }
    public List<Alert> RecentAlerts { get; set; } = new();

    public async Task OnGetAsync()
    {
        try
        {
            // Get recent alerts
            RecentAlerts = (await _alertRepository.ListAsync(limit: 10)).ToList();

            // Count active alerts
            var activeAlerts = await _alertRepository.ListByStatusAsync(AlertStatus.Received, limit: 1000);
            ActiveAlertsCount = activeAlerts.Count();

            // Count resolved today
            var allAlerts = await _alertRepository.ListAsync(limit: 1000);
            ResolvedTodayCount = allAlerts.Count(a =>
                (a.Status == AlertStatus.Resolved || a.Status == AlertStatus.Healthy) &&
                a.ResolvedAt.HasValue &&
                a.ResolvedAt.Value.Date == DateTime.UtcNow.Date);

            // Count escalated
            var escalatedAlerts = await _alertRepository.ListByStatusAsync(AlertStatus.Escalated, limit: 1000);
            EscalatedCount = escalatedAlerts.Count();

            // Count customers
            var customers = await _customerRepository.ListActiveAsync(limit: 1000);
            CustomersCount = customers.Count();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading dashboard data");
        }
    }
}
