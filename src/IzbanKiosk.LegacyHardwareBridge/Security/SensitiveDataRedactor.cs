using System;
using System.Security.Cryptography;
using System.Text;

namespace IzbanKiosk.LegacyHardwareBridge.Security
{
    public class SensitiveDataRedactor
    {
        private readonly byte[] _hmacKey;

        public SensitiveDataRedactor(string hmacKeyBase64)
        {
            if (string.IsNullOrWhiteSpace(hmacKeyBase64))
            {
                throw new ArgumentException("HMAC key cannot be null or empty. It must be provided securely via environment variables or secret store.");
            }

            try
            {
                _hmacKey = Convert.FromBase64String(hmacKeyBase64);
            }
            catch (Exception ex)
            {
                throw new ArgumentException("Failed to decode HMAC key from Base64 format.", ex);
            }

            if (_hmacKey.Length < 32)
            {
                throw new ArgumentException("HMAC key must contain at least 32 bytes after Base64 decoding.");
            }
        }

        public string GenerateStoragePseudonym(string rawCardId)
        {
            if (string.IsNullOrEmpty(rawCardId))
                return string.Empty;

            using (var hmac = new HMACSHA256(_hmacKey))
            {
                byte[] valBytes = Encoding.UTF8.GetBytes(rawCardId);
                byte[] hashBytes = hmac.ComputeHash(valBytes);
                // Return hex string of hash as pseudonym
                var sb = new StringBuilder();
                foreach (byte b in hashBytes)
                {
                    sb.Append(b.ToString("x2"));
                }
                return sb.ToString();
            }
        }

        public string MaskCardReference(string rawCardRef)
        {
            if (string.IsNullOrEmpty(rawCardRef))
                return string.Empty;

            // Raw Card Reference format is usually like: "35-IZM-9921" or alias numeric string
            // Mask it like: "35-I••••9921" or show only first 4 and last 4 characters
            if (rawCardRef.Length <= 8)
            {
                return new string('•', rawCardRef.Length);
            }

            string head = rawCardRef.Substring(0, 4);
            string tail = rawCardRef.Substring(rawCardRef.Length - 4);
            return $"{head}••••{tail}";
        }
    }
}
