using BorderLink.Server.Filters;
using BorderLink.Server.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using BorderLink.Server.Services;

namespace BorderLink.Server.Pages;

[ServiceFilter(typeof(ViewerAuthorizationFilter))]
public class ViewerModel(IDataService _dataService) : PageModel
{
    public string FaviconUrl { get; } = "favicon.ico";
    public string LogoUrl { get; set; } = string.Empty;
    public string PageDescription { get; } = "Open-source remote support tools.";
    public string PageTitle { get; } = "BorderLink Remote Control";
    public string ThemeUrl { get; private set; } = string.Empty;
    public string UserDisplayName { get; private set; } = string.Empty;

    public async Task OnGet()
    {
        ThemeUrl = "/css/remote-control-paper.css";
        UserDisplayName = await GetUserDisplayName();
        LogoUrl = await GetLogoUrl();
    }

    private Task<string> GetLogoUrl()
    {
        return Task.FromResult("/images/viewer/borderlink-logo-light.svg");
    }

    private Task<ViewerPageTheme> GetTheme()
    {
        // Paper theme is the only viewer skin currently shipped.
        return Task.FromResult(ViewerPageTheme.Light);
        //if (User.Identity.IsAuthenticated)
        //{
        //    var user = _dataService.GetUserByNameWithOrg(User.Identity.Name);

        //    var userTheme = user.UserOptions.Theme switch
        //    {
        //        Theme.Light => ViewerPageTheme.Light,
        //        Theme.Dark => ViewerPageTheme.Dark,
        //        _ => ViewerPageTheme.Dark
        //    };
        //    return Task.FromResult(userTheme);
        //}

        //var appTheme = _appConfig.Theme switch
        //{
        //    Theme.Light => ViewerPageTheme.Light,
        //    Theme.Dark => ViewerPageTheme.Dark,
        //    _ => ViewerPageTheme.Dark
        //};
        //return Task.FromResult(appTheme);
    }

    private async Task<string> GetUserDisplayName()
    {
        if (string.IsNullOrWhiteSpace(User?.Identity?.Name))
        {
            return string.Empty;
        }

        var userResult = await _dataService.GetUserByName(User.Identity.Name);

        if (!userResult.IsSuccess)
        {
            return string.Empty;
        }

        var user = userResult.Value;
        var displayName = user.UserOptions?.DisplayName ?? user.UserName ?? string.Empty;
        return displayName;
    }
}
