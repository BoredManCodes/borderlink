using Avalonia.Controls;
using Avalonia.Media.Imaging;
using BorderLink.Shared.Entities;
using System.Diagnostics;

namespace BorderLink.Desktop.UI.ViewModels.Fakes;

public class FakeBrandedViewModelBase : IBrandedViewModelBase
{
    private readonly BrandingInfo _brandingInfo;
    private Bitmap? _icon;

    public FakeBrandedViewModelBase()
    {
        _brandingInfo = new BrandingInfo();
        _icon = GetBitmapImageIcon(_brandingInfo);
    }
    public Bitmap? Icon
    {
        get => _icon;
        set => _icon = value;
    }
    public string ProductName { get; set; } = "Test Product";
    public WindowIcon? WindowIcon { get; set; }

    public Task ApplyBranding()
    {
        return Task.CompletedTask;
    }

    private Bitmap? GetBitmapImageIcon(BrandingInfo bi)
    {
        try
        {
            using var imageStream = typeof(Shared.Services.AppState)
                .Assembly
                .GetManifestResourceStream("BorderLink.Desktop.Shared.Assets.DefaultIcon.png") ?? new MemoryStream();

            return new Bitmap(imageStream);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex.Message);
            return null;
        }
    }
}
