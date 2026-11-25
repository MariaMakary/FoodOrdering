using Client.Web.Models.Entities;
using Client.Web.Models.Identity;
using Client.Web.Models.ViewModels;
using Client.Web.Repositories;
using Shared.Contracts.Enums;
using Shared.Contracts.Messages;
using Shared.Persistence.Outbox;

namespace Client.Web.Services;

public class OrderService(IOrderRepository orders, ICartRepository carts, IOutbox outbox) : IOrderService
{
    public const string OrderNumberPrefix = "FO-";

    public async Task<ServiceResult<CheckoutViewModel>> GetCheckoutAsync(Guid customerId, CancellationToken ct = default)
    {
        ApplicationUser? customer = await orders.GetCustomerAsync(customerId, ct);
        if (customer is null)
        {
            return ServiceResult<CheckoutViewModel>.Failure(OrderErrors.CustomerNotFound);
        }

        Cart? cart = await carts.GetByCustomerAsync(customerId, ct);
        if (cart is null || cart.Items.Count == 0)
        {
            return ServiceResult<CheckoutViewModel>.Failure(OrderErrors.EmptyCart);
        }

        return ServiceResult<CheckoutViewModel>.Success(new CheckoutViewModel
        {
            DeliveryType = DeliveryType.Delivery,
            Address = customer.DefaultAddress,
            PaymentMethod = PaymentMethod.Online,
            Cart = CartMapping.ToViewModel(cart)
        });
    }

    public async Task<ServiceResult<Guid>> PlaceOrderAsync(
        Guid customerId,
        CheckoutViewModel model,
        CancellationToken ct = default)
    {
        string? address = model.DeliveryType == DeliveryType.Delivery ? Clean(model.Address) : null;
        if (model.DeliveryType == DeliveryType.Delivery && address is null)
        {
            return ServiceResult<Guid>.Failure(OrderErrors.AddressRequired);
        }

        ApplicationUser? customer = await orders.GetCustomerAsync(customerId, ct);
        if (customer is null)
        {
            return ServiceResult<Guid>.Failure(OrderErrors.CustomerNotFound);
        }

        Cart? cart = await carts.GetByCustomerAsync(customerId, ct);
        if (cart is null || cart.Items.Count == 0)
        {
            return ServiceResult<Guid>.Failure(OrderErrors.EmptyCart);
        }

        List<string> unavailable = cart.Items
            .Where(i => !CartMapping.IsOrderable(i.MenuItem))
            .Select(i => i.MenuItem.Name)
            .Distinct()
            .ToList();
        if (unavailable.Count > 0)
        {
            return ServiceResult<Guid>.Failure(OrderErrors.UnavailableDishes(unavailable));
        }

        long number = await orders.NextOrderNumberAsync(ct);
        DateTime now = DateTime.UtcNow;
        Order order = NewOrder(customerId, model, address, cart, $"{OrderNumberPrefix}{number}", now);

        orders.Add(order);
        carts.ClearItems(cart);
        outbox.Add(ToMessage(order, customer, now));
        await orders.SaveChangesAsync(ct);

        return ServiceResult<Guid>.Success(order.Id);
    }

    private static Order NewOrder(
        Guid customerId,
        CheckoutViewModel model,
        string? address,
        Cart cart,
        string orderNumber,
        DateTime now)
    {
        Guid orderId = Guid.NewGuid();
        List<OrderItem> items = cart.Items
            .OrderBy(i => i.CreatedAt)
            .ThenBy(i => i.Id)
            .Select(i => ToOrderItem(orderId, i))
            .ToList();

        return new Order
        {
            Id = orderId,
            OrderNumber = orderNumber,
            CustomerId = customerId,
            DeliveryType = model.DeliveryType,
            Address = address,
            Notes = Clean(model.Notes),
            PaymentMethod = model.PaymentMethod,
            PaymentStatus = model.PaymentMethod == PaymentMethod.Cash ? PaymentStatus.CashOnDelivery : PaymentStatus.Unpaid,
            Status = OrderStatus.Placed,
            Total = items.Sum(i => (i.UnitPrice + i.Extras.Sum(e => e.Price)) * i.Quantity),
            LastStatusChangedAt = now,
            Items = items,
            StatusHistory =
            [
                new OrderStatusHistory
                {
                    Id = Guid.NewGuid(),
                    OrderId = orderId,
                    Status = OrderStatus.Placed,
                    ChangedAt = now
                }
            ]
        };
    }

    private static OrderItem ToOrderItem(Guid orderId, CartItem cartItem)
    {
        Guid orderItemId = Guid.NewGuid();

        return new OrderItem
        {
            Id = orderItemId,
            OrderId = orderId,
            MenuItemId = cartItem.MenuItemId,
            Name = cartItem.MenuItem.Name,
            UnitPrice = cartItem.MenuItem.Price,
            Quantity = cartItem.Quantity,
            Extras = cartItem.Extras
                .Select(e => e.MenuItemExtra)
                .OrderBy(e => e.Name)
                .Select(e => new OrderItemExtra
                {
                    Id = Guid.NewGuid(),
                    OrderItemId = orderItemId,
                    Name = e.Name,
                    Price = e.Price
                })
                .ToList()
        };
    }

    private static OrderPlaced ToMessage(Order order, ApplicationUser customer, DateTime now) => new()
    {
        CreatedAt = now,
        CorrelationId = order.Id,
        OrderId = order.Id,
        OrderNumber = order.OrderNumber,
        CustomerId = order.CustomerId,
        CustomerName = $"{customer.FirstName} {customer.LastName}".Trim(),
        CustomerPhone = customer.PhoneNumber ?? string.Empty,
        DeliveryType = order.DeliveryType,
        Address = order.Address,
        Notes = order.Notes,
        PaymentMethod = order.PaymentMethod,
        Items = order.Items
            .Select(i => new OrderItemDto
            {
                MenuItemId = i.MenuItemId,
                Name = i.Name,
                UnitPrice = i.UnitPrice,
                Quantity = i.Quantity,
                Extras = i.Extras.Select(e => new ExtraDto { Name = e.Name, Price = e.Price }).ToList()
            })
            .ToList(),
        Total = order.Total,
        PlacedAt = now
    };

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
