# Admin database ERD (UNI-55)

**Diagram:** [erd-admin.png](erd-admin.png) (edit [erd-admin.drawio](erd-admin.drawio) with draw.io, then export the PNG again)
**Based on:** [DEC-001](decisions-log.md) (statuses), [messages.md](messages.md) (message fields)
**Used by:** UNI-7 (turns this into EF Core entities and migrations)

## Rules

- **All Ids are `Guid`, including Identity.** Users and roles use `IdentityUser<Guid>` and `IdentityRole<Guid>`, and the context is `IdentityDbContext<ApplicationUser, ApplicationRole, Guid>`.
- **Identity tables drop the `AspNet` prefix:** `Users`, `Roles`, `UserRoles`, `UserClaims`, `UserLogins`, `UserTokens`, `RoleClaims` (renamed in `OnModelCreating`).
- **`Order` uses optimistic concurrency.** PostgreSQL's built-in `xmin` row version is the concurrency token. If two changes to the same order are saved at the same time (staff accepting while `OrderCancelled` is processed, or two staff at once), the second save throws `DbUpdateConcurrencyException`, reloads the order and checks the DEC-001 rules again.
- **No foreign keys to the Client database.** `Order.Id` is the same Guid the Client created, and `CustomerId` is a plain value copied from `OrderPlaced`.
- **Money** is `decimal(10,2)`, never negative: every money column has a `>= 0` check constraint (`CK_<Table>_<Column>`). **Times** are UTC.
- **Audit columns ([DEC-003](decisions-log.md)):** every business table has `CreatedAt` and `UpdatedAt`, set on save by `AuditInterceptor` from `Shared.Persistence`. Never set them by hand. Not on `UserRoles`, `OrderStatusHistory` or the messaging tables. Times sent by the Client keep their own columns: `Order.PlacedAt` and `Rating.RatedAt`.
- **Categories have a `SortOrder`** (lowest first on the menu). It is sent to Client in `MenuItemUpdated.category.sortOrder`.
- **Enums** (`OrderStatus`, `PaymentStatus`, `DeliveryType`, `PaymentMethod`) are stored as strings, the same as in the messages.
- **Menu items are never really deleted.** `IsDeleted = true` hides them, because old orders and ratings still point to them.

## Tables

| Group | Table | What it stores |
|---|---|---|
| Identity | `Users` | Staff logins. Extended with `FirstName`, `LastName`, `IsActive` (`PhoneNumber` is built into Identity). |
| Identity | `Roles`, `UserRoles` | Roles `Admin`, `Staff`, `Manager` and who has them |
| Menu | `Category` | Menu categories (Name is unique). `SortOrder` sets their order on the menu. |
| Menu | `MenuItem` | The master menu. Same columns as the Client copy. |
| Menu | `MenuItemExtra` | Extras a dish can have (cheese, bacon, ...) |
| Orders | `Order` | One row per order, copied from `OrderPlaced`, then updated by staff. `xmin` protects it from two changes at once. |
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
| `placedAt` | `Order.PlacedAt`. `Order.CreatedAt` is when Admin got the message. |

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

Also: `Order.Status = Cancelled`, `ChangedByUserId = null`. If the cancel is refused (DEC-001 race rule), the order and its history don't change: only an `OutboxMessage` row gets an `OrderStatusChanged` with the current status (UNI-41).

### `DishRated` (received)

| Field | Column |
|---|---|
| `ratingId` | `Rating.Id` |
| `orderId` | `Rating.OrderId` |
| `menuItemId` | `Rating.MenuItemId` |
| `customerName` | `Rating.CustomerName` |
| `stars` | `Rating.Stars` |
| `comment` | `Rating.Comment` |
| `ratedAt` | `Rating.RatedAt` |

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
| `category.id`, `category.name`, `category.sortOrder` | `Category.Id`, `Category.Name`, `Category.SortOrder` |
| `extras[].id`, `.name`, `.price` | `MenuItemExtra.Id`, `.Name`, `.Price` |
| `updatedAt` | Not a column: `DateTime.UtcNow` when the message is built. `MenuItem.UpdatedAt` is set later by the interceptor on save, and does not move when only the extras change. |

✅ Every field of all 6 messages has a column.

## Points to review with Maria

1. Ids are Guids everywhere, including Identity (`IdentityUser<Guid>`). The Client ERD (UNI-54) must match: its customers are `IdentityUser<Guid>` too, so `customerId` in `OrderPlaced` is a real Guid.
2. `OrderItem.MenuItemId` and `Rating.MenuItemId` are real foreign keys to `MenuItem` (same database), which is why menu items are soft-deleted.
3. A rejection reason or cancel reason is stored in `OrderStatusHistory.Note`, not on `Order`.
4. `Payment.OrderId` is unique: one online payment per order.
5. `Rating` has a unique (`OrderId`, `MenuItemId`): a customer rates each dish once per order.
6. "Needs refund" (DEC-001) is not a column: an order that is `Rejected` or `Cancelled` and has a `Payment` row needs a refund.
