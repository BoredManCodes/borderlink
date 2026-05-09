using BorderLink.Shared.Enums;
using BorderLink.Shared.Models;

namespace BorderLink.Server.Models.Messages;

public record PowerShellCompletionsMessage(PwshCommandCompletion Completion, CompletionIntent Intent);