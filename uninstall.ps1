#Requires -Version 5.1
[CmdletBinding()]
param(
    [switch]$KeepData,
    [switch]$Quiet
)

$ErrorActionPreference = 'Stop'
$product = 'PrivacyGuard'
$dest = Join-Path $env:ProgramFiles 'PrivacyGuard'
$data = Join-Path $env:ProgramData 'PrivacyGuard'

function Test-Admin {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

if (-not (Test-Admin)) {
    $quotedScript = '"' + $PSCommandPath + '"'
    $argLine = "-NoProfile -ExecutionPolicy Bypass -File $quotedScript"
    if ($KeepData) { $argLine += ' -KeepData' }
    if ($Quiet) { $argLine += ' -Quiet' }
    Start-Process -FilePath 'powershell.exe' -Verb RunAs -ArgumentList $argLine
    exit
}

if (-not $Quiet) { Write-Host '正在停止 PrivacyGuard...' -ForegroundColor Cyan }

Get-Process -Name $product -ErrorAction SilentlyContinue | ForEach-Object {
    try {
        if ($_.MainWindowHandle -ne 0) { Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue }
    } catch {
    }
}

if (Get-Service -Name $product -ErrorAction SilentlyContinue) {
    Stop-Service -Name $product -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 700
}

$exe = Join-Path $dest 'PrivacyGuard.exe'
if (Test-Path $exe) {
    $proc = Start-Process -FilePath $exe -ArgumentList '--uninstall', '--quiet' -Wait -PassThru
    if ($proc.ExitCode -ne 0 -and -not $Quiet) {
        Write-Warning "程序卸载器返回 $($proc.ExitCode)，将继续清理。"
    }
} else {
    # 程序文件损坏时尽量清理服务和任务
    sc.exe stop $product | Out-Null
    Start-Sleep -Milliseconds 500
    sc.exe delete $product | Out-Null
    try { Unregister-ScheduledTask -TaskName $product -Confirm:$false -ErrorAction SilentlyContinue } catch { }
}

try {
    Unregister-ScheduledTask -TaskName $product -Confirm:$false -ErrorAction SilentlyContinue
} catch {
}

$startMenu = Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs\PrivacyGuard 隐私卫士.lnk'
Remove-Item -Path $startMenu -Force -ErrorAction SilentlyContinue
$desktop = Join-Path ([Environment]::GetFolderPath('Desktop')) 'PrivacyGuard 隐私卫士.lnk'
Remove-Item -Path $desktop -Force -ErrorAction SilentlyContinue

if (-not $KeepData) {
    if (-not $Quiet) { Write-Host '删除配置、日志与策略备份...' }
    Remove-Item -Path $data -Recurse -Force -ErrorAction SilentlyContinue
}

if (-not $Quiet) {
    Write-Host '卸载完成。' -ForegroundColor Green
    if ($KeepData) { Write-Host "已保留数据目录：$data" -ForegroundColor Gray }
}

# 程序目录不能由当前进程直接删除干净，交给一个短暂的 cmd 进程。
if (Test-Path $dest) {
    $cleanup = Join-Path $env:TEMP ('pg-cleanup-' + [Guid]::NewGuid().ToString('N') + '.cmd')
    $content = '@echo off' + "`r`n" + 'timeout /t 2 /nobreak >nul' + "`r`n" +
               'rmdir /s /q "' + $dest + '"' + "`r`n" + 'del "%~f0"' + "`r`n"
    Set-Content -Path $cleanup -Value $content -Encoding ASCII
    Start-Process -FilePath 'cmd.exe' -ArgumentList '/c', ('"' + $cleanup + '"') -WindowStyle Hidden -WorkingDirectory $env:TEMP
}
