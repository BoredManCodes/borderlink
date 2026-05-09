using Microsoft.AspNetCore.Components.Authorization;
using BorderLink.Shared.Entities;

namespace BorderLink.Server.Services;

public interface IAuthService
{
    Task<bool> IsAuthenticated();
    Task<Result<BorderLinkUser>> GetUser();
}

public class AuthService : IAuthService
{
    private readonly AuthenticationStateProvider _authProvider;
    private readonly IDataService _dataService;

    public AuthService(
        AuthenticationStateProvider authProvider,
        IDataService dataService)
    {
        _authProvider = authProvider;
        _dataService = dataService;
    }

    public async Task<bool> IsAuthenticated()
    {
        var principal = await _authProvider.GetAuthenticationStateAsync();
        return principal?.User?.Identity?.IsAuthenticated ?? false;
    }

    public async Task<Result<BorderLinkUser>> GetUser()
    {
        var principal = await _authProvider.GetAuthenticationStateAsync();

        if (principal?.User?.Identity?.IsAuthenticated == true)
        {
            return await _dataService.GetUserByName($"{principal.User.Identity.Name}");
        }

        return Result.Fail<BorderLinkUser>("Not authenticated.");
    }
}
