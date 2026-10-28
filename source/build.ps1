param([string]$OutputName='LumaPlayer.exe',[switch]$NativeRife=$true,[switch]$RifeThroughput=$true)
$ErrorActionPreference = 'Stop'
if(!$NativeRife -or !$RifeThroughput) { throw 'This portable package requires NativeRife and RifeThroughput' }
if([IO.Path]::GetFileName($OutputName) -ne $OutputName -or $OutputName -notlike '*.exe') { throw 'OutputName must be an exe filename' }
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$root = Split-Path $PSScriptRoot -Parent
[string[]]$defines=@()
if($RifeThroughput){$defines += "/define:LUMA_RIFE_THROUGHPUT"}
if($NativeRife){$defines += "/define:LUMA_RIFE_NATIVE"}
& $compiler @defines /nologo /target:winexe /platform:x64 /optimize+ /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll /out:"$root\$OutputName" "$PSScriptRoot\LumaPlayer.cs" "$PSScriptRoot\CacheMaintenance.cs" "$PSScriptRoot\PreRenderSession.cs"
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed' }
