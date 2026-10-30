param([string]$ModsPath = (Join-Path $env:APPDATA 'VintagestoryData\Mods'))
$ErrorActionPreference = 'Stop'
# Read installed dependency packages into this project's ignored build directory.
foreach ($dependency in @(
    @{Id='electricalprogressivebasics'; Package='electricalprogressivebasics_3.3.1.zip'},
    @{Id='electricalprogressivecore'; Package='electricalprogressivecore_3.3.0.zip'}
)) {
    $root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ('build/dependencies/' + $dependency.Id)))
    $zip = [IO.Compression.ZipFile]::OpenRead((Join-Path $ModsPath $dependency.Package))
    try {
        foreach ($entry in $zip.Entries) {
            if (!$entry.Name) { continue }
            $destination = [IO.Path]::GetFullPath((Join-Path $root $entry.FullName))
            if (!$destination.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid archive path' }
            New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force | Out-Null
            [IO.Compression.ZipFileExtensions]::ExtractToFile($entry,$destination,$true)
        }
    } finally { $zip.Dispose() }
    Write-Output "Prepared $($dependency.Id) in $root"
}
