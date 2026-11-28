using Client.Web.Models.Entities;
using Client.Web.Models.Identity;
using Client.Web.Models.ViewModels;
using Client.Web.Repositories;
using Microsoft.EntityFrameworkCore;
using Shared.Contracts.Enums;
using Shared.Contracts.Messages;
using Shared.Persistence.Outbox;

namespace Client.Web.Services;

public class OrderService(IOrderRepository orders, ICartRepository carts, IOutbox outbox) : IOrderService
{
    public const string OrderNumberPrefix = "FO-";
    public const string CancelReason = "Cancelled by customer";

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

    public async Task<IReadOnlyList<OrderListItemViewModel>> GetCustomerOrdersAsync(
        Guid customerId,
        CancellationToken ct = default)
    {
        List<Order> customerOrders = await orders.GetCustomerOrdersAsync(customerId, ct);

        return customerOrders
            .Select(o => new OrderListItemViewModel
            {
                Id = o.Id,
                OrderNumber = o.OrderNumber,
                CreatedAt = o.CreatedAt,
                Total = o.Total,
                Status = o.Status,
                PaymentStatus = o.PaymentStatus
            })
            .ToList();
    }

    public async Task<OrderDetailsViewModel?> GetOrderDetailsAsync(
        Guid orderId,
        Guid customerId,
        CancellationToken ct = default)
    {
        Order? order = await orders.GetOrderWithDetailsAsync(orderId, ct);
        if (order is null || order.CustomerId != customerId)
        {
            return null;
        }

        List<OrderItemViewModel> items = ToItemViewModels(order);

        return new OrderDetailsViewModel
        {
            Id = order.Id,
            OrderNumber = order.OrderNumber,
            CreatedAt = order.CreatedAt,
            Status = order.Status,
            DeliveryType = order.DeliveryType,
            Address = order.Address,
            Notes = order.Notes,
            PaymentMethod = order.PaymentMethod,
            PaymentStatus = order.PaymentStatus,
            Total = order.Total,
            EstimatedDeliveryTime = order.EstimatedDeliveryTime,
            RejectionReason = order.RejectionReason,
            Items = items,
            StatusHistory = order.StatusHistory
                .OrderBy(h => h.ChangedAt)
                .Select(h => new OrderStatusHistoryViewModel { Status = h.Status, ChangedAt = h.ChangedAt, Note = h.Note })
                .ToList(),
            CanCancel = order.Status == OrderStatus.Placed,
            CanRate = order.Status == OrderStatus.Delivered && items.Any(i => !i.IsRated)
        };
    }

    public async Task<OrderConfirmationViewModel?> GetConfirmationAsync(
        Guid orderId,
        Guid customerId,
        CancellationToken ct = default)
    {
        Order? order = await orders.GetOrderWithDetailsAsync(orderId, ct);
        if (order is null || order.CustomerId != customerId)
        {
            return null;
        }

        return new OrderConfirmationViewModel
        {
            OrderId = order.Id,
            OrderNumber = order.OrderNumber,
            Items = ToItemViewModels(order),
            Total = order.Total,
            DeliveryType = order.DeliveryType,
            Address = order.Address,
            PaymentStatus = order.PaymentStatus,
            Status = order.Status,
            EstimatedTime = order.EstimatedDeliveryTime
        };
    }

    public async Task<ServiceResult> CancelOrderAsync(Guid orderId, Guid customerId, CancellationToken ct = default)
    {
        Order? order = await orders.GetOrderForUpdateAsync(orderId, ct);
        if (order is null || order.CustomerId != customerId)
        {
            return ServiceResult.Failure(OrderErrors.OrderNotFound);
        }

        if (order.Status != OrderStatus.Placed)
        {
            return ServiceResult.Failure(OrderErrors.CannotCancel);
        }

        DateTime cancelledAt = DateTime.UtcNow;
        order.Status = OrderStatus.Cancelled;
        order.LastStatusChangedAt = cancelledAt;
        orders.AddHistory(new OrderStatusHistory
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            Status = OrderStatus.Cancelled,
            ChangedAt = cancelledAt,
            Note = CancelReason
        });
        outbox.Add(new OrderCancelled
        {
            CreatedAt = cancelledAt,
            CorrelationId = order.Id,
            OrderId = order.Id,
            Reason = CancelReason,
            CancelledAt = cancelledAt
        });

        try
        {
            await orders.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            orders.DiscardChanges();
            return ServiceResult.Failure(OrderErrors.ChangedByRestaurant);
        }

        return ServiceResult.Success();
    }

    private static List<OrderItemViewModel> ToItemViewModels(Order order)
    {
        HashSet<Guid> ratedDishIds = order.Ratings.Select(r => r.MenuItemId).ToHashSet();

        return order.Items
            .OrderBy(i => i.CreatedAt)
            .ThenBy(i => i.Name)
            .Select(i => ToItemViewModel(i, ratedDishIds))
            .ToList();
    }

    private static OrderItemViewModel ToItemViewModel(OrderItem item, HashSet<Guid> ratedDishIds)
    {
        List<OrderItemExtraViewModel> extras = item.Extras
            .OrderBy(e => e.Name)
            .Select(e => new OrderItemExtraViewModel { Name = e.Name, Price = e.Price })
            .ToList();

        return new OrderItemViewModel
        {
            MenuItemId = item.MenuItemId,
            Name = item.Name,
            Extras = extras,
            Quantity = item.Quantity,
            UnitPrice = item.UnitPrice,
            LineTotal = (item.UnitPrice + extras.Sum(e => e.Price)) * item.Quantity,
            IsRated = ratedDishIds.Contains(item.MenuItemId)
        };
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
