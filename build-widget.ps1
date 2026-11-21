# Compilation only: deliberately runs no tests and does not launch the widget.
param([switch]$Pending)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'build-common.ps1')
$build = Get-WidgetBuildContext -ProjectRoot $PSScriptRoot
$common = $build.CompilerArguments
$refs = $build.References
$sources = $build.Sources
$destination = Join-Path $PSScriptRoot 'bin\CodexUsageWidget.exe'
if ($Pending) { $destination = Join-Path $PSScriptRoot 'bin\CodexUsageWidget.pending.exe' }
New-Item -ItemType Directory -Force -Path (Split-Path $destination -Parent) | Out-Null
& $build.Compiler @common /optimize+ /target:winexe "/win32manifest:$PSScriptRoot\app.manifest" "/win32icon:$PSScriptRoot\assets\ring.ico" "/resource:$PSScriptRoot\assets\ring.ico,CodexUsageBubble.ring.ico" "/out:$destination" @refs @sources
if ($LASTEXITCODE -ne 0) { throw 'Widget compilation failed.' }
Write-Host "Built: $destination"
