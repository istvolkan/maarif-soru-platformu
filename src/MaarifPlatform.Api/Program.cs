using MaarifPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var connectionString = builder.Configuration.GetConnectionString("MaarifDb")
    ?? throw new InvalidOperationException("ConnectionStrings:MaarifDb tanımlı değil.");

builder.Services.AddDbContext<MaarifDbContext>(options =>
    options.UseNpgsql(connectionString, npg => npg.UseVector()));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

// §L/§20 MVP: altyapı ayakta mı ve DB'ye ulaşılabiliyor mu diye hızlı kontrol.
app.MapGet("/health", async (MaarifDbContext db) =>
{
    var canConnect = await db.Database.CanConnectAsync();
    return Results.Ok(new { status = "ok", database = canConnect ? "connected" : "unreachable" });
});

app.Run();
