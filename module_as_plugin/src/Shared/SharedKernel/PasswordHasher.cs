// Argon2id password hashing — closes the P5 "password verification stubbed" gap.
// Format: argon2id$<v=19$m=<kb>,t=<iter>,p=<lanes)>$<saltB64>$<hashB64> (self-describing,
// parameters stored with the hash so cost can be raised without breaking old rows).
// Legacy formats:
//   sha256:<hex>      — previous change-password storage; verified + transparently
//                       upgraded to Argon2 on next successful login (NeedsRehash).
//   dev-stub / other  — never verifies; the caller decides the Development-only
//                       upgrade-on-login path (Platform login), never Production.
using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace SharedKernel
{
    public static class PasswordHasher
    {
        private const string Prefix = "argon2id";
        private const int DefaultMemoryKb = 19456;  // OWASP argon2id minimum (19 MiB)
        private const int DefaultIterations = 2;
        private const int DefaultLanes = 1;
        private const int SaltBytes = 16;

        public static string Hash(string password)
        {
            var salt = RandomNumberGenerator.GetBytes(SaltBytes);
            var hash = Argon2Bytes(password, salt, DefaultMemoryKb, DefaultIterations, DefaultLanes);
            return $"{Prefix}$v=19$m={DefaultMemoryKb},t={DefaultIterations},p={DefaultLanes}" +
                   $"${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
        }

        /// <summary>Verifies a password against a stored hash. Returns (ok, needsRehash).</summary>
        public static (bool Ok, bool NeedsRehash) Verify(string password, string? stored)
        {
            if (string.IsNullOrEmpty(stored)) return (false, false);
            if (stored.StartsWith(Prefix + "$", StringComparison.Ordinal))
                return (VerifyArgon2(password, stored), false);
            if (stored.StartsWith("sha256:", StringComparison.Ordinal))
            {
                var expected = stored["sha256:".Length..];
                var actual = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(password))).ToLowerInvariant();
                // constant-time compare; rehash on success (legacy upgrade path)
                return (CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(actual), Encoding.UTF8.GetBytes(expected)), true);
            }
            return (false, false); // dev-stub / unknown format: never verifies
        }

        /// <summary>True when the stored value cannot verify anything (e.g. old 'dev-stub'
        /// placeholder) — callers may allow a Development-only upgrade-on-login, never Production.</summary>
        public static bool IsUnverifiablePlaceholder(string? stored) =>
            !string.IsNullOrEmpty(stored) && !stored.StartsWith(Prefix + "$", StringComparison.Ordinal)
            && !stored.StartsWith("sha256:", StringComparison.Ordinal);

        private static bool VerifyArgon2(string password, string stored)
        {
            try
            {
                // $argon2id$v=19$m=<kb>,t=<iter>,p=<lanes>$<salt>$<hash> → 5 segments
                var parts = stored.Split('$');
                if (parts.Length != 5 || parts[0] != Prefix) return false;
                var parms = parts[2].Split(','); // v=19 is parts[1]
                var mem = int.Parse(parms[0][2..]);    // "m=19456"
                var iter = int.Parse(parms[1][2..]);   // "t=2"
                var lanes = int.Parse(parms[2][2..]);  // "p=1"
                var salt = Convert.FromBase64String(parts[3]);
                var expected = Convert.FromBase64String(parts[4]);
                var actual = Argon2Bytes(password, salt, mem, iter, lanes);
                return CryptographicOperations.FixedTimeEquals(actual, expected);
            }
            catch { return false; }
        }

        private static byte[] Argon2Bytes(string password, byte[] salt, int memoryKb, int iterations, int lanes)
        {
            using var argon2 = new Argon2id(Encoding.UTF8.GetBytes(password))
            {
                Salt = salt,
                DegreeOfParallelism = lanes,
                MemorySize = memoryKb,
                Iterations = iterations,
            };
            return argon2.GetBytes(32);
        }
    }
}
