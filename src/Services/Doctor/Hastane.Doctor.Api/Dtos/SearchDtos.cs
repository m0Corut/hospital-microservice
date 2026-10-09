using System.ComponentModel.DataAnnotations;
using Hastane.Doctor.Api.Entities;

namespace Hastane.Doctor.Api.Dtos;

public class PagedResult<T>
{
    public List<T> Items { get; set; } = [];
    public long TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

// Arama sonucu kartı. Phone/Email bilerek YOK.
public class DoctorSearchItemDto
{
    public string Id { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string? Degrees { get; set; }
    public DoctorGender Gender { get; set; }
    public string BranchId { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public List<string> Services { get; set; } = [];
    // İlk 4 klinik fotoğrafı (kart önizlemesi).
    public List<string> Gallery { get; set; } = [];
    public string? PrimaryCity { get; set; }
    public decimal? MinFee { get; set; }
    public decimal? MaxFee { get; set; }
    public string? Currency { get; set; }
}

// Toplu getirme: reçete "Created by Dr. X", randevu listeleri. Phone/Email YOK.
public class DoctorSummaryDto
{
    public string Id { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string? Degrees { get; set; }
    public string BranchId { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
}

// Sorgu parametreleri. Metin alanları ham gelir; ayrıştırma ve 400'ler serviste (tek doğruluk yeri).
public class DoctorSearchQuery
{
    [MaxLength(100)]
    public string? Text { get; set; }
    public string? Gender { get; set; }
    public string? BranchIds { get; set; }
    public string? City { get; set; }
    [Range(0, double.MaxValue)]
    public decimal? MinFee { get; set; }
    [Range(0, double.MaxValue)]
    public decimal? MaxFee { get; set; }
    public string? Date { get; set; }
    public string? Sort { get; set; }
    [Range(1, 10000)]
    public int Page { get; set; } = 1;
    [Range(1, 50)]
    public int PageSize { get; set; } = 10;
}
