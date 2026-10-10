namespace Hastane.WebUI.Models.Api.Admin;

// Branch servisi (GET/POST/PUT/DELETE /gateway/branches) JSON'unun WebUI karşılığı.
public sealed class AdminBranchApiModel
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Icon { get; set; }
}

public sealed class AdminBranchWriteBody
{
    public string? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Icon { get; set; }
}
