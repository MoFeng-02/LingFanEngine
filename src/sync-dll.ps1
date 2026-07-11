# ==============================================================================
# sync-dll.ps1 — 引擎 DLL 自动同步脚本
#
# 用途：编译引擎三个项目（Release）并复制 DLL/PDB 到模板项目和 SDK 目录。
#       每次引擎源码修改后运行此脚本，确保模板/SDK 引用的是最新编译结果。
#
# 用法：
#   cd e:\Project\Engine
#   .\src\sync-dll.ps1
# ==============================================================================

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSCommandPath | Split-Path -Parent
Set-Location $root

Write-Host "=== 灵泛引擎 DLL 同步 ===" -ForegroundColor Cyan
Write-Host "工作目录: $root"
Write-Host ""

# 1. 编译三个引擎项目（Release 配置）
$projects = @(
    "src/EngineCore/LingFanEngine.Abstractions",
    "src/EngineCore/LingFanEngine.DslCore",
    "src/EngineCore/LingFanEngine"
)

Write-Host "[1/3] 编译引擎项目 (Release)..." -ForegroundColor Yellow
foreach ($proj in $projects) {
    Write-Host "  → dotnet build $proj"
    dotnet build $proj -c Release --nologo
    if ($LASTEXITCODE -ne 0) {
        Write-Host "  ✗ 编译失败: $proj" -ForegroundColor Red
        exit 1
    }
}
Write-Host "  ✓ 编译成功" -ForegroundColor Green
Write-Host ""

# 2. 复制 DLL 到目标目录
$srcDir = "src/EngineCore/LingFanEngine/bin/Release/net10.0"
$absDir = "src/EngineCore/LingFanEngine.Abstractions/bin/Release/net10.0"
$dslDir = "src/EngineCore/LingFanEngine.DslCore/bin/Release/net10.0"

$targets = @(
    "src/Template/V1/_LingFanEngineTemplateTitle_/DLL",
    "src/SDK_Toolkit/LingFanEngine.SDK/DLL"
)

$dlls = @(
    @{ Src = "$srcDir/LingFanEngine.dll"; Name = "LingFanEngine.dll" },
    @{ Src = "$srcDir/LingFanEngine.pdb"; Name = "LingFanEngine.pdb" },
    @{ Src = "$absDir/LingFanEngine.Abstractions.dll"; Name = "LingFanEngine.Abstractions.dll" },
    @{ Src = "$absDir/LingFanEngine.Abstractions.pdb"; Name = "LingFanEngine.Abstractions.pdb" },
    @{ Src = "$dslDir/LingFanEngine.DslCore.dll"; Name = "LingFanEngine.DslCore.dll" },
    @{ Src = "$dslDir/LingFanEngine.DslCore.pdb"; Name = "LingFanEngine.DslCore.pdb" }
)

Write-Host "[2/3] 复制 DLL 到目标目录..." -ForegroundColor Yellow
foreach ($target in $targets) {
    $targetPath = Join-Path $root $target
    if (!(Test-Path $targetPath)) {
        Write-Host "  ⚠ 目标目录不存在，跳过: $target" -ForegroundColor DarkYellow
        continue
    }
    Write-Host "  → $target"
    foreach ($dll in $dlls) {
        $srcPath = Join-Path $root $dll.Src
        $dstPath = Join-Path $targetPath $dll.Name
        if (Test-Path $srcPath) {
            Copy-Item $srcPath $dstPath -Force
            Write-Host "    ✓ $($dll.Name)" -ForegroundColor DarkGreen
        } else {
            Write-Host "    ⚠ 源文件不存在: $($dll.Src)" -ForegroundColor DarkYellow
        }
    }
}
Write-Host ""

# 3. 验证
Write-Host "[3/3] 验证 DLL 哈希..." -ForegroundColor Yellow
foreach ($target in $targets) {
    $targetPath = Join-Path $root $target
    if (!(Test-Path $targetPath)) { continue }
    Write-Host "  → $target"
    foreach ($dll in $dlls) {
        $srcPath = Join-Path $root $dll.Src
        $dstPath = Join-Path $targetPath $dll.Name
        if (!(Test-Path $srcPath) -or !(Test-Path $dstPath)) { continue }
        $srcHash = (Get-FileHash $srcPath -Algorithm MD5).Hash
        $dstHash = (Get-FileHash $dstPath -Algorithm MD5).Hash
        if ($srcHash -eq $dstHash) {
            Write-Host "    ✓ $($dll.Name) — 哈希匹配" -ForegroundColor DarkGreen
        } else {
            Write-Host "    ✗ $($dll.Name) — 哈希不匹配！" -ForegroundColor Red
        }
    }
}

Write-Host ""
Write-Host "=== DLL 同步完成 ===" -ForegroundColor Cyan
