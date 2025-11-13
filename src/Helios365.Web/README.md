# Helios365.Web - Blazor Server Application

## Overview

Helios365.Web is a Blazor Server application that provides a modern, interactive web interface for the Helios365 monitoring and incident response platform. It offers the same functionality as the Platform project but with real-time updates and a rich interactive experience.

## Features

### 🔐 **Azure AD (Entra ID) Authentication**
- Single Sign-On (SSO) with Entra ID
- Role-based access control
- Secure logout and session management

### 📊 **Interactive Dashboard**
- Real-time metrics and statistics
- Active alerts monitoring
- System health overview
- Today's activity summary

### 🎨 **Modern UI/UX**
- Responsive design (mobile and desktop)
- Bootstrap 5.3 framework
- Bootstrap Icons
- Gradient header with navigation
- Professional color scheme matching Platform project

### ⚡ **Real-time Updates**
- SignalR integration for live data
- Server-side rendering for performance
- Interactive components

## Project Structure

```
src/Helios365.Web/
├── Pages/
│   ├── Index.razor              # Dashboard page with stats and alerts
│   ├── Counter.razor            # Sample counter (can be removed)
│   ├── FetchData.razor          # Sample data fetching (can be removed)
│   ├── Error.cshtml             # Error page
│   └── _Host.cshtml             # Main HTML host page
├── Shared/
│   ├── MainLayout.razor         # Main layout with header, navigation, footer
│   ├── NavMenu.razor            # Navigation menu (legacy, replaced by MainLayout)
│   └── LoginDisplay.razor       # Authentication UI component
├── Data/
│   └── WeatherForecastService.cs # Sample service (can be removed)
├── wwwroot/
│   └── css/
│       └── site.css             # Custom styles matching Platform design
├── Program.cs                   # Application startup and configuration
├── appsettings.json             # Configuration settings
└── Helios365.Web.csproj         # Project file
```

## Configuration

### Entra ID Setup

Update `appsettings.json` with your Azure AD configuration:

```json
{
  "AzureAd": {
    "Instance": "https://login.microsoftonline.com/",
    "Domain": "your-tenant.onmicrosoft.com",
    "TenantId": "your-tenant-id",
    "ClientId": "your-client-id",
    "CallbackPath": "/signin-oidc"
  }
}
```

### Local Development

For local development, use user secrets:

```bash
cd src/Helios365.Web
dotnet user-secrets set "AzureAd:TenantId" "your-tenant-id"
dotnet user-secrets set "AzureAd:ClientId" "your-client-id"
dotnet user-secrets set "AzureAd:Domain" "your-domain.onmicrosoft.com"
```

## Dependencies

### NuGet Packages
- **Microsoft.AspNetCore.Authentication.OpenIdConnect** (8.0.11): Entra ID authentication
- **Microsoft.Identity.Web** (3.3.1): Microsoft Identity platform integration
- **Microsoft.Identity.Web.UI** (3.3.1): Pre-built authentication UI

### CDN Dependencies
- **Bootstrap** 5.3.2: UI framework
- **Bootstrap Icons** 1.11.1: Icon library

### Project References
- **Helios365.Core**: Shared domain models, repositories, and services

## Running the Application

### Prerequisites
- .NET 8 SDK
- Azure AD tenant with app registration

### Development
```bash
# Navigate to project directory
cd src/Helios365.Web

# Configure user secrets (first time only)
dotnet user-secrets set "AzureAd:TenantId" "your-tenant-id"
dotnet user-secrets set "AzureAd:ClientId" "your-client-id" 
dotnet user-secrets set "AzureAd:Domain" "your-domain.onmicrosoft.com"

# Run the application
dotnet run
```

The application will start on `https://localhost:7XXX` (port varies).

### Production Build
```bash
dotnet publish --configuration Release --output ./publish
```

## Authentication Flow

1. **Unauthenticated users** are redirected to Azure AD login
2. **After successful authentication**, users are redirected back to the application
3. **User information** is available through `ClaimsPrincipal`
4. **Authorization policies** control access to features

## Key Components

### Dashboard (`Pages/Index.razor`)
- Displays real-time system metrics
- Shows recent alerts in a data table
- Implements loading states and error handling
- Uses dependency injection for data access

### MainLayout (`Shared/MainLayout.razor`)
- Responsive header with gradient background
- Desktop and mobile navigation
- User authentication dropdown
- Footer with branding

### LoginDisplay (`Shared/LoginDisplay.razor`)
- Bootstrap dropdown for authenticated users
- Profile and settings links
- Sign in/out functionality

## Styling

The application uses a custom CSS framework based on:
- **CSS Variables** for consistent theming
- **Bootstrap 5** as the base framework
- **Custom gradients** and color schemes
- **Responsive design** principles
- **Professional card layouts** and typography

## Development Notes

### Real-time Features
- The application is configured for SignalR integration
- Components can be updated in real-time as data changes
- State management follows Blazor best practices

### Authentication
- All pages require authentication by default
- The `[AllowAnonymous]` attribute can be used for public pages
- User context is available in all components

### Performance
- Server-side rendering for fast initial loads
- Minimal JavaScript for enhanced interactivity
- Optimized CSS and asset loading

## Future Enhancements

1. **Additional Pages**: Alerts, Customers, Resources management
2. **Real-time Notifications**: Toast notifications for new alerts
3. **Dark Mode**: Theme switching capability
4. **Advanced Charts**: Interactive monitoring dashboards
5. **Export Features**: PDF reports and data export

## Troubleshooting

### Common Issues

1. **Authentication Redirect Loop**
   - Verify Azure AD app registration settings
   - Check callback URL configuration
   - Ensure proper app permissions

2. **Build Warnings**
   - Microsoft.Identity.Web vulnerability warnings are known and being tracked
   - They don't affect functionality in current implementation

3. **CSS Not Loading**
   - Verify Bootstrap CDN links in `_Host.cshtml`
   - Clear browser cache
   - Check network connectivity

### Debugging
- Enable detailed logging in `appsettings.Development.json`
- Use browser developer tools for client-side issues
- Check Azure AD logs for authentication problems