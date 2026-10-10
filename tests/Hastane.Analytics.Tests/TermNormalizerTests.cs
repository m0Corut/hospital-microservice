using Hastane.Analytics.Api.Services;

namespace Hastane.Analytics.Tests;

// Konteyner gerektirmeyen saf birim testler.
public class TermNormalizerTests
{
    [Theory]
    [InlineData("Kardiyoloji", "kardiyoloji")]
    [InlineData("KARDİYOLOJİ", "kardiyoloji")]
    [InlineData("  Kardiyoloji  ", "kardiyoloji")]
    [InlineData("ÇOCUK SAĞLIĞI", "cocuk sagligi")]
    [InlineData("Göz   Hastalıkları", "goz hastaliklari")]
    [InlineData("ışık", "isik")]
    [InlineData("ŞÜKRÜ", "sukru")]
    [InlineData("IRMAK", "irmak")]
    public void Lowercases_and_folds_turkish_letters(string input, string expected)
        => Assert.Equal(expected, TermNormalizer.Normalize(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a")]
    [InlineData("ab")]
    [InlineData(" Çö ")]
    public void Empty_and_too_short_terms_are_skipped(string? input)
        => Assert.Null(TermNormalizer.Normalize(input));

    [Theory]
    [InlineData("ali.veli@gmail.com")]
    [InlineData("12345678901")]
    [InlineData("0532 123 45 67")]
    public void Terms_that_look_like_personal_data_are_skipped(string input)
        => Assert.Null(TermNormalizer.Normalize(input));

    [Fact]
    public void Short_numbers_are_kept_and_long_terms_are_clipped()
    {
        Assert.Equal("kat 3", TermNormalizer.Normalize("Kat 3"));
        Assert.Equal(TermNormalizer.MaxLength, TermNormalizer.Normalize(new string('x', 200))!.Length);
    }

    [Fact]
    public void Day_is_the_istanbul_calendar_day()
    {
        // 22:30 UTC = ertesi gün 01:30 İstanbul
        Assert.Equal("2026-10-11", AnalyticsDay.Of(new DateTimeOffset(2026, 10, 10, 22, 30, 0, TimeSpan.Zero)));
        Assert.Equal("2026-10-10", AnalyticsDay.Of(new DateTimeOffset(2026, 10, 10, 20, 59, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void Day_strings_sort_like_dates()
    {
        var days = new[] { "2026-10-09", "2026-09-30", "2026-10-10" };
        Assert.Equal(["2026-09-30", "2026-10-09", "2026-10-10"], days.Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void Replay_coordinator_allows_one_pending_request_at_a_time()
    {
        var coordinator = new Hastane.Analytics.Api.Kafka.ReplayCoordinator();

        var first = coordinator.Request();
        var second = coordinator.Request();

        Assert.NotNull(first);
        Assert.Null(second);
        Assert.True(coordinator.TryTake(out var taken));
        Assert.Same(first, taken.Task);
        Assert.False(coordinator.TryTake(out _));
        Assert.NotNull(coordinator.Request()); // alındıktan sonra yenisi bırakılabilir
    }
}
