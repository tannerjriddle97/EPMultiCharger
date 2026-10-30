param([string]$GamePath = 'E:/Games/Vintagestory', [string]$EPBasics = "$PSScriptRoot/build/dependencies/electricalprogressivebasics", [string]$EPCore = "$PSScriptRoot/build/dependencies/electricalprogressivecore", [switch]$Test)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$build = Join-Path $root 'build'
New-Item -ItemType Directory -Path $build -Force | Out-Null
$runtime = & dotnet --list-runtimes | Where-Object { $_ -match '^Microsoft.NETCore.App 10\.' } | Select-Object -Last 1
if (!$runtime) { throw '.NET 10 runtime is required (as used by Vintage Story 1.22.7).' }
$runtime -match '^Microsoft.NETCore.App ([^ ]+) \[([^\]]+)\]' | Out-Null
$runtimePath = Join-Path $Matches[2] $Matches[1]
$sdk = & dotnet --list-sdks | Select-Object -Last 1
$sdk -match '^([^ ]+) \[([^\]]+)\]' | Out-Null
$compiler = Join-Path $Matches[2] "$($Matches[1])/Roslyn/bincore/csc.dll"
$references = @(Get-ChildItem $runtimePath -Filter '*.dll' | Where-Object { ($_.Name -like 'System.*' -and $_.Name -notlike '*.Native.dll') -or $_.Name -in 'mscorlib.dll','netstandard.dll','Microsoft.CSharp.dll' } | ForEach-Object { '/r:' + $_.FullName })
$references += '/r:' + (Join-Path $GamePath 'VintagestoryAPI.dll')
$references += '/r:' + (Join-Path $GamePath 'Lib/0Harmony.dll')
$references += '/r:' + (Join-Path $GamePath 'Mods/VSEssentials.dll')
$references += '/r:' + (Join-Path $GamePath 'Mods/VSSurvivalMod.dll')
$references += '/r:' + (Join-Path $GamePath 'Lib/Newtonsoft.Json.dll')
$references += '/r:' + (Join-Path $EPBasics 'ElectricalProgressive-Basics.dll')
$references += '/r:' + (Join-Path $EPCore 'ElectricalProgressive-Core.dll')
& dotnet $compiler /nologo /target:library /langversion:latest /nullable:disable "/out:$build/EPMultiCharger.dll" $references (Join-Path $root 'MultiCharger.cs')
if ($LASTEXITCODE) { throw 'Compilation failed.' }
Copy-Item (Join-Path $root 'modinfo.json') $build
Copy-Item (Join-Path $root 'assets') $build -Recurse -Force
Write-Output "Built $build/EPMultiCharger.dll"
if ($Test) {
    & dotnet $compiler /nologo /target:exe /langversion:latest "/out:$build/Tests.dll" "/r:$build/EPMultiCharger.dll" $references (Join-Path $root 'Tests.cs')
    if ($LASTEXITCODE) { throw 'Test compilation failed.' }
    '{"runtimeOptions":{"tfm":"net10.0","framework":{"name":"Microsoft.NETCore.App","version":"10.0.0"}}}' | Set-Content (Join-Path $build 'Tests.runtimeconfig.json')
    & dotnet (Join-Path $build 'Tests.dll') $root $GamePath $EPBasics $EPCore
    if ($LASTEXITCODE) { throw 'Tests failed.' }
}
