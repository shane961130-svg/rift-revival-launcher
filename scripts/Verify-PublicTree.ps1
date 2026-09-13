[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$excluded = @((Join-Path $project '.git'), (Join-Path $project '.build'), (Join-Path $project 'artifacts'), (Join-Path $project 'work'))
$files = Get-ChildItem -LiteralPath $project -File -Recurse | Where-Object {
    $path = $_.FullName
    -not ($excluded | Where-Object { $path.StartsWith($_ + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) })
}

$forbiddenExtensions = @('.exe', '.dll', '.zip', '.db', '.ssk', '.pfx', '.p12', '.key')
$forbidden = @($files | Where-Object { $forbiddenExtensions -contains $_.Extension.ToLowerInvariant() })
if ($forbidden.Count) { throw 'Forbidden binary, save, or key file in public tree: ' + ($forbidden.FullName -join ', ') }

$textExtensions = @('.cs', '.ps1', '.md', '.txt', '.yml', '.yaml', '.json', '.manifest', '.gitignore', '.gitattributes')
$patterns = @(
    [regex]::Escape(('C:' + '\Users\' + 'Shcowley')),
    ('BEGIN ' + '(RSA |EC |OPENSSH )?' + 'PRIVATE KEY'),
    ('Steam' + 'AppTicket')
)
foreach ($file in $files | Where-Object { $textExtensions -contains $_.Extension.ToLowerInvariant() -or $_.Name -in @('.gitignore', '.gitattributes') }) {
    $content = [IO.File]::ReadAllText($file.FullName)
    foreach ($pattern in $patterns) {
        if ($content -match $pattern) { throw "Private or credential-like content in $($file.FullName): $pattern" }
    }
}

Write-Output "Public tree guard passed: $($files.Count) files checked."
