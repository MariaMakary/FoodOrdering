# Client database ERD (UNI-54)

**Diagram:** [erd-client.png](erd-client.png) (edit [erd-client.drawio](erd-client.drawio) with draw.io, then export the PNG again)
**Based on:** [DEC-001](decisions-log.md) (statuses), [DEC-002](decisions-log.md) (out-of-order rule), [messages.md](messages.md) (message fields), [erd-admin.md](erd-admin.md) (same rules)
**Used by:** UNI-6 (turns this into EF Core entities and migrations)

## Rules

- **All Ids are `Guid`, including Identity.** Customers use `IdentityUser<Guid>` and `IdentityRole<Guid>`, and the context is `IdentityDbContext<ApplicationUser, ApplicationRole, Guid>`, the same as Admin.
- **Identity tables drop the `AspNet` prefix:** `Users`, `Roles`, `UserRoles`, `UserClaims`, `UserLogins`, `UserTokens`, `RoleClaims` (renamed in `OnModelCreating`). The only role is `Customer`.
- **No foreign keys to the Admin database.** `MenuItem`, `MenuItemExtra` and `Category` are a copy of the Admin menu and keep the **Admin Ids** from `MenuItemUpdated`, so `OrderPlaced.items[].menuItemId` and `DishRated.menuItemId` match Admin's rows.
- **Audit columns ([DEC-003](decisions-log.md)):** every business table has `CreatedAt` and `UpdatedAt`, set on save by `AuditInterceptor` from `Shared.Persistence`. Never set them by hand. Not on the join tables, `OrderStatusHistory` or the messaging tables.
- **The menu is read-only for Client.** Only the `MenuItemUpdated` consumer writes `MenuItem`, `MenuItemExtra` and `Category`. Admin's `updatedAt` is stored in `MenuItem.SourceUpdatedAt`, and a message is applied only when its `updatedAt >= SourceUpdatedAt`. `UpdatedAt` is the Client's own audit time and is never compared with Admin's.
- **`Order` and `MenuItem` use optimistic concurrency** with PostgreSQL's `xmin`. `Order` is changed by the customer (cancel, payment) and by the `OrderStatusChanged` consumer. `MenuItem` can get two `MenuItemUpdated` messages at once. The second save throws `DbUpdateConcurrencyException`, which is retried.
- **Out-of-order status messages (DEC-002):** `Order.LastStatusChangedAt` holds the `changedAt` of the last applied `OrderStatusChanged`. A message with an older `changedAt` is ignored.
- **Order lines are copies.** `OrderItem` and `OrderItemExtra` store the name and price **at checkout**, so later menu changes never change an old order.
- **`OrderNumber`** is `FO-<number>`, taken from the PostgreSQL sequence `OrderNumbers` (starts at 1000), and unique.
- **Money** is `numeric(10,2)`, never negative: every money column has a `>= 0` check constraint (`CK_<Table>_<Column>`), the same as Admin. **Times** are UTC `timestamptz`, always `DateTime.UtcNow`.
- **Enums** (`OrderStatus`, `PaymentStatus`, `DeliveryType`, `PaymentMethod`) are stored as strings, the same as in the messages. `PaymentAttemptStatus` (`Pending`, `Succeeded`, `Failed`) is a Client-only enum in `Client.Web/Models/Enums`.
- **Menu items are never really deleted.** `MenuItemUpdated` with `Deleted` sets `IsDeleted = true`, because old orders and ratings still point to them. Extras that disappear from the list are deleted, and their `CartItemExtra` rows are deleted with them (cascade).

## Tables

| Group | Table | What it stores |
|---|---|---|
| Identity | `Users` | Customer logins. Extended with `FirstName`, `LastName`, `DefaultAddress` (`PhoneNumber` is built into Identity). |
| Identity | `Roles`, `UserRoles` | The `Customer` role and who has it |
| Cart | `Cart` | One basket per customer (`CustomerId` is unique) |
| Cart | `CartItem`, `CartItemExtra` | Dishes in the basket and the extras chosen for each one. Prices are read live from the menu. |
| Orders | `Order` | One row per order, created at checkout, then updated by `OrderStatusChanged`. `xmin` protects it from two changes at once. |
| Orders | `OrderItem`, `OrderItemExtra` | Dishes and extras in the order, with name and price **at checkout** |
| Orders | `OrderStatusHistory` | Every status the customer has seen, with when and why (timeline on the order page) |
| Orders | `Payment` | The Stripe payment for an online order. At most one per order. |
| Orders | `Rating` | The customer's dish ratings. One per dish per order. |
| Menu | `Category` | Categories from `MenuItemUpdated` (Name is unique). `SortOrder` sets their order on the menu page. |
| Menu | `MenuItem` | Copy of the Admin menu. Same columns as Admin, plus `SourceUpdatedAt` (Admin's time) and `xmin`. |
| Menu | `MenuItemExtra` | Extras a dish can have. The full list is replaced by each `MenuItemUpdated`. |
| Messaging | `ProcessedMessage` | MessageIds already handled, so a duplicate message is skipped |
| Messaging | `MessageLog` | Every message in or out, for debugging. `Direction`: In, Out. `Status`: Received, Processed, Published, Failed. |
| Messaging | `OutboxMessage` | Messages waiting to be sent to RabbitMQ (UNI-44) |

## Where every message field comes from or is stored

This is the test for this ticket: every field Client sends has a column to come from, and every field Client receives has a column to land in.

**Every message:** `messageId` → `OutboxMessage.Id` when sent, `ProcessedMessage.MessageId` when received, and `MessageLog.MessageId` both ways. `createdAt` and `correlationId` are kept in `MessageLog.Payload` (`correlationId` is the `OrderId`).

### `OrderPlaced` (sent at checkout)

| Field | Column |
|---|---|
| `orderId` | `Order.Id` |
| `orderNumber` | `Order.OrderNumber` |
| `customerId` | `Order.CustomerId` |
| `customerName` | `Users.FirstName` + `Users.LastName` |
| `customerPhone` | `Users.PhoneNumber` |
| `deliveryType` | `Order.DeliveryType` |
| `address` | `Order.Address` (pre-filled from `Users.DefaultAddress`) |
| `notes` | `Order.Notes` |
| `paymentMethod` | `Order.PaymentMethod` |
| `items[].menuItemId` | `OrderItem.MenuItemId` |
| `items[].name` | `OrderItem.Name` |
| `items[].unitPrice` | `OrderItem.UnitPrice` |
| `items[].quantity` | `OrderItem.Quantity` |
| `items[].extras[].name` | `OrderItemExtra.Name` |
| `items[].extras[].price` | `OrderItemExtra.Price` |
| `total` | `Order.Total` |
| `placedAt` | `Order.CreatedAt` (the Client creates the row when the order is placed) |

Also: `Order.Status = Placed`, `Order.LastStatusChangedAt = CreatedAt`, `Order.PaymentStatus` is `Unpaid` (Online) or `CashOnDelivery` (Cash), a first `OrderStatusHistory` row is added, the cart is emptied, and the message goes into `OutboxMessage` in the same save.

### `PaymentCompleted` (sent when Stripe confirms)

| Field | Column |
|---|---|
| `orderId` | `Payment.OrderId` |
| `paymentId` | `Payment.Id` |
| `amount` | `Payment.Amount` |
| `method` | `Payment.Method` |
| `paidAt` | `Payment.PaidAt` |

Also: `Payment.Status = Succeeded`, `Order.PaymentStatus = Paid`.

### `OrderCancelled` (sent when the customer cancels a `Placed` order)

| Field | Column |
|---|---|
| `orderId` | `Order.Id` |
| `reason` | `OrderStatusHistory.Note` |
| `cancelledAt` | `OrderStatusHistory.ChangedAt` and `Order.LastStatusChangedAt` |

Also: `Order.Status = Cancelled`. If Admin had already accepted it, Admin's `OrderStatusChanged` (newer `changedAt`) puts the real status back (DEC-001).

### `DishRated` (sent when the customer rates a dish)

| Field | Column |
|---|---|
| `ratingId` | `Rating.Id` |
| `orderId` | `Rating.OrderId` |
| `menuItemId` | `Rating.MenuItemId` |
| `customerName` | `Users.FirstName` + `Users.LastName` (via `Rating.CustomerId`) |
| `stars` | `Rating.Stars` |
| `comment` | `Rating.Comment` |
| `ratedAt` | `Rating.CreatedAt` (the Client creates the row when the dish is rated) |

### `OrderStatusChanged` (received)

| Field | Column |
|---|---|
| `orderId` | `Order.Id` |
| `newStatus` | `Order.Status` and `OrderStatusHistory.Status` |
| `estimatedDeliveryTime` | `Order.EstimatedDeliveryTime` |
| `reason` | `Order.RejectionReason` (when `Rejected`) and `OrderStatusHistory.Note` |
| `changedAt` | `Order.LastStatusChangedAt` and `OrderStatusHistory.ChangedAt` |

Also: `Delivered` on a cash order sets `Order.PaymentStatus = Paid`.

### `MenuItemUpdated` (received)

| Field | Column |
|---|---|
| `action` | `Created` (insert), `Updated` (update), `Deleted` (`MenuItem.IsDeleted = true`) |
| `menuItemId` | `MenuItem.Id` |
| `name` | `MenuItem.Name` |
| `description` | `MenuItem.Description` |
| `price` | `MenuItem.Price` |
| `photoUrl` | `MenuItem.PhotoUrl` |
| `isVegetarian` | `MenuItem.IsVegetarian` |
| `isAvailable` | `MenuItem.IsAvailable` |
| `category.id`, `category.name`, `category.sortOrder` | `Category.Id`, `Category.Name`, `Category.SortOrder` (created if missing, updated if changed) and `MenuItem.CategoryId` |
| `extras[].id`, `.name`, `.price` | `MenuItemExtra.Id`, `.Name`, `.Price` |
| `updatedAt` | `MenuItem.SourceUpdatedAt` |

✅ Every field of all 6 messages has a column.

## Points to review with Joy

1. `MenuItem`, `MenuItemExtra` and `Category` use the Admin Ids, so Admin can match `OrderPlaced` and `DishRated` to its own menu without a lookup.
2. `Category.SortOrder` sets the order of categories on the menu page. Admin owns it (new `SortOrder` column on the Admin `Category`), and `MenuItemUpdated.category.sortOrder` copies it to Client.
3. `customerName` and `customerPhone` are read from `Users` at checkout and are not copied onto `Order`. The outbox row keeps the exact values that were sent.
4. `OrderStatusHistory.Note` holds the cancel reason the customer typed and the rejection reason. `Order.RejectionReason` is a copy of the latest rejection reason, so the order page doesn't need to search the history.
5. `Payment` has `Method` (sent in `PaymentCompleted`) and a Client-only `Status` (`PaymentAttemptStatus`: Pending, Succeeded, Failed). A failed Stripe attempt updates the same row with a new `ProviderSessionId`.
6. `Rating.CustomerId` repeats `Order.CustomerId`, so "my ratings" is one query.
