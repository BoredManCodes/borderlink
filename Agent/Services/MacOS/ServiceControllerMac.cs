using BorderLink.Agent.Interfaces;
using Microsoft.Extensions.Logging;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace BorderLink.Agent.Services.MacOS;

/// <summary>
/// Wraps <c>launchctl</c>. We use <c>kickstart -k</c> for restart (it both stops
/// and starts the job in-place) and <c>kickstart</c>/<c>stop</c> for the
/// individual verbs. <c>bootstrap</c>/<c>bootout</c> would be more correct for
/// adding/removing jobs from the domain, but we don't want to remove user
/// services from the agent.
/// </summary>
public class ServiceControllerMac : IServiceController
{
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(30);

    private readonly ILogger<ServiceControllerMac> _logger;

    public ServiceControllerMac(ILogger<ServiceControllerMac> logger)
    {
        _logger = logger;
    }

    public Task<bool> StartAsync(string name, CancellationToken cancellationToken = default) =>
        Run("kickstart", $"system/{Sanitize(name)}", cancellationToken);

    public Task<bool> StopAsync(string name, CancellationToken cancellationToken = default) =>
        Run("stop", Sanitize(name), cancellationToken);

    public Task<bool> RestartAsync(string name, CancellationToken cancellationToken = default) =>
        Run("kickstart", $"-k system/{Sanitize(name)}", cancellationToken);

    private Task<bool> Run(string verb, string args, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            try
            {
                var psi = new ProcessStartInfo("launchctl", $"{verb} {args}")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };

                using var proc = Process.Start(psi);
                if (proc is null)
                {
                    return false;
                }

                if (!proc.WaitForExit(_timeout))
                {
                    try { proc.Kill(entireProcessTree: true); } catch { }
                    return false;
                }

                return proc.ExitCode == 0;
            }
            catch (Win32Exception ex)
            {
                _logger.LogWarning(ex, "launchctl is not available.");
                return false;
            }
            catch (FileNotFoundException ex)
            {
                _logger.LogWarning(ex, "launchctl is not available.");
                return false;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Failed to {verb} service.", verb);
                return false;
            }
        }, cancellationToken);
    }

    private static string Sanitize(string label)
    {
        var safe = new System.Text.StringBuilder(label.Length);
        foreach (var c in label)
        {
            if (char.IsLetterOrDigit(c) || c is '.' or '-' or '_' or ':')
            {
                safe.Append(c);
            }
        }
        return safe.ToString();
    }
}
