using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "CryptoTrading API",
        Version = "v1",
        Description = "REST API for strategy parameters, backtest results, chart data, and exchange management"
    });
});

// CORS for React dashboard
builder.Services.AddCors(options =>
{
    options.AddPolicy("Dashboard", policy =>
    {
        policy.WithOrigins("http://localhost:3000", "http://localhost:5173")
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});

// TODO: Register services
// builder.Services.AddSingleton<IStrategyParameters>(sp => new DbStrategyParameters(dataSource, strategyName));
// builder.Services.AddSignalR();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("Dashboard");
app.UseAuthorization();
app.MapControllers();

// TODO: Map SignalR hubs
// app.MapHub<TradingHub>("/hubs/trading");

app.Run();
