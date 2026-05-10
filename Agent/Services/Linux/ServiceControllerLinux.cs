using BorderLink.Agent.Interfaces;
using Microsoft.Extensions.Logging;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace BorderLink.Agent.Services.Linux;

/// <summary>
/// Wraps <c>systemctl start/stop/restart</c>. Returns <c>true</c> only when
/// the underlying command exits 0.
/// </summary>
public class ServiceControllerLinux : IServiceController
{
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(30);

    private readonly ILogger<ServiceControllerLinux> _logger;

    public ServiceControllerLinux(ILogger<ServiceControllerLinux> logger)
    {
        _logger = logger;
    }

    public Task<bool> StartAsync(string name, CancellationToken cancellationToken = default) =>
        RunSystemctl("start", name, cancellationToken);

    public Task<bool> StopAsync(string name, CancellationToken cancellationToken = default) =>
        RunSystemctl("stop", name, cancellationToken);

    public Task<bool> RestartAsync(string name, CancellationToken cancellationToken = default) =>
        RunSystemctl("restart", name, cancellationToken);

    private Task<bool> RunSystemctl(string verb, string name, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            try
            {
                var psi = new ProcessStartInfo("systemctl", $"{verb} {EscapeUnit(name)}")
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
                _logger.LogWarning(ex, "systemctl is not available.");
                return false;
            }
            catch (FileNotFoundException ex)
            {
                _logger.LogWarning(ex, "systemctl is not available.");
                return false;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Failed to {verb} service {name}.", verb, name);
                return false;
            }
        }, cancellationToken);
    }

    private static string EscapeUnit(string name)
    {
        // Unit names are tightly constrained — strip anything that could form a
        // shell injection vector even though we don't shell-out via /bin/sh.
        var safe = new System.Text.StringBuilder(name.Length);
        foreach (var c in name)
        {
            if (char.IsLetterOrDigit(c) || c is '.' or '-' or '_' or '@' or ':' or '\\')
            {
                safe.Append(c);
            }
        }
        return safe.ToString();
    }
}
