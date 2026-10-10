using Hastane.Pharmacy.Api.Common;
using MediatR;

namespace Hastane.Pharmacy.Api.Features.Medicines.Queries.GetMedicines;

// SORGU = "sistemi DEĞİŞTİRMEDEN bir şey sor". Komutlarla aynı ayrım: bu sınıfın handler'ı hiçbir zaman SaveChanges çağırmaz.
// Form metin gelir ("Tablet"); geçersizse validator 400 verir.
public sealed record GetMedicinesQuery(string? Search = null, string? Form = null, int Page = 1, int PageSize = 20)
    : IRequest<OperationResult<PagedResult<MedicineDto>>>;
