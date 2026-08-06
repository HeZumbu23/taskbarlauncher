<#
.SYNOPSIS
    Beendet die laufende TaskbarLauncher-Instanz, holt den neuesten Stand,
    baut die App neu (Release) und startet sie wieder.
.DESCRIPTION
    Für die lokale Entwicklung gedacht: einfach nach jedem Pull/Änderung
    ausführen, um mit der aktuellen Version weiterzuarbeiten.
#>

$ErrorActionPreference = "Stop"

$repoRoot = $PSScriptRoot
Set-Location $repoRoot

function Write-Step([string]$Text) {
    Write-Host ""
    Write-Host "==> $Text" -ForegroundColor Cyan
}

Write-Step "Beende laufende Instanz (falls vorhanden)"
$proc = Get-Process -Name "TaskbarLauncher" -ErrorAction SilentlyContinue
if ($proc) {
    $proc | Stop-Process -Force
    $proc | Wait-Process -Timeout 5 -ErrorAction SilentlyContinue
    # Kurze Pufferzeit, damit Windows die .exe-Datei wirklich freigibt,
    # bevor dotnet publish versucht, sie zu überschreiben.
    Start-Sleep -Milliseconds 500
    Write-Host "Instanz beendet."
}
else {
    Write-Host "Keine laufende Instanz gefunden."
}

Write-Step "Hole neuesten Stand (git pull)"
git pull
if ($LASTEXITCODE -ne 0) {
    throw "git pull fehlgeschlagen (Exit-Code $LASTEXITCODE)."
}

Write-Step "Baue Release (dotnet publish)"
dotnet publish -c Release
if ($LASTEXITCODE -ne 0) {
    throw "Build fehlgeschlagen (Exit-Code $LASTEXITCODE)."
}

$exePath = Join-Path $repoRoot "bin\Release\net8.0-windows\win-x64\publish\TaskbarLauncher.exe"
if (-not (Test-Path $exePath)) {
    throw "Erwartete .exe wurde nicht gefunden: $exePath"
}

Write-Step "Starte TaskbarLauncher"
Start-Process -FilePath $exePath

Write-Host ""
Write-Host "Fertig." -ForegroundColor Green
