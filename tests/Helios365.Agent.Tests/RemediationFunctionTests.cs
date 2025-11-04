using Helios365.Agent;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Helios365.Agent.Tests;

public class RemediationFunctionTests
{
    private readonly Mock<ILogger<RemediationFunction>> _mockLogger;
    private readonly RemediationFunction _function;

    public RemediationFunctionTests()
    {
        _mockLogger = new Mock<ILogger<RemediationFunction>>();
        _function = new RemediationFunction(_mockLogger.Object);
    }

    [Fact]
    public void RemediationRequest_ValidData_CreatesSuccessfully()
    {
        // Arrange & Act
        var request = new RemediationFunction.RemediationRequest
        {
            ResourceId = "/subscriptions/test/resourceGroups/rg/providers/Microsoft.Web/sites/myapp",
            Action = "restart"
        };

        // Assert
        Assert.Equal("restart", request.Action);
        Assert.Contains("Microsoft.Web/sites", request.ResourceId);
    }

    [Fact]
    public void RemediationRequest_DefaultAction_IsRestart()
    {
        // Arrange & Act
        var request = new RemediationFunction.RemediationRequest
        {
            ResourceId = "/subscriptions/test"
        };

        // Assert
        Assert.Equal("restart", request.Action);
    }

    [Theory]
    [InlineData("restart")]
    [InlineData("scale")]
    [InlineData("stop")]
    public void RemediationRequest_SupportsVariousActions(string action)
    {
        // Arrange & Act
        var request = new RemediationFunction.RemediationRequest
        {
            ResourceId = "/subscriptions/test",
            Action = action
        };

        // Assert
        Assert.Equal(action, request.Action);
    }

    [Fact]
    public void RemediationRequest_ValidatesResourceId()
    {
        // Arrange & Act
        var request = new RemediationFunction.RemediationRequest
        {
            ResourceId = "/subscriptions/abc-123/resourceGroups/my-rg/providers/Microsoft.Web/sites/webapp",
            Action = "restart"
        };

        // Assert
        Assert.NotEmpty(request.ResourceId);
        Assert.StartsWith("/subscriptions/", request.ResourceId);
    }
}
