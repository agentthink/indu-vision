#requires -Version 5.1
<#
Publish a self-contained Windows x64 folder and package it for GitHub Releases.
Usage: .\Publish.ps1 -Version 1.0.1
Outputs are unique per run; previous releases are never overwritten.
#>
[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?$')]
    [string]$Version = '1.0.1',
    [switch]$OpenOutputFolder
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$runDirectory = $null

try {
    $project = Join-Path $PSScriptRoot 'DigitalTwinIndustrialAutomationPlatform.csproj'
    if (-not (Test-Path -LiteralPath $project -PathType Leaf)) {
        throw 'Project file not found. Keep Publish.ps1 beside the .csproj file.'
    }
    $dotnet = Get-Command dotnet -CommandType Application -ErrorAction SilentlyContinue
    if ($null -eq $dotnet) { throw 'Install the .NET 10 SDK, reopen this terminal, and try again.' }
    $sdks = @(& $dotnet.Source --list-sdks)
    if ($LASTEXITCODE -ne 0) { throw 'Unable to query installed .NET SDKs.' }
    $hasSdk = @($sdks | Where-Object { $_ -match '^(\d+)\.' -and [int]$Matches[1] -ge 10 }).Count -gt 0
    if (-not $hasSdk) { throw 'The project requires the .NET 10 SDK or a newer compatible SDK.' }

    $outputRoot = Join-Path $PSScriptRoot 'artifacts'
    $runName = 'v{0}_{1}_{2}' -f $Version, (Get-Date -Format 'yyyyMMdd_HHmmss'), ([Guid]::NewGuid().ToString('N').Substring(0, 6))
    $runDirectory = Join-Path $outputRoot $runName
    $publishDirectory = Join-Path $runDirectory 'publish'
    $null = New-Item -ItemType Directory -Path $publishDirectory -Force
    $archiveName = 'DigitalTwinIndustrialAutomationPlatform_v{0}_win-x64.zip' -f $Version
    $archivePath = Join-Path $runDirectory $archiveName
    $partialArchive = Join-Path $runDirectory 'package.partial.zip'

    Write-Host '[1/4] Publishing Release / win-x64 / self-contained...' -ForegroundColor Cyan
    $publishArguments = @(
        'publish', $project, '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true',
        '-p:EnableWindowsTargeting=true', '-p:PublishTrimmed=false', '-p:PublishSingleFile=false',
        '-p:DebugType=None', '-p:DebugSymbols=false', "-p:Version=$Version", '-o', $publishDirectory
    )
    & $dotnet.Source @publishArguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit code $LASTEXITCODE). No release ZIP was produced." }

    Write-Host '[2/4] Checking application and native dependencies...' -ForegroundColor Cyan
    $exeName = 'DigitalTwinIndustrialAutomationPlatform.exe'
    foreach ($required in @($exeName, 'OpenCvSharp.dll', 'coreclr.dll', 'PresentationFramework.dll')) {
        if (-not (Test-Path -LiteralPath (Join-Path $publishDirectory $required) -PathType Leaf)) {
            throw "Publish output is incomplete: missing $required."
        }
    }
    $native = @(Get-ChildItem -LiteralPath $publishDirectory -File -Recurse -Filter 'OpenCvSharpExtern.dll')
    if ($native.Count -eq 0) { throw 'Publish output is missing the OpenCvSharp native library.' }

    $instructions = @"
Digital Twin Industrial Automation Platform v$Version
Windows x64 portable release / .NET runtime included

1. Extract the entire ZIP to a local folder.
2. Run $exeName.
3. Keep all DLLs and subfolders beside the application.

Camera acquisition requires a compatible Windows camera driver.
If native-library loading fails on another PC, install the Microsoft
Visual C++ x64 Redistributable and retry.
"@
    Set-Content -LiteralPath (Join-Path $publishDirectory 'RUN_INSTRUCTIONS.txt') -Value $instructions -Encoding UTF8

    Write-Host '[3/4] Creating ZIP...' -ForegroundColor Cyan
    # Escape the parent path so square brackets in a workspace path stay literal.
    $contents = Join-Path ([System.Management.Automation.WildcardPattern]::Escape($publishDirectory)) '*'
    Compress-Archive -Path $contents -DestinationPath $partialArchive -CompressionLevel Optimal
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [System.IO.Compression.ZipFile]::OpenRead($partialArchive)
    try {
        $names = @($zip.Entries | ForEach-Object { $_.FullName.Replace('\', '/') })
        if ($names -notcontains $exeName) { throw 'ZIP verification failed: application must be at the archive root.' }
        foreach ($entry in $zip.Entries) {
            $stream = $entry.Open()
            try { $stream.CopyTo([System.IO.Stream]::Null) } finally { $stream.Dispose() }
        }
    }
    finally { $zip.Dispose() }
    Move-Item -LiteralPath $partialArchive -Destination $archivePath

    Write-Host '[4/4] Writing SHA-256 checksum...' -ForegroundColor Cyan
    $hash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    Set-Content -LiteralPath ($archivePath + '.sha256') -Value "$hash  $archiveName" -Encoding ASCII
    Write-Host ''
    Write-Host 'Release package created successfully.' -ForegroundColor Green
    Write-Host "ZIP: $archivePath"
    Write-Host "SHA256: $archivePath.sha256"
    Write-Host "Portable folder: $publishDirectory"
    Write-Host 'Upload the ZIP (and optionally its .sha256 file) to GitHub Release assets.'
    if ($OpenOutputFolder -and $env:OS -eq 'Windows_NT') {
        try { Start-Process explorer.exe -ArgumentList ('"{0}"' -f $runDirectory) }
        catch { Write-Warning "Package is ready; open the output folder manually: $runDirectory" }
    }
    exit 0
}
catch {
    Write-Host ('Packaging failed: ' + $_.Exception.Message) -ForegroundColor Red
    if ($null -ne $runDirectory) { Write-Host "Diagnostic output, if any: $runDirectory" }
    exit 1
}
