using System;
using Cafe.API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Cafe.API.Migrations
{
    /// <summary>Честный знак: документы вывода из оборота</summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261025180000_AddChzDocuments")]
    public partial class AddChzDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "chz_documents",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    product_group = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    action = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    action_date = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    document_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    codes = table.Column<string>(type: "text", nullable: false),
                    codes_count = table.Column<int>(type: "integer", nullable: false),
                    product_document = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    chz_document_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    result = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    comment = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    stock_document_id = table.Column<int>(type: "integer", nullable: true),
                    created_by_user_id = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    sent_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    checked_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_chz_documents", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_chz_documents_created_at",
                table: "chz_documents",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "IX_chz_documents_status",
                table: "chz_documents",
                column: "status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "chz_documents");
        }
    }
}
