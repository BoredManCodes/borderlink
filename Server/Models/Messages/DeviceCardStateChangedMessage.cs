using BorderLink.Server.Enums;

namespace BorderLink.Server.Models.Messages;

public record DeviceCardStateChangedMessage(string DeviceId, DeviceCardState State);