<#
.SYNOPSIS
    Watches the current branch for new commits and redeploys automatically.
.DESCRIPTION
    Every -IntervalSeconds (default 60), fetches from origin. Only when the
    remote branch actually has new commits does it stop the running
    instance, pull, rebuild (Release), and relaunch - an unchanged remote
    is a silent no-op. Press Ctrl+C to stop watching.
.PARAMETER IntervalSeconds
    How often to check for new commits, in seconds. Default: 60.
.PARAMETER Once
    Deploy immediately, once, without entering the watch loop.
#>

param(
    [int]$IntervalSeconds = 60,
    [switch]$Once
)

$ErrorActionPreference = "Stop"

$repoRoot = $PSScriptRoot
Set-Location $repoRoot

function Write-Step([string]$Text) {
    Write-Host ""
    Write-Host "==> $Text" -ForegroundColor Cyan
}

function Invoke-Deploy {
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
    Write-Host "Deployed." -ForegroundColor Green
}

if ($Once) {
    Invoke-Deploy
    return
}

$branch = (git rev-parse --abbrev-ref HEAD).Trim()
Write-Host "Watching for new commits every $IntervalSeconds s. Press Ctrl+C to stop." -ForegroundColor Yellow

while ($true) {
    git fetch origin $branch --quiet
    if ($LASTEXITCODE -ne 0) {
        Write-Host "git fetch failed (exit code $LASTEXITCODE) - will retry next interval." -ForegroundColor Red
    }
    else {
        $localHash = (git rev-parse HEAD).Trim()
        $remoteHash = (git rev-parse "origin/$branch").Trim()

        if ($localHash -ne $remoteHash) {
            Write-Host ""
            Write-Host "New commits detected: $($localHash.Substring(0,7)) -> $($remoteHash.Substring(0,7))" -ForegroundColor Yellow
            try {
                Invoke-Deploy
            }
            catch {
                Write-Host "Deploy failed: $_" -ForegroundColor Red
            }
        }
    }

    Start-Sleep -Seconds $IntervalSeconds
}
