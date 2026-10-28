using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace ToolFB.App.Update;

public static class SignatureVerifier
{
    // Public Key (NIST P-256) embedded directly in the application.
    // Generated specifically for GiangDev ToolFB Auto-Update System.
    // ONLY the public key is embedded in client builds.
    public const string EmbeddedPublicKeyPem = 
@"-----BEGIN PUBLIC KEY-----
MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEO/SUOCVaO+PIwNsrxc4a+dHGOec3
i6auPUax6J5fScCZhtpaEG+WFFpkn9+lXhfZ5lonz0YLQSOay6/5Vglplg==
-----END PUBLIC KEY-----";

    public static bool VerifySignature(UpdateManifest manifest, string? customPublicKeyPem = null)
    {
        if (manifest == null || string.IsNullOrWhiteSpace(manifest.Signature))
            return false;

        try
        {
            var payload = manifest.GetSigningPayload();
            var payloadBytes = Encoding.UTF8.GetBytes(payload);
            var signatureBytes = Convert.FromBase64String(manifest.Signature);

            string pem = !string.IsNullOrWhiteSpace(customPublicKeyPem) ? customPublicKeyPem : EmbeddedPublicKeyPem;

            using var ecdsa = ECDsa.Create();
            ecdsa.ImportFromPem(pem);

            return ecdsa.VerifyData(payloadBytes, signatureBytes, HashAlgorithmName.SHA256);
        }
        catch
        {
            return false;
        }
    }

    public static bool VerifyFileSha256(string filePath, string expectedSha256)
    {
        if (!File.Exists(filePath) || string.IsNullOrWhiteSpace(expectedSha256))
            return false;

        try
        {
            using var sha256 = SHA256.Create();
            using var stream = File.OpenRead(filePath);
            var hashBytes = sha256.ComputeHash(stream);
            var actualHex = Convert.ToHexString(hashBytes);

            return string.Equals(actualHex, expectedSha256.Trim(), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public static bool IsVersionNewer(string latestVersionStr, string currentVersionStr)
    {
        try
        {
            var cleanLatest = CleanVersion(latestVersionStr);
            var cleanCurrent = CleanVersion(currentVersionStr);

            var vLatest = Version.Parse(cleanLatest);
            var vCurrent = Version.Parse(cleanCurrent);

            return vLatest > vCurrent;
        }
        catch
        {
            return false;
        }
    }

    public static bool IsVersionBelowMinimum(string currentVersionStr, string? minimumVersionStr)
    {
        if (string.IsNullOrWhiteSpace(minimumVersionStr))
            return false;

        try
        {
            var vCurrent = Version.Parse(CleanVersion(currentVersionStr));
            var vMin = Version.Parse(CleanVersion(minimumVersionStr));

            return vCurrent < vMin;
        }
        catch
        {
            return false;
        }
    }

    private static string CleanVersion(string v)
    {
        v = v.Trim().TrimStart('v', 'V');
        // Ensure at least 2 parts (Major.Minor)
        var parts = v.Split('.');
        if (parts.Length == 1) return $"{parts[0]}.0.0";
        if (parts.Length == 2) return $"{parts[0]}.{parts[1]}.0";
        return v;
    }
}
