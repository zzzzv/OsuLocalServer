using Microsoft.AspNetCore.SignalR;

namespace OsuLocalServer;

public class ServerHub : Hub
{
    /// <summary>SignalR 端点路径。</summary>
    public const string Path = "/ws/hub";
}
