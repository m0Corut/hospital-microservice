using System.ComponentModel.DataAnnotations;

namespace Hastane.Identity.Api.Dtos;

public class CreateRoleDto
{
    // Harf/rakam, en çok 50: boşluk, noktalama ve çok uzun adlar claim'e ve politika adına sorun çıkarmasın.
    [Required, StringLength(50), RegularExpression("^[A-Za-z0-9]+$", ErrorMessage = "Rol adı yalnız harf ve rakam içerebilir.")]
    public string Name { get; set; } = string.Empty;
}
