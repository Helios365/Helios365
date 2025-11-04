using Helios365.Core.Models;
using Helios365.Core.Repositories;
using Helios365.Core.Services;
using Helios365.Processor.Activities;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Helios365.Processor.Tests.Activities;

public class AlertActivitiesTests
{
    private readonly Mock<IAlertRepository> _mockAlertRepo;
    private readonly Mock<ICustomerRepository> _mockCustomerRepo;
    private readonly Mock<IHealthCheckService> _mockHealthCheckService;
    private readonly Mock<ILogger<AlertActivities>> _mockLogger;
    private readonly AlertActivities _activities;

    public AlertActivitiesTests()
    {
        _mockAlertRepo = new Mock<IAlertRepository>();
        _mockCustomerRepo = new Mock<ICustomerRepository>();
        _mockHealthCheckService = new Mock<IHealthCheckService>();
        _mockLogger = new Mock<ILogger<AlertActivities>>();

        _activities = new AlertActivities(
            _mockLogger.Object,
            _mockAlertRepo.Object,
            _mockCustomerRepo.Object,
            _mockHealthCheckService.Object
        );
    }

    [Fact]
    public async Task UpdateAlertActivity_UpdatesAlert()
    {
        // Arrange
        var alert = new Alert
        {
            Id = "alert-1",
            CustomerId = "customer-1",
            ResourceId = "/subscriptions/test",
            ResourceType = "Microsoft.Web/sites",
            AlertType = "ServiceHealthAlert",
            Status = AlertStatus.Checking
        };

        _mockAlertRepo
            .Setup(x => x.UpdateAsync(It.IsAny<string>(), It.IsAny<Alert>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(alert);

        // Act
        await _activities.UpdateAlertActivity(alert);

        // Assert
        _mockAlertRepo.Verify(
            x => x.UpdateAsync(alert.Id, alert, It.IsAny<CancellationToken>()),
            Times.Once
        );
    }

    [Fact]
    public async Task LoadCustomerActivity_ReturnsCustomer()
    {
        // Arrange
        var customerId = "customer-1";
        var customer = new Customer
        {
            Id = customerId,
            Name = "Test Customer",
            Active = true
        };

        _mockCustomerRepo
            .Setup(x => x.GetAsync(customerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(customer);

        // Act
        var result = await _activities.LoadCustomerActivity(customerId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(customerId, result.Id);
        _mockCustomerRepo.Verify(
            x => x.GetAsync(customerId, It.IsAny<CancellationToken>()),
            Times.Once
        );
    }

    [Fact]
    public async Task HealthCheckActivity_WithValidUrl_CallsHealthCheckService()
    {
        // Arrange
        var alert = new Alert
        {
            Id = "alert-1",
            CustomerId = "customer-1",
            ResourceId = "/subscriptions/test",
            ResourceType = "Microsoft.Web/sites",
            AlertType = "ServiceHealthAlert",
            HealthCheckUrl = "https://example.com/health"
        };

        _mockHealthCheckService
            .Setup(x => x.CheckAsync(It.IsAny<HealthCheckConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _activities.HealthCheckActivity(alert);

        // Assert
        Assert.True(result);
        _mockHealthCheckService.Verify(
            x => x.CheckAsync(It.IsAny<HealthCheckConfig>(), It.IsAny<CancellationToken>()),
            Times.Once
        );
    }

    [Fact]
    public async Task HealthCheckActivity_WithoutUrl_ReturnsFalse()
    {
        // Arrange
        var alert = new Alert
        {
            Id = "alert-1",
            CustomerId = "customer-1",
            ResourceId = "/subscriptions/test",
            ResourceType = "Microsoft.Web/sites",
            AlertType = "ServiceHealthAlert",
            HealthCheckUrl = null // No URL
        };

        // Act
        var result = await _activities.HealthCheckActivity(alert);

        // Assert
        Assert.False(result);
        _mockHealthCheckService.Verify(
            x => x.CheckAsync(It.IsAny<HealthCheckConfig>(), It.IsAny<CancellationToken>()),
            Times.Never
        );
    }

    [Fact]
    public async Task RemediationActivity_WithoutEndpoint_ReturnsFalse()
    {
        // Arrange
        var alert = new Alert
        {
            Id = "alert-1",
            CustomerId = "customer-1",
            ResourceId = "/subscriptions/test",
            ResourceType = "Microsoft.Web/sites",
            AlertType = "ServiceHealthAlert"
        };

        var customer = new Customer
        {
            Id = "customer-1",
            Name = "Test Customer",
            Config = new CustomerConfig
            {
                TenantId = "tenant-1",
                Helios365Endpoint = null // No endpoint
            }
        };

        // Act
        var result = await _activities.RemediationActivity((alert, customer));

        // Assert
        Assert.False(result);
    }
}
