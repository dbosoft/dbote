using System.Reflection;
using Microsoft.IdentityModel.Tokens;
using System.Security.Cryptography;
using System.Text;
using SuperBus.Connector.KeyGenerator;

var keyInfo = new KeyGenerator().GenerateKey();

Console.WriteLine("Key pair for SuperBus connector:");
Console.WriteLine($"Key ID: {keyInfo.KeyId}");
Console.WriteLine($"Public Key: {keyInfo.PublicKey}");
Console.WriteLine($"Private Key: {keyInfo.PrivateKey}");
