using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace WarehouseService;

public sealed class WarehouseDbContextFactory : IDesignTimeDbContextFactory<WarehouseDbContext>
{
    public WarehouseDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__WarehouseDb")
            ?? "Data Source=DESKTOP-AKAJP53\\MSSQLSERVER01;Database=SmartLogisticsWarehouses;Trusted_Connection=True;TrustServerCertificate=True;";
        return new WarehouseDbContext(new DbContextOptionsBuilder<WarehouseDbContext>().UseSqlServer(connectionString).Options);
    }
}