using BorderLink.Shared.Enums;

namespace BorderLink.Desktop.Shared.Abstractions;

public interface IRemoteControlAccessService
{
    bool IsPromptOpen { get; }

    Task<PromptForAccessResult> PromptForAccess(string requesterName, string organizationName);
}
