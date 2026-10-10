using Hastane.Order.Api.Entities;
using Hastane.Order.Api.Mapping;
using Hastane.Order.Api.Services;
using Mapster;

namespace Hastane.Order.Tests;

// Veritabanı/HTTP gerektirmeyen saf kurallar.
public class UnitTests
{
    [Theory]
    [InlineData(2026, 1, "SIP-2026-000001")]
    [InlineData(2026, 123, "SIP-2026-000123")]
    [InlineData(2027, 999999, "SIP-2027-999999")]
    public void OrderNumber_IsReadable_AndZeroPadded(int year, int seq, string expected)
        => Assert.Equal(expected, OrderNumberGenerator.Format(year, seq));

    [Theory]
    [InlineData(OrderStatus.Received, OrderStatus.Preparing, true)]
    [InlineData(OrderStatus.Preparing, OrderStatus.Shipped, true)]
    [InlineData(OrderStatus.Shipped, OrderStatus.Delivered, true)]
    [InlineData(OrderStatus.Received, OrderStatus.Cancelled, true)]
    [InlineData(OrderStatus.Preparing, OrderStatus.Cancelled, true)]
    [InlineData(OrderStatus.Received, OrderStatus.Shipped, false)]
    [InlineData(OrderStatus.Received, OrderStatus.Delivered, false)]
    [InlineData(OrderStatus.Preparing, OrderStatus.Received, false)]
    [InlineData(OrderStatus.Shipped, OrderStatus.Cancelled, false)]
    [InlineData(OrderStatus.Delivered, OrderStatus.Cancelled, false)]
    [InlineData(OrderStatus.Cancelled, OrderStatus.Received, false)]
    [InlineData(OrderStatus.Received, OrderStatus.Received, false)]
    public void Transitions_FollowTheStateMachine(OrderStatus from, OrderStatus to, bool allowed)
        => Assert.Equal(allowed, OrderStatusTransitions.IsAllowed(from, to));

    [Theory]
    [InlineData(OrderStatus.Received, true, true)]
    [InlineData(OrderStatus.Preparing, false, true)]
    [InlineData(OrderStatus.Shipped, false, false)]
    [InlineData(OrderStatus.Delivered, false, false)]
    [InlineData(OrderStatus.Cancelled, false, false)]
    public void CancelRules_PatientOnlyReceived_AdminReceivedOrPreparing(OrderStatus status, bool patient, bool admin)
    {
        Assert.Equal(patient, OrderStatusTransitions.PatientCanCancel(status));
        Assert.Equal(admin, OrderStatusTransitions.AdminCanCancel(status));
    }

    [Theory]
    [InlineData("0123456789abcdef", true)]
    [InlineData("3f2b8c1e-9d4a-4c7b-8a55-1e2d3c4b5a69", true)]
    [InlineData("kisa", false)]
    [InlineData("bosluk var bosluk var bosluk", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IdempotencyKeyRules_AcceptOnlyUnguessableKeys(string? key, bool valid)
        => Assert.Equal(valid, IdempotencyKeyRules.IsValid(key));

    [Fact]
    public void MappingConfig_Compiles()
    {
        var config = new TypeAdapterConfig();
        new OrderMappingConfig().Register(config);

        config.Compile();
    }

    [Fact]
    public void MappingConfig_ItemCount_ComesFromItems()
    {
        var config = new TypeAdapterConfig();
        new OrderMappingConfig().Register(config);
        var order = new Api.Entities.Order
        {
            OrderNumber = "SIP-2026-000001",
            Items = [new OrderItem { Quantity = 1 }, new OrderItem { Quantity = 2 }],
        };

        var dto = order.Adapt<Api.Dtos.ResultOrderDto>(config);

        Assert.Equal(2, dto.ItemCount);
    }
}
