using BorderLink.Shared.Primitives;
using BorderLink.Shared.Utilities;

namespace BorderLink.Shared.Services;
public static class FileLoggerDefaults
{
    private static readonly SemaphoreSlim _logLock = new(1, 1);

    public static string LogsFolderPath
    {
        get
        {
            if (OperatingSystem.IsWindows())
            {
                var logsPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "BorderLink",
                    "Logs");

                if (EnvironmentHelper.IsDebug)
                {
                    logsPath += "_Debug";
                }
                return logsPath;
            }

            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                if (EnvironmentHelper.IsDebug)
                {
                    return "/var/log/borderlink_debug";
                }
                return "/var/log/borderlink";
            }

            throw new PlatformNotSupportedException();
        }
    }

    public static async Task<IDisposable> AcquireLock(CancellationToken cancellationToken = default)
    {
        await _logLock.WaitAsync(cancellationToken);
        return new CallbackDisposable(() => _logLock.Release());
    }

    public static string GetLogPath(string componentName) => 
        Path.Combine(LogsFolderPath, componentName, $"LogFile_{DateTime.Now:yyyy-MM-dd}.log");

}
