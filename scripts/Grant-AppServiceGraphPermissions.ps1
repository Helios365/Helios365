param(
    [Parameter(Mandatory = $true)]
    [string]$ResourceGroupName,
    [Parameter(Mandatory = $true)]
    [string]$AppServiceName,
    [string[]]$Roles = @("GroupMember.Read.All", "User.Read.All")
)

# Ensure Az modules are available for web app lookup and Graph modules for role assignment.
Import-Module Az.Accounts -ErrorAction Stop
Import-Module Az.Websites -ErrorAction Stop

Connect-AzAccount -ErrorAction Stop | Out-Null

$app = Get-AzWebApp -ResourceGroupName $ResourceGroupName -Name $AppServiceName -ErrorAction Stop
if (-not $app.Identity -or -not $app.Identity.PrincipalId) {
    throw "App Service '$AppServiceName' does not have a system-assigned managed identity enabled."
}
$miPrincipalId = $app.Identity.PrincipalId

# Use Graph to assign app roles to the managed identity service principal
Connect-MgGraph -Scopes @("AppRoleAssignment.ReadWrite.All") | Out-Null

$graphSp = Get-MgServicePrincipal -Filter "appId eq '00000003-0000-0000-c000-000000000000'"
if (-not $graphSp) {
    throw "Could not find Microsoft Graph service principal."
}

$appRoleLookup = @{}
foreach ($role in $Roles) {
    $appRole = $graphSp.AppRoles | Where-Object { $_.Value -eq $role -and $_.IsEnabled }
    if (-not $appRole) {
        throw "Role '$role' not found on Microsoft Graph."
    }
    $appRoleLookup[$role] = $appRole
}

$existingAssignments = Get-MgServicePrincipalAppRoleAssignment -ServicePrincipalId $miPrincipalId

$results = foreach ($role in $Roles) {
    $appRole = $appRoleLookup[$role]
    $already = $existingAssignments | Where-Object { $_.AppRoleId -eq $appRole.Id -and $_.ResourceId -eq $graphSp.Id }
    if ($already) {
        [PSCustomObject]@{
            Role       = $role
            Action     = "Skipped (already assigned)"
            ResourceId = $graphSp.Id
        }
        continue
    }

    New-MgServicePrincipalAppRoleAssignment `
        -ServicePrincipalId $miPrincipalId `
        -PrincipalId $miPrincipalId `
        -ResourceId $graphSp.Id `
        -AppRoleId $appRole.Id | Out-Null

    [PSCustomObject]@{
        Role       = $role
        Action     = "Assigned"
        ResourceId = $graphSp.Id
    }
}

[PSCustomObject]@{
    ManagedIdentityObjectId = $miPrincipalId
    AppServiceName          = $AppServiceName
    ResourceGroupName       = $ResourceGroupName
    GraphServicePrincipalId = $graphSp.Id
    RoleResults             = $results
}
