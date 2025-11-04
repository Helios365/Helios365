using Helios365.Core.Models;
using Helios365.Core.Repositories;
using Helios365.Platform.Pages;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Helios365.Platform.Tests.Pages;

public class IndexModelTests
{
    private readonly Mock<IAlertRepository> _mockAlertRepo;
    private readonly Mock<ICustomerRepository> _mockCustomerRepo;
    private readonly Mock<ILogger<IndexModel>> _mockLogger;
    private readonly IndexModel _pageModel;

    public IndexModelTests()
    {
        _mockAlertRepo = new Mock<IAlertRepository>();
        _mockCustomerRepo = new Mock<ICustomerRepository>();
        _mockLogger = new Mock<ILogger<IndexModel>>();

        _pageModel = new IndexModel(
            _mockAlertRepo.Object,
            _mockCustomerRepo.Object,
            _mockLogger.Object
        );
    }

    [Fact]
    public async Task OnGetAsync_LoadsRecentAlerts()
    {
        // Arrange
        var alerts = new List<Alert>
        {
            new Alert
            {
                Id = "alert-1",
                CustomerId = "customer-1",
                ResourceId = "/subscriptions/test",
                ResourceType = "Microsoft.Web/sites",
                AlertType = "ServiceHealthAlert",
                Status = AlertStatus.Received
            },
            new Alert
            {
                Id = "alert-2",
                CustomerId = "customer-1",
                ResourceId = "/subscriptions/test",
                ResourceType = "Microsoft.Web/sites",
                AlertType = "ServiceHealthAlert",
                Status = AlertStatus.Resolved
            }
        };

        _mockAlertRepo
            .Setup(x => x.ListAsync(10, 0, It.IsAny<CancellationToken>()))
            .ReturnsAsync(alerts);

        _mockAlertRepo
            .Setup(x => x.ListByStatusAsync(It.IsAny<AlertStatus>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Alert>());

        _mockCustomerRepo
            .Setup(x => x.ListActiveAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Customer>());

        // Act
        await _pageModel.OnGetAsync();

        // Assert
        Assert.Equal(2, _pageModel.RecentAlerts.Count);
        Assert.Equal("alert-1", _pageModel.RecentAlerts[0].Id);
    }

    [Fact]
    public async Task OnGetAsync_CountsActiveAlerts()
    {
        // Arrange
        var activeAlerts = new List<Alert>
        {
            new Alert { Id = "1", CustomerId = "c1", ResourceId = "r1", ResourceType = "t", AlertType = "t", Status = AlertStatus.Received },
            new Alert { Id = "2", CustomerId = "c1", ResourceId = "r1", ResourceType = "t", AlertType = "t", Status = AlertStatus.Received },
            new Alert { Id = "3", CustomerId = "c1", ResourceId = "r1", ResourceType = "t", AlertType = "t", Status = AlertStatus.Received }
        };

        _mockAlertRepo
            .Setup(x => x.ListAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Alert>());

        _mockAlertRepo
            .Setup(x => x.ListByStatusAsync(AlertStatus.Received, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(activeAlerts);

        _mockAlertRepo
            .Setup(x => x.ListByStatusAsync(AlertStatus.Escalated, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Alert>());

        _mockCustomerRepo
            .Setup(x => x.ListActiveAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Customer>());

        // Act
        await _pageModel.OnGetAsync();

        // Assert
        Assert.Equal(3, _pageModel.ActiveAlertsCount);
    }

    [Fact]
    public async Task OnGetAsync_CountsCustomers()
    {
        // Arrange
        var customers = new List<Customer>
        {
            new Customer { Id = "c1", Name = "Customer 1", Active = true },
            new Customer { Id = "c2", Name = "Customer 2", Active = true }
        };

        _mockAlertRepo
            .Setup(x => x.ListAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Alert>());

        _mockAlertRepo
            .Setup(x => x.ListByStatusAsync(It.IsAny<AlertStatus>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Alert>());

        _mockCustomerRepo
            .Setup(x => x.ListActiveAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(customers);

        // Act
        await _pageModel.OnGetAsync();

        // Assert
        Assert.Equal(2, _pageModel.CustomersCount);
    }

    [Fact]
    public async Task OnGetAsync_HandlesRepositoryErrors()
    {
        // Arrange
        _mockAlertRepo
            .Setup(x => x.ListAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Database error"));

        // Act
        await _pageModel.OnGetAsync();

        // Assert - Should not throw, but log error
        Assert.Empty(_pageModel.RecentAlerts);
        Assert.Equal(0, _pageModel.ActiveAlertsCount);
    }
}
