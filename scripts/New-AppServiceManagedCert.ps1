param(
    [Parameter(Mandatory = $true)]
    [string]$ResourceGroupName,

    [Parameter(Mandatory = $true)]
    [string]$AppServiceName,

    [Parameter(Mandatory = $true)]
    [string]$HostName
)

Import-Module Az.Accounts -ErrorAction Stop
Import-Module Az.Websites -ErrorAction Stop

if (-not (Get-AzContext)) {
    Connect-AzAccount -ErrorAction Stop | Out-Null
}

# Get app and hostnames
$App = Get-AzWebApp -ResourceGroupName $ResourceGroupName -Name $AppServiceName -ErrorAction Stop
$ExistingHosts = @($App.HostNames)
if ($ExistingHosts -notcontains $HostName) {
    $UpdatedHosts = $ExistingHosts + $HostName
    # Ensure we don't lose existing hostnames
    Set-AzWebApp -ResourceGroupName $ResourceGroupName -Name $AppServiceName -HostNames $UpdatedHosts | Out-Null
}
$App = Get-AzWebApp -ResourceGroupName $ResourceGroupName -Name $AppServiceName -ErrorAction Stop
if ($App.HostNames -notcontains $HostName) {
    throw "Hostname '$HostName' could not be added to web app '$AppServiceName'. Verify custom domain binding prerequisites and DNS."
}

# Certificate name and existence check
$CertName = "$AppServiceName-managedcert-$($HostName -replace '\\.', '-')"
$ExistingCert = Get-AzWebAppCertificate -ResourceGroupName $ResourceGroupName | Where-Object { $_.Name -eq $CertName -or ($_.HostNames -and $_.HostNames -contains $HostName) }

if (-not $ExistingCert) {
    # Allow brief time for hostname binding propagation
    Start-Sleep -Seconds 5
    try {
        New-AzWebAppCertificate `
            -ResourceGroupName $ResourceGroupName `
            -WebAppName $AppServiceName `
            -Name $CertName `
            -HostName $HostName `
            -AddBinding `
            -SslState SniEnabled `
            -ErrorAction Stop | Out-Null
    }
    catch {
        $PrimaryError = $_.Exception.Message
        try {
            # Fallback without custom name/addbinding (bind separately)
            New-AzWebAppCertificate `
                -ResourceGroupName $ResourceGroupName `
                -WebAppName $AppServiceName `
                -HostName $HostName `
                -SslState SniEnabled `
                -ErrorAction Stop | Out-Null
        }
        catch {
            throw "Failed to create managed certificate for '$HostName'. Primary error: $PrimaryError. Fallback error: $($_.Exception.Message)"
        }
    }
}

# Ensure SSL binding exists with the cert thumbprint
$Cert = Get-AzWebAppCertificate -ResourceGroupName $ResourceGroupName | Where-Object { $_.Name -eq $CertName -or ($_.HostNames -and $_.HostNames -contains $HostName) } | Select-Object -First 1
if (-not $Cert) {
    throw "Managed certificate '$CertName' was not created."
}

New-AzWebAppSSLBinding `
    -ResourceGroupName $ResourceGroupName `
    -WebAppName $AppServiceName `
    -Name $HostName `
    -Thumbprint $Cert.Thumbprint `
    -SslState SniEnabled `
    | Out-Null

[PSCustomObject]@{
    WebAppName      = $AppServiceName
    ResourceGroup   = $ResourceGroupName
    HostName        = $HostName
    CertificateName = $CertName
    Thumbprint      = $Cert.Thumbprint
    Binding         = 'SNI'
}
