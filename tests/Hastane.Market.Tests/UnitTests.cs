using System.ComponentModel.DataAnnotations;
using Hastane.Market.Api.Caching;
using Hastane.Market.Api.Dtos;
using Hastane.Market.Api.Entities;
using Hastane.Market.Api.Mapping;
using Hastane.Market.Api.Security;
using Hastane.Market.Api.Validation;
using Mapster;

namespace Hastane.Market.Tests;

// Konteyner gerektirmeyen saf mantık testleri.
public class UnitTests
{
    [Theory]
    [InlineData("Vitamin", true, ProductCategory.Vitamin)]
    [InlineData("vitamin", true, ProductCategory.Vitamin)]
    [InlineData(" MedicalDevice ", true, ProductCategory.MedicalDevice)]
    [InlineData("MOTHERBABY", true, ProductCategory.MotherBaby)]
    [InlineData("1", false, default(ProductCategory))]
    [InlineData("Foo", false, default(ProductCategory))]
    [InlineData("", false, default(ProductCategory))]
    [InlineData(null, false, default(ProductCategory))]
    public void ProductCategories_TryParse_AcceptsNamesOnly(string? text, bool ok, ProductCategory expected)
    {
        Assert.Equal(ok, ProductCategories.TryParse(text, out var category));
        Assert.Equal(expected, category);
    }

    [Fact]
    public void ProductCategory_HasTheFiveFixedValues()
        => Assert.Equal(["Vitamin", "Care", "MedicalDevice", "MotherBaby", "FirstAid"], Enum.GetNames<ProductCategory>());

    [Theory]
    [InlineData("secret", "secret", true)]
    [InlineData("secret", "Secret", false)]
    [InlineData("secret", "secre", false)]
    [InlineData("secret", "secret ", false)]
    [InlineData("secret", "", false)]
    [InlineData("secret", null, false)]
    [InlineData("", "", false)]
    [InlineData("", "anything", false)]
    [InlineData(null, "anything", false)]
    [InlineData("   ", "   ", false)]
    public void InternalApiKey_KeysMatch(string? configured, string? provided, bool expected)
        => Assert.Equal(expected, InternalApiKeyAttribute.KeysMatch(configured, provided));

    [Theory]
    [InlineData(10.5, true)]
    [InlineData(10.55, true)]
    [InlineData(10.555, false)]
    [InlineData(0.001, false)]
    public void MaxDecimalPlaces_AllowsAtMostTwo(double value, bool valid)
        => Assert.Equal(valid, new MaxDecimalPlacesAttribute(2).IsValid((decimal)value));

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("/img/products/a.svg", true)]
    [InlineData("https://cdn.example.com/a.png", true)]
    [InlineData("http://cdn.example.com/a.png", true)]
    [InlineData("//evil.example/a.png", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("data:image/svg+xml;base64,AAAA", false)]
    [InlineData("ftp://x/y", false)]
    [InlineData("img/a.png", false)]
    [InlineData("/img/a\\b.png", false)]
    public void ProductImageUrl_AcceptsOnlyLocalPathOrHttp(string? value, bool valid)
        => Assert.Equal(valid, new ProductImageUrlAttribute().IsValid(value));

    [Fact]
    public void ListKey_IsDeterministic_CaseInsensitiveForSearch_AndDiffersByParameters()
    {
        var a = ProductCache.ListKey("Vitamin C", "Vitamin", 1, 12);

        Assert.Equal(a, ProductCache.ListKey(" vitamin c ", "vitamin", 1, 12));
        Assert.NotEqual(a, ProductCache.ListKey("Vitamin C", "Vitamin", 2, 12));
        Assert.NotEqual(a, ProductCache.ListKey("Vitamin C", "Care", 1, 12));
        Assert.NotEqual(a, ProductCache.ListKey("Vitamin C", "Vitamin", 1, 24));
        Assert.StartsWith(ProductCache.ListKeyPrefix, a);
        // Arama metni (kullanıcı girdisi) anahtarda düz yazılmaz.
        Assert.DoesNotContain("Vitamin", a, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProductKey_UsesProductPrefix()
    {
        var id = Guid.NewGuid();
        Assert.Equal($"product:{id}", ProductCache.ProductKey(id));
    }

    [Fact]
    public void RedisCartStore_KeyIsCartPrefixedUserId()
        => Assert.Equal("cart:user-1", RedisCartStore.Key("user-1"));

    [Fact]
    public void Mapster_Configuration_Compiles_AndMapsCategoryToText()
    {
        var config = new TypeAdapterConfig();
        new MarketMappingConfig().Register(config);
        config.Compile();

        var product = new Product { Id = Guid.NewGuid(), Name = "A", Category = ProductCategory.FirstAid, Price = 5m, Stock = 2, IsActive = true };
        var dto = product.Adapt<ResultProductDto>(config);

        Assert.Equal("FirstAid", dto.Category);
        Assert.Equal(5m, dto.Price);
    }

    [Fact]
    public void CreateProductDto_Validation_RejectsMissingCategoryAndBadPrice()
    {
        var dto = new CreateProductDto { Name = "Geçerli ad", Price = 0m };

        var results = new List<ValidationResult>();
        var valid = Validator.TryValidateObject(dto, new ValidationContext(dto), results, validateAllProperties: true);

        Assert.False(valid);
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(CreateProductDto.Category)));
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(CreateProductDto.Price)));
    }
}
