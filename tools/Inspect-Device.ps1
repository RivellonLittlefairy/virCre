#requires -Version 7.0
param([Parameter(Mandatory)][string]$Serial, [string]$Adb = 'adb')
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Device.ps1')
Assert-P0Device | ConvertTo-Json
foreach ($service in @('virtualdevice','virtualdevice_native','virtual_camera')) {
    Invoke-DeviceAdb -AdbArgs @('shell','service','check',$service)
}
Invoke-DeviceAdb -AdbArgs @('shell','cmd','companiondevice','help')
Invoke-DeviceAdb -AdbArgs @('shell','dumpsys','virtualdevice')
