using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SlingKing.Infrastructure.Migrations
{
    /// <summary>
    /// Three invariants that belong in the database rather than in a service.
    ///
    /// The coalesce in ux_variant_combination is load-bearing: Postgres treats every NULL as
    /// distinct, so a plain unique index on (product_id, colour, size) would happily accept
    /// ('Tan', NULL) twice. The predicate is load-bearing too — without "where is_active" a
    /// retired colour could never be reintroduced.
    ///
    /// Written as raw SQL because neither an expression index nor a partial index is
    /// expressible in the EF model, which is also why the scaffolded migration was empty.
    /// </summary>
    public partial class CatalogConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
create unique index ux_variant_combination
    on product_variants (product_id, coalesce(colour, ''), coalesce(size, ''))
 where is_active;");

            migrationBuilder.Sql(@"
alter table product_variants
  add constraint ck_variant_threshold_non_negative
  check (low_stock_threshold >= 0);");

            migrationBuilder.Sql(@"
create unique index ux_categories_name on categories (lower(name));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("drop index if exists ux_categories_name;");
            migrationBuilder.Sql(
                "alter table product_variants drop constraint if exists ck_variant_threshold_non_negative;");
            migrationBuilder.Sql("drop index if exists ux_variant_combination;");
        }
    }
}
