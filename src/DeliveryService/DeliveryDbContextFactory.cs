using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DeliveryService;

public sealed class DeliveryDbContextFactory : IDesignTimeDbContextFactory<DeliveryDbContext>
{
    public DeliveryDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DeliveryDb")
            ?? "Data Source=DESKTOP-AKAJP53\\MSSQLSERVER01;Database=SmartLogisticsDelivery;Trusted_Connection=True;TrustServerCertificate=True;";
        var options = new DbContextOptionsBuilder<DeliveryDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        return new DeliveryDbContext(options);
    }
}