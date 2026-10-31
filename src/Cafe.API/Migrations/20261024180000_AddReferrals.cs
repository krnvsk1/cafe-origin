using System;
using Cafe.API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cafe.API.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261024180000_AddReferrals")]
    public partial class AddReferrals : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(name: "referral_code", table: "users", type: "character varying(20)", maxLength: 20, nullable: true);
            migrationBuilder.AddColumn<int>(name: "referred_by_user_id", table: "users", type: "integer", nullable: true);
            migrationBuilder.AddColumn<DateTime>(name: "referral_rewarded_at", table: "users", type: "timestamp with time zone", nullable: true);

            migrationBuilder.CreateIndex(name: "IX_users_referral_code", table: "users", column: "referral_code", unique: true);
            migrationBuilder.CreateIndex(name: "IX_users_referred_by_user_id", table: "users", column: "referred_by_user_id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_users_referral_code", table: "users");
            migrationBuilder.DropIndex(name: "IX_users_referred_by_user_id", table: "users");
            migrationBuilder.DropColumn(name: "referral_code", table: "users");
            migrationBuilder.DropColumn(name: "referred_by_user_id", table: "users");
            migrationBuilder.DropColumn(name: "referral_rewarded_at", table: "users");
        }
    }
}
