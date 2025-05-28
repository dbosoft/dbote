// See https://aka.ms/new-console-template for more information

using System.ComponentModel.DataAnnotations;
using Microsoft.IdentityModel.Tokens;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

Console.WriteLine("Hello, World!");


using var ecdsa = ECDsa.Create(ECCurve.CreateFromFriendlyName("secp256r1"));

var ecdsaPublickey = ECDsa.Create();
ecdsaPublickey.ImportSubjectPublicKeyInfo(ecdsa.ExportSubjectPublicKeyInfo(), out _);

var securityKey = new ECDsaSecurityKey(ecdsa);
var securityPublicKey = new ECDsaSecurityKey(ecdsaPublickey);

var jsonKey = JsonWebKeyConverter.ConvertFromECDsaSecurityKey(securityKey);
var publicJsonWebKey = JsonWebKeyConverter.ConvertFromECDsaSecurityKey(securityPublicKey);

/*
var privateJson = Js

securityKey.PrivateKeyStatus
var jsonWebKey = JsonWebKeyConverter.ConvertFromECDsaSecurityKey(securityKey);

jsonWebKey.

signingCredentials.
securityKey.
var signingKey = 

    @{
    PublicKey = [System.Convert]::ToBase64String($ecdsa.ExportSubjectPublicKeyInfo())
    PrivateKey = [System.Convert]::ToBase64String($ecdsa.ExportPkcs8PrivateKey())
}
*/