# Builds DnDRoller.exe with the C# compiler that ships with Windows (.NET Framework 4), runs its self-test,
# then puts it at the project root and keeps a copy in Versions\Archive. Refuses to rebuild an archived version.
# -Test builds and self-tests into build\ only, for trying changes without archiving anything.
param([switch]$Test)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$src = Join-Path $root 'DnDRoller.cs'

$m = [regex]::Match((Get-Content $src -Raw), 'const string Version = "(\d+\.\d+\.\d+)"')
if (-not $m.Success) { throw 'Could not find the Version line in DnDRoller.cs.' }
$version = $m.Groups[1].Value
$archive = Join-Path $root "Versions\Archive\DnDRoller-$version.exe"
if ((Test-Path $archive) -and -not $Test) { throw "Version $version is already built and archived. Raise Version in DnDRoller.cs first." }

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
$build = Join-Path $root 'build'
New-Item -ItemType Directory -Force $build | Out-Null
$exe = Join-Path $build 'DnDRoller.exe'

& $csc /nologo /target:winexe /optimize+ /codepage:65001 "/out:$exe" "/win32icon:$(Join-Path $root 'app.ico')" "/win32manifest:$(Join-Path $root 'app.manifest')" /r:System.Windows.Forms.dll /r:System.Drawing.dll $src
if ($LASTEXITCODE -ne 0) { throw 'Compile failed.' }

$err = Join-Path $build 'selftest.txt'
$p = Start-Process $exe -ArgumentList '--selftest' -Wait -PassThru -NoNewWindow -RedirectStandardError $err
if ($p.ExitCode -ne 0) { Get-Content $err; throw 'Self-test failed.' }
if ($Test) { Write-Host "Test build (self-test passed): $exe" -ForegroundColor Green; return }

Copy-Item $exe (Join-Path $root 'DnDRoller.exe') -Force
New-Item -ItemType Directory -Force (Split-Path $archive) | Out-Null
Copy-Item $exe $archive
Write-Host "Built DnDRoller $version (self-test passed): $archive" -ForegroundColor Green
