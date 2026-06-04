using Microsoft.AspNetCore.SignalR;

namespace OsuLocalServer.Management;

public class ManagementHub : Hub
{
    // 客户端可以调用 Hub 方法（暂无需要），这里只用作服务端→客户端推送
}
