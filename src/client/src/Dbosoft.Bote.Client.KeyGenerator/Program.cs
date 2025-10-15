using Dbosoft.Bote.Client.KeyGenerator;

var keyInfo = new KeyGenerator().GenerateKey();

// !!! Work in progress !!!
// Use ./samples/New-KeyPair.ps1 for now

Console.WriteLine("Key pair for Bote Client:");
Console.WriteLine($"Key ID: {keyInfo.KeyId}");
Console.WriteLine($"Public Key: {keyInfo.PublicKey}");
Console.WriteLine($"Private Key: {keyInfo.PrivateKey}");
