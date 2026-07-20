# pack.ps1 - Pack template directory to ZIP (exclude bin/obj/.vs/.git)
# Usage: run .\pack.ps1 in this directory
# Output: ../template.zip
# Uses .NET ZipArchive API for speed (Compress-Archive is too slow for large files)

$ErrorActionPreference = 'Stop'

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$templateRoot = Split-Path -Parent $scriptDir
$outputFile = Join-Path $templateRoot 'template.zip'

$excludeDirs = @('bin', 'obj', '.vs', '.git')

# Delete old ZIP
if (Test-Path $outputFile) {
    try { Remove-Item $outputFile -Force -ErrorAction Stop }
    catch { Write-Host "[pack] Old ZIP locked, will overwrite" }
}

Write-Host "[pack] Source: $scriptDir"
Write-Host "[pack] Output: $outputFile"

# Use .NET ZipArchive for fast streaming compression (no temp copy needed)
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$fileCount = 0
$totalBytes = 0L

# Normalize path for matching
$normalizedScriptDir = $scriptDir.TrimEnd('\', '/')

# Collect all files (excluding bin/obj/.vs/.git and pack.ps1 itself)
$files = Get-ChildItem -Path $scriptDir -Recurse -File -Force | Where-Object {
    $relative = $_.FullName.Substring($normalizedScriptDir.Length).TrimStart('\', '/')
    if ($relative -eq 'pack.ps1') { return $false }
    # Check if any path segment matches an excluded dir name
    $segments = $relative -split '[\\/]'
    foreach ($ex in $excludeDirs) {
        if ($segments -contains $ex) { return $false }
    }
    return $true
}

Write-Host "[pack] Files to pack: $($files.Count)"

# Create ZIP and stream files directly
$zipStream = [System.IO.File]::Open($outputFile, [System.IO.FileMode]::Create)
$zip = New-Object System.IO.Compression.ZipArchive($zipStream, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in $files) {
        $relative = $file.FullName.Substring($normalizedScriptDir.Length).TrimStart('\', '/') -replace '\\', '/'
        $entry = $zip.CreateEntry($relative, [System.IO.Compression.CompressionLevel]::Optimal)
        $entryStream = $entry.Open()
        $fileStream = [System.IO.File]::OpenRead($file.FullName)
        try {
            $fileStream.CopyTo($entryStream)
            $totalBytes += $file.Length
        } finally {
            $fileStream.Dispose()
            $entryStream.Dispose()
        }
        $fileCount++
    }
} finally {
    $zip.Dispose()
    $zipStream.Dispose()
}

$sizeKB = [math]::Round((Get-Item $outputFile).Length / 1KB)
$sourceKB = [math]::Round($totalBytes / 1KB)
Write-Host "[pack] Done: $fileCount files, source ${sourceKB} KB -> zip ${sizeKB} KB"
Write-Host "[pack] Output: $outputFile"
