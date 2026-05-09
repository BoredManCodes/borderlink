using BorderLink.Desktop.Shared.Abstractions;
using BorderLink.Desktop.Shared.Startup;
using Microsoft.Extensions.DependencyInjection;
using BorderLink.Desktop.Linux.Services;
using BorderLink.Desktop.UI.Services;
using BorderLink.Desktop.UI.Startup;

namespace BorderLink.Desktop.Linux.Startup;

public static class IServiceCollectionExtensions
{
    /// <summary>
    /// Adds Linux and cross-platform remote control services to the service collection.
    /// All methods on <see cref="IRemoteControlClientBuilder"/> must be called to register
    /// required services.
    /// </summary>
    /// <param name="services"></param>
    /// <param name="clientConfig"></param>
    public static void AddRemoteControlLinux(this IServiceCollection services)
    {
        services.AddRemoteControlXplat();
        services.AddRemoteControlUi();

        services.AddSingleton<IAppStartup, AppStartup>();
        services.AddSingleton<ICursorIconWatcher, CursorIconWatcherLinux>();
        services.AddSingleton<IKeyboardMouseInput, KeyboardMouseInputLinux>();
        services.AddSingleton<IClipboardService, ClipboardService>();
        services.AddSingleton<IAudioCapturer, AudioCapturerLinux>();
        services.AddTransient<IScreenCapturer, ScreenCapturerLinux>();
        services.AddScoped<IFileTransferService, FileTransferServiceLinux>();
        services.AddSingleton<ISessionIndicator, SessionIndicator>();
        services.AddSingleton<IShutdownService, ShutdownServiceLinux>();
    }
}
