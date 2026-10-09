using Hastane.Payment.Api.Dtos;
using Hastane.Payment.Api.Entities;
using Mapster;
using PaymentEntity = Hastane.Payment.Api.Entities.Payment;

namespace Hastane.Payment.Api.Mapping;

public class PaymentMappingConfig : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        // Yalnız çıkış yönü: CreatePaymentDto -> Payment eşlemesi bilerek yok (kart verisi entity'ye taşınmasın;
        // sunucu alanlarını servis tek tek atar). Status enum'u metne çevrilir.
        config.NewConfig<PaymentEntity, PaymentResultDto>()
            .Map(d => d.InvoiceId, s => s.Invoice == null ? (Guid?)null : s.Invoice.Id)
            .Map(d => d.InvoiceNumber, s => s.Invoice == null ? null : s.Invoice.InvoiceNumber);

        config.NewConfig<Invoice, ResultInvoiceDto>();
        config.NewConfig<Invoice, GetByIdInvoiceDto>();
        config.NewConfig<InvoiceLine, InvoiceLineDto>();
    }
}
