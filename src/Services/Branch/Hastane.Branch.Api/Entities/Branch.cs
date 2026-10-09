using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Hastane.Branch.Api.Entities;

// Veritabanındaki kaydın birebir şekli. API'nin dışına çıkmaz; dışarıya DTO gider.
public class Branch
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    // İleride arayüzde Font Awesome sınıfı olarak kullanılacak, ör. "fa-heart-pulse".
    public string? Icon { get; set; }
}
