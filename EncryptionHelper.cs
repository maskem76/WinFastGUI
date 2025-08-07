using System;
using System.Security.Cryptography;
using System.Text;

namespace WinFastGUI
{
    public class EncryptionHelper
    {
        private readonly byte[] Key;
        private readonly byte[] IV;

        public EncryptionHelper()
        {
            var guid1 = Guid.NewGuid().ToByteArray();
            var guid2 = Guid.NewGuid().ToByteArray();
            Key = guid1.Concat(guid2).Take(32).ToArray();
            IV = Guid.NewGuid().ToByteArray().Take(12).ToArray();
        }

        public string EncryptCommand(string command)
        {
            byte[] plaintext = Encoding.UTF8.GetBytes(command);
            byte[] ciphertext = new byte[plaintext.Length];
            byte[] tag = new byte[16];

            using (AesGcm aesGcm = new AesGcm(Key, tag.Length))
            {
                aesGcm.Encrypt(IV, plaintext, ciphertext, tag);
            }

            return Convert.ToBase64String(ciphertext) + "|" + Convert.ToBase64String(tag);
        }

        public string DecryptCommand(string encrypted)
        {
            var parts = encrypted.Split('|');
            if (parts.Length != 2) throw new Exception("Şifreleme hatası.");
            byte[] ciphertext = Convert.FromBase64String(parts[0]);
            byte[] tag = Convert.FromBase64String(parts[1]);
            byte[] plaintext = new byte[ciphertext.Length];

            using (AesGcm aesGcm = new AesGcm(Key, tag.Length))
            {
                aesGcm.Decrypt(IV, ciphertext, tag, plaintext);
            }

            return Encoding.UTF8.GetString(plaintext);
        }
    }
}