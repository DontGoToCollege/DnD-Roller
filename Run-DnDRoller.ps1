$exe = Join-Path $PSScriptRoot 'DnDRoller.exe'
if (-not (Test-Path $exe)) { & (Join-Path $PSScriptRoot 'Build-DnDRoller.ps1') }
Start-Process -FilePath $exe
