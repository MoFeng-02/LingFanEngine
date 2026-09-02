# pack-template.ps1 — 打包模板目录为 ZIP（排除 bin/obj/.vs/.git）
# 跨平台：PowerShell 7（pwsh）在 Windows / Linux / macOS 均可用。
# 用法: pwsh -File pack-template.ps1 -SourceDir "path/to/V1" -OutputFile "path/to/template.zip"
param(
    [Parameter(Mandatory=$true)]
    [string]$SourceDir,
    [Parameter(Mandatory=$true)]
    [string]$OutputFile
)

# 跨平台路径分隔符归一（MSBuild 在 Linux/macOS 传入的 $(TemplateSourceDir) 可能含反斜杠）
$SourceDir  = $SourceDir -replace '\\', '/'
$OutputFile = $OutputFile -replace '\\', '/'

$excludeDirs = @('bin', 'obj', '.vs', '.git')

# 确保输出目录存在
$outputDir = Split-Path $OutputFile -Parent
if ($outputDir -and !(Test-Path $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
}

# 删除旧 ZIP
if (Test-Path $OutputFile) {
    Remove-Item $OutputFile -Force
}

# 跨平台临时目录：Windows $env:TEMP / Linux·macOS $env:TMPDIR 可能缺失，
# 用 [System.IO.Path]::GetTempPath() 兜底（三平台均返回合法临时根）
$tempBase = if ($env:TEMP) { $env:TEMP }
            elseif ($env:TMPDIR) { $env:TMPDIR }
            else { [System.IO.Path]::GetTempPath() }
$tempDir = Join-Path $tempBase "lingfan_template_$(Get-Random)"

try {
    # 解析源目录（路径已归一为前置斜杠，跨平台可解析；-ErrorAction Stop 确保不存在时明确报错）
    $sourcePath = Resolve-Path $SourceDir -ErrorAction Stop
    $sourceRoot = $sourcePath.Path

    Get-ChildItem -Path $sourceRoot -Recurse | ForEach-Object {
        $relativePath = $_.FullName.Substring($sourceRoot.Length).TrimStart('\', '/')

        # 检查是否在排除目录中
        $shouldExclude = $false
        foreach ($excluded in $excludeDirs) {
            if ($relativePath -like "$excluded\*" -or $relativePath -like "$excluded/*" -or $relativePath -eq $excluded) {
                $shouldExclude = $true
                break
            }
        }

        if (-not $shouldExclude -and $relativePath) {
            $destPath = Join-Path $tempDir $relativePath
            if ($_.PSIsContainer) {
                New-Item -ItemType Directory -Path $destPath -Force | Out-Null
            } else {
                $destParent = Split-Path $destPath -Parent
                if (!(Test-Path $destParent)) {
                    New-Item -ItemType Directory -Path $destParent -Force | Out-Null
                }
                Copy-Item $_.FullName $destPath -Force
            }
        }
    }

    # 打包 ZIP
    Compress-Archive -Path (Join-Path $tempDir '*') -DestinationPath $OutputFile -Force
    Write-Host "[pack-template] Created: $OutputFile"
} finally {
    # 清理临时目录
    if (Test-Path $tempDir) {
        Remove-Item $tempDir -Recurse -Force
    }
}
