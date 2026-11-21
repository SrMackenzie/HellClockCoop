# Compila HellClockCoop.dll no Windows (equivalente ao build.sh), sem NuGet.
# Uso: scripts\build.ps1 [-Jogo "C:\Program Files (x86)\Steam\steamapps\common\Hell Clock"] [-Instalar]
param(
    [string]$Jogo = "C:\Program Files (x86)\Steam\steamapps\common\Hell Clock",
    [switch]$Instalar
)
$ErrorActionPreference = "Stop"
$raiz = Split-Path -Parent $PSScriptRoot
$managed = Join-Path $Jogo "Hell Clock_Data\Managed"
$core = Join-Path $Jogo "BepInEx\core"
$sdk = Split-Path -Parent (Get-Command dotnet).Source
$csc = Get-ChildItem (Join-Path $sdk "sdk") -Recurse -Filter csc.dll | Select-Object -First 1
if (-not $csc) { throw "csc.dll não encontrado: instale o .NET SDK 8 ou superior." }
New-Item -ItemType Directory -Force (Join-Path $raiz "build") | Out-Null
$refs = Get-ChildItem $managed -Filter *.dll | ForEach-Object { "-r:$($_.FullName)" }
Push-Location $raiz
try {
    & dotnet $csc.FullName -nologo -noconfig -nostdlib+ -target:library -langversion:9 -optimize+ -deterministic `
        -nowarn:1701,1702 -out:build/HellClockCoop.dll "-recurse:src/HellClockCoop/*.cs" @refs `
        "-r:$core\BepInEx.dll" "-r:$core\0Harmony.dll"
    if ($LASTEXITCODE -ne 0) { throw "Falha na compilação." }
    Write-Host "OK: build/HellClockCoop.dll"
    if ($Instalar) {
        $destino = Join-Path $Jogo "BepInEx\plugins\HellClockCoop"
        New-Item -ItemType Directory -Force $destino | Out-Null
        Copy-Item build/HellClockCoop.dll $destino -Force
        Write-Host "Instalado em $destino"
    }
} finally { Pop-Location }
