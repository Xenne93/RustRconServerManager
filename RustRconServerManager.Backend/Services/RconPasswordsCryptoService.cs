using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;


// Encryption service for RCON Passwords and Host addresses using AES-256-GCM.
// AES-GCM provides authenticated encryption, ensuring both confidentiality and integrity.
// Stores results in JSON format with nonce, ciphertext, and authentication tag.


public interface IRconPasswordsCryptoService
{
    string Encrypt(string plainText);
    string Decrypt(string encryptedJson);
}

public class RconPasswordsCryptoService : IRconPasswordsCryptoService
{
    private readonly byte[] _key;
    private const int NonceSize = 12;  // 96 bits - recommended for GCM
    private const int TagSize = 16;    // 128 bits - maximum security

    public RconPasswordsCryptoService(IConfiguration config)
    {
        var keyString = config["RconEncryption:Key"];
        if (string.IsNullOrWhiteSpace(keyString) || keyString.Length != 32)
            throw new ArgumentException("RconEncryption:Key ontbreekt of is ongeldig (32 tekens vereist).");

        _key = Encoding.UTF8.GetBytes(keyString);
    }

    public string Encrypt(string plainText)
    {
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var nonce = new byte[NonceSize];
        var tag = new byte[TagSize];
        var cipherBytes = new byte[plainBytes.Length];

        // Generate cryptographically secure random nonce
        RandomNumberGenerator.Fill(nonce);

        using var aesGcm = new AesGcm(_key, TagSize);
        aesGcm.Encrypt(nonce, plainBytes, cipherBytes, tag);

        var result = new EncryptedPayload
        {
            nonce = Convert.ToBase64String(nonce),
            data = Convert.ToBase64String(cipherBytes),
            tag = Convert.ToBase64String(tag)
        };

        return JsonSerializer.Serialize(result);
    }

    public string Decrypt(string encryptedJson)
    {
        var payload = JsonSerializer.Deserialize<EncryptedPayload>(encryptedJson);

        if (payload == null || string.IsNullOrWhiteSpace(payload.data))
            throw new InvalidOperationException("Ongeldige versleutelde data.");

        // Check if this is old CBC format (has 'iv' but no 'tag') for backward compatibility
        if (!string.IsNullOrWhiteSpace(payload.iv) && string.IsNullOrWhiteSpace(payload.tag))
        {
            return DecryptLegacyCbc(payload);
        }

        // GCM decryption
        if (string.IsNullOrWhiteSpace(payload.nonce) || string.IsNullOrWhiteSpace(payload.tag))
            throw new InvalidOperationException("Ongeldige versleutelde data (GCM velden ontbreken).");

        var nonce = Convert.FromBase64String(payload.nonce);
        var cipherBytes = Convert.FromBase64String(payload.data);
        var tag = Convert.FromBase64String(payload.tag);
        var plainBytes = new byte[cipherBytes.Length];

        using var aesGcm = new AesGcm(_key, TagSize);
        aesGcm.Decrypt(nonce, cipherBytes, tag, plainBytes);

        return Encoding.UTF8.GetString(plainBytes);
    }

    /// <summary>
    /// Backward compatibility: Decrypt data encrypted with old AES-CBC format
    /// </summary>
    private string DecryptLegacyCbc(EncryptedPayload payload)
    {
        using var aes = Aes.Create();
        aes.Key = _key;
        aes.IV = Convert.FromBase64String(payload.iv!);

        using var decryptor = aes.CreateDecryptor();
        var cipherBytes = Convert.FromBase64String(payload.data);
        var plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);

        return Encoding.UTF8.GetString(plainBytes);
    }

    private class EncryptedPayload
    {
        // GCM fields
        public string? nonce { get; set; }
        public string data { get; set; } = "";
        public string? tag { get; set; }

        // Legacy CBC field (for backward compatibility)
        public string? iv { get; set; }
    }
}
