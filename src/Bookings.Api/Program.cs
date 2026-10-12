using Bookings.Api;
using Bookings.Api.Features;
using Bookings.Api.Messaging;
using Contracts;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog((context, log) => log.ReadFrom.Configuration(context.Configuration).WriteTo.Console());
var connection = builder.Configuration.GetConnectionString("Database") ?? "Host=localhost;Port=5434;Database=bookings;Username=ticketing;Password=ticketing";
builder.Services.AddDbContext<BookingsDbContext>(options => options.UseNpgsql(connection)); builder.Services.AddSingleton(TimeProvider.System); builder.Services.AddHostedService<ExpirationWorker>();
builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<PaymentCompletedConsumer>(); x.AddConsumer<PaymentRejectedConsumer>(); x.AddConsumer<RefundCompletedConsumer>(); x.AddConsumer<RefundFailedConsumer>(); x.AddRequestClient<HoldSeats>(TimeSpan.FromSeconds(15));
    x.AddEntityFrameworkOutbox<BookingsDbContext>(o => { o.UsePostgres(); o.UseBusOutbox(); });
    x.UsingRabbitMq((context, cfg) => { var rabbitHost = builder.Configuration["RabbitMq:Host"] ?? "localhost"; var rabbitPort = builder.Configuration.GetValue<ushort>("RabbitMq:Port", 5672); cfg.Host(rabbitHost, rabbitPort, "/", h => { h.Username(builder.Configuration["RabbitMq:Username"] ?? "ticketing"); h.Password(builder.Configuration["RabbitMq:Password"] ?? "ticketing"); }); cfg.UseMessageRetry(r => r.Intervals(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(10))); cfg.ConfigureEndpoints(context); });
});
builder.Services.AddEndpointsApiExplorer(); builder.Services.AddSwaggerGen(); builder.Services.AddProblemDetails(); builder.Services.AddHealthChecks().AddNpgSql(connection);
var app = builder.Build(); app.UseSerilogRequestLogging(); app.UseExceptionHandler(); app.UseSwagger(); app.UseSwaggerUI(); app.MapHealthChecks("/health"); app.MapBookingEndpoints(); await MigrateDatabase(app.Services); app.Run();
static async Task MigrateDatabase(IServiceProvider services) { for (var attempt = 1; ; attempt++) try { await using var scope = services.CreateAsyncScope(); await scope.ServiceProvider.GetRequiredService<BookingsDbContext>().Database.MigrateAsync(); return; } catch when (attempt < 10) { await Task.Delay(TimeSpan.FromSeconds(attempt)); } }
public partial class Program;
