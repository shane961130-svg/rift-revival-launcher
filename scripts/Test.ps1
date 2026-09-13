[CmdletBinding()]
param([switch]$CompileOnly)
$ErrorActionPreference = 'Stop'
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifacts = Join-Path $project 'artifacts'
$launcher = Join-Path $artifacts 'RiftRevivalLauncher.exe'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'

& (Join-Path $PSScriptRoot 'Verify-PublicTree.ps1')
& (Join-Path $PSScriptRoot 'Build.ps1') -OutputDirectory $artifacts
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $launcher)) { throw 'Launcher build failed.' }

$fixtures = Join-Path $project 'work\public-tests'
New-Item -ItemType Directory -Path $fixtures -Force | Out-Null
foreach ($suite in @('LauncherUpdates.Tests', 'Launcher.UiTests')) {
    $testExe = Join-Path $fixtures ($suite + '.exe')
    & $compiler /nologo /target:exe /platform:x64 /codepage:65001 /langversion:5 "/out:$testExe" "/reference:$launcher" /reference:System.Web.Extensions.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll /reference:System.Windows.Forms.dll /reference:System.Drawing.dll (Join-Path $project ('tests\' + $suite + '.cs'))
    if ($LASTEXITCODE -ne 0) { throw "$suite compilation failed." }
    if (-not $CompileOnly) {
        & $testExe $fixtures
        if ($LASTEXITCODE -ne 0) { throw "$suite failed." }
    }
}

if ($CompileOnly) { Write-Output 'Public launcher build and test compilation passed; test execution was intentionally skipped.' }
else { Write-Output 'Public launcher build and offline tests passed.' }
