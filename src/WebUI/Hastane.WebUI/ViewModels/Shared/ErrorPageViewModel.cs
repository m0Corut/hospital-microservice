namespace Hastane.WebUI.ViewModels.Shared;

public sealed record ErrorPageViewModel(int Code, string Title, string Message, string? RequestId = null);
