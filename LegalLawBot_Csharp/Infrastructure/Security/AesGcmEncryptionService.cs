using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace LegalLawBot_Csharp.Infrastructure.Security
{
    public interface IEncryptionService
    {
        (string EncryptedBase64, string IvBase64) Encrypt(string plainText);
        string Decrypt(string encryptedBase64, string ivBase64);
    }

    public class AesGcmEncryptionService : IEncryptionService
    {
        private readonly byte[] _masterKey;

        public AesGcmEncryptionService(IConfiguration configuration)
        {
            // Pobiera Master Key (musi mieć 32 bajty / 256 bitów po zdekodowaniu lub konwersji)
            string keySecret = configuration["MasterEncryptionKey"]
                               ?? Environment.GetEnvironmentVariable("MASTER_ENCRYPTION_KEY")
                               ?? throw new InvalidOperationException("Brak klucza MasterEncryptionKey w konfiguracji!");

            // Dba o to, aby klucz miał dokładnie 32 bajty (256 bitów)
            using var sha256 = SHA256.Create();
            _masterKey = sha256.ComputeHash(Encoding.UTF8.GetBytes(keySecret));
        }

        public (string EncryptedBase64, string IvBase64) Encrypt(string plainText)
        {
            if (string.IsNullOrWhiteSpace(plainText))
                throw new ArgumentException("Tekst do zaszyfrowania nie może być pusty.", nameof(plainText));

            byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);

            // GCM wymaga IV / Nonce o długości dokładnie 12 bajtów (96 bitów)
            byte[] iv = new byte[12];
            RandomNumberGenerator.Fill(iv);

            byte[] cipherBytes = new byte[plainBytes.Length];
            byte[] tag = new byte[16]; // Tag uwierzytelniający AEAD (128 bitów)

            using (var aesGcm = new AesGcm(_masterKey, tag.Length))
            {
                aesGcm.Encrypt(iv, plainBytes, cipherBytes, tag);
            }

            // Łączy szyfrogram z tagiem (CipherBytes + Tag), aby łatwo przechowywać je w jednej kolumnie
            byte[] encryptedWithTag = new byte[cipherBytes.Length + tag.Length];
            Buffer.BlockCopy(cipherBytes, 0, encryptedWithTag, 0, cipherBytes.Length);
            Buffer.BlockCopy(tag, 0, encryptedWithTag, cipherBytes.Length, tag.Length);

            return (
                EncryptedBase64: Convert.ToBase64String(encryptedWithTag),
                IvBase64: Convert.ToBase64String(iv)
            );
        }

        public string Decrypt(string encryptedBase64, string ivBase64)
        {
            if (string.IsNullOrWhiteSpace(encryptedBase64) || string.IsNullOrWhiteSpace(ivBase64))
                throw new ArgumentException("Szyfrogram i IV nie mogą być puste.");

            byte[] encryptedWithTag = Convert.FromBase64String(encryptedBase64);
            byte[] iv = Convert.FromBase64String(ivBase64);

            int tagLength = 16;
            int cipherLength = encryptedWithTag.Length - tagLength;

            if (cipherLength < 0)
                throw new CryptographicException("Nieprawidłowy format szyfrogramu.");

            byte[] cipherBytes = new byte[cipherLength];
            byte[] tag = new byte[tagLength];

            Buffer.BlockCopy(encryptedWithTag, 0, cipherBytes, 0, cipherLength);
            Buffer.BlockCopy(encryptedWithTag, cipherLength, tag, 0, tagLength);

            byte[] plainBytes = new byte[cipherLength];

            using (var aesGcm = new AesGcm(_masterKey, tag.Length))
            {
                aesGcm.Decrypt(iv, cipherBytes, tag, plainBytes);
            }

            return Encoding.UTF8.GetString(plainBytes);
        }
    }
}