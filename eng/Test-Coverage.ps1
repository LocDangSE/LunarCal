<#
.SYNOPSIS
    Fails when line coverage of a source folder is below a threshold (spec NFR-MAINT, Phase 1 DoD).

.DESCRIPTION
    Reads every Cobertura report produced by `dotnet test --coverage --coverage-output-format cobertura`,
    merges hits per source line (max across reports), and checks each "path=threshold" gate.

.EXAMPLE
    ./eng/Test-Coverage.ps1 -Reports TestResults/*.cobertura.xml -Gate 'src/LunarCal.Core/Lunar=95','src/LunarCal.Core=85'
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string[]] $Reports,
    [Parameter(Mandatory)] [string[]] $Gate
)

$ErrorActionPreference = 'Stop'

$files = @($Reports | ForEach-Object { Get-ChildItem -Path $_ -File })
if ($files.Count -eq 0) { throw "No coverage reports match: $($Reports -join ', ')" }

# Key "normalized/file/path:line" -> max hits.
$hits = @{}
foreach ($file in $files) {
    [xml] $xml = Get-Content -LiteralPath $file.FullName -Raw
    foreach ($class in $xml.SelectNodes('//class')) {
        $path = $class.filename -replace '\\', '/'
        foreach ($line in $class.SelectNodes('lines/line')) {
            $key = "${path}:$($line.number)"
            $count = [int] $line.hits
            if (-not $hits.ContainsKey($key) -or $hits[$key] -lt $count) { $hits[$key] = $count }
        }
    }
}

$failed = $false
# `pwsh -File` passes "a=1,b=2" as one string; accept both forms.
foreach ($entry in @($Gate | ForEach-Object { $_ -split ',' } | Where-Object { $_ })) {
    $folder, $threshold = $entry -split '=', 2
    $needle = '/' + ($folder.Trim('/') -replace '\\', '/') + '/'
    $lines = @($hits.Keys | Where-Object { $_.Contains($needle) })
    if ($lines.Count -eq 0) {
        Write-Host "::error::No covered lines found under $folder"
        $failed = $true
        continue
    }

    $covered = @($lines | Where-Object { $hits[$_] -gt 0 }).Count
    $percent = [math]::Round(100.0 * $covered / $lines.Count, 2)
    $ok = $percent -ge [double] $threshold
    Write-Host ("{0,-28} {1,6}% ({2}/{3} lines), threshold {4}% {5}" -f $folder, $percent, $covered, $lines.Count, $threshold, ($(if ($ok) { 'OK' } else { 'FAIL' })))
    if (-not $ok) { $failed = $true }
}

if ($failed) { exit 1 }
