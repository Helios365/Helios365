param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("Web", "Function")]
    [string]$AppType,

    [Parameter(Mandatory = $true)]
    [string]$Version,

    [string]$StorageAccountName = "infrastructurepackages",

    [string]$ContainerName = "foss",

    [string]$Configuration = "Release",

    [string]$ResourceGroupName,

    [string]$AppName
)

$ErrorActionPreference = "Stop"

Import-Module Az.Accounts -ErrorAction Stop
Import-Module Az.Storage -ErrorAction Stop

if (-not (Get-AzContext)) {
    Connect-AzAccount -ErrorAction Stop | Out-Null
}

# Determine paths and blob name
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Split-Path -Parent $ScriptDir

switch ($AppType) {
    "Web" {
        $ProjectPath = Join-Path $RepoRoot "src/Helios365.Web/Helios365.Web.csproj"
        $BlobName = "helios-web-$Version.zip"
    }
    "Function" {
        $ProjectPath = Join-Path $RepoRoot "src/Helios365.Functions/Helios365.Functions.csproj"
        $BlobName = "helios-api-$Version.zip"
    }
}

if (-not (Test-Path $ProjectPath)) {
    throw "Project file not found: $ProjectPath"
}

$TempDir = Join-Path ([System.IO.Path]::GetTempPath()) "helios365-deploy-$([System.Guid]::NewGuid().ToString('N').Substring(0, 8))"
$PublishDir = Join-Path $TempDir "publish"
$ZipPath = Join-Path $TempDir $BlobName

New-Item -ItemType Directory -Path $TempDir -Force | Out-Null

try {
    Write-Host "Cleaning project..." -ForegroundColor Cyan
    dotnet clean $ProjectPath --configuration $Configuration --verbosity minimal
    if ($LASTEXITCODE -ne 0) { throw "dotnet clean failed" }

    Write-Host "Publishing project..." -ForegroundColor Cyan
    dotnet publish $ProjectPath --configuration $Configuration --output $PublishDir --no-cache
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

    Write-Host "Creating deployment package..." -ForegroundColor Cyan
    if (Test-Path $ZipPath) { Remove-Item $ZipPath -Force }

    # Verify publish output
    Write-Host "Published files:" -ForegroundColor Yellow
    Get-ChildItem -Path $PublishDir -Recurse | Select-Object -First 20 | ForEach-Object {
        Write-Host "  $($_.FullName.Replace($PublishDir, ''))" -ForegroundColor Gray
    }

    # Create zip from inside the publish directory to ensure correct structure
    Push-Location $PublishDir
    try {
        Get-ChildItem -Path . | Compress-Archive -DestinationPath $ZipPath -Force
    }
    finally {
        Pop-Location
    }

    # Verify zip contents
    $zipSize = (Get-Item $ZipPath).Length / 1MB
    Write-Host "Zip file size: $([math]::Round($zipSize, 2)) MB" -ForegroundColor Yellow

    # Upload to blob storage
    Write-Host "Uploading to blob storage..." -ForegroundColor Cyan
    $storageContext = (Get-AzStorageAccount | Where-Object { $_.StorageAccountName -eq $StorageAccountName }).Context
    if (-not $storageContext) {
        throw "Storage account '$StorageAccountName' not found. Ensure you have access to this storage account."
    }

    $blobUrl = "https://$StorageAccountName.blob.core.windows.net/$ContainerName/$BlobName"
    Write-Host "Uploading to $blobUrl..." -ForegroundColor Cyan

    Set-AzStorageBlobContent -File $ZipPath -Container $ContainerName -Blob $BlobName -Context $storageContext -Force | Out-Null

    Write-Host "Package uploaded successfully: $blobUrl" -ForegroundColor Green

    # Optionally sync the app if ResourceGroupName and AppName are provided
    if ($ResourceGroupName -and $AppName) {
        Write-Host "Syncing app '$AppName' to use new package..." -ForegroundColor Cyan
        Import-Module Az.Websites -ErrorAction Stop

        # Restart the app to pick up the new package
        Restart-AzWebApp -ResourceGroupName $ResourceGroupName -Name $AppName | Out-Null
        Write-Host "App restarted to load new package." -ForegroundColor Green
    }

    [PSCustomObject]@{
        AppType        = $AppType
        Version        = $Version
        BlobUrl        = $blobUrl
        ZipSizeMB      = [math]::Round($zipSize, 2)
        Configuration  = $Configuration
        Status         = "Success"
    }
}
finally {
    Write-Host "Cleaning up temporary files..." -ForegroundColor Cyan
    if (Test-Path $TempDir) { Remove-Item $TempDir -Recurse -Force }
}
