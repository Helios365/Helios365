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
    private readonly Mock<IResourceRepository> _mockResourceRepo;
    private readonly Mock<IServicePrincipalRepository> _mockSpRepo;
    private readonly Mock<IActionRepository> _mockActionRepo;
    private readonly Mock<IActionExecutor> _mockActionExecutor;
    private readonly Mock<IEmailService> _mockEmailService;
    private readonly Mock<ILogger<AlertActivities>> _mockLogger;
    private readonly AlertActivities _activities;

    public AlertActivitiesTests()
    {
        _mockAlertRepo = new Mock<IAlertRepository>();
        _mockCustomerRepo = new Mock<ICustomerRepository>();
        _mockResourceRepo = new Mock<IResourceRepository>();
        _mockSpRepo = new Mock<IServicePrincipalRepository>();
        _mockActionRepo = new Mock<IActionRepository>();
        _mockActionExecutor = new Mock<IActionExecutor>();
        _mockEmailService = new Mock<IEmailService>();
        _mockLogger = new Mock<ILogger<AlertActivities>>();

        _activities = new AlertActivities(
            _mockAlertRepo.Object,
            _mockCustomerRepo.Object,
            _mockResourceRepo.Object,
            _mockSpRepo.Object,
            _mockActionRepo.Object,
            _mockActionExecutor.Object,
            _mockEmailService.Object,
            _mockLogger.Object
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
    public async Task LoadResourceActivity_ReturnsResource()
    {
        // Arrange
        var customerId = "customer-1";
        var resourceId = "/subscriptions/test/sites/app";
        var resource = new Resource
        {
            Id = "resource-1",
            CustomerId = customerId,
            ResourceId = resourceId,
            Name = "Test App"
        };

        _mockResourceRepo
            .Setup(x => x.GetByResourceIdAsync(customerId, resourceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(resource);

        // Act
        var result = await _activities.LoadResourceActivity((customerId, resourceId));

        // Assert
        Assert.NotNull(result);
        Assert.Equal(resourceId, result.ResourceId);
    }

    [Fact]
    public async Task ExecuteActionActivity_CallsActionExecutor()
    {
        // Arrange
        var action = new HealthCheckAction
        {
            Id = "action-1",
            Url = "https://example.com/health"
        };
        var resource = new Resource
        {
            Id = "resource-1",
            ResourceId = "/subscriptions/test"
        };
        var sp = new ServicePrincipal
        {
            Id = "sp-1",
            TenantId = "tenant-1",
            ClientId = "client-1"
        };

        _mockActionExecutor
            .Setup(x => x.ExecuteAsync(action, resource, sp, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _activities.ExecuteActionActivity((action, resource, sp));

        // Assert
        Assert.True(result);
        _mockActionExecutor.Verify(
            x => x.ExecuteAsync(action, resource, sp, It.IsAny<CancellationToken>()),
            Times.Once
        );
    }
}
