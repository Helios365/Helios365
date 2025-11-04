using Helios365.Core.Models;
using Xunit;

namespace Helios365.Core.Tests.Models;

public class AlertTests
{
    [Fact]
    public void Alert_CreatesWithReceivedStatus()
    {
        // Arrange & Act
        var alert = new Alert
        {
            Id = "test-1",
            CustomerId = "customer-1",
            ResourceId = "/subscriptions/test/resourceGroups/rg/providers/Microsoft.Web/sites/app",
            ResourceType = "Microsoft.Web/sites",
            AlertType = "ServiceHealthAlert"
        };

        // Assert
        Assert.Equal("test-1", alert.Id);
        Assert.Equal(AlertStatus.Received, alert.Status);
        Assert.True(alert.IsActive());
    }

    [Fact]
    public void MarkStatus_UpdatesStatusAndTimestamp()
    {
        // Arrange
        var alert = new Alert 
        { 
            Id = "test-1", 
            CustomerId = "cust-1", 
            ResourceId = "res-1", 
            ResourceType = "type", 
            AlertType = "type" 
        };
        var originalTime = alert.UpdatedAt;

        // Act
        Thread.Sleep(10); // Ensure time difference
        alert.MarkStatus(AlertStatus.Checking);

        // Assert
        Assert.Equal(AlertStatus.Checking, alert.Status);
        Assert.True(alert.UpdatedAt > originalTime);
    }

    [Fact]
    public void MarkStatus_Resolved_SetsResolvedAt()
    {
        // Arrange
        var alert = new Alert 
        { 
            Id = "test-1", 
            CustomerId = "cust-1", 
            ResourceId = "res-1", 
            ResourceType = "type", 
            AlertType = "type" 
        };
        Assert.Null(alert.ResolvedAt);

        // Act
        alert.MarkStatus(AlertStatus.Resolved);

        // Assert
        Assert.Equal(AlertStatus.Resolved, alert.Status);
        Assert.NotNull(alert.ResolvedAt);
        Assert.False(alert.IsActive());
    }

    [Fact]
    public void MarkStatus_Escalated_SetsEscalatedAt()
    {
        // Arrange
        var alert = new Alert 
        { 
            Id = "test-1", 
            CustomerId = "cust-1", 
            ResourceId = "res-1", 
            ResourceType = "type", 
            AlertType = "type" 
        };
        Assert.Null(alert.EscalatedAt);

        // Act
        alert.MarkStatus(AlertStatus.Escalated);

        // Assert
        Assert.Equal(AlertStatus.Escalated, alert.Status);
        Assert.NotNull(alert.EscalatedAt);
        Assert.True(alert.IsActive()); // Escalated is still active
    }

    [Fact]
    public void IsActive_ReturnsFalse_WhenHealthy()
    {
        // Arrange
        var alert = new Alert 
        { 
            Id = "test-1", 
            CustomerId = "cust-1", 
            ResourceId = "res-1", 
            ResourceType = "type", 
            AlertType = "type" 
        };
        alert.MarkStatus(AlertStatus.Healthy);

        // Act & Assert
        Assert.False(alert.IsActive());
    }

    [Theory]
    [InlineData(AlertStatus.Received)]
    [InlineData(AlertStatus.Routing)]
    [InlineData(AlertStatus.Checking)]
    [InlineData(AlertStatus.Remediating)]
    [InlineData(AlertStatus.Rechecking)]
    [InlineData(AlertStatus.Escalated)]
    public void IsActive_ReturnsTrue_ForActiveStatuses(AlertStatus status)
    {
        // Arrange
        var alert = new Alert 
        { 
            Id = "test-1", 
            CustomerId = "cust-1", 
            ResourceId = "res-1", 
            ResourceType = "type", 
            AlertType = "type" 
        };
        alert.MarkStatus(status);

        // Act & Assert
        Assert.True(alert.IsActive());
    }
}
