param(
    [string]$AppName = "HeliosAppRegistration",
    [string[]]$RedirectUris = @("https://localhost:7098/signin-oidc"),
    [string]$LogoutUrl = "https://localhost:7098/signout-oidc",
    [ValidateSet("AzureADMyOrg", "AzureADMultipleOrgs", "AzureADandPersonalMicrosoftAccount")]
    [string]$SignInAudience = "AzureADMyOrg"
)

Connect-MgGraph -Scopes "Application.ReadWrite.All"

$App = New-MgApplication `
  -DisplayName $AppName `
  -SignInAudience $SignInAudience `
  -Web @{
      redirectUris = $RedirectUris
      logoutUrl    = $LogoutUrl
    }

$SP  = New-MgServicePrincipal -AppId $App.AppId

$Passwd = Add-MgApplicationPassword -ApplicationId $App.Id -PasswordCredential @{
  displayName = "web-secret"
  endDateTime = (Get-Date).AddYears(1)
}
$ClientSecret = $Passwd.SecretText

[PSCustomObject]@{
  AppDisplayName = $AppName
  TenantId       = (Get-MgContext).TenantId
  ClientId       = $App.AppId
  ObjectId       = $App.Id
  ServicePrincipalId = $SP.Id
  ClientSecret   = $ClientSecret
}