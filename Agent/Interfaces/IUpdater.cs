using System.Threading.Tasks;

namespace BorderLink.Agent.Interfaces;

public interface IUpdater
{
    Task BeginChecking();
    Task CheckForUpdates();
    Task InstallLatestVersion();
}