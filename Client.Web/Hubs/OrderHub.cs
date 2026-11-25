using Client.Web.Models.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Client.Web.Hubs;

[Authorize(Roles = ClientRoles.Customer)]
public class OrderHub : Hub
{
    public const string Path = "/hubs/orders";
    public const string OrderStatusUpdatedEvent = "OrderStatusUpdated";
}
