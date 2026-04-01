using System.Reflection;
using BookingTemplate.Application;
using BookingTemplate.Infrastructure.DependencyInjection;
using BookingTemplate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// 显式加载 User Secrets（与 csproj 中 UserSecretsId 对应），避免仅依赖默认 Development 行为导致 Gemini:ApiKey 读不到
builder.Configuration.AddUserSecrets(Assembly.GetExecutingAssembly());

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddCors(options =>
{
    options.AddPolicy("FrontendPolicy", policy =>
    {
        policy
            .WithOrigins("http://localhost:5173")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    try
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
        await DbInitializer.SeedDemoDataIfEmptyAsync(db);
    }
    catch
    {
        // 数据库未就绪时仍允许启动（演示种子可稍后重试）
    }

    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("FrontendPolicy");
app.UseAuthorization();

app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

public partial class Program { }
