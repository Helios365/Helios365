// TODO: Implement Platform (Web Dashboard)

// 1. Program.cs
//    - Add Azure AD authentication (Microsoft.Identity.Web)
//    - Configure Cosmos DB repositories
//    - Add Razor Pages

// 2. Pages to Create:
//    - Index.cshtml - Dashboard (alerts overview, stats)
//    - Customers/
//      - List.cshtml - List customers
//      - Create.cshtml - Create customer (generate API key)
//      - Edit.cshtml - Edit customer
//      - Details.cshtml - View customer details
//    - ServicePrincipals/
//      - List.cshtml - List SPs for customer
//      - Create.cshtml - Create SP (store secret in Key Vault)
//      - Edit.cshtml - Edit SP
//    - Resources/
//      - List.cshtml - List resources
//      - Create.cshtml - Create resource, link to SP
//      - Edit.cshtml - Edit resource
//      - CreateFromAlert.cshtml - Create resource from unmatched alert
//    - Actions/
//      - List.cshtml - List actions (default or resource-specific)
//      - Create.cshtml - Create action (HealthCheck, Restart, Scale)
//      - Edit.cshtml - Edit action, set order
//      - Reorder.cshtml - Drag-and-drop reorder actions
//    - Alerts/
//      - List.cshtml - List alerts with filters
//      - Details.cshtml - View alert details, timeline
//      - ManualRemediate.cshtml - Trigger manual action

// 3. appsettings.json
//    {
//      "AzureAd": {
//        "Instance": "https://login.microsoftonline.com/",
//        "TenantId": "your-tenant-id",
//        "ClientId": "your-client-id",
//        "CallbackPath": "/signin-oidc"
//      },
//      "CosmosDb": { ... },
//      "KeyVault": {
//        "VaultUri": "https://your-vault.vault.azure.net/"
//      }
//    }

// Example Program.cs structure:
/*
var builder = WebApplication.CreateBuilder(args);

// Azure AD
builder.Services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApp(builder.Configuration.GetSection("AzureAd"));

builder.Services.AddAuthorization();
builder.Services.AddRazorPages().AddMicrosoftIdentityUI();

// Cosmos DB & Repositories (same as Processor)

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.MapRazorPages();
app.Run();
*/

// Example Dashboard Page Model:
/*
public class IndexModel : PageModel
{
    private readonly IAlertRepository _alertRepo;
    private readonly ICustomerRepository _customerRepo;

    public int ActiveAlertsCount { get; set; }
    public int EscalatedAlertsCount { get; set; }
    public List<Alert> RecentAlerts { get; set; }

    public async Task OnGetAsync()
    {
        RecentAlerts = (await _alertRepo.ListAsync(limit: 10)).ToList();
        // ... calculate stats
    }
}
*/
