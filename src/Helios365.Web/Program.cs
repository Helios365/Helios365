using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.UI;
using Helios365.Web.Extensions;
using Azure.Identity;
using Azure.Extensions.AspNetCore.Configuration.Secrets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication;
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
builder.Services.AddServerSideBlazor()
    .AddMicrosoftIdentityConsentHandler();

// Add Cosmos DB and Helios365 Core services
builder.Services.AddCosmosDb(builder.Configuration, builder.Environment.IsDevelopment());
builder.Services.AddRepositories(builder.Configuration);
builder.Services.AddMudServices();

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
