<#
.SYNOPSIS
    Stops the running TaskbarLauncher instance, pulls the latest changes,
    rebuilds (Release), and starts it again.
.DESCRIPTION
    Meant for local development: run it after every pull/change to keep
    working with the current version.
#>

$ErrorActionPreference = "Stop"

$repoRoot = $PSScriptRoot
Set-Location $repoRoot

function Write-Step([string]$Text) {
    Write-Host ""
    Write-Host "==> $Text" -ForegroundColor Cyan
}

Write-Step "Stopping running instance (if any)"
$proc = Get-Process -Name "TaskbarLauncher" -ErrorAction SilentlyContinue
if ($proc) {
    $proc | Stop-Process -Force
    $proc | Wait-Process -Timeout 5 -ErrorAction SilentlyContinue
    # Brief buffer so Windows actually releases the .exe file before
    # dotnet publish tries to overwrite it.
    Start-Sleep -Milliseconds 500
    Write-Host "Instance stopped."
}
else {
    Write-Host "No running instance found."
}

Write-Step "Pulling latest changes (git pull)"
git pull
if ($LASTEXITCODE -ne 0) {
    throw "git pull failed (exit code $LASTEXITCODE)."
}

Write-Step "Building Release (dotnet publish)"
dotnet publish -c Release
if ($LASTEXITCODE -ne 0) {
    throw "Build failed (exit code $LASTEXITCODE)."
}

$exePath = Join-Path $repoRoot "bin\Release\net8.0-windows\win-x64\publish\TaskbarLauncher.exe"
if (-not (Test-Path $exePath)) {
    throw "Expected .exe was not found: $exePath"
}

Write-Step "Starting TaskbarLauncher"
Start-Process -FilePath $exePath

Write-Host ""
Write-Host "Done." -ForegroundColor Green
