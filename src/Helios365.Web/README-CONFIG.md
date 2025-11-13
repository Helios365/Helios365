# Helios365.Web Configuration Setup

## Configuration Strategy

This project uses a simple 2-file approach:

1. **`appsettings.json`** - Base settings (committed to git)
2. **`appsettings.Development.json`** - Local development secrets (NOT committed)

## Local Development Setup

1. **Edit your local secrets**:
   ```bash
   # Edit src/Helios365.Web/appsettings.Development.json
   # Replace the REPLACE-WITH-* placeholders with your actual values
   ```

2. **Required values**:
   - `AzureAd:Domain` - Your Azure AD domain (e.g., "yourcompany.onmicrosoft.com")
   - `AzureAd:TenantId` - Your Azure AD tenant ID
   - `AzureAd:ClientId` - Your app registration client ID  
   - `CosmosDb:ConnectionString` - Your Cosmos DB connection string
   - `KeyVault:VaultUri` - Your Key Vault URI (optional for local dev)

3. **Run the application**:
   ```bash
   dotnet run
   ```

## Production Deployment

For Azure App Service, set these as Application Settings:

- `AzureAd__Domain`
- `AzureAd__TenantId`  
- `AzureAd__ClientId`
- `CosmosDb__ConnectionString`
- `KeyVault__VaultUri`

## Security

- ✅ `appsettings.Development.json` is gitignored
- ✅ Never commit secrets to source control
- ✅ Use Key Vault references in production