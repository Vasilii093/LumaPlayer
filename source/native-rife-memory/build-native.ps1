param([Parameter(Mandatory=$true)][string]$Toolchain,
      [Parameter(Mandatory=$true)][string]$NcnnSource,
      [string]$BuildDirectory)
$ErrorActionPreference='Stop'
# Require a private, already patched dependency tree. Never modify a caller's
# dependency checkout or replace the running player's DLL as part of a build.
foreach($name in @('command.cpp','command.h')) {
    $actual=Join-Path $NcnnSource "src\$name"
    $expected=Join-Path $PSScriptRoot "ncnn-overrides\$name"
    if(-not (Test-Path -LiteralPath $actual) -or
       (Get-FileHash -LiteralPath $actual).Hash -ne (Get-FileHash -LiteralPath $expected).Hash) {
        throw "Use a private copy of the pinned ncnn tree with ncnn-overrides\$name copied into src."
    }
}
$app=(Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$runtime=Join-Path $app 'engine\runtime-trt'
$cmake=Join-Path $runtime 'Lib\site-packages\cmake\data\bin\cmake.exe'
$env:PATH=(Join-Path $Toolchain 'bin')+';'+(Join-Path $runtime 'Scripts')+';'+$env:PATH
if(-not $BuildDirectory){$BuildDirectory=Join-Path $PSScriptRoot 'build'}
$vs=Join-Path $app 'engine\runtime\Lib\site-packages\vapoursynth\include'
& $cmake -S $PSScriptRoot -B $BuildDirectory -G Ninja "-DCMAKE_C_COMPILER=$Toolchain/bin/clang.exe" "-DCMAKE_CXX_COMPILER=$Toolchain/bin/clang++.exe" -DCMAKE_BUILD_TYPE=Release "-DVS_INCLUDE=$vs" "-DNCNN_SOURCE=$NcnnSource"
if($LASTEXITCODE -ne 0){throw 'Native configure failed'}
& $cmake --build $BuildDirectory -j 8
if($LASTEXITCODE -ne 0){throw 'Native build failed'}
Write-Output (Join-Path $BuildDirectory 'librife-native.dll')
