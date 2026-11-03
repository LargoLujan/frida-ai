<#
.SYNOPSIS
  Firma el ejecutable de TeamsJarvis con un certificado de firma de código autofirmado
  (gratuito). Crea el certificado la primera vez y lo reutiliza después.

.DESCRIPTION
  La firma autofirmada da INTEGRIDAD e IDENTIDAD (verifica que el binario no se ha
  modificado y quién lo firmó) y, si instalas el certificado en el almacén de
  "Editores de confianza" del equipo, elimina el aviso de "editor desconocido" EN ESE
  equipo. NO da reputación de SmartScreen para terceros: para eso hace falta un
  certificado comercial (OV/EV, de pago) o el servicio gratuito SignPath (solo OSS).

.EXAMPLE
  pwsh ./sign/sign.ps1                       # compila en Release, crea cert si falta y firma
  pwsh ./sign/sign.ps1 -InstallToTrusted     # además instala el cert como editor de confianza (requiere admin)
#>
param(
    [string]$Subject = "CN=TeamsJarvis (self-signed)",
    [string]$Configuration = "Release",
    [switch]$InstallToTrusted
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$proj = Join-Path $repo "src/TeamsJarvis/TeamsJarvis.csproj"

Write-Host "1) Compilando en $Configuration..." -ForegroundColor Cyan
dotnet build $proj -c $Configuration | Out-Null

$exe = Get-ChildItem -Path (Join-Path $repo "src/TeamsJarvis/bin/$Configuration") -Recurse -Filter "TeamsJarvis.exe" |
    Select-Object -First 1 -ExpandProperty FullName
if (-not $exe) { throw "No se encontró TeamsJarvis.exe. ¿Compiló bien?" }

Write-Host "2) Buscando/creando certificado de firma..." -ForegroundColor Cyan
$cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq $Subject -and $_.HasPrivateKey } | Select-Object -First 1
if (-not $cert) {
    $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject $Subject `
        -CertStoreLocation Cert:\CurrentUser\My -KeyUsage DigitalSignature `
        -NotAfter (Get-Date).AddYears(5)
    Write-Host "   Certificado creado (huella $($cert.Thumbprint))." -ForegroundColor Green
} else {
    Write-Host "   Reutilizando certificado existente (huella $($cert.Thumbprint))." -ForegroundColor Green
}

if ($InstallToTrusted) {
    Write-Host "3) Instalando el certificado como Editor de confianza (puede pedir permisos de admin)..." -ForegroundColor Cyan
    Export-Certificate -Cert $cert -FilePath "$env:TEMP\teamsjarvis-cert.cer" | Out-Null
    Import-Certificate -FilePath "$env:TEMP\teamsjarvis-cert.cer" -CertStoreLocation Cert:\LocalMachine\TrustedPublisher | Out-Null
    Import-Certificate -FilePath "$env:TEMP\teamsjarvis-cert.cer" -CertStoreLocation Cert:\LocalMachine\Root | Out-Null
}

Write-Host "4) Firmando $exe ..." -ForegroundColor Cyan
Set-AuthenticodeSignature -FilePath $exe -Certificate $cert `
    -TimestampServer "http://timestamp.digicert.com" -HashAlgorithm SHA256 | Format-List Status, StatusMessage

Write-Host "Listo. Firma verificada:" -ForegroundColor Green
(Get-AuthenticodeSignature $exe) | Format-List Status, SignerCertificate
