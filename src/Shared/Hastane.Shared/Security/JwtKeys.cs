using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace Hastane.Shared.Security;

// PEM -> SecurityKey dönüşümleri ve anahtar parmak izi (kid). Identity (imzalar) ile doğrulayanlar aynı kid'i üretir.
public static class JwtKeys
{
    private const string EmbeddedPublicKeyName = "Hastane.Shared.jwt-public.pem";

    public static string LoadEmbeddedPublicKeyPem()
    {
        using var stream = typeof(JwtKeys).Assembly.GetManifestResourceStream(EmbeddedPublicKeyName)
            ?? throw new InvalidOperationException($"Gömülü açık anahtar bulunamadı: {EmbeddedPublicKeyName}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    // RSA nesnesi anahtarla birlikte yaşar (singleton); bu yüzden Dispose edilmez.
    public static RsaSecurityKey CreatePublicKey(string publicPem) => FromPem(publicPem);

    public static RsaSecurityKey CreatePrivateKey(string privatePem) => FromPem(privatePem);

    private static RsaSecurityKey FromPem(string pem)
    {
        var rsa = RSA.Create();
        rsa.ImportFromPem(pem);
        return new RsaSecurityKey(rsa) { KeyId = ComputeKeyId(rsa) };
    }

    // kid = açık anahtarın (SubjectPublicKeyInfo) SHA-256 özetinin ilk 16 baytı, base64url.
    // Özel ve açık anahtar aynı çiftse aynı kid çıkar.
    public static string ComputeKeyId(RSA rsa)
    {
        var hash = SHA256.HashData(rsa.ExportSubjectPublicKeyInfo());
        return Base64UrlEncoder.Encode(hash, 0, 16);
    }
}
