using Cafe.API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cafe.API.Migrations
{
    /// <summary>
    /// Колонка print_jobs.result (ответ кассы/УТМ через программу на кассовом ПК). Её дописали в AddPrintJobs уже после того,
    /// как та миграция прошла на некоторых базах, — там колонки нет. IF NOT EXISTS: на новых базах она уже есть.
    /// </summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261104150000_PrintJobsResultColumn")]
    public partial class PrintJobsResultColumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE print_jobs ADD COLUMN IF NOT EXISTS result text;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Колонка — часть AddPrintJobs; при откате этой миграции не удаляем
        }
    }
}
