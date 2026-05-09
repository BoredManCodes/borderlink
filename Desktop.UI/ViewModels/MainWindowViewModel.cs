using Microsoft.Extensions.Logging;
using BorderLink.Desktop.Shared.Services;

namespace BorderLink.Desktop.UI.ViewModels;

public interface IMainWindowViewModel : IBrandedViewModelBase
{
}

public class MainWindowViewModel : BrandedViewModelBase, IMainWindowViewModel
{
    public MainWindowViewModel(
        IBrandingProvider brandingProvider,
        IUiDispatcher dispatcher,
        ILogger<BrandedViewModelBase> logger)
        : base(brandingProvider, dispatcher, logger)
    {
    }
}
