using System.Threading.Tasks;

namespace BorderLink.Desktop.Core.Interfaces
{
    public interface IChatClientService
    {
        Task StartChat(string requesterID, string organizationName);
    }
}
