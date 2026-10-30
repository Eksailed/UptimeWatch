using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace UptimeWatch.Web.Hubs;

/// <summary>
/// Pushes <c>MonitorUpdated(MonitorStatusDto)</c> to the browser. A connection only joins the group of its own
/// user, so a client can never subscribe to somebody else's monitors; the group name is never taken from the client.
/// </summary>
[Authorize]
public sealed class MonitorHub : Hub
{
    public const string Route = "/hubs/monitors";
    public const string MonitorUpdatedEvent = "MonitorUpdated";

    public static string OwnerGroup(string ownerId) => $"owner-{ownerId}";

    public override async Task OnConnectedAsync()
    {
        // UserIdentifier is the NameIdentifier claim, i.e. the Identity user id that is used as SiteMonitor.OwnerId.
        var userId = Context.UserIdentifier
            ?? throw new HubException("The connection has no user identifier.");
        await Groups.AddToGroupAsync(Context.ConnectionId, OwnerGroup(userId));
        await base.OnConnectedAsync();
    }
}
