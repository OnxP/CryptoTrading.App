using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CryptoTrading.App.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "algo_state",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    strategy_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    instance_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    state_json = table.Column<string>(type: "jsonb", nullable: true),
                    last_bar_time = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_1h_bar_count = table.Column<int>(type: "integer", nullable: false),
                    last_4h_bar_count = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_algo_state", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "candlesticks",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    exchange_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    symbol = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    interval = table.Column<int>(type: "integer", nullable: false),
                    open_time = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    close_time = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    open = table.Column<double>(type: "double precision", nullable: false),
                    high = table.Column<double>(type: "double precision", nullable: false),
                    low = table.Column<double>(type: "double precision", nullable: false),
                    close = table.Column<double>(type: "double precision", nullable: false),
                    volume = table.Column<decimal>(type: "numeric", nullable: false),
                    quote_volume = table.Column<decimal>(type: "numeric", nullable: false),
                    number_of_trades = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_candlesticks", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "exchange_configs",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    exchange_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    api_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    api_secret = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    run_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    refresh_interval_minutes = table.Column<int>(type: "integer", nullable: false),
                    last_refreshed = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_exchange_configs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "model_artifacts",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    strategy_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    trained_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    manifest_json = table.Column<string>(type: "jsonb", nullable: true),
                    model_1h_onnx = table.Column<byte[]>(type: "bytea", nullable: true),
                    model_4h_onnx = table.Column<byte[]>(type: "bytea", nullable: true),
                    val_r2_1h = table.Column<double>(type: "double precision", nullable: false),
                    val_r2_4h = table.Column<double>(type: "double precision", nullable: false),
                    train_n_bars = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    promoted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_model_artifacts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "strategy_parameters",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    strategy_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    parameter_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    value = table.Column<double>(type: "double precision", nullable: false),
                    min_value = table.Column<double>(type: "double precision", nullable: false),
                    max_value = table.Column<double>(type: "double precision", nullable: false),
                    step_size = table.Column<double>(type: "double precision", nullable: false),
                    category = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    parameter_set_id = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    last_modified = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_strategy_parameters", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "trade_log",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    exchange_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    strategy_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    symbol = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    side = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    entry_price = table.Column<double>(type: "double precision", nullable: false),
                    exit_price = table.Column<double>(type: "double precision", nullable: false),
                    quantity = table.Column<double>(type: "double precision", nullable: false),
                    pnl = table.Column<double>(type: "double precision", nullable: false),
                    entry_time = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    exit_time = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    signal_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    metadata_json = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trade_log", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "trading_pairs",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    exchange_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    symbol = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trading_pairs", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_algo_state_strategy_instance",
                table: "algo_state",
                columns: new[] { "strategy_name", "instance_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_candlesticks_exchange_symbol_interval_time",
                table: "candlesticks",
                columns: new[] { "exchange_id", "symbol", "interval", "open_time" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_candlesticks_symbol_interval_time",
                table: "candlesticks",
                columns: new[] { "symbol", "interval", "open_time" });

            migrationBuilder.CreateIndex(
                name: "ix_exchange_configs_exchange_id",
                table: "exchange_configs",
                column: "exchange_id");

            migrationBuilder.CreateIndex(
                name: "ix_model_artifacts_strategy_active",
                table: "model_artifacts",
                columns: new[] { "strategy_name", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_model_artifacts_strategy_version",
                table: "model_artifacts",
                columns: new[] { "strategy_name", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_trade_log_strategy_symbol_time",
                table: "trade_log",
                columns: new[] { "strategy_name", "symbol", "entry_time" });

            migrationBuilder.CreateIndex(
                name: "ix_trading_pairs_exchange_symbol",
                table: "trading_pairs",
                columns: new[] { "exchange_id", "symbol" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "algo_state");

            migrationBuilder.DropTable(
                name: "candlesticks");

            migrationBuilder.DropTable(
                name: "exchange_configs");

            migrationBuilder.DropTable(
                name: "model_artifacts");

            migrationBuilder.DropTable(
                name: "strategy_parameters");

            migrationBuilder.DropTable(
                name: "trade_log");

            migrationBuilder.DropTable(
                name: "trading_pairs");
        }
    }
}
