# pack-template.ps1 — 打包模板目录为 ZIP（排除 bin/obj/.vs/.git）
# 用法: powershell -File pack-template.ps1 -SourceDir "path/to/V1" -OutputFile "path/to/template.zip"
param(
    [Parameter(Mandatory=$true)]
    [string]$SourceDir,
    [Parameter(Mandatory=$true)]
    [string]$OutputFile
)

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

# 创建临时目录用于打包（排除 bin/obj 等）
$tempDir = Join-Path $env:TEMP "lingfan_template_$(Get-Random)"
try {
    # 复制目录（排除指定子目录）
    $sourcePath = Resolve-Path $SourceDir -ErrorAction Stop

    Get-ChildItem -Path $sourcePath.Path -Recurse | ForEach-Object {
        $relativePath = $_.FullName.Substring($sourcePath.Path.Length).TrimStart('\', '/')

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
