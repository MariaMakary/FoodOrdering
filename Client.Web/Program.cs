using Microsoft.Extensions.Options;
using Shared.Contracts.Options;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

builder.Services.AddOptions<RabbitMqOptions>()
    .Bind(builder.Configuration.GetSection(RabbitMqOptions.SectionName))
    .Validate(o => !string.IsNullOrWhiteSpace(o.Host), "RabbitMq:Host is missing.")
    .Validate(o => !string.IsNullOrWhiteSpace(o.Username), "RabbitMq:Username is missing. Set it in user-secrets.")
    .Validate(o => !string.IsNullOrWhiteSpace(o.Password), "RabbitMq:Password is missing. Set it in user-secrets.")
    .Validate(o => !string.IsNullOrWhiteSpace(o.ExchangeName), "RabbitMq:ExchangeName is missing.")
    .ValidateOnStart();

var app = builder.Build();

var rabbitMq = app.Services.GetRequiredService<IOptions<RabbitMqOptions>>().Value;
app.Logger.LogInformation("RabbitMQ: {Host}:{Port}, vhost {VirtualHost}, exchange {Exchange}, user {Username}",
    rabbitMq.Host, rabbitMq.Port, rabbitMq.VirtualHost, rabbitMq.ExchangeName, rabbitMq.Username);

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();


app.Run();
