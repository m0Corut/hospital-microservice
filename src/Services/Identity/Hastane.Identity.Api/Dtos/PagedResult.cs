namespace Hastane.Identity.Api.Dtos;

// Sayfalı liste zarfı (Payment / Doctor ile aynı biçim): items + totalCount + page + pageSize.
public class PagedResult<T>
{
    public List<T> Items { get; set; } = [];
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}
