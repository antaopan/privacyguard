#Requires -Version 5.1
[CmdletBinding()]
param(
    [switch]$NoStart,
    [switch]$Quiet,
    [switch]$DesktopShortcut
)

$ErrorActionPreference = 'Stop'
$product = 'PrivacyGuard'
$dest = Join-Path $env:ProgramFiles 'PrivacyGuard'

function Test-Admin {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

if (-not (Test-Admin)) {
    $quotedScript = '"' + $PSCommandPath + '"'
    $argLine = "-NoProfile -ExecutionPolicy Bypass -File $quotedScript"
    if ($NoStart) { $argLine += ' -NoStart' }
    if ($Quiet) { $argLine += ' -Quiet' }
    if ($DesktopShortcut) { $argLine += ' -DesktopShortcut' }
    Start-Process -FilePath 'powershell.exe' -Verb RunAs -ArgumentList $argLine
    exit
}

if (-not $Quiet) {
    Write-Host ''
    Write-Host '  PrivacyGuard 隐私卫士 - 安装程序' -ForegroundColor Cyan
    Write-Host '  ---------------------------------' -ForegroundColor DarkGray
    Write-Host ''
}

$service = Get-Service -Name $product -ErrorAction SilentlyContinue
if ($service -and $service.Status -ne 'Stopped') {
    if (-not $Quiet) { Write-Host '停止旧版后台服务...' }
    Stop-Service -Name $product -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 700
}

Get-Process -Name $product -ErrorAction SilentlyContinue | ForEach-Object {
    try {
        if ($_.MainWindowHandle -ne 0 -or $_.Path -eq (Join-Path $dest 'PrivacyGuard.exe')) {
            Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue
        }
    } catch {
    }
}

New-Item -ItemType Directory -Force -Path $dest | Out-Null

$source = (Resolve-Path $PSScriptRoot).Path
$target = (Resolve-Path $dest).Path
if ($source -ne $target) {
    if (-not $Quiet) { Write-Host "复制程序文件到 $dest ..." }
    Copy-Item -Path (Join-Path $PSScriptRoot '*') -Destination $dest -Recurse -Force
}

Get-ChildItem -Path $dest -File | Unblock-File -ErrorAction SilentlyContinue

if (-not $Quiet) { Write-Host '注册后台服务、开机自启与登录启动任务...' }
$installer = Join-Path $dest 'PrivacyGuard.exe'
$proc = Start-Process -FilePath $installer -ArgumentList '--install', '--quiet' -Wait -PassThru
if ($proc.ExitCode -ne 0) {
    throw "后台服务安装失败（退出码 $($proc.ExitCode)）。请检查是否以管理员身份运行，并查看 $env:ProgramData\PrivacyGuard\logs。"
}

if ($DesktopShortcut) {
    try {
        $shell = New-Object -ComObject WScript.Shell
        $link = $shell.CreateShortcut((Join-Path ([Environment]::GetFolderPath('Desktop')) 'PrivacyGuard 隐私卫士.lnk'))
        $link.TargetPath = $installer
        $link.WorkingDirectory = $dest
        $link.IconLocation = "$installer,0"
        $link.Description = 'PrivacyGuard 隐私卫士'
        $link.Save()
    } catch {
        Write-Warning "桌面快捷方式创建失败：$($_.Exception.Message)"
    }
}

if (-not $NoStart) {
    Start-Process -FilePath $installer -WorkingDirectory $dest
}

if (-not $Quiet) {
    Write-Host ''
    Write-Host '安装完成。' -ForegroundColor Green
    Write-Host '后台服务已注册为自动启动；界面会在登录时以 --minimized 启动。' -ForegroundColor Gray
    Write-Host '首次使用请添加需要隔离的 AI 应用，并选择网络策略。' -ForegroundColor Gray
    Write-Host ''
}
