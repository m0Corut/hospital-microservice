using System.Net;
using System.Net.Http.Headers;
using Hastane.Chat.Api.Integration;
using Hastane.Chat.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hastane.Chat.Tests;

public class MessagePreviewTests
{
    [Fact]
    public void ShortText_IsKept()
        => Assert.Equal("merhaba", MessagePreview.Create("merhaba"));

    [Fact]
    public void LongText_IsCutTo80()
        => Assert.Equal(80, MessagePreview.Create(new string('x', 500)).Length);

    [Fact]
    public void NewlinesAndRuns_CollapseToSingleSpaces()
        => Assert.Equal("a b c", MessagePreview.Create("a\n\n  b\r\n\tc"));

    [Fact]
    public void SurrogatePair_IsNotSplitAtTheCut()
    {
        var text = new string('x', 79) + "😀tail"; // emoji 79-80. konumlarda: ikiye bölünmemeli
        var preview = MessagePreview.Create(text);

        Assert.True(preview.Length <= 80);
        Assert.False(char.IsHighSurrogate(preview[^1]));
    }
}

// Typed HttpClient katmanı (gerçek Appointment yerine sahte HTTP işleyicisi): Authorization iletimi ve hata sınıflaması.
public class AppointmentLookupTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(respond(request));
        }
    }

    private static AppointmentLookup Create(StubHandler handler, string? authorization)
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        if (authorization is not null) accessor.HttpContext.Request.Headers.Authorization = authorization;
        return new AppointmentLookup(new HttpClient(handler) { BaseAddress = new Uri("http://appointment.test") }, accessor, NullLogger<AppointmentLookup>.Instance);
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK)
        => new(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    [Fact]
    public async Task ForwardsAuthorizationHeader_AndQueriesByDoctorAndPatient()
    {
        var handler = new StubHandler(_ => Json("""[{"id":"3f2504e0-4f89-11d3-9a0c-0305e82c3301","status":"Pending"}]"""));
        var lookup = Create(handler, "Bearer abc.def.ghi");

        var result = await lookup.ListAsync("doc 1", "pat-1", TestContext.Current.CancellationToken);

        Assert.Equal(LookupOutcome.Found, result.Outcome);
        Assert.Single(result.Value!);
        Assert.Equal(new AuthenticationHeaderValue("Bearer", "abc.def.ghi"), handler.LastRequest!.Headers.Authorization);
        Assert.Equal("/api/appointments?doctorId=doc%201&patientId=pat-1", handler.LastRequest.RequestUri!.PathAndQuery);
    }

    [Fact]
    public async Task EmptyList_IsFoundWithZeroItems()
    {
        var lookup = Create(new StubHandler(_ => Json("[]")), "Bearer x");

        var result = await lookup.ListAsync("d", "p", TestContext.Current.CancellationToken);

        Assert.Equal(LookupOutcome.Found, result.Outcome);
        Assert.Empty(result.Value!);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task ServerErrorsAndUnexpected4xx_AreUnavailable(HttpStatusCode status)
    {
        var lookup = Create(new StubHandler(_ => new HttpResponseMessage(status)), "Bearer x");

        Assert.Equal(LookupOutcome.Unavailable, (await lookup.ListAsync("d", "p", TestContext.Current.CancellationToken)).Outcome);
    }

    [Fact]
    public async Task ConnectionFailure_IsUnavailable()
    {
        var handler = new ThrowingHandler();
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        var lookup = new AppointmentLookup(new HttpClient(handler) { BaseAddress = new Uri("http://appointment.test") }, accessor, NullLogger<AppointmentLookup>.Instance);

        Assert.Equal(LookupOutcome.Unavailable, (await lookup.ListAsync("d", "p", TestContext.Current.CancellationToken)).Outcome);
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new HttpRequestException("bağlantı reddedildi");
    }
}
