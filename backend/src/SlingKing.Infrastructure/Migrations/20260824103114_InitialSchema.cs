using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SlingKing.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "app_settings",
                columns: table => new
                {
                    key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    value = table.Column<string>(type: "text", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_app_settings", x => x.key);
                });

            migrationBuilder.CreateTable(
                name: "categories",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    parent_id = table.Column<int>(type: "integer", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_categories", x => x.id);
                    table.ForeignKey(
                        name: "fk_categories_categories_parent_id",
                        column: x => x.parent_id,
                        principalTable: "categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "customers",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    phone = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    instagram_handle = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    address = table.Column<string>(type: "text", nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_customers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "expense_categories",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_expense_categories", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "import_batches",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    file_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    total_rows = table.Column<int>(type: "integer", nullable: false),
                    valid_rows = table.Column<int>(type: "integer", nullable: false),
                    error_rows = table.Column<int>(type: "integer", nullable: false),
                    uploaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    committed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    uploaded_by = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_import_batches", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "inventory_cost_layers",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    variant_id = table.Column<long>(type: "bigint", nullable: false),
                    location_id = table.Column<int>(type: "integer", nullable: false),
                    source_type = table.Column<short>(type: "smallint", nullable: false),
                    source_id = table.Column<long>(type: "bigint", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    original_qty = table.Column<int>(type: "integer", nullable: false),
                    remaining_qty = table.Column<int>(type: "integer", nullable: false),
                    unit_cost = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inventory_cost_layers", x => x.id);
                    table.CheckConstraint("ck_layer_cost_non_negative", "unit_cost >= 0");
                    table.CheckConstraint("ck_layer_original_positive", "original_qty > 0");
                    table.CheckConstraint("ck_layer_remaining_non_negative", "remaining_qty >= 0");
                    table.CheckConstraint("ck_remaining_within_original", "remaining_qty <= original_qty");
                });

            migrationBuilder.CreateTable(
                name: "inventory_transactions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    variant_id = table.Column<long>(type: "bigint", nullable: false),
                    location_id = table.Column<int>(type: "integer", nullable: false),
                    type = table.Column<short>(type: "smallint", nullable: false),
                    physical_delta = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    reserved_delta = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    unit_cost = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    total_cost = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    reference_type = table.Column<short>(type: "smallint", nullable: true),
                    reference_id = table.Column<long>(type: "bigint", nullable: true),
                    reason = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inventory_transactions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "locations",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_locations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "number_sequences",
                columns: table => new
                {
                    key = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    prefix = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    next_value = table.Column<long>(type: "bigint", nullable: false),
                    padding = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_number_sequences", x => x.key);
                });

            migrationBuilder.CreateTable(
                name: "order_status_history",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    order_id = table.Column<long>(type: "bigint", nullable: false),
                    from_status = table.Column<short>(type: "smallint", nullable: true),
                    to_status = table.Column<short>(type: "smallint", nullable: false),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    changed_by = table.Column<long>(type: "bigint", nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_status_history", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "payments",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    order_id = table.Column<long>(type: "bigint", nullable: false),
                    direction = table.Column<short>(type: "smallint", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    method = table.Column<short>(type: "smallint", nullable: false),
                    reference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    paid_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    notes = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payments", x => x.id);
                    table.CheckConstraint("ck_payment_amount_positive", "amount > 0");
                });

            migrationBuilder.CreateTable(
                name: "returns",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    order_id = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    return_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    refund_amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    restocked_value = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    reason = table.Column<string>(type: "text", nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_returns", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sales_channels",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    uses_reservation = table.Column<bool>(type: "boolean", nullable: false),
                    requires_shipping = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sales_channels", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "stock_levels",
                columns: table => new
                {
                    variant_id = table.Column<long>(type: "bigint", nullable: false),
                    location_id = table.Column<int>(type: "integer", nullable: false),
                    physical_qty = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    reserved_qty = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    available_qty = table.Column<int>(type: "integer", nullable: false, computedColumnSql: "physical_qty - reserved_qty", stored: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_levels", x => new { x.variant_id, x.location_id });
                    table.CheckConstraint("ck_physical_non_negative", "physical_qty >= 0");
                    table.CheckConstraint("ck_reserved_non_negative", "reserved_qty >= 0");
                    table.CheckConstraint("ck_reserved_within_physical", "reserved_qty <= physical_qty");
                });

            migrationBuilder.CreateTable(
                name: "suppliers",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    contact_person = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    phone = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    address = table.Column<string>(type: "text", nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_suppliers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    password_hash = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    display_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "products",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    category_id = table.Column<int>(type: "integer", nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_products", x => x.id);
                    table.ForeignKey(
                        name: "fk_products_categories_category_id",
                        column: x => x.category_id,
                        principalTable: "categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "expenses",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    category_id = table.Column<int>(type: "integer", nullable: false),
                    channel_id = table.Column<int>(type: "integer", nullable: true),
                    amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    expense_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    description = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_expenses", x => x.id);
                    table.CheckConstraint("ck_expense_amount_positive", "amount > 0");
                    table.ForeignKey(
                        name: "fk_expenses_expense_categories_category_id",
                        column: x => x.category_id,
                        principalTable: "expense_categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "import_rows",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    batch_id = table.Column<long>(type: "bigint", nullable: false),
                    row_number = table.Column<int>(type: "integer", nullable: false),
                    raw_data = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    error_message = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    resolved_variant_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_import_rows", x => x.id);
                    table.ForeignKey(
                        name: "fk_import_rows_import_batches_batch_id",
                        column: x => x.batch_id,
                        principalTable: "import_batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "inventory_cost_consumptions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    cost_layer_id = table.Column<long>(type: "bigint", nullable: false),
                    inventory_transaction_id = table.Column<long>(type: "bigint", nullable: false),
                    order_item_id = table.Column<long>(type: "bigint", nullable: true),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    reversed_qty = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    unit_cost = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    total_cost = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    consumed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inventory_cost_consumptions", x => x.id);
                    table.CheckConstraint("ck_consumption_quantity_positive", "quantity > 0");
                    table.CheckConstraint("ck_consumption_reversed_non_negative", "reversed_qty >= 0");
                    table.CheckConstraint("ck_reversed_within_quantity", "reversed_qty <= quantity");
                    table.ForeignKey(
                        name: "fk_inventory_cost_consumptions_inventory_cost_layers_cost_laye",
                        column: x => x.cost_layer_id,
                        principalTable: "inventory_cost_layers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "return_items",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    return_id = table.Column<long>(type: "bigint", nullable: false),
                    order_item_id = table.Column<long>(type: "bigint", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    condition = table.Column<short>(type: "smallint", nullable: false),
                    cogs_reversed = table.Column<decimal>(type: "numeric(18,2)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_return_items", x => x.id);
                    table.CheckConstraint("ck_return_item_quantity_positive", "quantity > 0");
                    table.ForeignKey(
                        name: "fk_return_items_returns_return_id",
                        column: x => x.return_id,
                        principalTable: "returns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "orders",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    channel_id = table.Column<int>(type: "integer", nullable: false),
                    customer_id = table.Column<long>(type: "bigint", nullable: true),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    order_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    shipped_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    subtotal_amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    discount_amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    shipping_amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    total_amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    payment_status = table.Column<short>(type: "smallint", nullable: false),
                    notes = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_orders", x => x.id);
                    table.ForeignKey(
                        name: "fk_orders_customers_customer_id",
                        column: x => x.customer_id,
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_orders_sales_channels_channel_id",
                        column: x => x.channel_id,
                        principalTable: "sales_channels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "purchases",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    supplier_id = table.Column<int>(type: "integer", nullable: false),
                    purchase_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    total_amount = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_purchases", x => x.id);
                    table.ForeignKey(
                        name: "fk_purchases_suppliers_supplier_id",
                        column: x => x.supplier_id,
                        principalTable: "suppliers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "refresh_tokens",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    replaced_by_token_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_refresh_tokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_refresh_tokens_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "product_variants",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    product_id = table.Column<long>(type: "bigint", nullable: false),
                    sku = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    colour = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    size = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    selling_price = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    low_stock_threshold = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product_variants", x => x.id);
                    table.CheckConstraint("ck_variant_price_non_negative", "selling_price >= 0");
                    table.ForeignKey(
                        name: "fk_product_variants_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "order_items",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    order_id = table.Column<long>(type: "bigint", nullable: false),
                    variant_id = table.Column<long>(type: "bigint", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    discount_amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    line_total = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    cogs_amount = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    product_name_snapshot = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    sku_snapshot = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_items", x => x.id);
                    table.CheckConstraint("ck_order_item_quantity_positive", "quantity > 0");
                    table.ForeignKey(
                        name: "fk_order_items_orders_order_id",
                        column: x => x.order_id,
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "purchase_items",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    purchase_id = table.Column<long>(type: "bigint", nullable: false),
                    variant_id = table.Column<long>(type: "bigint", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    unit_cost = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    line_total = table.Column<decimal>(type: "numeric(18,2)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_purchase_items", x => x.id);
                    table.CheckConstraint("ck_purchase_item_quantity_positive", "quantity > 0");
                    table.ForeignKey(
                        name: "fk_purchase_items_purchases_purchase_id",
                        column: x => x.purchase_id,
                        principalTable: "purchases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_categories_parent_id",
                table: "categories",
                column: "parent_id");

            migrationBuilder.CreateIndex(
                name: "ix_customers_instagram",
                table: "customers",
                column: "instagram_handle");

            migrationBuilder.CreateIndex(
                name: "ix_customers_phone",
                table: "customers",
                column: "phone");

            migrationBuilder.CreateIndex(
                name: "ux_expense_categories_name",
                table: "expense_categories",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_expenses_category_id",
                table: "expenses",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_expenses_date",
                table: "expenses",
                column: "expense_date");

            migrationBuilder.CreateIndex(
                name: "ix_import_rows_batch",
                table: "import_rows",
                column: "batch_id");

            migrationBuilder.CreateIndex(
                name: "ix_consumptions_order_item",
                table: "inventory_cost_consumptions",
                column: "order_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_cost_consumptions_cost_layer_id",
                table: "inventory_cost_consumptions",
                column: "cost_layer_id");

            migrationBuilder.CreateIndex(
                name: "ix_layers_fifo",
                table: "inventory_cost_layers",
                columns: new[] { "variant_id", "location_id", "received_at", "id" },
                filter: "remaining_qty > 0");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_transactions_variant_time",
                table: "inventory_transactions",
                columns: new[] { "variant_id", "location_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_order_items_order_id",
                table: "order_items",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_order_status_history_order",
                table: "order_status_history",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_orders_channel_id",
                table: "orders",
                column: "channel_id");

            migrationBuilder.CreateIndex(
                name: "ix_orders_completed_at",
                table: "orders",
                column: "completed_at");

            migrationBuilder.CreateIndex(
                name: "ix_orders_customer_id",
                table: "orders",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ux_orders_number",
                table: "orders",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payments_order",
                table: "payments",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_product_variants_product_id",
                table: "product_variants",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ux_product_variants_sku",
                table: "product_variants",
                column: "sku",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_products_category_id",
                table: "products",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ux_products_code",
                table: "products",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_purchase_items_purchase_id",
                table: "purchase_items",
                column: "purchase_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchases_supplier_id",
                table: "purchases",
                column: "supplier_id");

            migrationBuilder.CreateIndex(
                name: "ux_purchases_number",
                table: "purchases",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_user_id",
                table: "refresh_tokens",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ux_refresh_tokens_hash",
                table: "refresh_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_return_items_return_id",
                table: "return_items",
                column: "return_id");

            migrationBuilder.CreateIndex(
                name: "ix_returns_order",
                table: "returns",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ux_returns_number",
                table: "returns",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_sales_channels_code",
                table: "sales_channels",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_users_email",
                table: "users",
                column: "email",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "app_settings");

            migrationBuilder.DropTable(
                name: "expenses");

            migrationBuilder.DropTable(
                name: "import_rows");

            migrationBuilder.DropTable(
                name: "inventory_cost_consumptions");

            migrationBuilder.DropTable(
                name: "inventory_transactions");

            migrationBuilder.DropTable(
                name: "locations");

            migrationBuilder.DropTable(
                name: "number_sequences");

            migrationBuilder.DropTable(
                name: "order_items");

            migrationBuilder.DropTable(
                name: "order_status_history");

            migrationBuilder.DropTable(
                name: "payments");

            migrationBuilder.DropTable(
                name: "product_variants");

            migrationBuilder.DropTable(
                name: "purchase_items");

            migrationBuilder.DropTable(
                name: "refresh_tokens");

            migrationBuilder.DropTable(
                name: "return_items");

            migrationBuilder.DropTable(
                name: "stock_levels");

            migrationBuilder.DropTable(
                name: "expense_categories");

            migrationBuilder.DropTable(
                name: "import_batches");

            migrationBuilder.DropTable(
                name: "inventory_cost_layers");

            migrationBuilder.DropTable(
                name: "orders");

            migrationBuilder.DropTable(
                name: "products");

            migrationBuilder.DropTable(
                name: "purchases");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "returns");

            migrationBuilder.DropTable(
                name: "customers");

            migrationBuilder.DropTable(
                name: "sales_channels");

            migrationBuilder.DropTable(
                name: "categories");

            migrationBuilder.DropTable(
                name: "suppliers");
        }
    }
}
