#requires -Version 5.1
$ErrorActionPreference = 'Stop'
$compiler = Get-Command csc.exe -ErrorAction Stop
$output = Join-Path $PSScriptRoot 'bin\Release'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$sources = @(Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' | ForEach-Object FullName)
$sources += Join-Path $PSScriptRoot 'Properties\AssemblyInfo.cs'
& $compiler.Source /nologo /target:winexe /platform:anycpu /optimize+ /langversion:7.3 "/win32manifest:$PSScriptRoot\app.manifest" "/out:$output\DeleteDuplicates.exe" $sources
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
Write-Output 'Built bin\Release\DeleteDuplicates.exe'
