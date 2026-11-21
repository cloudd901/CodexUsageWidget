$ErrorActionPreference = 'Stop'
# Build first so compilation failure leaves the current widget running.
& (Join-Path $PSScriptRoot 'build-widget.ps1') -Pending
& (Join-Path $PSScriptRoot 'restart-widget.ps1') -InstallPending

