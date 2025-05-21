#Requires -Version 7.4
[CmdletBinding()]
param()

# Only Powershell 7.4 or later
$PSNativeCommandUseErrorActionPreference = $true
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3.0

$ecdsa = [System.Security.Cryptography.ECDsa]::Create([System.Security.Cryptography.ECCurve]::CreateFromFriendlyName("secp256r1"))

@{
    PublicKey = [System.Convert]::ToBase64String($ecdsa.ExportSubjectPublicKeyInfo())
    PrivateKey = [System.Convert]::ToBase64String($ecdsa.ExportPkcs8PrivateKey())
}