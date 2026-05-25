using Microsoft.AspNetCore.SignalR;

namespace Itm.Notification.Api.Hubs;

public class TicketHub : Hub
{
    public async Task JoinGroup(string email)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, email);
    }
}
