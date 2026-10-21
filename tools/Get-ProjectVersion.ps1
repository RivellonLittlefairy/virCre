#requires -Version 7.0
param([string]$Version)
$ErrorActionPreference='Stop'
if([string]::IsNullOrWhiteSpace($Version)) {
    $Version=[IO.File]::ReadAllText((Join-Path (Split-Path $PSScriptRoot -Parent) 'VERSION')).Trim()
}
if($Version -notmatch '^\d+\.\d+(?:\.\d+)?$') {throw 'Invalid project version. Use major.minor.patch in VERSION, or pass a numeric -Version override.'}
Write-Output $Version
