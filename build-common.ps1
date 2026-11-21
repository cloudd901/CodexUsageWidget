# Shared configuration only. Dot-sourcing this file runs no builds or tests.
function Get-WidgetBuildContext {
    param(
        [Parameter(Mandatory = $true)][string]$ProjectRoot,
        [ValidateSet('Desktop','Core')][string]$ReferenceSet = 'Desktop'
    )
    $root = [IO.Path]::GetFullPath($ProjectRoot)
    $framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
    $compiler = Join-Path $framework 'csc.exe'
    if (-not (Test-Path -LiteralPath $compiler -PathType Leaf)) { throw "Framework compiler not found: $compiler" }
    $assemblies = @('System.dll','System.Core.dll','System.Web.Extensions.dll')
    if ($ReferenceSet -eq 'Desktop') {
        $assemblies += @('System.Windows.Forms.dll','System.Drawing.dll','WPF\WindowsBase.dll','WPF\PresentationCore.dll','WPF\PresentationFramework.dll','System.Xaml.dll')
    }
    $references = @($assemblies | ForEach-Object {
        $assembly = Join-Path $framework $_
        if (-not (Test-Path -LiteralPath $assembly -PathType Leaf)) { throw "Required assembly not found: $assembly" }
        '/reference:' + $assembly
    })
    $sourceRoot = Join-Path $root 'src'
    $sources = @(Get-ChildItem -LiteralPath $sourceRoot -Filter '*.cs' -File -ErrorAction Stop | Sort-Object Name | ForEach-Object FullName)
    $core = @('DesktopAppClient.cs','Diagnostics.cs','Usage.cs','Client.cs','AppServer.cs','ChatActions.cs','ResetActions.cs','AdaptivePolling.cs')
    $coreSources = @($core | ForEach-Object {
        $source = Join-Path $sourceRoot $_
        if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Required source not found: $source" }
        $source
    })
    [pscustomobject]@{
        Root = $root
        Compiler = $compiler
        CompilerArguments = @('/nologo','/codepage:65001')
        References = $references
        Sources = $sources
        CoreSources = $coreSources
        PollingSources = @((Join-Path $sourceRoot 'Usage.cs'), (Join-Path $sourceRoot 'AdaptivePolling.cs'))
    }
}
