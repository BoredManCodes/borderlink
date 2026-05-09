using BorderLink.Shared.Utilities;
using System;
using System.Diagnostics;
using System.IO;

namespace BorderLink.Agent.Services;

public interface IUninstaller
{
    void UninstallAgent();
}

public class Uninstaller : IUninstaller
{
    public void UninstallAgent()
    {
        if (EnvironmentHelper.IsWindows)
        {
            Process.Start("cmd.exe", "/c sc delete BorderLink_Service");

            var view = Environment.Is64BitOperatingSystem ?
                "/reg:64" :
                "/reg:32";

            Process.Start("cmd.exe", @$"/c REG DELETE HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\BorderLink /f {view}");

            var currentDir = Path.GetDirectoryName(typeof(Uninstaller).Assembly.Location);
            Process.Start("cmd.exe", $"/c timeout 5 & rd /s /q \"{currentDir}\"");
        }
        else if (EnvironmentHelper.IsLinux)
        {
            Process.Start("sudo", "systemctl stop borderlink-agent").WaitForExit();
            Directory.Delete("/usr/local/bin/BorderLink", true);
            File.Delete("/etc/systemd/system/borderlink-agent.service");
            Process.Start("sudo", "systemctl daemon-reload").WaitForExit();
        }
        Environment.Exit(0);
    }
}
