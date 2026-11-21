param([switch]$InstallPending, [switch]$IsolatedStartup)

$ErrorActionPreference = 'Stop'
$destination = Join-Path $PSScriptRoot 'bin\CodexUsageWidget.exe'
$pending = Join-Path $PSScriptRoot 'bin\CodexUsageWidget.pending.exe'
$resultPath = Join-Path $PSScriptRoot 'bin\restart-widget-result.json'
$result = [ordered]@{ Succeeded = $false; VerifiedUtc = $null; ProcessId = $null; Responding = $false; WindowReady = $false; Executable = $destination; InstalledPending = $false; Error = $null }

$ready = $null
$launched = $null
try {
	New-Item -ItemType Directory -Force -Path (Split-Path $resultPath -Parent) | Out-Null
	if ($InstallPending -and -not (Test-Path -LiteralPath $pending -PathType Leaf)) {
		throw 'The pending executable is missing.'
	}
	if (-not $InstallPending -and -not (Test-Path -LiteralPath $destination -PathType Leaf)) {
		throw 'The widget executable is missing. Run build-and-run.ps1 first.'
	}

	$widgets = @(Get-Process -Name CodexUsageWidget -ErrorAction SilentlyContinue)
	foreach ($widget in $widgets) {
		if ($widget.Path -eq $destination) {
			Stop-Process -Id $widget.Id -Force -ErrorAction Stop
		}
	}

	# A first build has no installed executable to unlock.
	$unlocked = -not (Test-Path -LiteralPath $destination -PathType Leaf)
	for ($attempt = 0; $attempt -lt 30; $attempt++) {
		if ($unlocked) { break }
		try {
			$handle = [System.IO.File]::Open($destination, 'Open', 'ReadWrite', 'None')
			$handle.Dispose()
			$unlocked = $true
			break
		}
		catch [System.IO.IOException] {
			Start-Sleep -Milliseconds 100
		}
	}

	if (-not $unlocked) {
		throw 'The widget executable is still locked; the restart could not replace it.'
	}

	if ($InstallPending) {
		Copy-Item -LiteralPath $pending -Destination $destination -Force
		if ((Get-FileHash -LiteralPath $pending).Hash -ne (Get-FileHash -LiteralPath $destination).Hash) {
			throw 'The installed executable does not match the pending build.'
		}

		$result.InstalledPending = $true
	}

	$startupId = [Guid]::NewGuid().ToString('N')
	$ready = [Threading.EventWaitHandle]::new($false, [Threading.EventResetMode]::ManualReset, ('Local\CodexUsageBubble.Startup.' + $startupId))
	$start = [Diagnostics.ProcessStartInfo]::new($destination, ('--startup-ready ' + $startupId))
	$start.UseShellExecute = $false
	$start.WorkingDirectory = $PSScriptRoot
	if ($IsolatedStartup) { $start.Arguments += ' --isolated' }
	$launched = [Diagnostics.Process]::Start($start)
	$result.ProcessId = $launched.Id
	$startup = [Diagnostics.Stopwatch]::StartNew()
	do {
		$windowReady = $ready.WaitOne(100)
		$launched.Refresh()
		if ($launched.HasExited) {
			throw ('The relaunched widget exited with code ' + $launched.ExitCode + '.')
		}
	} until ($windowReady -or $startup.Elapsed.TotalSeconds -ge 10)

	if (-not $windowReady) {
		throw 'The widget did not confirm a visible, rendered window within 10 seconds.'
	}
	$result.WindowReady = $true
	if (-not $launched.Responding) {
		throw 'The relaunched widget is not responding.'
	}
	$result.Responding = $true
	$result.Succeeded = $true
}
catch {
	$result.Error = $_.Exception.Message
	if ($launched -and -not $launched.HasExited) {
		Stop-Process -InputObject $launched -Force -ErrorAction SilentlyContinue
	}
}
finally {
	if ($ready) { $ready.Dispose() }
	if ($launched) { $launched.Dispose() }
	$result.VerifiedUtc = [DateTime]::UtcNow.ToString('o')
	$result | ConvertTo-Json | Set-Content -LiteralPath $resultPath -Encoding UTF8
}

if (-not $result.Succeeded) {
	throw $result.Error
}

Write-Output ('Widget window verified and responding: PID ' + $result.ProcessId)
