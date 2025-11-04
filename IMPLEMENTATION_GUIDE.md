# Helios365 Implementation Guide

This solution provides a **working foundation** with the new architecture. Here's what's done and what needs to be implemented.

## ✅ What's Implemented

### Models (100% Complete)
- ✅ Customer (with ApiKey, NotificationEmails)
- ✅ ServicePrincipal (with Key Vault reference, cloud support)
- ✅ Resource (links resources to SPs)
- ✅ Actions (HealthCheckAction, RestartAction, ScaleAction)
- ✅ Alert (with escalation tracking)

### Repository Interfaces (100% Complete)
- ✅ IRepository<T> (base)
- ✅ ICustomerRepository (with GetByApiKeyAsync)
- ✅ IResourceRepository (with GetByResourceIdAsync)
- ✅ IServicePrincipalRepository
- ✅ IActionRepository
- ✅ IAlertRepository

### Repository Implementations (40% Complete)
- ✅ CustomerRepository (DONE - with API key lookup)
- ✅ ResourceRepository (DONE - with customer/resource queries)
- ⏳ AlertRepository (TODO)
- ⏳ ServicePrincipalRepository (TODO)
- ⏳ ActionRepository (TODO - needs polymorphism handling)

### Service Interfaces (100% Complete)
- ✅ IActionExecutor
- ✅ IEmailService

### Service Implementations (0% Complete)
- ⏳ ActionExecutor (TODO - Key Vault + Azure RM)
- ⏳ EmailService (TODO - Azure Communication Services)

### Processor (10% Complete)
- ✅ Project setup with all packages
- ✅ Program.cs with DI configured
- ✅ host.json with Durable Functions
- ⏳ AlertIngestionTrigger (TODO)
- ⏳ AlertOrchestrator (TODO)
- ⏳ Activity functions (TODO)

### Platform (5% Complete)
- ✅ Project setup
- ⏳ Authentication (TODO)
- ⏳ All pages (TODO)

## 🎯 Implementation Priority

### Phase 1: Complete Core (Estimated: 4-6 hours)
1. **AlertRepository.cs**
   - Pattern: Copy CustomerRepository, adapt for alerts
   - Key methods: ListByStatusAsync, ListByCustomerAsync
   - Partition key: customerId

2. **ServicePrincipalRepository.cs**
   - Pattern: Copy CustomerRepository
   - Key method: ListByCustomerAsync
   - Partition key: customerId

3. **ActionRepository.cs**
   - Pattern: Copy CustomerRepository
   - Challenge: Handle polymorphism (HealthCheck/Restart/Scale)
   - Methods: ListByResourceAsync, ListDefaultActionsAsync, ListAutomaticActionsAsync
   - Partition key: customerId
   - Tip: Use discriminator field or separate containers

4. **ActionExecutor.cs**
   - Dependencies: SecretClient (Key Vault), HttpClient
   - Steps:
     a) Load SP secret from Key Vault
     b) Create ClientSecretCredential
     c) Create ArmClient
     d) Switch on action type
     e) Execute action

5. **EmailService.cs**
   - Dependency: EmailClient (Azure Communication Services)
   - Build HTML template
   - Send to customer.NotificationEmails

### Phase 2: Complete Processor (Estimated: 6-8 hours)
1. **AlertIngestionTrigger.cs**
   - HTTP POST /api/alerts?apiKey={key}
   - Validate customer
   - Look up resource
   - Create alert
   - Start orchestration or escalate

2. **AlertOrchestrator.cs**
   - Load resource, SP, actions
   - Execute actions in order
   - Handle waits (CreateTimer)
   - Escalate if needed

3. **Activity Functions**
   - ExecuteActionActivity
   - UpdateAlertActivity
   - SendEscalationEmailActivity

### Phase 3: Complete Platform (Estimated: 8-10 hours)
1. **Authentication**
   - Azure AD setup
   - Add to Program.cs

2. **Pages**
   - Dashboard (index)
   - Customers (CRUD)
   - Service Principals (CRUD + Key Vault)
   - Resources (CRUD)
   - Actions (CRUD + ordering)
   - Alerts (list + details)

## 📝 Code Templates

### Repository Template
```csharp
public class XRepository : IXRepository
{
    private readonly Container _container;
    private readonly ILogger<XRepository> _logger;

    public XRepository(CosmosClient cosmosClient, string databaseName, string containerName, ILogger<XRepository> logger)
    {
        _container = cosmosClient.GetContainer(databaseName, containerName);
        _logger = logger;
    }

    // Copy CRUD methods from CustomerRepository
    // Adapt queries for your specific needs
    // Use customerId as partition key for efficient queries
}
```

### Action Executor Template
```csharp
public class ActionExecutor : IActionExecutor
{
    private readonly SecretClient _secretClient;
    private readonly HttpClient _httpClient;
    private readonly ILogger<ActionExecutor> _logger;

    public async Task<bool> ExecuteAsync(ActionBase action, Resource resource, ServicePrincipal sp, CancellationToken ct)
    {
        return action switch
        {
            HealthCheckAction hca => await ExecuteHealthCheckAsync(hca, ct),
            RestartAction ra => await ExecuteRestartAsync(ra, resource, sp, ct),
            ScaleAction sa => await ExecuteScaleAsync(sa, resource, sp, ct),
            _ => throw new ActionExecutionException($"Unknown action type: {action.Type}")
        };
    }

    private async Task<bool> ExecuteHealthCheckAsync(HealthCheckAction action, CancellationToken ct)
    {
        var request = new HttpRequestMessage(
            action.Method == HttpMethod.GET ? System.Net.Http.HttpMethod.Get : System.Net.Http.HttpMethod.Post,
            action.Url
        );

        foreach (var header in action.Headers)
        {
            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(action.TimeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, ct);

        try
        {
            var response = await _httpClient.SendAsync(request, linkedCts.Token);
            return (int)response.StatusCode == action.ExpectedStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> ExecuteRestartAsync(RestartAction action, Resource resource, ServicePrincipal sp, CancellationToken ct)
    {
        // 1. Get secret from Key Vault
        var secret = await _secretClient.GetSecretAsync($"sp-{sp.Id}", ct);

        // 2. Create credential
        var credential = new ClientSecretCredential(sp.TenantId, sp.ClientId, secret.Value.Value);

        // 3. Create ARM client
        var armClient = new ArmClient(credential);

        // 4. Get resource
        var resourceIdentifier = new ResourceIdentifier(resource.ResourceId);

        // 5. Restart based on resource type
        if (resource.ResourceType.Contains("Microsoft.Web/sites"))
        {
            var site = armClient.GetWebSiteResource(resourceIdentifier);
            await Task.Delay(TimeSpan.FromSeconds(action.WaitBeforeSeconds), ct);
            await site.RestartAsync(WaitUntil.Started, ct);
            await Task.Delay(TimeSpan.FromSeconds(action.WaitAfterSeconds), ct);
            return true;
        }

        // Add more resource types...
        return false;
    }
}
```

## 🧪 Testing Strategy

1. **Unit Tests**
   - Test repositories with mock CosmosClient
   - Test models (Alert.MarkStatus, etc.)
   - Test action execution logic

2. **Integration Tests**
   - Test against Cosmos DB Emulator
   - Test full alert workflow

3. **Manual Testing**
   - Use Postman to POST alerts
   - Verify in Platform dashboard
   - Check emails sent

## 🚀 Quick Start

```bash
# 1. Build
dotnet restore
dotnet build

# 2. Configure Cosmos DB
# Create database "helios365"
# Create containers: customers, resources, servicePrincipals, actions, alerts

# 3. Add test data
# Create a customer with API key
# Create a service principal
# Create a resource
# Create actions for the resource

# 4. Run Processor
cd src/Helios365.Processor
# Copy local.settings.json.example to local.settings.json
# Fill in connection strings
func start

# 5. Send test alert
curl -X POST "http://localhost:7071/api/alerts?apiKey=YOUR_API_KEY" \
  -H "Content-Type: application/json" \
  -d '{"resourceId":"/subscriptions/.../sites/myapp","alertType":"ServiceHealthAlert","severity":"High"}'
```

## 📚 Resources

- [Cosmos DB .NET SDK](https://learn.microsoft.com/azure/cosmos-db/nosql/sdk-dotnet-v3)
- [Azure Functions Durable](https://learn.microsoft.com/azure/azure-functions/durable/)
- [Azure Resource Manager SDK](https://learn.microsoft.com/dotnet/api/overview/azure/resourcemanager-readme)
- [Azure Communication Services](https://learn.microsoft.com/azure/communication-services/quickstarts/email/send-email)
- [Azure AD Authentication](https://learn.microsoft.com/aspnet/core/security/authentication/azure-active-directory/)

## 💡 Tips

1. **Start Small**: Implement AlertRepository first, it's the simplest
2. **Test Early**: Write tests as you implement
3. **Use Cosmos Emulator**: For local development
4. **Key Vault Local**: Use user secrets or environment variables locally
5. **Email Testing**: Use a test email address initially

Good luck! 🌞
