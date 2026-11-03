# Publica Frida.ai (self-contained, win-x64) en installer\publish para empaquetarla.
# Uso:  pwsh installer\publish.ps1
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $PSScriptRoot 'publish'

if (Test-Path $out) { Remove-Item $out -Recurse -Force }

dotnet publish (Join-Path $root 'src\TeamsJarvis\TeamsJarvis.csproj') `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=false -o $out

Write-Host "Publicado en: $out"
Write-Host "Ahora: coloca los instaladores en installer\redist\ y compila con 'iscc installer\frida-setup.iss'."
