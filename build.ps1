#Requires -Version 5.1
[CmdletBinding()]
param(
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$version = '1.1.0'
$distRoot = Join-Path $root 'dist'
$packageDir = Join-Path $distRoot 'PrivacyGuard'
$zipPath = Join-Path $distRoot ("PrivacyGuard-{0}-win-x64.zip" -f $version)

Write-Host '构建 PrivacyGuard...' -ForegroundColor Cyan
$msbuild = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe'
if (-not (Test-Path $msbuild)) {
    throw '找不到 .NET Framework MSBuild（4.0.30319）。请安装 .NET Framework 4.8。'
}

& $msbuild (Join-Path $root 'PrivacyGuard.csproj') "/p:Configuration=$Configuration" /v:minimal
if ($LASTEXITCODE -ne 0) {
    throw "构建失败，MSBuild 退出码 $LASTEXITCODE"
}

$exe = Join-Path $root ("bin\{0}\PrivacyGuard.exe" -f $Configuration)
if (-not (Test-Path $exe)) {
    throw "找不到构建产物：$exe"
}

if (Test-Path $packageDir) {
    Remove-Item -Path $packageDir -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $packageDir | Out-Null

Copy-Item -Path $exe -Destination $packageDir -Force
foreach ($name in @('README.md', 'VERIFICATION.md', 'CHANGELOG.md', 'LICENSE', 'install.ps1', 'uninstall.ps1')) {
    Copy-Item -Path (Join-Path $root $name) -Destination $packageDir -Force
}

# 发布包内不放测试工具和源码。
Get-ChildItem -Path $packageDir -File | Unblock-File -ErrorAction SilentlyContinue

if (Test-Path $zipPath) {
    Remove-Item -Path $zipPath -Force
}
Compress-Archive -Path (Join-Path $packageDir '*') -DestinationPath $zipPath -CompressionLevel Optimal

$sha = Join-Path $distRoot 'SHA256SUMS.txt'
$lines = @()
$lines += ((Get-FileHash -Path (Join-Path $packageDir 'PrivacyGuard.exe') -Algorithm SHA256).Hash.ToLowerInvariant() + '  PrivacyGuard/PrivacyGuard.exe')
$lines += ((Get-FileHash -Path $zipPath -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + (Split-Path $zipPath -Leaf))
Set-Content -Path $sha -Value $lines -Encoding ASCII

Write-Host ''
Write-Host '构建完成：' -ForegroundColor Green
Write-Host "  目录: $packageDir"
Write-Host "  压缩包: $zipPath"
Write-Host "  校验和: $sha"
Write-Host ''
Get-Content $sha

