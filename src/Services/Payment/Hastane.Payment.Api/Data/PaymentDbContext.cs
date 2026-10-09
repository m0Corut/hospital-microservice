using Hastane.Payment.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace Hastane.Payment.Api.Data;

public class PaymentDbContext : DbContext
{
    public const string InvoiceSequenceName = "InvoiceNumberSeq";

    public PaymentDbContext(DbContextOptions<PaymentDbContext> options) : base(options)
    {
    }

    public DbSet<Entities.Payment> Payments => Set<Entities.Payment>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceLine> InvoiceLines => Set<InvoiceLine>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Fatura sıra numarası: SQL Server SEQUENCE. Her NEXT VALUE FOR çağrısı, kaç istek aynı anda gelirse gelsin
        // benzersiz bir sayı verir ("MAX+1" gibi oku-sonra-yaz yarışı yok).
        modelBuilder.HasSequence<int>(InvoiceSequenceName).StartsAt(1).IncrementsBy(1);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PaymentDbContext).Assembly);
    }
}
