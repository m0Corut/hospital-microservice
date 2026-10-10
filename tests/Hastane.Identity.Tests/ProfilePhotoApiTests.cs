using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Hastane.Identity.Api.Dtos;
using Hastane.Identity.Api.Entities;
using Hastane.Shared.Security;
using Microsoft.Extensions.DependencyInjection;

namespace Hastane.Identity.Tests;

public class ProfilePhotoApiTests : IClassFixture<IdentityApiFactory>
{
    private readonly IdentityApiFactory _factory;

    public ProfilePhotoApiTests(IdentityApiFactory factory) => _factory = factory;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static byte[] Png() => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 73, 72, 68, 82, 1, 2, 3];

    private static byte[] Jpeg() => [0xFF, 0xD8, 0xFF, 0xE0, 0, 16, 74, 70, 73, 70, 0, 1, 2, 3];

    private static byte[] WebP()
        => [.. "RIFF"u8.ToArray(), 0x1A, 0, 0, 0, .. "WEBP"u8.ToArray(), .. "VP8 "u8.ToArray(), 1, 2, 3, 4];

    private static MultipartFormDataContent Form(byte[] bytes, string fileName, string contentType, string field = "file")
    {
        var part = new ByteArrayContent(bytes);
        part.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        return new MultipartFormDataContent { { part, field, fileName } };
    }

    private async Task<(AppUser User, HttpClient Client)> NewUserAsync()
    {
        var user = await IdentityTestData.CreateUserAsync(_factory, [HastaneRoles.Patient]);
        return (user, IdentityTestData.ClientFor(_factory, user, HastaneRoles.Patient));
    }

    private IEnumerable<string> FilesOf(string userId)
        => Directory.Exists(_factory.PhotoDirectory)
            ? Directory.GetFiles(_factory.PhotoDirectory, $"{userId}-*")
            : [];

    [Theory]
    [InlineData("png", "image/png")]
    [InlineData("jpeg", "image/jpeg")]
    [InlineData("webp", "image/webp")]
    public async Task Upload_ValidImage_Succeeds_AndAnonymousGetReturnsItWithSafeHeaders(string kind, string contentType)
    {
        var (user, client) = await NewUserAsync();
        var bytes = kind switch { "png" => Png(), "jpeg" => Jpeg(), _ => WebP() };

        var put = await client.PutAsync("/api/auth/me/photo", Form(bytes, $"avatar.{kind}", contentType), Ct);

        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var summary = (await put.Content.ReadFromJsonAsync<UserSummaryDto>(Ct))!;
        Assert.Equal($"/gateway/users/{user.Id}/photo", summary.PhotoUrl);

        var get = await _factory.CreateClient().GetAsync($"/api/users/{user.Id}/photo", Ct); // anonim
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal(contentType, get.Content.Headers.ContentType!.MediaType);
        Assert.Equal("nosniff", Assert.Single(get.Headers.GetValues("X-Content-Type-Options")));
        var cache = get.Headers.CacheControl!;
        Assert.True(cache.MaxAge is { } age && age <= TimeSpan.FromMinutes(5));
        Assert.Equal(bytes, await get.Content.ReadAsByteArrayAsync(Ct));
    }

    [Fact]
    public async Task ContentType_IsDecidedByMagicBytes_NotByUploadedHeader()
    {
        var (user, client) = await NewUserAsync();

        // Gerçek PNG, ama "image/jpeg" ve .jpg diye gönderilir: sunucu PNG olarak saklar/sunar.
        var put = await client.PutAsync("/api/auth/me/photo", Form(Png(), "x.jpg", "image/jpeg"), Ct);
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        var get = await _factory.CreateClient().GetAsync($"/api/users/{user.Id}/photo", Ct);
        Assert.Equal("image/png", get.Content.Headers.ContentType!.MediaType);
    }

    [Theory]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>", "avatar.svg", "image/svg+xml")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>", "avatar.png", "image/png")]
    [InlineData("<html><script>alert(1)</script></html>", "avatar.png", "image/png")]
    [InlineData("<?xml version=\"1.0\"?><svg/>", "avatar.png", "image/png")]
    [InlineData("bu sadece düz metin", "avatar.png", "image/png")]
    [InlineData("GIF89a....", "avatar.gif", "image/gif")]
    [InlineData("BM......", "avatar.bmp", "image/bmp")]
    public async Task Upload_NotARealSupportedImage_Returns400_AndStoresNothing(string content, string fileName, string contentType)
    {
        var (user, client) = await NewUserAsync();

        var put = await client.PutAsync("/api/auth/me/photo", Form(Encoding.UTF8.GetBytes(content), fileName, contentType), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
        Assert.Empty(FilesOf(user.Id));
        Assert.Null((await IdentityTestData.GetUserAsync(_factory, user.Id)).PhotoFileName);
    }

    [Fact]
    public async Task Upload_RiffThatIsNotWebP_Returns400()
    {
        var (_, client) = await NewUserAsync();
        var wav = "RIFF\u0010\0\0\0WAVEfmt "u8.ToArray();

        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PutAsync("/api/auth/me/photo", Form(wav, "a.webp", "image/webp"), Ct)).StatusCode);
    }

    [Fact]
    public async Task Upload_EmptyFileOrWrongFieldName_Returns400()
    {
        var (_, client) = await NewUserAsync();

        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PutAsync("/api/auth/me/photo", Form([], "a.png", "image/png"), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PutAsync("/api/auth/me/photo", Form(Png(), "a.png", "image/png", field: "avatar"), Ct)).StatusCode);
    }

    [Fact]
    public async Task Upload_OverTwoMegabytes_Returns413()
    {
        var (user, client) = await NewUserAsync();
        var big = new byte[2 * 1024 * 1024 + 1];
        Png().CopyTo(big, 0);

        var put = await client.PutAsync("/api/auth/me/photo", Form(big, "big.png", "image/png"), Ct);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, put.StatusCode);
        Assert.Empty(FilesOf(user.Id));
    }

    [Fact]
    public async Task Upload_ExactlyTwoMegabytes_IsAccepted()
    {
        var (_, client) = await NewUserAsync();
        var edge = new byte[2 * 1024 * 1024];
        Png().CopyTo(edge, 0);

        Assert.Equal(HttpStatusCode.OK, (await client.PutAsync("/api/auth/me/photo", Form(edge, "edge.png", "image/png"), Ct)).StatusCode);
    }

    [Fact]
    public async Task Upload_HugeBody_IsRejected_At413Or400()
    {
        var (user, client) = await NewUserAsync();
        var huge = new byte[6 * 1024 * 1024];
        Png().CopyTo(huge, 0);

        var put = await client.PutAsync("/api/auth/me/photo", Form(huge, "huge.png", "image/png"), Ct);

        // Gerçek Kestrel RequestSizeLimit ile 413 verir; test sunucusunda sınırı form limiti (400) ya da kontrol (413) uygular.
        Assert.Contains(put.StatusCode, new[] { HttpStatusCode.RequestEntityTooLarge, HttpStatusCode.BadRequest });
        Assert.Empty(FilesOf(user.Id));
    }

    [Fact]
    public async Task Upload_Unauthenticated_Returns401()
    {
        var response = await _factory.CreateClient().PutAsync("/api/auth/me/photo", Form(Png(), "a.png", "image/png"), Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Upload_FileNameIsServerGenerated_ClientNameAndTraversalAreIgnored()
    {
        var (user, client) = await NewUserAsync();

        var put = await client.PutAsync("/api/auth/me/photo", Form(Png(), "../../evil/../pwn.png", "image/png"), Ct);
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        var stored = (await IdentityTestData.GetUserAsync(_factory, user.Id)).PhotoFileName!;
        Assert.Matches($@"^{user.Id}-[0-9a-f]{{32}}\.png$", stored);
        Assert.DoesNotContain("pwn", stored);
        Assert.True(File.Exists(Path.Combine(_factory.PhotoDirectory, stored)));
        Assert.Empty(Directory.GetFiles(_factory.PhotoDirectory, "*pwn*", SearchOption.AllDirectories));
        Assert.False(File.Exists(Path.Combine(_factory.PhotoDirectory, "..", "pwn.png")));
    }

    [Fact]
    public async Task Upload_Again_ReplacesAndDeletesOldFile()
    {
        var (user, client) = await NewUserAsync();

        await client.PutAsync("/api/auth/me/photo", Form(Png(), "a.png", "image/png"), Ct);
        var first = (await IdentityTestData.GetUserAsync(_factory, user.Id)).PhotoFileName!;
        await client.PutAsync("/api/auth/me/photo", Form(Jpeg(), "b.jpg", "image/jpeg"), Ct);
        var second = (await IdentityTestData.GetUserAsync(_factory, user.Id)).PhotoFileName!;

        Assert.NotEqual(first, second);
        Assert.False(File.Exists(Path.Combine(_factory.PhotoDirectory, first)));
        Assert.Single(FilesOf(user.Id));
        Assert.EndsWith(".jpg", second);
    }

    [Fact]
    public async Task Delete_RemovesPhoto_FileAndUrl_AndIsIdempotent()
    {
        var (user, client) = await NewUserAsync();
        await client.PutAsync("/api/auth/me/photo", Form(Png(), "a.png", "image/png"), Ct);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("/api/auth/me/photo", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("/api/auth/me/photo", Ct)).StatusCode);

        Assert.Empty(FilesOf(user.Id));
        Assert.Equal(HttpStatusCode.NotFound, (await _factory.CreateClient().GetAsync($"/api/users/{user.Id}/photo", Ct)).StatusCode);
        var me = await client.GetFromJsonAsync<UserSummaryDto>("/api/auth/me", Ct);
        Assert.Null(me!.PhotoUrl);
    }

    [Fact]
    public async Task Delete_OnlyAffectsOwnPhoto_NotAnotherUsers()
    {
        var (alice, aliceClient) = await NewUserAsync();
        var (_, bobClient) = await NewUserAsync();
        await aliceClient.PutAsync("/api/auth/me/photo", Form(Png(), "a.png", "image/png"), Ct);

        // Bob "me" ucunu çağırır: kendi (olmayan) fotoğrafını siler, Alice'inkine dokunamaz.
        Assert.Equal(HttpStatusCode.NoContent, (await bobClient.DeleteAsync("/api/auth/me/photo", Ct)).StatusCode);
        // Başkasının fotoğrafını silecek bir uç YOK.
        var other = await bobClient.DeleteAsync($"/api/users/{alice.Id}/photo", Ct);
        Assert.Contains(other.StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed });
        var put = await bobClient.PutAsync($"/api/users/{alice.Id}/photo", Form(Png(), "a.png", "image/png"), Ct);
        Assert.Contains(put.StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed });

        Assert.Equal(HttpStatusCode.OK, (await _factory.CreateClient().GetAsync($"/api/users/{alice.Id}/photo", Ct)).StatusCode);
        Assert.Single(FilesOf(alice.Id));
    }

    [Fact]
    public async Task Get_UserWithoutPhoto_OrUnknownOrBadId_Returns404()
    {
        var (user, _) = await NewUserAsync();
        var anon = _factory.CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync($"/api/users/{user.Id}/photo", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync($"/api/users/{Guid.NewGuid()}/photo", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync("/api/users/..%2F..%2Fsecrets/photo", Ct)).StatusCode);
    }

    [Fact]
    public async Task Get_IgnoresTamperedFileNameInDatabase()
    {
        var (user, _) = await NewUserAsync();
        // Veritabanı bozulsa/elle değişse bile ad biçimi doğrulanmadan diske gidilmez.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Hastane.Identity.Api.Data.HastaneIdentityDbContext>();
            var row = await db.Users.FindAsync([user.Id], Ct);
            row!.PhotoFileName = "..\\..\\Windows\\win.ini";
            await db.SaveChangesAsync(Ct);
        }

        Assert.Equal(HttpStatusCode.NotFound, (await _factory.CreateClient().GetAsync($"/api/users/{user.Id}/photo", Ct)).StatusCode);
    }

    [Fact]
    public async Task UserSummary_HasPhotoUrlOnlyWhenPhotoExists()
    {
        var (user, client) = await NewUserAsync();
        Assert.Null((await client.GetFromJsonAsync<UserSummaryDto>("/api/auth/me", Ct))!.PhotoUrl);

        await client.PutAsync("/api/auth/me/photo", Form(Png(), "a.png", "image/png"), Ct);

        var admin = IdentityTestData.AdminClient(_factory);
        var viaAdmin = await admin.GetFromJsonAsync<UserSummaryDto>($"/api/users/{user.Id}", Ct);
        Assert.Equal($"/gateway/users/{user.Id}/photo", viaAdmin!.PhotoUrl);
        // Dosya adı dışarı sızmaz.
        Assert.DoesNotContain("PhotoFileName", await admin.GetStringAsync($"/api/users/{user.Id}", Ct), StringComparison.OrdinalIgnoreCase);
    }
}
