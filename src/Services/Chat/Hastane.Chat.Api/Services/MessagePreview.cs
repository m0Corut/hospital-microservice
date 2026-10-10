namespace Hastane.Chat.Api.Services;

public static class MessagePreview
{
    public const int MaxLength = 80;

    // Satır sonları ve ardışık boşluklar tek boşluğa insin (liste satırı tek satır kalsın); en çok 80 karakter.
    public static string Create(string text)
    {
        var collapsed = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (collapsed.Length <= MaxLength) return collapsed;
        var cut = MaxLength;
        if (char.IsHighSurrogate(collapsed[cut - 1])) cut--; // vekil çiftini ortadan bölme
        return collapsed[..cut];
    }
}
