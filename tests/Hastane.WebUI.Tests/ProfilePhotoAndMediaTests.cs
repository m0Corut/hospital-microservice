using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Hastane.TestSupport;
using static Hastane.WebUI.Tests.AdminPanelTestHelpers;

namespace Hastane.WebUI.Tests;

// Profil fotoğrafı (yükle / kaldır) ve medya vekili (/media/users/{id}/photo). Sahte gateway deseni (UI-KURALLARI §7).
public class ProfilePhotoTests
{
    private const int TwoMb = 2 * 1024 * 1024;

    private static object Me(string? photoUrl) => new
    {
        id = "user-1",
        firstName = "Ayşe",
        lastName = "Yılmaz",
        email = "ayse@test.local",
        roles = new[] { "Patient" },
        photoUrl,
    };

    private static async Task<(WebUiTestHost Host, HttpClient Client)> StartAsync(string? photoUrl = null)
    {
        var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", "/gateway/auth/me", 200, Me(photoUrl));
        return (host, await ClientAsync(host, "Patient"));
    }

    private static async Task<HttpResponseMessage> UploadAsync(HttpClient client, byte[] bytes, string contentType, string fileName = "benim-fotom.png")
    {
        var html = await (await client.GetAsync("/Account/Profile")).Content.ReadAsStringAsync();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        var form = new MultipartFormDataContent
        {
            { new StringContent(WebUiTestHost.ExtractAntiforgeryToken(html)), "__RequestVerificationToken" },
            { file, "file", fileName },
        };
        return await client.PostAsync("/Account/UploadPhoto", form);
    }

    private static byte[] Bytes(int length) => new byte[length];

    [Fact]
    public async Task Profile_without_a_photo_shows_the_initials_avatar_and_an_upload_form_only()
    {
        var (host, client) = await StartAsync();
        await using var _ = host;

        var html = await HtmlAsync(await client.GetAsync("/Account/Profile"));

        Assert.Contains("Profil fotoğrafı", html);
        Assert.Contains("enctype=\"multipart/form-data\"", html);
        Assert.Contains("accept=\"image/png,image/jpeg,image/webp\"", html);
        Assert.Contains("name=\"file\"", html);
        Assert.Contains("<svg class=\"avatar avatar-xl\"", html); // baş harf avatarı
        Assert.DoesNotContain("Fotoğrafı kaldır", html);
    }

    [Fact]
    public async Task Profile_with_a_photo_uses_the_media_proxy_address_and_offers_removal()
    {
        var (host, client) = await StartAsync("/gateway/users/user-1/photo");
        await using var _ = host;

        var html = await HtmlAsync(await client.GetAsync("/Account/Profile"));

        Assert.Contains("src=\"/media/users/user-1/photo?v=", html);
        Assert.DoesNotContain("/gateway/users/user-1/photo", html); // gateway adresi tarayıcıya sızmaz
        Assert.Contains("Fotoğrafı kaldır", html);
    }

    [Fact]
    public async Task Upload_forwards_the_file_as_multipart_with_the_bearer_token()
    {
        var (host, client) = await StartAsync();
        await using var _ = host;
        host.Gateway.MapJson("PUT", "/gateway/auth/me/photo", 200, Me("/gateway/users/user-1/photo"));

        var response = await UploadAsync(client, Encoding.ASCII.GetBytes("PNGDATA"), "image/png");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Account/Profile", Location(response));
        var sent = Last(host, "PUT", "/gateway/auth/me/photo");
        Assert.StartsWith("Bearer ", sent.Authorization);
        Assert.NotEmpty(sent.Body);
        Assert.Contains("name=file", sent.Body.Replace("\"", ""));
        Assert.Contains("Content-Type: image/png", sent.Body);
        Assert.Contains("PNGDATA", sent.Body);
        Assert.DoesNotContain("benim-fotom", sent.Body); // kullanıcının dosya adı üst servise gitmez
        Assert.Contains("Fotoğrafınız güncellendi", await HtmlAsync(await client.GetAsync(Location(response))));
    }

    [Fact]
    public async Task Upload_over_2_MB_is_refused_without_calling_the_service()
    {
        var (host, client) = await StartAsync();
        await using var _ = host;

        var response = await UploadAsync(client, Bytes(TwoMb + 1), "image/png");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.DoesNotContain(host.Gateway.Requests, r => r.Method == "PUT");
        Assert.Contains("en çok 2 MB", await HtmlAsync(await client.GetAsync(Location(response))));
    }

    [Fact]
    public async Task Upload_of_exactly_2_MB_is_accepted()
    {
        var (host, client) = await StartAsync();
        await using var _ = host;
        host.Gateway.MapJson("PUT", "/gateway/auth/me/photo", 200, Me("/gateway/users/user-1/photo"));

        var response = await UploadAsync(client, Bytes(TwoMb), "image/webp");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains(host.Gateway.Requests, r => r.Method == "PUT" && r.Path == "/gateway/auth/me/photo");
    }

    [Theory]
    [InlineData("image/svg+xml")]
    [InlineData("image/gif")]
    [InlineData("text/html")]
    [InlineData("application/octet-stream")]
    public async Task Upload_of_other_content_types_is_refused_without_calling_the_service(string contentType)
    {
        var (host, client) = await StartAsync();
        await using var _ = host;

        var response = await UploadAsync(client, Bytes(10), contentType, "x.svg");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.DoesNotContain(host.Gateway.Requests, r => r.Method == "PUT");
        Assert.Contains("Yalnız PNG, JPEG ya da WebP", await HtmlAsync(await client.GetAsync(Location(response))));
    }

    [Fact]
    public async Task Upload_without_a_file_asks_for_one()
    {
        var (host, client) = await StartAsync();
        await using var _ = host;
        var html = await (await client.GetAsync("/Account/Profile")).Content.ReadAsStringAsync();
        var form = new MultipartFormDataContent { { new StringContent(WebUiTestHost.ExtractAntiforgeryToken(html)), "__RequestVerificationToken" } };

        var response = await client.PostAsync("/Account/UploadPhoto", form);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.DoesNotContain(host.Gateway.Requests, r => r.Method == "PUT");
        Assert.Contains("bir görsel seçin", await HtmlAsync(await client.GetAsync(Location(response))));
    }

    [Theory]
    [InlineData(400, "Yalnız PNG, JPEG ya da WebP görseller kabul edilir.")]
    [InlineData(413, "Fotoğraf en çok 2 MB olabilir.")]
    public async Task Service_400_and_413_are_shown_as_messages(int status, string expected)
    {
        var (host, client) = await StartAsync();
        await using var _ = host;
        host.MapProblem("PUT", "/gateway/auth/me/photo", status, status == 400 ? expected : "ham-servis-metni");

        var response = await UploadAsync(client, Bytes(10), "image/jpeg");

        var html = await HtmlAsync(await client.GetAsync(Location(response)));
        Assert.Contains(expected, html);
        Assert.DoesNotContain("ham-servis-metni", html);
    }

    [Fact]
    public async Task Upload_without_antiforgery_token_is_rejected()
    {
        var (host, client) = await StartAsync();
        await using var _ = host;
        var form = new MultipartFormDataContent { { new ByteArrayContent(Bytes(10)), "file", "a.png" } };

        var response = await client.PostAsync("/Account/UploadPhoto", form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain(host.Gateway.Requests, r => r.Method == "PUT");
    }

    [Fact]
    public async Task Anonymous_upload_and_remove_are_sent_to_login()
    {
        await using var host = await WebUiTestHost.StartAsync();
        var client = host.CreateClient();

        var upload = await PostAsync(host, client, "/Account/Login", "/Account/UploadPhoto");
        var remove = await PostAsync(host, client, "/Account/Login", "/Account/RemovePhoto");

        Assert.Equal(HttpStatusCode.Redirect, upload.StatusCode);
        Assert.StartsWith("/Account/Login", Location(upload));
        Assert.Equal(HttpStatusCode.Redirect, remove.StatusCode);
        Assert.StartsWith("/Account/Login", Location(remove));
        Assert.DoesNotContain(host.Gateway.Requests, r => r.Method is "PUT" or "DELETE");
    }

    [Fact]
    public async Task Remove_sends_DELETE_and_reports_success()
    {
        var (host, client) = await StartAsync("/gateway/users/user-1/photo");
        await using var _ = host;
        host.Gateway.MapStatus("DELETE", "/gateway/auth/me/photo", 204);

        var response = await PostAsync(host, client, "/Account/Profile", "/Account/RemovePhoto");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("Bearer ", Last(host, "DELETE", "/gateway/auth/me/photo").Authorization);
        Assert.Contains("Fotoğrafınız kaldırıldı", await HtmlAsync(await client.GetAsync(Location(response))));
    }

    [Fact]
    public async Task Remove_when_there_is_no_photo_404_is_a_friendly_notice()
    {
        var (host, client) = await StartAsync("/gateway/users/user-1/photo");
        await using var _ = host;
        host.Gateway.MapStatus("DELETE", "/gateway/auth/me/photo", 404);

        var response = await PostAsync(host, client, "/Account/Profile", "/Account/RemovePhoto");

        Assert.Contains("Kaldırılacak bir fotoğraf yok", await HtmlAsync(await client.GetAsync(Location(response))));
    }

    [Fact]
    public async Task Remove_without_antiforgery_token_is_rejected()
    {
        var (host, client) = await StartAsync("/gateway/users/user-1/photo");
        await using var _ = host;

        var response = await client.PostAsync("/Account/RemovePhoto", new FormUrlEncodedContent(new Dictionary<string, string>()));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain(host.Gateway.Requests, r => r.Method == "DELETE");
    }
}

public class MediaProxyTests
{
    private const string Id = "0b1c2d3e-4f50-6172-8394-a5b6c7d8e9f0";
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];

    private static void MapPhoto(WebUiTestHost host, string id, string contentType, byte[]? body = null, int status = 200)
        => host.Gateway.Map("GET", $"/gateway/users/{id}/photo", async ctx =>
        {
            ctx.Response.StatusCode = status;
            if (status != 200) return;
            ctx.Response.ContentType = contentType;
            await ctx.Response.Body.WriteAsync(body ?? Png);
        });

    [Theory]
    [InlineData("image/png")]
    [InlineData("image/jpeg")]
    [InlineData("image/webp")]
    public async Task Streams_allowed_image_types_anonymously_with_safe_headers(string contentType)
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapPhoto(host, Id, contentType);

        var response = await host.CreateClient().GetAsync($"/media/users/{Id}/photo");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(contentType, response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(Png, await response.Content.ReadAsByteArrayAsync());
        Assert.Equal(["nosniff"], response.Headers.GetValues("X-Content-Type-Options"));
        Assert.True(response.Headers.CacheControl?.Public);
        Assert.Equal(TimeSpan.FromSeconds(60), response.Headers.CacheControl?.MaxAge);
        Assert.Equal("", host.Gateway.Requests.Last().Authorization); // anonim istek: oturum belirteci eklenmez
    }

    [Theory]
    [InlineData("image/svg+xml")]
    [InlineData("image/gif")]
    [InlineData("text/html")]
    [InlineData("application/octet-stream")]
    public async Task Other_content_types_are_not_forwarded(string contentType)
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapPhoto(host, Id, contentType, Encoding.ASCII.GetBytes("<script>alert(1)</script>"));

        var response = await host.CreateClient().GetAsync($"/media/users/{Id}/photo");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("<script>alert(1)</script>", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Gateway_404_is_404()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapPhoto(host, Id, "image/png", status: 404);

        var response = await host.CreateClient().GetAsync($"/media/users/{Id}/photo");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Service_down_is_404_not_an_error_page_for_an_image()
    {
        await using var host = WebUiTestHost.StartWithDeadGateway();

        var response = await host.CreateClient().GetAsync($"/media/users/{Id}/photo");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("a.b")]
    [InlineData("..%2F..%2Fgateway%2Froles")]
    [InlineData("%2e%2e")]
    [InlineData("a%5Cb")]
    [InlineData("a%20b")]
    [InlineData("a%3Fb")]
    [InlineData("a%23b")]
    public async Task Invalid_ids_are_404_and_never_reach_the_gateway(string id)
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapPhoto(host, id, "image/png");

        var response = await host.CreateClient().GetAsync($"/media/users/{id}/photo");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(host.Gateway.Requests);
    }

    [Fact]
    public async Task Overlong_id_is_404_and_never_reaches_the_gateway()
    {
        await using var host = await WebUiTestHost.StartAsync();

        var response = await host.CreateClient().GetAsync($"/media/users/{new string('a', 65)}/photo");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(host.Gateway.Requests);
    }

    [Fact]
    public async Task Only_GET_is_served()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapPhoto(host, Id, "image/png");

        var response = await host.CreateClient().PostAsync($"/media/users/{Id}/photo", new FormUrlEncodedContent(new Dictionary<string, string>()));

        Assert.True(response.StatusCode is HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotFound);
        Assert.Empty(host.Gateway.Requests);
    }
}

public class MediaAuthorizationCoverageTests
{
    [Fact]
    public async Task Every_endpoint_including_media_and_account_recovery_declares_authorization_or_anonymous()
    {
        await using var host = await WebUiTestHost.StartAsync(withProbe: false);

        var missing = AuthorizationCoverage.FindEndpointsWithoutAuthorizationMetadata(host.Factory.Services);

        Assert.Empty(missing);
    }
}
