using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.UI;
using Helios365.Web.Extensions;
using Helios365.Core.Services;
using Helios365.Web.Services;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;


using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

// Configure Azure Key Vault
var keyVaultUri = builder.Configuration["KeyVault:VaultUri"];
if (!string.IsNullOrEmpty(keyVaultUri))
{
    builder.Configuration.AddAzureKeyVault(
        new Uri(keyVaultUri),
        new Azure.Identity.DefaultAzureCredential());
}

builder.Services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApp(options =>
    {
        builder.Configuration.Bind("AzureAd", options);
        options.ResponseType = "code"; // Use authorization code flow instead of implicit
        options.TokenValidationParameters.RoleClaimType = ClaimTypes.Role;
        options.Events.OnTokenValidated = async context =>
        {
            var oid = context.Principal?.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value;
            if (string.IsNullOrWhiteSpace(oid))
            {
                return;
            }

            var directoryService = context.HttpContext.RequestServices.GetService<IDirectoryService>();
            var syncService = context.HttpContext.RequestServices.GetService<IDirectorySyncService>();
            var logger = context.HttpContext.RequestServices.GetService<ILoggerFactory>()?.CreateLogger("AuthSync");

            if (directoryService == null || syncService == null)
            {
                logger?.LogWarning("Directory services not available during token validation for user {UserId}", oid);
                return;
            }

            try
            {
                var directoryUser = await directoryService.GetUserAsync(oid, context.HttpContext.RequestAborted).ConfigureAwait(false);
                if (directoryUser != null)
                {
                    await syncService.EnsureProfileAsync(directoryUser, context.HttpContext.RequestAborted).ConfigureAwait(false);
                }
                else
                {
                    logger?.LogWarning("Directory user {UserId} not found during sign-in", oid);
                }
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Failed to sync directory profile for user {UserId}", oid);
            }
        };
    });
builder.Services.AddControllersWithViews()
    .AddMicrosoftIdentityUI();

builder.Services.AddAuthorization(options =>
{
    // Require an app role for all requests and expose specific policies
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .RequireRole("Helios.Admin", "Helios.Operator", "Helios.Reader")
        .Build();
    options.AddPolicy("AdminOnly", policy => policy.RequireRole("Helios.Admin"));
    options.AddPolicy("OperatorOrAbove", policy => policy.RequireRole("Helios.Admin", "Helios.Operator"));
    options.AddPolicy("ReaderOrAbove", policy => policy.RequireRole("Helios.Admin", "Helios.Operator", "Helios.Reader"));
});

builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor(options =>
    {
        options.DetailedErrors = builder.Environment.IsDevelopment();
        options.DisconnectedCircuitRetentionPeriod = TimeSpan.FromMinutes(10);
    })
    .AddHubOptions(options =>
    {
        options.ClientTimeoutInterval = TimeSpan.FromMinutes(2);
        options.KeepAliveInterval = TimeSpan.FromSeconds(15);
    })
    .AddMicrosoftIdentityConsentHandler();

// Add Cosmos DB and Helios365 Core services
builder.Services.AddCosmosDb(builder.Configuration, builder.Environment.IsDevelopment());
builder.Services.AddRepositories(builder.Configuration);
builder.Services.AddMudServices();
builder.Services.AddScoped<IProfileService, ProfileService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();
