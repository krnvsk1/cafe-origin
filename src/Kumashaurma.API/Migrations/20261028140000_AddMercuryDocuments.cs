using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Kumashaurma.API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Kumashaurma.API.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261028140000_AddMercuryDocuments")]
    public partial class AddMercuryDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "mercury_documents",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    uuid = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    issue_date = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    vet_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    product_name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    volume = table.Column<decimal>(type: "numeric(14,3)", nullable: false),
                    unit_name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    production_date = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    expiry_date = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    consignor_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    waybill = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    xml = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    application_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    stock_entry_guid = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    processed_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    processed_by_user_id = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mercury_documents", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_mercury_documents_issue_date",
                table: "mercury_documents",
                column: "issue_date");

            migrationBuilder.CreateIndex(
                name: "IX_mercury_documents_status",
                table: "mercury_documents",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_mercury_documents_uuid",
                table: "mercury_documents",
                column: "uuid",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mercury_documents");
        }
    }
}
