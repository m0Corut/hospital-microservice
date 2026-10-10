using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Hastane.Pharmacy.Api.Common;

public static class SqlErrors
{
    // 2601 = benzersiz dizin ihlali, 2627 = UNIQUE kısıt ihlali. Barkod ve PrescriptionId yarışları buraya düşer.
    public static bool IsUniqueViolation(DbUpdateException ex)
        => ex.InnerException is SqlException { Number: 2601 or 2627 };
}
