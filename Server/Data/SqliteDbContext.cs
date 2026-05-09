using Microsoft.EntityFrameworkCore;

namespace BorderLink.Server.Data;

public class SqliteDbContext : AppDb
{
    private readonly IConfiguration _configuration;

    public SqliteDbContext(IConfiguration configuration, IWebHostEnvironment hostEnv)
        : base(hostEnv)
    {
        _configuration = configuration;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        options.UseSqlite(_configuration.GetConnectionString("SQLite"));
        base.OnConfiguring(options);
    }
}