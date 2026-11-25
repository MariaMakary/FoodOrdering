using Admin.Web;
using Admin.Web.Data;
using Admin.Web.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Shared.Messaging;
using Shared.Persistence;
using Shared.Persistence.Outbox;
using Shared.Persistence.ProcessedMessages;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

builder.Services.AddRabbitMq(builder.Configuration);

var adminDbConnection = builder.Configuration.GetConnectionString("AdminDb")
    ?? throw new InvalidOperationException("ConnectionStrings:AdminDb is missing. Set it in appsettings.Development.json.");

builder.Services.AddDbContext<AdminDbContext>(options => options.UseNpgsql(adminDbConnection));
builder.Services.AddOutbox<AdminDbContext>();
builder.Services.AddProcessedMessages<AdminDbContext, ProcessedMessage>();

builder.Services.AddAdminServices();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    await app.MigrateDatabaseAsync<AdminDbContext>();
}

app.UseErrorPages();

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();


app.Run();
