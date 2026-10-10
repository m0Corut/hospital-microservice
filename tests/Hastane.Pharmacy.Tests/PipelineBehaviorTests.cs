using Hastane.Pharmacy.Api.Behaviors;
using Hastane.Pharmacy.Api.Validation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Hastane.Pharmacy.Tests;

// Test için küçük bir komut + handler + validator: gerçek MediatR hattı kurulur ama veritabanı yok.
public sealed record PingCommand(string Text, string Secret = "") : IRequest<string>;

public sealed class PingValidator : IValidator<PingCommand>
{
    public IEnumerable<ValidationFailure> Validate(PingCommand request)
    {
        if (string.IsNullOrWhiteSpace(request.Text)) yield return new("Text", "Metin gerekli.");
    }
}

public sealed class PingHandler(PingCounter counter) : IRequestHandler<PingCommand, string>
{
    public Task<string> Handle(PingCommand request, CancellationToken cancellationToken)
    {
        counter.Calls++;
        return Task.FromResult("pong:" + request.Text);
    }
}

public sealed class PingCounter
{
    public int Calls { get; set; }
}

public sealed class ListLogger<T> : ILogger<T>
{
    public List<string> Lines { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        => Lines.Add($"[{logLevel}] {formatter(state, exception)}");
}

public class PipelineBehaviorTests
{
    private static ServiceProvider BuildPipeline(out PingCounter counter, bool withLogging = true)
    {
        var services = new ServiceCollection();
        var shared = new PingCounter();
        counter = shared;
        services.AddSingleton(shared);
        services.AddLogging();
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssemblyContaining<PingHandler>();
            // Üretimle aynı sıra: Logging en dışta, Validation içte.
            if (withLogging) cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
        });
        services.AddValidatorsFromAssembly(typeof(PingValidator).Assembly);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task InvalidCommand_NeverReachesTheHandler()
    {
        await using var provider = BuildPipeline(out var counter);
        var sender = provider.GetRequiredService<ISender>();

        var ex = await Assert.ThrowsAsync<RequestValidationException>(() => sender.Send(new PingCommand(" "), TestContext.Current.CancellationToken));

        Assert.Equal(0, counter.Calls); // handler hiç çalışmadı
        Assert.Equal("Text", Assert.Single(ex.Failures).Property);
    }

    [Fact]
    public async Task ValidCommand_ReachesTheHandlerExactlyOnce()
    {
        await using var provider = BuildPipeline(out var counter);

        var result = await provider.GetRequiredService<ISender>().Send(new PingCommand("merhaba"), TestContext.Current.CancellationToken);

        Assert.Equal("pong:merhaba", result);
        Assert.Equal(1, counter.Calls);
    }

    [Fact]
    public async Task ValidationBehavior_NoValidatorsRegistered_PassesThrough()
    {
        var calls = 0;
        var behavior = new ValidationBehavior<PingCommand, string>([]);

        var result = await behavior.Handle(new PingCommand(""), _ =>
        {
            calls++;
            return Task.FromResult("ok");
        }, TestContext.Current.CancellationToken);

        Assert.Equal("ok", result);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ValidationBehavior_CollectsFailuresFromAllValidators()
    {
        var calls = 0;
        var behavior = new ValidationBehavior<PingCommand, string>([new PingValidator(), new PingValidator()]);

        var ex = await Assert.ThrowsAsync<RequestValidationException>(() => behavior.Handle(new PingCommand(""), _ =>
        {
            calls++;
            return Task.FromResult("ok");
        }, TestContext.Current.CancellationToken));

        Assert.Equal(0, calls);
        Assert.Equal(2, ex.Failures.Count);
    }

    [Fact]
    public void RequestValidationException_MessageDoesNotContainInput()
    {
        var ex = new RequestValidationException([new ValidationFailure("Name", "İlaç adı gerekli.")]);
        Assert.DoesNotContain("İlaç adı", ex.Message);
    }

    [Fact]
    public async Task LoggingBehavior_LogsRequestNameAndDuration_ButNeverTheBody()
    {
        var logger = new ListLogger<LoggingBehavior<PingCommand, string>>();
        var behavior = new LoggingBehavior<PingCommand, string>(logger);

        await behavior.Handle(new PingCommand("a", Secret: "GIZLI-HASTA-VERISI-123"), _ => Task.FromResult("ok"), TestContext.Current.CancellationToken);

        var line = Assert.Single(logger.Lines);
        Assert.Contains(nameof(PingCommand), line);
        Assert.Contains("ms", line);
        Assert.DoesNotContain("GIZLI-HASTA-VERISI-123", line);
    }

    [Fact]
    public async Task LoggingBehavior_OnValidationFailure_LogsWarningWithoutValues()
    {
        var logger = new ListLogger<LoggingBehavior<PingCommand, string>>();
        var behavior = new LoggingBehavior<PingCommand, string>(logger);

        await Assert.ThrowsAsync<RequestValidationException>(() => behavior.Handle(
            new PingCommand("x", "GIZLI-HASTA-VERISI-123"),
            _ => throw new RequestValidationException([new ValidationFailure("Text", "x")]),
            TestContext.Current.CancellationToken));

        var line = Assert.Single(logger.Lines);
        Assert.StartsWith("[Warning]", line);
        Assert.DoesNotContain("GIZLI-HASTA-VERISI-123", line);
    }

    [Fact]
    public async Task LoggingBehavior_OnUnexpectedException_LogsOnlyTheExceptionType()
    {
        var logger = new ListLogger<LoggingBehavior<PingCommand, string>>();
        var behavior = new LoggingBehavior<PingCommand, string>(logger);

        await Assert.ThrowsAsync<InvalidOperationException>(() => behavior.Handle(
            new PingCommand("x"), _ => throw new InvalidOperationException("içinde GIZLI-HASTA-VERISI-123 var"),
            TestContext.Current.CancellationToken));

        var line = Assert.Single(logger.Lines);
        Assert.StartsWith("[Error]", line);
        Assert.Contains(nameof(InvalidOperationException), line);
        Assert.DoesNotContain("GIZLI-HASTA-VERISI-123", line);
    }
}
