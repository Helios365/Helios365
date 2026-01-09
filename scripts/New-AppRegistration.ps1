param(
    [string]$AppName = "HeliosAppRegistration",
    [Parameter(Mandatory = $true)]
    [string]$Hostname,
    [ValidateSet("AzureADMyOrg", "AzureADMultipleOrgs", "AzureADandPersonalMicrosoftAccount")]
    [string]$SignInAudience = "AzureADMyOrg",
    [Guid]$AdminGroupId,
    [Guid]$OperatorGroupId,
    [Guid]$ReaderGroupId
)

# Build redirect URIs for both localhost and the provided hostname
$RedirectUris = @(
    "https://localhost:7098/signin-oidc",
    "https://$Hostname/signin-oidc"
)

Connect-MgGraph -Scopes @("Application.ReadWrite.All", "AppRoleAssignment.ReadWrite.All")

$appRoles = @(
    @{
        AllowedMemberTypes = @("User")
        Description        = "Full control of Helios365"
        DisplayName        = "Helios Admin"
        Id                 = [Guid]::NewGuid()
        IsEnabled          = $true
        Value              = "Helios.Admin"
    },
    @{
        AllowedMemberTypes = @("User")
        Description        = "Operate alerts/actions in Helios365"
        DisplayName        = "Helios Operator"
        Id                 = [Guid]::NewGuid()
        IsEnabled          = $true
        Value              = "Helios.Operator"
    },
    @{
        AllowedMemberTypes = @("User")
        Description        = "Read-only access to Helios365"
        DisplayName        = "Helios Reader"
        Id                 = [Guid]::NewGuid()
        IsEnabled          = $true
        Value              = "Helios.Reader"
    }
)

$App = New-MgApplication `
  -DisplayName $AppName `
  -SignInAudience $SignInAudience `
  -AppRoles $appRoles `
  -Web @{
      redirectUris = $RedirectUris
      logoutUrl    = "https://$Hostname/signout-oidc"
    }

$SP  = New-MgServicePrincipal -AppId $App.AppId

$Passwd = Add-MgApplicationPassword -ApplicationId $App.Id -PasswordCredential @{
  displayName = "web-secret"
  endDateTime = (Get-Date).AddYears(1)
}
$ClientSecret = $Passwd.SecretText

[PSCustomObject]$appRoleLookup = @{
    Admin    = $appRoles | Where-Object { $_.Value -eq "Helios.Admin" }
    Operator = $appRoles | Where-Object { $_.Value -eq "Helios.Operator" }
    Reader   = $appRoles | Where-Object { $_.Value -eq "Helios.Reader" }
}

$adminGroup  = $AdminGroupId.ToString()
$operatorGroup = $OperatorGroupId.ToString()
$readerGroup = $ReaderGroupId.ToString()

New-MgGroupAppRoleAssignment -GroupId $adminGroup -PrincipalId $adminGroup -ResourceId $SP.Id -AppRoleId $appRoleLookup.Admin.Id | Out-Null
New-MgGroupAppRoleAssignment -GroupId $operatorGroup -PrincipalId $operatorGroup -ResourceId $SP.Id -AppRoleId $appRoleLookup.Operator.Id | Out-Null
New-MgGroupAppRoleAssignment -GroupId $readerGroup -PrincipalId $readerGroup -ResourceId $SP.Id -AppRoleId $appRoleLookup.Reader.Id | Out-Null

[PSCustomObject]@{
  AppDisplayName     = $AppName
  TenantId           = (Get-MgContext).TenantId
  ClientId           = $App.AppId
  ObjectId           = $App.Id
  ServicePrincipalId = $SP.Id
  ClientSecret       = $ClientSecret
  RedirectUris       = $RedirectUris
  LogoutUrl          = "https://$Hostname/signout-oidc"
  AppRoles           = $appRoles | Select-Object DisplayName, Value, Id
  GroupAssignments   = @(
    [PSCustomObject]@{ Role = "Helios.Admin";    GroupId = $adminGroup }
    [PSCustomObject]@{ Role = "Helios.Operator"; GroupId = $operatorGroup }
    [PSCustomObject]@{ Role = "Helios.Reader";   GroupId = $readerGroup }
  )
}
