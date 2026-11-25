using Client.Web;
using Client.Web.Data;
using Microsoft.EntityFrameworkCore;
using Shared.Messaging;
using Shared.Persistence;
using Shared.Persistence.Outbox;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

builder.Services.AddRabbitMq(builder.Configuration);

var clientDbConnection = builder.Configuration.GetConnectionString("ClientDb")
    ?? throw new InvalidOperationException("ConnectionStrings:ClientDb is missing. Set it in appsettings.Development.json.");

builder.Services.AddDbContext<ClientDbContext>(options => options.UseNpgsql(clientDbConnection));
builder.Services.AddOutbox<ClientDbContext>();

builder.Services.AddClientIdentity();
builder.Services.AddClientServices();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    await app.MigrateDatabaseAsync<ClientDbContext>();
}

await IdentitySeeder.SeedRolesAsync(app.Services);

app.UseErrorPages();

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();


app.Run();
