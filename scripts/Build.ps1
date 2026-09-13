[CmdletBinding()]
param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'The Windows .NET Framework C# compiler is required.' }
$output = if($OutputDirectory){[IO.Path]::GetFullPath($OutputDirectory)}else{Join-Path $project 'artifacts'}
New-Item -ItemType Directory -Path $output -Force | Out-Null
$exe = Join-Path $output 'RiftRevivalLauncher.exe'
$dependencyRoot = Join-Path $project '.build\mono.cecil.0.11.6'
$package = Join-Path $dependencyRoot 'mono.cecil.0.11.6.nupkg'
$cecil = Join-Path $dependencyRoot 'lib\net40\Mono.Cecil.dll'
if (-not (Test-Path -LiteralPath $cecil)) {
    New-Item -ItemType Directory -Path $dependencyRoot -Force | Out-Null
    Invoke-WebRequest -UseBasicParsing -Uri 'https://api.nuget.org/v3-flatcontainer/mono.cecil/0.11.6/mono.cecil.0.11.6.nupkg' -OutFile $package
    if ((Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash -ne 'D2A23832AAA948BA9A01ACC42B5726E34C5F995958F1B30D45C0E7C70B3A72D5') { throw 'Mono.Cecil package hash mismatch.' }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::ExtractToDirectory($package, $dependencyRoot)
}
if((Get-FileHash -LiteralPath $cecil).Hash -ne 'C41BDB9FFD3C5F6E17D2382C1012D73703E035E3F1100245FDD4E08C8DC6EB5B'){throw 'Unrecognized patch compiler dependency.'}
$hashes=Join-Path $project 'launcher\assets\cartridge-hashes.txt'
if(-not(Test-Path -LiteralPath $hashes)){throw 'Prepare the reviewed cartridge hash table first.'}
foreach ($process in @(Get-Process -Name RiftRevivalLauncher -ErrorAction SilentlyContinue)) {
    if ($process.Path -eq $exe) { throw 'Close the launcher before rebuilding it.' }
}
$requiredArt = @('hero.png','logo.png','icons.ttf','launcher.ico')
$resources = @($requiredArt | ForEach-Object { $file = Join-Path $project ('launcher\assets\' + $_); if (-not (Test-Path -LiteralPath $file)) { throw "Required artwork missing: $_" }; '/resource:' + $file + ',Art.' + $_ })
$iconArgs = @()
$iconPath = Join-Path $project 'launcher\assets\launcher.ico'
if (Test-Path -LiteralPath $iconPath) { $iconArgs = @('/win32icon:' + $iconPath) }
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 /langversion:5 "/out:$exe" "/win32manifest:$project\launcher\app.manifest" "/reference:System.Windows.Forms.dll" "/reference:System.Drawing.dll" "/reference:System.Web.Extensions.dll" "/reference:System.IO.Compression.dll" "/reference:System.IO.Compression.FileSystem.dll" "/reference:$cecil" "/resource:$hashes,Mods.hashes" "/resource:$project\launcher\assets\cartridge-legacy-hashes.txt,Mods.legacyhashes" "/resource:$project\launcher\assets\cartridge-spacing-v1-hashes.txt,Mods.spacingv1hashes" "/resource:$project\launcher\assets\cartridge-spacing-v2-hashes.txt,Mods.spacingv2hashes" "/resource:$project\launcher\assets\cartridge-details-v1-hashes.txt,Mods.detailsv1hashes" "/resource:$project\launcher\assets\backpack-v1-hashes.txt,Mods.backpackv1hashes" "/resource:$project\launcher\assets\backpack-printer-v1-hashes.txt,Mods.backpackprinterv1hashes" @resources @iconArgs "$project\launcher\RecipePatch.cs" "$project\launcher\ModLibrary.cs" "$project\launcher\ModSets.cs" "$project\launcher\ModCatalog.cs" "$project\launcher\CartridgePatch.cs" "$project\launcher\GloveBatteryPatch.cs" "$project\launcher\BackpackPatch.cs" "$project\launcher\LauncherUpdates.cs" "$project\launcher\MainForm.cs" "$project\launcher\AssemblyInfo.cs"
if ($LASTEXITCODE -ne 0) { throw 'Launcher compilation failed.' }
Copy-Item -LiteralPath $cecil -Destination (Join-Path $output 'Mono.Cecil.dll') -Force
[pscustomobject]@{ Launcher = $exe; SHA256 = (Get-FileHash -LiteralPath $exe).Hash; GameExecuted = $false }
