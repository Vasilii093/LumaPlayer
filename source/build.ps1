param([string]$OutputName='LumaPlayer.exe')
$ErrorActionPreference = 'Stop'
if([IO.Path]::GetFileName($OutputName) -ne $OutputName -or $OutputName -notlike '*.exe') { throw 'OutputName must be an exe filename' }
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$root = Split-Path $PSScriptRoot -Parent
& $compiler /nologo /target:winexe /win32icon:"$PSScriptRoot\LumaPlayer.ico" /platform:x64 /optimize+ /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll /out:"$root\$OutputName" "$PSScriptRoot\LumaPlayer.cs" "$PSScriptRoot\CacheMaintenance.cs" "$PSScriptRoot\PreRenderSession.cs"
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed' }
