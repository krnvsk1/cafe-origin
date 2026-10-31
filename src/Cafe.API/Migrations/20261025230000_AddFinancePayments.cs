using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Cafe.API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;

#nullable disable

namespace Cafe.API.Migrations
{
    /// <summary>«Финансы»: срок оплаты из УПД и отметка об оплате у накладных СБИС; отсрочка и реквизиты у поставщиков</summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261025230000_AddFinancePayments")]
    public partial class AddFinancePayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "bank_account",
                table: "suppliers",
                type: "character varying(34)",
                maxLength: 34,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "bank_name",
                table: "suppliers",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "bik",
                table: "suppliers",
                type: "character varying(9)",
                maxLength: 9,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "corr_account",
                table: "suppliers",
                type: "character varying(34)",
                maxLength: 34,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "payment_delay_days",
                table: "suppliers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "paid_at",
                table: "edo_documents",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "paid_by_user_id",
                table: "edo_documents",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "terms_parsed",
                table: "edo_documents",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "upd_due_date",
                table: "edo_documents",
                type: "timestamp without time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "bank_account",
                table: "suppliers");

            migrationBuilder.DropColumn(
                name: "bank_name",
                table: "suppliers");

            migrationBuilder.DropColumn(
                name: "bik",
                table: "suppliers");

            migrationBuilder.DropColumn(
                name: "corr_account",
                table: "suppliers");

            migrationBuilder.DropColumn(
                name: "payment_delay_days",
                table: "suppliers");

            migrationBuilder.DropColumn(
                name: "paid_at",
                table: "edo_documents");

            migrationBuilder.DropColumn(
                name: "paid_by_user_id",
                table: "edo_documents");

            migrationBuilder.DropColumn(
                name: "terms_parsed",
                table: "edo_documents");

            migrationBuilder.DropColumn(
                name: "upd_due_date",
                table: "edo_documents");
        }
    }
}
