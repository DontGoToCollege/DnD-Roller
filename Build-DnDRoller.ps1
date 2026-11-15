# Builds a portable Windows executable using the .NET Framework compiler built into Windows PowerShell.
$ErrorActionPreference = 'Stop'
$source = Join-Path $PSScriptRoot 'DnDRoller.cs'
$output = Join-Path $PSScriptRoot 'DnDRoller.exe'
if (-not (Test-Path $source)) { throw "Source file not found: $source" }
Add-Type -Path $source -OutputAssembly $output -OutputType WindowsApplication -ReferencedAssemblies @('System.Windows.Forms.dll','System.Drawing.dll')
Write-Host "Built: $output" -ForegroundColor Green
Write-Host 'Run DnDRoller.exe. It is portable and works offline.'
