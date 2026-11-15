using System;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.WebUtilities;

namespace WindowsNotch.Rewards
{
    public sealed class EntitlementPayload
    {
        [JsonPropertyName("v")]
        public int V { get; set; }

        [JsonPropertyName("code")]
        public string Code { get; set; } = string.Empty;

        [JsonPropertyName("claim_number")]
        public int ClaimNumber { get; set; }

        [JsonPropertyName("bg_id")]
        public string BgId { get; set; } = string.Empty;

        [JsonPropertyName("badge_id")]
        public string BadgeId { get; set; } = string.Empty;

        [JsonPropertyName("rarity")]
        public string Rarity { get; set; } = string.Empty;

        [JsonPropertyName("device_hash")]
        public string DeviceHash { get; set; } = string.Empty;

        [JsonPropertyName("issued_at")]
        public long IssuedAt { get; set; }
    }

    public static class RewardCrypto
    {
        // Embedded 32-byte Ed25519 Public Key
        public const string EmbeddedPublicKeyBase64 = "iwfllOWyxPTLkYV7Rmqx2hpEdF5ugKATlLinhrbpp9s=";

        private static readonly byte[] EmbeddedPublicKey = Convert.FromBase64String(EmbeddedPublicKeyBase64);

        private static readonly BigInteger P = BigInteger.Pow(2, 255) - 19;
        private static readonly BigInteger L = BigInteger.Parse("7237005577332262213973186563042994240857116359379907606001950938285454250989");
        private static readonly BigInteger D = Mod(-121665 * ModInverse(121666, P), P);
        private static readonly BigInteger I = BigInteger.ModPow(2, (P - 1) / 4, P);

        private static readonly Point BasePoint;

        static RewardCrypto()
        {
            BigInteger y = Mod(4 * ModInverse(5, P), P);
            BigInteger x = RecoverX(y, 0);
            BasePoint = new Point(x, y);
        }

        public static EntitlementPayload? VerifyAndParseToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token)) return null;

            string[] parts = token.Trim().Split('.');
            if (parts.Length != 2) return null;

            byte[] payloadBytes;
            byte[] signatureBytes;

            try
            {
                payloadBytes = WebEncoders.Base64UrlDecode(parts[0]);
                signatureBytes = WebEncoders.Base64UrlDecode(parts[1]);
            }
            catch
            {
                return null;
            }

            if (!VerifyEd25519(EmbeddedPublicKey, payloadBytes, signatureBytes))
            {
                return null;
            }

            try
            {
                string json = Encoding.UTF8.GetString(payloadBytes);
                var payload = JsonSerializer.Deserialize<EntitlementPayload>(json);
                if (payload == null || payload.V != 1 || string.IsNullOrWhiteSpace(payload.BgId))
                {
                    return null;
                }

                // Verify device hash
                string localHash = DeviceId.GetDeviceHash();
                if (!string.Equals(payload.DeviceHash, localHash, StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                return payload;
            }
            catch
            {
                return null;
            }
        }

        public static bool VerifyEd25519(byte[] publicKey, byte[] message, byte[] signature)
        {
            if (publicKey == null || publicKey.Length != 32) return false;
            if (signature == null || signature.Length != 64) return false;

            byte[] rBytes = new byte[32];
            byte[] sBytes = new byte[32];
            Buffer.BlockCopy(signature, 0, rBytes, 0, 32);
            Buffer.BlockCopy(signature, 32, sBytes, 0, 32);

            byte[] sUnsigned = new byte[33];
            Buffer.BlockCopy(sBytes, 0, sUnsigned, 0, 32);
            BigInteger s = new BigInteger(sUnsigned);
            if (s >= L) return false;

            Point aPoint;
            Point rPoint;
            try
            {
                aPoint = DecodePoint(publicKey);
                rPoint = DecodePoint(rBytes);
            }
            catch
            {
                return false;
            }

            byte[] hashInput = new byte[32 + 32 + message.Length];
            Buffer.BlockCopy(rBytes, 0, hashInput, 0, 32);
            Buffer.BlockCopy(publicKey, 0, hashInput, 32, 32);
            Buffer.BlockCopy(message, 0, hashInput, 64, message.Length);

            byte[] kHash = SHA512.HashData(hashInput);
            byte[] kUnsigned = new byte[65];
            Buffer.BlockCopy(kHash, 0, kUnsigned, 0, 64);
            BigInteger k = Mod(new BigInteger(kUnsigned), L);

            Point sb = ScalarMult(BasePoint, s);
            Point ka = ScalarMult(aPoint, k);
            Point rka = PointAdd(rPoint, ka);

            return sb.X == rka.X && sb.Y == rka.Y;
        }

        private readonly struct Point
        {
            public readonly BigInteger X;
            public readonly BigInteger Y;

            public Point(BigInteger x, BigInteger y)
            {
                X = x;
                Y = y;
            }

            public static readonly Point Identity = new(0, 1);
        }

        private static BigInteger Mod(BigInteger a, BigInteger m)
        {
            BigInteger r = a % m;
            return r < 0 ? r + m : r;
        }

        private static BigInteger ModInverse(BigInteger a, BigInteger m)
        {
            return BigInteger.ModPow(Mod(a, m), m - 2, m);
        }

        private static BigInteger RecoverX(BigInteger y, int sign)
        {
            BigInteger y2 = Mod(y * y, P);
            BigInteger u = Mod(y2 - 1, P);
            BigInteger v = Mod(D * y2 + 1, P);
            BigInteger xx = Mod(u * ModInverse(v, P), P);

            if (xx == 0) return 0;

            BigInteger x = BigInteger.ModPow(xx, (P + 3) / 8, P);
            if (Mod(x * x - xx, P) != 0)
            {
                x = Mod(x * I, P);
            }
            if (Mod(x * x - xx, P) != 0)
            {
                throw new ArgumentException("Invalid curve point");
            }
            if ((x % 2 == 1 ? 1 : 0) != sign)
            {
                x = P - x;
            }
            return x;
        }

        private static Point DecodePoint(byte[] bytes)
        {
            if (bytes.Length != 32) throw new ArgumentException("Point must be 32 bytes");
            byte[] copy = (byte[])bytes.Clone();
            int sign = (copy[31] >> 7) & 1;
            copy[31] &= 0x7F;

            byte[] unsigned = new byte[33];
            Buffer.BlockCopy(copy, 0, unsigned, 0, 32);
            BigInteger y = new BigInteger(unsigned);
            if (y >= P) throw new ArgumentException("Y out of range");

            BigInteger x = RecoverX(y, sign);
            return new Point(x, y);
        }

        private static Point PointAdd(Point p1, Point p2)
        {
            BigInteger x1 = p1.X, y1 = p1.Y;
            BigInteger x2 = p2.X, y2 = p2.Y;

            BigInteger x1x2 = Mod(x1 * x2, P);
            BigInteger y1y2 = Mod(y1 * y2, P);
            BigInteger dx1x2y1y2 = Mod(D * x1x2 * y1y2, P);

            BigInteger x3 = Mod(Mod(x1 * y2 + y1 * x2, P) * ModInverse(1 + dx1x2y1y2, P), P);
            BigInteger y3 = Mod(Mod(y1 * y2 + x1x2, P) * ModInverse(1 - dx1x2y1y2, P), P);

            return new Point(x3, y3);
        }

        private static Point ScalarMult(Point p, BigInteger s)
        {
            Point result = Point.Identity;
            Point addend = p;

            while (s > 0)
            {
                if ((s & 1) == 1)
                {
                    result = PointAdd(result, addend);
                }
                addend = PointAdd(addend, addend);
                s >>= 1;
            }
            return result;
        }
    }
}
