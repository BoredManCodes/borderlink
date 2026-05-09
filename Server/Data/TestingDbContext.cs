using Microsoft.EntityFrameworkCore;

namespace BorderLink.Server.Data;

public class TestingDbContext : AppDb
{
    public TestingDbContext(IWebHostEnvironment hostEnvironment) 
        : base(hostEnvironment)
    {
    }

    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        options.UseInMemoryDatabase("BorderLink");
        base.OnConfiguring(options);
    }
}
