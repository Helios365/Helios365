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
    private readonly Mock<IResourceRepository> _mockResourceRepo;
    private readonly Mock<ILogger<IndexModel>> _mockLogger;
    private readonly IndexModel _pageModel;

    public IndexModelTests()
    {
        _mockAlertRepo = new Mock<IAlertRepository>();
        _mockCustomerRepo = new Mock<ICustomerRepository>();
        _mockResourceRepo = new Mock<IResourceRepository>();
        _mockLogger = new Mock<ILogger<IndexModel>>();

        _pageModel = new IndexModel(
            _mockAlertRepo.Object,
            _mockCustomerRepo.Object,
            _mockResourceRepo.Object,
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
            .Setup(x => x.ListAsync(20, 0, It.IsAny<CancellationToken>()))
            .ReturnsAsync(alerts);

        _mockAlertRepo
            .Setup(x => x.ListAsync(1000, 0, It.IsAny<CancellationToken>()))
            .ReturnsAsync(alerts);

        _mockCustomerRepo
            .Setup(x => x.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Customer { Id = "customer-1", Name = "Test Customer" });

        _mockCustomerRepo
            .Setup(x => x.ListActiveAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Customer>());

        _mockResourceRepo
            .Setup(x => x.ListAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Resource>());

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
        var alerts = new List<Alert>
        {
            new Alert { Id = "1", CustomerId = "c1", ResourceId = "r1", ResourceType = "t", AlertType = "t", Status = AlertStatus.Received },
            new Alert { Id = "2", CustomerId = "c1", ResourceId = "r1", ResourceType = "t", AlertType = "t", Status = AlertStatus.Checking },
            new Alert { Id = "3", CustomerId = "c1", ResourceId = "r1", ResourceType = "t", AlertType = "t", Status = AlertStatus.Resolved }
        };

        _mockAlertRepo
            .Setup(x => x.ListAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(alerts);

        _mockCustomerRepo
            .Setup(x => x.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Customer { Id = "customer-1", Name = "Test" });

        _mockCustomerRepo
            .Setup(x => x.ListActiveAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Customer>());

        _mockResourceRepo
            .Setup(x => x.ListAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Resource>());

        // Act
        await _pageModel.OnGetAsync();

        // Assert
        Assert.Equal(2, _pageModel.ActiveAlertsCount); // Received and Checking are active
    }

    [Fact]
    public async Task OnGetAsync_LoadsCustomers()
    {
        // Arrange
        var alerts = new List<Alert>
        {
            new Alert { Id = "1", CustomerId = "c1", ResourceId = "r", ResourceType = "t", AlertType = "t" }
        };

        var customer = new Customer { Id = "c1", Name = "Test Customer" };

        _mockAlertRepo
            .Setup(x => x.ListAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(alerts);

        _mockCustomerRepo
            .Setup(x => x.GetAsync("c1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(customer);

        _mockCustomerRepo
            .Setup(x => x.ListActiveAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Customer> { customer });

        _mockResourceRepo
            .Setup(x => x.ListAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Resource>());

        // Act
        await _pageModel.OnGetAsync();

        // Assert
        Assert.True(_pageModel.Customers.ContainsKey("c1"));
        Assert.Equal("Test Customer", _pageModel.Customers["c1"].Name);
    }

    [Fact]
    public async Task OnGetAsync_HandlesErrors()
    {
        // Arrange
        _mockAlertRepo
            .Setup(x => x.ListAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Database error"));

        // Act
        await _pageModel.OnGetAsync();

        // Assert - Should not throw, but handle gracefully
        Assert.Empty(_pageModel.RecentAlerts);
        Assert.Equal(0, _pageModel.ActiveAlertsCount);
    }
}
