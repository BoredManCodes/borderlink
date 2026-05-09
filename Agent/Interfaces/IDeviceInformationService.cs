using BorderLink.Shared.Dtos;
using System.Threading.Tasks;

namespace BorderLink.Agent.Interfaces;

public interface IDeviceInformationService
{
    Task<DeviceClientDto> CreateDevice(string deviceId, string orgId);
}
