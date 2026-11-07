using System;
using System.Security.Cryptography;
using System.Text;

namespace ToolLicensing
{
    /// <summary>
    /// Encrypts and decrypts a blob - a lookup table, a rules file, a core data set - under a
    /// 32-byte key. AES-256-CBC with a random IV, then HMAC-SHA256 over IV and ciphertext, checked
    /// before anything is decrypted, so a blob that was changed is refused rather than decrypted
    /// into garbage.
    ///
    /// This is what makes a license worth more than a check. Embed the product's data encrypted,
    /// open it with the key a valid license carries, and a copy of the DLL with the license check
    /// patched out still has nothing to run on.
    /// </summary>
    public static class SecretBox
    {
        private const int KeyBytes = 32;
        private const int IvBytes = 16;
        private const int MacBytes = 32;

        public static byte[] Seal(byte[] key, byte[] plain)
        {
            RequireKey(key);

            byte[] encKey = Derive(key, "enc");
            byte[] macKey = Derive(key, "mac");

            using (Aes aes = Aes.Create())
            {
                aes.Key = encKey;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.GenerateIV();

                byte[] cipher;
                using (ICryptoTransform encryptor = aes.CreateEncryptor())
                {
                    cipher = encryptor.TransformFinalBlock(plain, 0, plain.Length);
                }

                byte[] body = new byte[IvBytes + cipher.Length];
                Buffer.BlockCopy(aes.IV, 0, body, 0, IvBytes);
                Buffer.BlockCopy(cipher, 0, body, IvBytes, cipher.Length);

                byte[] mac = Hmac(macKey, body);
                byte[] sealedBytes = new byte[body.Length + MacBytes];
                Buffer.BlockCopy(body, 0, sealedBytes, 0, body.Length);
                Buffer.BlockCopy(mac, 0, sealedBytes, body.Length, MacBytes);
                return sealedBytes;
            }
        }

        public static bool TryOpen(byte[] key, byte[] sealedBytes, out byte[] plain)
        {
            plain = null;
            if (key == null || key.Length != KeyBytes || sealedBytes == null ||
                sealedBytes.Length < IvBytes + 16 + MacBytes)
            {
                return false;
            }

            byte[] encKey = Derive(key, "enc");
            byte[] macKey = Derive(key, "mac");

            int bodyLength = sealedBytes.Length - MacBytes;
            byte[] body = new byte[bodyLength];
            byte[] mac = new byte[MacBytes];
            Buffer.BlockCopy(sealedBytes, 0, body, 0, bodyLength);
            Buffer.BlockCopy(sealedBytes, bodyLength, mac, 0, MacBytes);

            if (!ConstantTimeEquals(mac, Hmac(macKey, body)))
            {
                return false;
            }

            try
            {
                byte[] iv = new byte[IvBytes];
                Buffer.BlockCopy(body, 0, iv, 0, IvBytes);

                using (Aes aes = Aes.Create())
                {
                    aes.Key = encKey;
                    aes.IV = iv;
                    aes.Mode = CipherMode.CBC;
                    aes.Padding = PaddingMode.PKCS7;
                    using (ICryptoTransform decryptor = aes.CreateDecryptor())
                    {
                        plain = decryptor.TransformFinalBlock(body, IvBytes, bodyLength - IvBytes);
                    }
                }
                return true;
            }
            catch (CryptographicException)
            {
                return false;
            }
        }

        private static void RequireKey(byte[] key)
        {
            if (key == null || key.Length != KeyBytes)
            {
                throw new ArgumentException("The key must be 32 bytes.", "key");
            }
        }

        private static byte[] Derive(byte[] key, string label)
        {
            return Hmac(key, Encoding.ASCII.GetBytes("secretbox-v1|" + label));
        }

        private static byte[] Hmac(byte[] key, byte[] data)
        {
            using (HMACSHA256 hmac = new HMACSHA256(key))
            {
                return hmac.ComputeHash(data);
            }
        }

        private static bool ConstantTimeEquals(byte[] a, byte[] b)
        {
            if (a.Length != b.Length)
            {
                return false;
            }

            int difference = 0;
            for (int i = 0; i < a.Length; i++)
            {
                difference |= a[i] ^ b[i];
            }
            return difference == 0;
        }
    }

    /// <summary>
    /// How a product's content key is made. The issuer holds one 32-byte master secret and derives
    /// each product's key from it, so every license for a product carries the same key and a
    /// resource encrypted once opens for all of them.
    /// </summary>
    public static class ContentKeys
    {
        public static byte[] Derive(byte[] masterSecret, string productId)
        {
            using (HMACSHA256 hmac = new HMACSHA256(masterSecret))
            {
                return hmac.ComputeHash(Encoding.UTF8.GetBytes("product-content-v1|" + productId.Trim().ToLowerInvariant()));
            }
        }
    }
}
