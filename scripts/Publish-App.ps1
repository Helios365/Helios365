param(
    [Parameter(Mandatory = $true)]
    [string]$ResourceGroupName,

    [Parameter(Mandatory = $true)]
    [string]$AppName,

    [Parameter(Mandatory = $true)]
    [ValidateSet("Web", "Function")]
    [string]$AppType,

    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

Import-Module Az.Accounts -ErrorAction Stop
Import-Module Az.Websites -ErrorAction Stop

if (-not (Get-AzContext)) {
    Connect-AzAccount -ErrorAction Stop | Out-Null
}

# Determine paths
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Split-Path -Parent $ScriptDir

switch ($AppType) {
    "Web" {
        $ProjectPath = Join-Path $RepoRoot "src/Helios365.Web/Helios365.Web.csproj"
    }
    "Function" {
        $ProjectPath = Join-Path $RepoRoot "src/Helios365.Functions/Helios365.Functions.csproj"
    }
}

if (-not (Test-Path $ProjectPath)) {
    throw "Project file not found: $ProjectPath"
}

$TempDir = Join-Path ([System.IO.Path]::GetTempPath()) "helios365-deploy-$([System.Guid]::NewGuid().ToString('N').Substring(0, 8))"
$PublishDir = Join-Path $TempDir "publish"
$ZipPath = Join-Path $TempDir "deploy.zip"

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
    Compress-Archive -Path "$PublishDir/*" -DestinationPath $ZipPath -Force

    Write-Host "Deploying to $AppType app '$AppName'..." -ForegroundColor Cyan
    Publish-AzWebApp -ResourceGroupName $ResourceGroupName -Name $AppName -ArchivePath (Resolve-Path $ZipPath) -Force | Out-Null

    Write-Host "Deployment completed successfully." -ForegroundColor Green

    [PSCustomObject]@{
        AppType           = $AppType
        AppName           = $AppName
        ResourceGroupName = $ResourceGroupName
        Configuration     = $Configuration
        Status            = "Success"
    }
}
finally {
    Write-Host "Cleaning up temporary files..." -ForegroundColor Cyan
    if (Test-Path $TempDir) { Remove-Item $TempDir -Recurse -Force }
}
