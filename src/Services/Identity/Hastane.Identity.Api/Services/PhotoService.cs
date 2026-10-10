using System.Text.RegularExpressions;
using Hastane.Identity.Api.Data;
using Hastane.Identity.Api.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Hastane.Identity.Api.Services;

public enum PhotoSaveStatus { Saved, UserNotFound, TooLarge, UnsupportedType }

public record PhotoFile(Stream Content, string ContentType);

public interface IPhotoService
{
    Task<PhotoSaveStatus> SaveAsync(string userId, Stream content, CancellationToken ct);
    Task<bool> DeleteAsync(string userId, CancellationToken ct);
    Task<PhotoFile?> OpenAsync(string userId, CancellationToken ct);
}

public partial class PhotoService(HastaneIdentityDbContext db, IOptions<StorageOptions> options, ILogger<PhotoService> logger)
    : IPhotoService
{
    public const int MaxBytes = 2 * 1024 * 1024;

    // Sunucunun ürettiği ad biçimi: {userId}-{guid:N}.{ext}. Diskten OKUMADAN önce de doğrulanır (veritabanı bozulsa bile yol geçişi olmaz).
    [GeneratedRegex(@"^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}-[0-9a-f]{32}\.(png|jpg|webp)\z")]
    private static partial Regex SafeName();

    public async Task<PhotoSaveStatus> SaveAsync(string userId, Stream content, CancellationToken ct)
    {
        if (!Guid.TryParse(userId, out var guid)) return PhotoSaveStatus.UserNotFound;

        // En çok MaxBytes+1 okunur: sınırı aşan akış bellekte büyümeden reddedilir.
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await content.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > MaxBytes) return PhotoSaveStatus.TooLarge;
            buffer.Write(chunk, 0, read);
        }

        var bytes = buffer.ToArray();
        // Tür YALNIZ içerik imzasından (magic bytes) anlaşılır; uzantıya / Content-Type'a bakılmaz. SVG/HTML/metin imzası yok -> reddedilir.
        var ext = DetectExtension(bytes);
        if (ext is null) return PhotoSaveStatus.UnsupportedType;

        var key = guid.ToString();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == key, ct);
        if (user is null) return PhotoSaveStatus.UserNotFound;

        var root = ResolveRoot();
        Directory.CreateDirectory(root);
        // Dosya adı sunucuda üretilir; istemciden hiçbir parça gelmez.
        var fileName = $"{key}-{Guid.NewGuid():N}.{ext}";
        var path = Path.Combine(root, fileName);
        await File.WriteAllBytesAsync(path, bytes, ct);

        var old = user.PhotoFileName;
        user.PhotoFileName = fileName;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            TryDelete(path);
            throw;
        }

        if (old is not null) TryDelete(PathFor(root, old));
        return PhotoSaveStatus.Saved;
    }

    public async Task<bool> DeleteAsync(string userId, CancellationToken ct)
    {
        if (!Guid.TryParse(userId, out var guid)) return false;
        var key = guid.ToString();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == key, ct);
        if (user is null) return false;

        var old = user.PhotoFileName;
        if (old is null) return true; // idempotent
        user.PhotoFileName = null;
        await db.SaveChangesAsync(ct);
        TryDelete(PathFor(ResolveRoot(), old));
        return true;
    }

    public async Task<PhotoFile?> OpenAsync(string userId, CancellationToken ct)
    {
        if (!Guid.TryParse(userId, out var guid)) return null;
        var key = guid.ToString();
        var name = await db.Users.AsNoTracking().Where(u => u.Id == key).Select(u => u.PhotoFileName).FirstOrDefaultAsync(ct);
        var path = name is null ? null : PathFor(ResolveRoot(), name);
        if (path is null || !File.Exists(path)) return null;

        var contentType = Path.GetExtension(name!) switch
        {
            ".png" => "image/png",
            ".jpg" => "image/jpeg",
            ".webp" => "image/webp",
            _ => null,
        };
        if (contentType is null) return null;
        return new PhotoFile(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete), contentType);
    }

    // PNG: 89 50 4E 47 0D 0A 1A 0A | JPEG: FF D8 FF | WebP: "RIFF" .... "WEBP"
    internal static string? DetectExtension(ReadOnlySpan<byte> b)
    {
        if (b.Length >= 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47
            && b[4] == 0x0D && b[5] == 0x0A && b[6] == 0x1A && b[7] == 0x0A) return "png";
        if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return "jpg";
        if (b.Length >= 12 && b[0] == 'R' && b[1] == 'I' && b[2] == 'F' && b[3] == 'F'
            && b[8] == 'W' && b[9] == 'E' && b[10] == 'B' && b[11] == 'P') return "webp";
        return null;
    }

    private string ResolveRoot()
    {
        var configured = options.Value.PhotoPath;
        var root = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "hastane-identity", "photos")
            : Environment.ExpandEnvironmentVariables(configured);
        return Path.GetFullPath(root);
    }

    // Ad biçimi doğrulanmazsa null: asla köke birleştirilmez.
    private static string PathFor(string root, string fileName)
        => SafeName().IsMatch(fileName) ? Path.Combine(root, fileName) : string.Empty;

    private void TryDelete(string path)
    {
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            File.Delete(path);
        }
        catch (IOException ex)
        {
            logger.LogWarning("Eski profil fotoğrafı silinemedi: {ExceptionType}", ex.GetType().Name);
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning("Eski profil fotoğrafı silinemedi: {ExceptionType}", ex.GetType().Name);
        }
    }
}
