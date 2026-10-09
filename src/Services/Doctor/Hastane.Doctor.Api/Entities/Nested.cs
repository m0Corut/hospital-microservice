namespace Hastane.Doctor.Api.Entities;

public class Education
{
    public string School { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public int StartYear { get; set; }
    public int EndYear { get; set; }
}

public class Experience
{
    public string Place { get; set; } = string.Empty;
    public string Position { get; set; } = string.Empty;
    public int StartYear { get; set; }
    // Boşsa hâlâ çalışıyor demektir.
    public int? EndYear { get; set; }
}

public class Award
{
    public string Title { get; set; } = string.Empty;
    public int Year { get; set; }
    public string? Description { get; set; }
}

public class ClinicLocation
{
    public string ClinicName { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string? District { get; set; }
    public string? Address { get; set; }
    public string? Floor { get; set; }
}
