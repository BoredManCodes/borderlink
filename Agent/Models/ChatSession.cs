using System.IO.Pipes;

namespace BorderLink.Agent.Models;

public class ChatSession
{
    public int ProcessID { get; set; }
    public NamedPipeClientStream? PipeStream { get; set; }
}
