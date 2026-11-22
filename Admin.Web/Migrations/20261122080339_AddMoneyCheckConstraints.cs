using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Admin.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddMoneyCheckConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "CK_Payments_Amount",
                table: "Payments",
                sql: "\"Amount\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Orders_Total",
                table: "Orders",
                sql: "\"Total\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OrderItems_UnitPrice",
                table: "OrderItems",
                sql: "\"UnitPrice\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OrderItemExtras_Price",
                table: "OrderItemExtras",
                sql: "\"Price\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_MenuItems_Price",
                table: "MenuItems",
                sql: "\"Price\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_MenuItemExtras_Price",
                table: "MenuItemExtras",
                sql: "\"Price\" >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Payments_Amount",
                table: "Payments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Orders_Total",
                table: "Orders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OrderItems_UnitPrice",
                table: "OrderItems");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OrderItemExtras_Price",
                table: "OrderItemExtras");

            migrationBuilder.DropCheckConstraint(
                name: "CK_MenuItems_Price",
                table: "MenuItems");

            migrationBuilder.DropCheckConstraint(
                name: "CK_MenuItemExtras_Price",
                table: "MenuItemExtras");
        }
    }
}
