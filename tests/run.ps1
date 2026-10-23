#requires -Version 5.1
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$compiler = Get-Command csc.exe -ErrorAction Stop
$output = Join-Path $root 'bin\Tests'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$sources = @(Get-ChildItem -LiteralPath $root -Filter '*.cs' | ForEach-Object FullName)
$sources += Join-Path $root 'Properties\AssemblyInfo.cs'
$sources += Join-Path $PSScriptRoot 'SafetyTests.cs'
$exe = Join-Path $output 'SafetyTests.exe'
& $compiler.Source /nologo /target:exe /langversion:7.3 /main:DeleteDuplicates.SafetyTests "/out:$exe" $sources
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed' }
& $exe (Join-Path $output 'fixtures')
if ($LASTEXITCODE -ne 0) { throw 'Safety tests failed' }
