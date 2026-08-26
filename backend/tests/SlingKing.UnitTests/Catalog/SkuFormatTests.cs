using Shouldly;
using Xunit;

namespace SlingKing.UnitTests.Catalog;

/// <summary>
/// The SKU format is a contract with the owner's eyes, not just with the database: she reads
/// these off a bag. Two digits, zero-padded, so they sort and scan.
/// </summary>
public class SkuFormatTests
{
    private static string Sku(string productCode, int ordinal) => $"{productCode}-V{ordinal:00}";

    [Theory]
    [InlineData(1, "BAG-000001-V01")]
    [InlineData(2, "BAG-000001-V02")]
    [InlineData(9, "BAG-000001-V09")]
    [InlineData(10, "BAG-000001-V10")]
    [InlineData(99, "BAG-000001-V99")]
    [InlineData(100, "BAG-000001-V100")]
    public void Ordinals_are_zero_padded_to_two_digits_and_grow_beyond(int ordinal, string expected)
    {
        Sku("BAG-000001", ordinal).ShouldBe(expected);
    }

    [Fact]
    public void A_sku_fits_the_48_character_column()
    {
        Sku("BAG-999999", 999).Length.ShouldBeLessThanOrEqualTo(48);
    }
}
