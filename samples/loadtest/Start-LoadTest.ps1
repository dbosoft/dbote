#!/usr/bin/env pwsh
# Brings up the mass-scalable load-test stack locally.
#   ./Start-LoadTest.ps1 -Edges 100      # number of simulated edge systems (default 30)
# Prerequisites (Podman on Windows): the shared emulator stack needs privileged port 80 for the
# service-bus management API - run once per machine boot:
#   podman machine ssh 'sudo sysctl -w net.ipv4.ip_unprivileged_port_start=80'
# and the servicebus-api container must run as root (add `user: "0:0"` via an override if needed).
param(
    [int]$Edges = 30
)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path "$PSScriptRoot/../.."
$env:LOADTEST_EDGES = $Edges

Write-Host "Publishing service images (linux-x64)..." -ForegroundColor Cyan
$projects = @(
    "$root/src/workers/src/Dbosoft.Bote.BasicIdentityProvider",
    "$root/src/workers/src/Dbosoft.Bote.BoteWorker",
    "$PSScriptRoot/Dbosoft.Bote.Samples.LoadTest.Controller",
    "$PSScriptRoot/Dbosoft.Bote.Samples.LoadTest.EdgeLoad"
)
foreach ($p in $projects) {
    dotnet publish $p -c Release -r linux-x64 --no-self-contained
}

Write-Host "Building and starting the stack (podman compose)..." -ForegroundColor Cyan
Push-Location $PSScriptRoot
try {
    podman compose build
    podman compose up -d
    Write-Host ""
    Write-Host "Up with $Edges edge(s)." -ForegroundColor Green
    Write-Host "Metrics (no local dashboard - the consolidator targets the Azure deployment):" -ForegroundColor Green
    Write-Host "  Edge round-trip + quiet/noisy p95:  podman compose logs -f edgeload" -ForegroundColor Green
    Write-Host "  Controller throughput + fairness:   podman compose logs -f controller" -ForegroundColor Green
    Write-Host "Change the edge count:  ./Start-LoadTest.ps1 -Edges <n>" -ForegroundColor Green
}
finally {
    Pop-Location
}
