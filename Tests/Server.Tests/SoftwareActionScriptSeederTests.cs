#nullable enable
using BorderLink.Server.Data;
using BorderLink.Server.Services;
using BorderLink.Shared.Constants;
using BorderLink.Shared.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BorderLink.Server.Tests;

[TestClass]
public class SoftwareActionScriptSeederTests
{
#nullable disable
    private TestData _testData;
    private IServiceScopeFactory _scopeFactory;
#nullable enable

    [TestInitialize]
    public async Task Init()
    {
        _testData = new TestData();
        await _testData.Init();
        _scopeFactory = IoCActivator.ServiceProvider.GetRequiredService<IServiceScopeFactory>();
    }

    [TestMethod]
    public async Task Seeder_PopulatesAllNineWellKnownScripts_AndIsIdempotent()
    {
        var seeder = new SoftwareActionScriptSeeder(
            _scopeFactory,
            NullLogger<SoftwareActionScriptSeeder>.Instance);

        await seeder.StartAsync(CancellationToken.None);

        using (var scope = _scopeFactory.CreateScope())
        {
            var dbFactory = scope.ServiceProvider.GetRequiredService<IAppDbFactory>();
            using var db = dbFactory.GetContext();
            foreach (var id in SoftwareActionScriptIds.All)
            {
                var script = await db.SavedScripts.FirstOrDefaultAsync(x => x.Id == id);
                Assert.IsNotNull(script, $"Script {id} should be seeded.");
                Assert.IsFalse(string.IsNullOrEmpty(script.Content));
                Assert.IsTrue(script.IsPublic);
            }
        }

        // Run a second time — must not throw and must not duplicate.
        await seeder.StartAsync(CancellationToken.None);

        using (var scope = _scopeFactory.CreateScope())
        {
            var dbFactory = scope.ServiceProvider.GetRequiredService<IAppDbFactory>();
            using var db = dbFactory.GetContext();
            var count = await db.SavedScripts
                .CountAsync(x => SoftwareActionScriptIds.All.Contains(x.Id));
            Assert.AreEqual(SoftwareActionScriptIds.All.Count, count);
        }
    }

    [TestMethod]
    public async Task Seeder_AllSeededScripts_AreSafeToFormatWithPackageId()
    {
        var seeder = new SoftwareActionScriptSeeder(
            _scopeFactory,
            NullLogger<SoftwareActionScriptSeeder>.Instance);

        await seeder.StartAsync(CancellationToken.None);

        using var scope = _scopeFactory.CreateScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IAppDbFactory>();
        using var db = dbFactory.GetContext();

        foreach (var id in SoftwareActionScriptIds.All)
        {
            var script = await db.SavedScripts.FirstOrDefaultAsync(x => x.Id == id);
            Assert.IsNotNull(script);

            // Each Content must format cleanly with a sample package id.
            // This guards against unbalanced braces (a common mistake when
            // PowerShell here-strings are inlined).
            var formatted = string.Format(script.Content!, "Sample.Package");
            StringAssert.Contains(formatted, "Sample.Package");
        }
    }
}
