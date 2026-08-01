using System;
using System.Security.Cryptography;
using System.Text;

namespace IzbanKiosk.Domain
{
    public record CardReference
    {
        public string Hash { get; }
        public string Masked { get; }

        private CardReference(string hash, string masked)
        {
            Hash = hash;
            Masked = masked;
        }

        public static CardReference Restore(string hash, string masked)
        {
            return new CardReference(hash, masked);
        }

        public static CardReference Create(string rawUid)
        {
            if (string.IsNullOrWhiteSpace(rawUid))
            {
                throw new ArgumentException("Card UID cannot be empty.", nameof(rawUid));
            }

            string cleanUid = rawUid.Trim().ToUpperInvariant();
            
            // Masking logic
            string masked;
            if (cleanUid.Length <= 8)
            {
                masked = cleanUid; // e.g. "35-IZM-9921" or simulator inputs
            }
            else
            {
                masked = cleanUid.Substring(0, 4) + "••••" + cleanUid.Substring(cleanUid.Length - 4);
            }

            // Pseudonym calculation (SHA256 Hash of Card UID)
            using var sha256 = SHA256.Create();
            byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(cleanUid));
            var sb = new StringBuilder();
            foreach (byte b in bytes)
            {
                sb.Append(b.ToString("x2"));
            }

            return new CardReference(sb.ToString(), masked);
        }

        public override string ToString() => Masked;
    }
}
