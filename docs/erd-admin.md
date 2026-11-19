# Admin database ERD (UNI-55)

**Diagram:** [erd-admin.png](erd-admin.png) (edit [erd-admin.drawio](erd-admin.drawio) with draw.io, then export the PNG again)
**Based on:** [DEC-001](decisions-log.md) (statuses), [messages.md](messages.md) (message fields)
**Used by:** UNI-7 (turns this into EF Core entities and migrations)

## Rules

- **All Ids are `Guid`**, except ASP.NET Identity tables, which keep Identity's default `string` Id.
- **No foreign keys to the Client database.** `Order.Id` is the same Guid the Client created, and `CustomerId` is a plain value copied from `OrderPlaced`.
- **Money** is `decimal(10,2)`. **Times** are UTC.
- **Enums** (`OrderStatus`, `PaymentStatus`, `DeliveryType`, `PaymentMethod`) are stored as strings, the same as in the messages.
- **Menu items are never really deleted.** `IsDeleted = true` hides them, because old orders and ratings still point to them.

## Tables

| Group | Table | What it stores |
|---|---|---|
| Identity | `AspNetUsers` | Staff logins. Extended with `FullName`, `Phone`, `IsActive`. |
| Identity | `AspNetRoles`, `AspNetUserRoles` | Roles `Admin`, `Staff`, `Manager` and who has them |
| Menu | `Category` | Menu categories (Name is unique) |
| Menu | `MenuItem` | The master menu. Same columns as the Client copy. |
| Menu | `MenuItemExtra` | Extras a dish can have (cheese, bacon, ...) |
| Orders | `Order` | One row per order, copied from `OrderPlaced`, then updated by staff |
| Orders | `OrderItem`, `OrderItemExtra` | Dishes and extras in the order, with name and price **at the time of ordering** |
| Orders | `OrderStatusHistory` | Every status change: who, when and why. `ChangedByUserId` is `null` when the customer cancelled. |
| Orders | `Payment` | Online payment from `PaymentCompleted`. At most one per order. |
| Orders | `Rating` | Dish ratings from `DishRated`. One per dish per order. |
| Messaging | `ProcessedMessage` | MessageIds already handled, so a duplicate message is skipped |
| Messaging | `MessageLog` | Every message in or out, for debugging. `Direction`: In, Out. `Status`: Received, Processed, Published, Failed. |
| Messaging | `OutboxMessage` | Messages waiting to be sent to RabbitMQ (UNI-44) |

## Where every message field is stored

This is the test for this ticket: every field of every message has a column to land in.

**Every message:** `messageId` → `ProcessedMessage.MessageId` and `MessageLog.MessageId`. `createdAt` and `correlationId` are kept in `MessageLog.Payload` (`correlationId` is also the `OrderId`).

### `OrderPlaced` (received)

| Field | Column |
|---|---|
| `orderId` | `Order.Id` |
| `orderNumber` | `Order.OrderNumber` |
| `customerId` | `Order.CustomerId` |
| `customerName` | `Order.CustomerName` |
| `customerPhone` | `Order.CustomerPhone` |
| `deliveryType` | `Order.DeliveryType` |
| `address` | `Order.Address` |
| `notes` | `Order.Notes` |
| `paymentMethod` | `Order.PaymentMethod`. Also sets `Order.PaymentStatus`: Cash → `CashOnDelivery`, Online → `Unpaid` |
| `items[].menuItemId` | `OrderItem.MenuItemId` |
| `items[].name` | `OrderItem.Name` |
| `items[].unitPrice` | `OrderItem.UnitPrice` |
| `items[].quantity` | `OrderItem.Quantity` |
| `items[].extras[].name` | `OrderItemExtra.Name` |
| `items[].extras[].price` | `OrderItemExtra.Price` |
| `total` | `Order.Total` |
| `placedAt` | `Order.CreatedAt`. `Order.ReceivedAt` is when Admin got the message. |

Also: `Order.Status = Placed`, and a first `OrderStatusHistory` row is added.

### `PaymentCompleted` (received)

| Field | Column |
|---|---|
| `orderId` | `Payment.OrderId` |
| `paymentId` | `Payment.Id` |
| `amount` | `Payment.Amount` |
| `method` | `Payment.Method` |
| `paidAt` | `Payment.PaidAt` |

Also: `Order.PaymentStatus = Paid`.

### `OrderCancelled` (received)

| Field | Column |
|---|---|
| `orderId` | `OrderStatusHistory.OrderId` |
| `reason` | `OrderStatusHistory.Note` |
| `cancelledAt` | `OrderStatusHistory.ChangedAt` |

Also: `Order.Status = Cancelled`, `ChangedByUserId = null`. If the cancel is refused (DEC-001 race rule), only `MessageLog` gets a row.

### `DishRated` (received)

| Field | Column |
|---|---|
| `ratingId` | `Rating.Id` |
| `orderId` | `Rating.OrderId` |
| `menuItemId` | `Rating.MenuItemId` |
| `customerName` | `Rating.CustomerName` |
| `stars` | `Rating.Stars` |
| `comment` | `Rating.Comment` |
| `ratedAt` | `Rating.CreatedAt` |

### `OrderStatusChanged` (sent): where the values come from

| Field | Column |
|---|---|
| `orderId` | `Order.Id` |
| `newStatus` | `Order.Status` and `OrderStatusHistory.Status` |
| `estimatedDeliveryTime` | `Order.EstimatedDeliveryTime` |
| `reason` | `OrderStatusHistory.Note` |
| `changedAt` | `OrderStatusHistory.ChangedAt` |

The message is saved in `OutboxMessage` in the same transaction as the status change, then published.

### `MenuItemUpdated` (sent): where the values come from

| Field | Column |
|---|---|
| `action` | `Created` (new row), `Updated` (row changed), `Deleted` (`MenuItem.IsDeleted = true`) |
| `menuItemId` | `MenuItem.Id` |
| `name` | `MenuItem.Name` |
| `description` | `MenuItem.Description` |
| `price` | `MenuItem.Price` |
| `photoUrl` | `MenuItem.PhotoUrl` |
| `isVegetarian` | `MenuItem.IsVegetarian` |
| `isAvailable` | `MenuItem.IsAvailable` |
| `category.id`, `category.name` | `Category.Id`, `Category.Name` |
| `extras[].id`, `.name`, `.price` | `MenuItemExtra.Id`, `.Name`, `.Price` |
| `updatedAt` | `MenuItem.UpdatedAt` |

✅ Every field of all 6 messages has a column.

## Points to review with Maria

1. Ids are Guids everywhere, and Identity stays `string`. The Client ERD (UNI-54) should match for `MenuItem`, `MenuItemExtra` and `Category`.
2. `OrderItem.MenuItemId` and `Rating.MenuItemId` are real foreign keys to `MenuItem` (same database), which is why menu items are soft-deleted.
3. A rejection reason or cancel reason is stored in `OrderStatusHistory.Note`, not on `Order`.
4. `Payment.OrderId` is unique: one online payment per order.
5. `Rating` has a unique (`OrderId`, `MenuItemId`): a customer rates each dish once per order.
6. "Needs refund" (DEC-001) is not a column: an order that is `Rejected` or `Cancelled` and has a `Payment` row needs a refund.
