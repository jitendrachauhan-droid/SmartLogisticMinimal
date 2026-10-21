using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace InventoryService;

public sealed class InventoryDbContextFactory : IDesignTimeDbContextFactory<InventoryDbContext>
{
    public InventoryDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__InventoryDb")
            ?? "Data Source=DESKTOP-AKAJP53\\MSSQLSERVER01;Database=SmartLogisticsInventory;Trusted_Connection=True;TrustServerCertificate=True;";
        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        return new InventoryDbContext(options);
    }
}