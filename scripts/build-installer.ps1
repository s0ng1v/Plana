param(
    [string]$Version = "0.1.13"
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot
$DistDir = Join-Path $RepoRoot "dist"
$StageDir = Join-Path $DistDir "stage"
$IssPath = Join-Path $RepoRoot "installer\Plana.iss"

# 查找 Inno Setup 编译器 ISCC.exe
$isccCandidates = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe"
)
$iscc = $isccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) {
    $cmd = Get-Command iscc -ErrorAction SilentlyContinue
    if ($cmd) { $iscc = $cmd.Source }
}
if (-not $iscc) {
    throw "未找到 Inno Setup 6 (ISCC.exe)，请先安装 Inno Setup 6。"
}

Write-Host "==> 清理临时发布目录: $StageDir"
if (Test-Path $StageDir) {
    Remove-Item $StageDir -Recurse -Force
}
New-Item -ItemType Directory -Path $StageDir -Force | Out-Null

Write-Host "==> 发布 Plana 主程序 (win-x64 self-contained, v$Version)..."
dotnet publish (Join-Path $RepoRoot "src\VGList.Desktop\VGList.Desktop.csproj") `
    -c Release `
    -f net10.0-windows `
    -r win-x64 `
    --self-contained true `
    -o $StageDir `
    -p:Version=$Version `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -nr:false -m:1
if ($LASTEXITCODE -ne 0) { throw "VGList.Desktop publish 失败" }

Write-Host "==> 发布 Plana MCP 伴生代理 (win-x64 self-contained, 共享运行时)..."
dotnet publish (Join-Path $RepoRoot "src\VGList.Mcp\VGList.Mcp.csproj") `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -o $StageDir `
    -p:Version=$Version `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -nr:false -m:1
if ($LASTEXITCODE -ne 0) { throw "VGList.Mcp publish 失败" }

# 移除开发期调试符号（若有残留）
Get-ChildItem $StageDir -Filter "*.pdb" -Recurse | Remove-Item -Force -ErrorAction SilentlyContinue

Write-Host "==> 使用 Inno Setup 编译安装程序..."
& $iscc "/DMyAppVersion=$Version" "/DStageDir=$StageDir" $IssPath
if ($LASTEXITCODE -ne 0) { throw "ISCC 编译失败" }

$outExe = Join-Path $DistDir "Plana-Setup-$Version.exe"
if (-not (Test-Path $outExe)) {
    throw "未生成预期安装包: $outExe"
}

$sizeMb = [Math]::Round((Get-Item $outExe).Length / 1MB, 2)
Write-Host "==> 打包完成: $outExe ($sizeMb MB)"
