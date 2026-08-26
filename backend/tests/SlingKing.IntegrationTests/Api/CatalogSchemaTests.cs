using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shouldly;
using SlingKing.IntegrationTests.Fixtures;
using Xunit;

namespace SlingKing.IntegrationTests.Api;

/// <summary>
/// These invariants are the database's job, not the service's. Each test writes raw SQL so
/// it proves the constraint itself rather than proving that a C# guard ran first.
/// </summary>
[Collection("database")]
public class CatalogSchemaTests
{
    private readonly TestSchemaFixture _schema;

    public CatalogSchemaTests(TestSchemaFixture schema) => _schema = schema;

    private async Task<NpgsqlConnection> OpenAsync()
    {
        var connection = new NpgsqlConnection(_schema.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> SeedProductAsync(NpgsqlConnection connection, string code)
    {
        await using var command = new NpgsqlCommand(
            @"insert into products (code, name, is_active, created_at, updated_at)
              values (@code, 'Schema Probe', true, now(), now())
              returning id;", connection);
        command.Parameters.AddWithValue("code", code);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static string InsertVariant(long productId, string sku, string colour, bool isActive) =>
        $@"insert into product_variants
             (product_id, sku, colour, size, selling_price, low_stock_threshold,
              is_active, created_at, updated_at)
           values ({productId}, '{sku}', {colour}, null, 100, 0, {isActive.ToString().ToLower()},
                   now(), now());";

    [Fact]
    public async Task Two_active_variants_cannot_share_a_colour_and_size()
    {
        await using var connection = await OpenAsync();
        var productId = await SeedProductAsync(connection, $"PRB-{Guid.NewGuid():N}"[..12]);

        await ExecuteAsync(connection, InsertVariant(productId, $"S1-{productId}", "'Tan'", true));

        var duplicate = await Should.ThrowAsync<PostgresException>(async () =>
            await ExecuteAsync(connection, InsertVariant(productId, $"S2-{productId}", "'Tan'", true)));

        duplicate.SqlState.ShouldBe("23505");
        duplicate.ConstraintName.ShouldBe("ux_variant_combination");
    }

    /// <summary>
    /// Postgres treats every NULL as distinct, so a plain unique index would NOT catch this.
    /// The coalesce in the index definition is what makes "no colour twice" a violation.
    /// </summary>
    [Fact]
    public async Task Two_active_variants_cannot_both_have_no_colour_and_no_size()
    {
        await using var connection = await OpenAsync();
        var productId = await SeedProductAsync(connection, $"PRC-{Guid.NewGuid():N}"[..12]);

        await ExecuteAsync(connection, InsertVariant(productId, $"N1-{productId}", "null", true));

        var duplicate = await Should.ThrowAsync<PostgresException>(async () =>
            await ExecuteAsync(connection, InsertVariant(productId, $"N2-{productId}", "null", true)));

        duplicate.SqlState.ShouldBe("23505");
    }

    /// <summary>
    /// The index is partial on is_active, so retiring Tan and introducing a new Tan is
    /// allowed. Without the predicate the owner could never reuse a colour she had retired.
    /// </summary>
    [Fact]
    public async Task A_retired_variant_does_not_block_reusing_its_colour()
    {
        await using var connection = await OpenAsync();
        var productId = await SeedProductAsync(connection, $"PRD-{Guid.NewGuid():N}"[..12]);

        await ExecuteAsync(connection, InsertVariant(productId, $"R1-{productId}", "'Tan'", false));
        await ExecuteAsync(connection, InsertVariant(productId, $"R2-{productId}", "'Tan'", true));

        await using var count = new NpgsqlCommand(
            $"select count(*) from product_variants where product_id = {productId};", connection);

        // Parenthesised deliberately: without the outer pair the cast binds after ShouldBe,
        // which calls ShouldBe(2L) on object and does not compile as intended.
        ((long)(await count.ExecuteScalarAsync())!).ShouldBe(2L);
    }

    [Fact]
    public async Task A_negative_low_stock_threshold_is_rejected()
    {
        await using var connection = await OpenAsync();
        var productId = await SeedProductAsync(connection, $"PRE-{Guid.NewGuid():N}"[..12]);

        var rejected = await Should.ThrowAsync<PostgresException>(async () =>
            await ExecuteAsync(connection,
                $@"insert into product_variants
                     (product_id, sku, colour, size, selling_price, low_stock_threshold,
                      is_active, created_at, updated_at)
                   values ({productId}, 'NEG-{productId}', null, null, 100, -1, true, now(), now());"));

        rejected.SqlState.ShouldBe("23514");
        rejected.ConstraintName.ShouldBe("ck_variant_threshold_non_negative");
    }

    [Fact]
    public async Task Category_names_are_unique_regardless_of_case()
    {
        await using var connection = await OpenAsync();
        var name = $"Probe {Guid.NewGuid():N}"[..16];

        await ExecuteAsync(connection,
            $"insert into categories (name, is_active, created_at) values ('{name}', true, now());");

        var duplicate = await Should.ThrowAsync<PostgresException>(async () =>
            await ExecuteAsync(connection,
                $"insert into categories (name, is_active, created_at) values ('{name.ToUpper()}', true, now());"));

        duplicate.SqlState.ShouldBe("23505");
    }
}
