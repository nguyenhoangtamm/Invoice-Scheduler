using Hangfire;
using Hangfire.PostgreSql;
using InvoiceSchedulerJob.Configuration;
using InvoiceSchedulerJob.Data;
using InvoiceSchedulerJob.Services;
using InvoiceSchedulerJob.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Nethereum.Web3;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers();

// Register all application services using extension methods
builder.Services
  .AddApplicationServices(builder.Configuration)
    .AddDatabaseContext(builder.Configuration)
    .AddHangfireServices(builder.Configuration)
 .AddBlockchainServices(builder.Configuration)
    .AddJobServices()
    .AddHttpClients();

var app = builder.Build();

// Configure the HTTP request pipeline
app.UseRouting();
app.UseAuthorization();
app.MapControllers();

// Add Hangfire Dashboard
app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = new[] { new HangfireAuthorizationFilter() }
});

// Ensure database is created
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<InvoiceDbContext>();
    try
    {
        await dbContext.Database.EnsureCreatedAsync();
        Console.WriteLine("Database ensured created successfully");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Database creation failed: {ex.Message}");
        throw; // Rethrow để không tiếp tục nếu DB failed
    }
}

// Configure recurring jobs
await app.Services.ConfigureRecurringJobsAsync();

app.Run();

// Simple authorization filter for Hangfire Dashboard
public class HangfireAuthorizationFilter : Hangfire.Dashboard.IDashboardAuthorizationFilter
{
    public bool Authorize(Hangfire.Dashboard.DashboardContext context)
    {
        // In production, implement proper authentication
  return true;
    }
}