# Decisions Log

Design decisions that both apps (Client.Web and Admin.Web) must follow. New decisions are added at the bottom.

---

## DEC-001: Order lifecycle and status rules (UNI-56)

**Date:** 2026-11-18
**Related tickets:** UNI-89 (Admin status changes), UNI-69 (Client cancel), UNI-57 (messages)
**Code:** `Shared.Contracts/Enums/`
**Diagram:** [order-lifecycle.png](order-lifecycle.png)

### Enums

| Enum | Values |
|---|---|
| `OrderStatus` | Placed, Accepted, Rejected, Preparing, Ready, OutForDelivery, Delivered, Cancelled |
| `PaymentStatus` | Unpaid, Paid, CashOnDelivery |
| `DeliveryType` | Delivery, Pickup |
| `PaymentMethod` | Online, Cash |

### Allowed status changes

Any change that is not in this table is not allowed. The app that receives it ignores it and logs a warning.

| From | To | Who | App | Message |
|---|---|---|---|---|
| (new) | Placed | Customer | Client | `OrderPlaced` |
| Placed | Cancelled | Customer | Client | `OrderCancelled` |
| Placed | Accepted (+ETA) | Staff/Admin | Admin | `OrderStatusChanged` |
| Placed | Rejected (+reason) | Staff/Admin | Admin | `OrderStatusChanged` |
| Accepted | Preparing | Staff/Admin | Admin | `OrderStatusChanged` |
| Preparing | Ready | Staff/Admin | Admin | `OrderStatusChanged` |
| Ready | OutForDelivery | Staff/Admin | Admin | `OrderStatusChanged` (Delivery orders only) |
| Ready / OutForDelivery | Delivered | Staff/Admin | Admin | `OrderStatusChanged` |

Rules that follow from the table:

- **Final statuses:** `Delivered`, `Rejected` and `Cancelled`. An order never leaves them.
- **Pickup orders** skip `OutForDelivery`. For pickup, `Ready → Delivered` means the customer collected the order.
- **Delivery orders** go `Ready → OutForDelivery → Delivered`. `Ready → Delivered` is also allowed so staff can fix a missed step.
- **Accepted must include an ETA** and **Rejected must include a reason**, both sent in `OrderStatusChanged`.
- **The customer can cancel only while the order is `Placed`.** After that they have to contact the restaurant.

### Source of truth

**The Admin app owns the order status.** The Client app keeps a copy to show the customer. It sets only two statuses itself: `Placed` (when the order is created) and `Cancelled` (while the order is still `Placed`). Every `OrderStatusChanged` from Admin overwrites the Client's copy.

### Race rule: the customer cancels while the restaurant accepts

We use the suggested rule, plus one extra step (step 3):

1. Admin checks every incoming message against the status in **its own database**.
2. `OrderCancelled` arrives and the order is still `Placed`: Admin sets it to `Cancelled`.
3. `OrderCancelled` arrives and the order is already `Accepted` or later: Admin **ignores the cancel and logs it** (order id, current status, time). Admin then **sends `OrderStatusChanged` again with the current status** (for example `Accepted` + ETA).
4. The Client always applies `OrderStatusChanged` from Admin, even if its own copy says `Cancelled`. The customer sees the status go back to `Accepted` with the note: *"The restaurant had already accepted your order, so it could not be cancelled."*
5. Staff try to accept an order that Admin already set to `Cancelled`: the change is not in the table, so Admin refuses it and shows "Order was cancelled by the customer".

**Why:** only one side can decide, and the restaurant is the side that starts spending money (cooking). Sending the current status again in step 3 means the Client is always corrected, even if the first `Accepted` message reached it before it sent the cancel.

### Other tricky cases

| Case | Rule |
|---|---|
| The same message arrives twice | Setting the same status again does nothing. No error. |
| A message asks for a change not in the table (e.g. `Ready` for a `Cancelled` order) | Ignored and logged as a warning. Status does not change. |
| Messages reach the Client out of order (e.g. `Preparing` before `Accepted`) | The Client takes whatever Admin sends, because Admin already checked it. The status is correct once all messages arrive. |
| Online payment, then the order is `Rejected` or `Cancelled` | `PaymentStatus` stays `Paid`, and Admin marks the order as needing a refund. Refunds are done by hand (out of scope). |
| Cash payment | The order is created as `CashOnDelivery`. Admin sets it to `Paid` when the order becomes `Delivered`. |
| Online payment | The order is created as `Unpaid` and becomes `Paid` when payment succeeds. Staff should not accept an `Unpaid` online order. |

### Scenario check (on paper)

**1. Normal delivery**

| Step | Change | Who / App | Message | In table? |
|---|---|---|---|---|
| Customer orders | (new) → Placed | Customer / Client | OrderPlaced | ✅ |
| Restaurant accepts | Placed → Accepted + ETA | Staff / Admin | OrderStatusChanged | ✅ |
| Cooking starts | Accepted → Preparing | Staff / Admin | OrderStatusChanged | ✅ |
| Food is ready | Preparing → Ready | Staff / Admin | OrderStatusChanged | ✅ |
| Driver leaves | Ready → OutForDelivery | Staff / Admin | OrderStatusChanged | ✅ (Delivery order) |
| Customer receives it | OutForDelivery → Delivered | Staff / Admin | OrderStatusChanged | ✅ |

**2. Pickup**

| Step | Change | Who / App | Message | In table? |
|---|---|---|---|---|
| Customer orders | (new) → Placed | Customer / Client | OrderPlaced | ✅ |
| Restaurant accepts | Placed → Accepted + ETA | Staff / Admin | OrderStatusChanged | ✅ |
| Cooking starts | Accepted → Preparing | Staff / Admin | OrderStatusChanged | ✅ |
| Food is ready | Preparing → Ready | Staff / Admin | OrderStatusChanged | ✅ |
| Staff tries "out for delivery" | Ready → OutForDelivery | Staff / Admin | none | ❌ refused (Pickup order) |
| Customer collects it | Ready → Delivered | Staff / Admin | OrderStatusChanged | ✅ |

**3. Cancel race**

| Time | Admin app | Client app |
|---|---|---|
| t0 | Order is `Placed` | Order is `Placed` |
| t1 | Staff click Accept → `Accepted`, sends OrderStatusChanged(Accepted) | Customer clicks Cancel → `Cancelled`, sends OrderCancelled |
| t2 | Receives OrderCancelled, order is `Accepted` → ignores it, logs it, sends OrderStatusChanged(Accepted) again | Receives OrderStatusChanged(Accepted) → sets `Accepted`, shows the note |
| t3 | `Accepted` | `Accepted` ✅ both apps agree |

All three scenarios are covered by the rules above.

---

## DEC-002: One topic exchange for all messages (UNI-11)

**Date:** 2026-11-21
**Related tickets:** UNI-10 (contracts), UNI-12 (publisher), UNI-17 (dead letters)
**Code:** `Shared.Contracts/Messaging/` (`RoutingKeys`, `QueueNames`) and `Shared.Messaging/` (`RabbitMqTopology`, `RabbitMqConnection`, `AddRabbitMq`)
**Details:** the full table of routing keys and queues is in [messages.md](messages.md#exchange-routing-keys-and-queues-uni-11)

### Decision

- **One exchange, `food.events`, of type `topic`, durable.** Both apps publish every message to it.
- **One durable queue per message type**, named after the app that reads it (`admin.order-placed`, `client.order-status-changed`, ...).
- **Routing keys** are `<subject>.<event>` with dots: `order.placed`, `order.status.changed`, `menu.item.updated`.

### Why a topic exchange and not a direct one

| | Direct | Topic (chosen) |
|---|---|---|
| How a queue is matched | Routing key must be **exactly** equal | Exact match **or** wildcards: `*` = one word, `#` = any number of words |
| Our 6 messages today | Works | Works the same way |
| Adding a listener for a whole group later | One binding per key, and a new binding every time a key is added | One binding: `order.#` gets every order message, including future ones |
| A queue that logs or audits everything | Bind all 6 keys by hand | One binding: `#` |

Today every binding is an exact key, so topic behaves exactly like direct. Choosing topic costs nothing now and keeps the door open, for example for a monitoring queue or a notification service that wants `order.#` without touching the publisher.

### Why one exchange and not one per app or per message

- The publisher always sends to the same place, `food.events`, and only changes the routing key. Adding a message never needs a new exchange.
- Which app gets which message is decided only by the bindings, so it is all visible on one page in the Management UI.

### Why one queue per message type and not one queue per app

- Each consumer handles one message type, so it always knows which class to read and can be tested alone.
- A problem with one message (for example `DishRated` failing) does not block the others, and the Management UI shows how many of each message are waiting.
- Failed messages are easy to find per type once dead-lettering is added (UNI-17).

### Messages about the same order can arrive out of order

RabbitMQ keeps order only inside one queue. Each queue has its own consumer, so `PaymentCompleted` or `OrderCancelled` can be processed before the `OrderPlaced` of the same order, and Admin can't save a payment or a cancel for an order it doesn't have yet (foreign key to `Orders`).

**Rule:** when a consumer receives a message for an order that doesn't exist yet, it retries the message (5 attempts, about 200 ms apart) before rejecting it to the dead-letter queue (UNI-17). The Client's `OrderStatusChanged` consumer ignores a message whose `changedAt` is older than the last one it applied for that order, so an older status never overwrites a newer one.

### Why both apps declare everything

Declaring an exchange or queue that already exists with the same settings does nothing. So both apps run the same `RabbitMqTopology.DeclareAsync` at startup, and it does not matter which app starts first: a message published before the other app ever ran still lands in a durable queue and waits.

### Code layout and connections

- **`Shared.Contracts`** holds only plain classes: messages, enums, `JsonOptions`, `RoutingKeys`, `QueueNames`. It has no RabbitMQ dependency, so anything can reference it.
- **`Shared.Messaging`** holds the RabbitMQ code. Each app calls `builder.Services.AddRabbitMq(builder.Configuration)`, which validates the settings, registers one `RabbitMqConnection` and declares the topology at startup (`RabbitMqTopologyInitializer`).
- **One connection per app, many channels.** RabbitMQ connections are expensive TCP connections, channels are cheap. `RabbitMqConnection` opens the connection once, shares it for the whole app, and hands out channels with `CreateChannelAsync`. The connection is named after the app (`Client.Web`, `Admin.Web`), so the Management UI shows who is connected.

---

## DEC-003: Audit columns on every business table (UNI-54)

**Date:** 2026-11-22
**Related tickets:** UNI-54 (Client ERD), UNI-55 (Admin ERD), UNI-6 (Client EF Core)
**Code:** `Shared.Persistence/` (`AuditableEntity`, `IAuditable`, `AuditInterceptor`)

### Decision

- Every business table has `CreatedAt` and `UpdatedAt` (UTC). The entity inherits `AuditableEntity`, which declares both once with private setters. `ApplicationUser` already inherits `IdentityUser<Guid>`, so it implements `IAuditable` directly.
- Nobody sets them by hand, and the private setters make it impossible. `AuditInterceptor` sets both through EF when a row is added, and only `UpdatedAt` when it changes.
- Each `DbContext` registers it in `OnConfiguring` with `optionsBuilder.AddInterceptors(AuditInterceptor.Instance)`, so the app, the tests and `dotnet ef` all use it.

### Which tables

- **With audit columns:** `Users` and every menu, cart and order table in both apps.
- **Without:** the join tables (`UserRoles`, `CartItemExtra`), `OrderStatusHistory` (a row is never changed, `ChangedAt` is its time) and the messaging tables (`ProcessedMessage`, `MessageLog`, `OutboxMessage`), which have their own time columns.

### Times that come from a message are separate columns

`CreatedAt` and `UpdatedAt` always mean "when this database wrote the row". A time that comes from the other app keeps its own name:

| App | Column | From |
|---|---|---|
| Admin | `Order.PlacedAt` | `OrderPlaced.placedAt` (`Order.CreatedAt` is when Admin received it) |
| Admin | `Rating.RatedAt` | `DishRated.ratedAt` |
| Client | `MenuItem.SourceUpdatedAt` | `MenuItemUpdated.updatedAt`, used to ignore older menu messages |

### Why a shared project

Both apps need the same interface and interceptor, and the interceptor needs EF Core. `Shared.Contracts` must stay free of dependencies and `Shared.Messaging` is for RabbitMQ, so EF Core code that both apps share goes in **`Shared.Persistence`**.

---

## DEC-004: Outbox on every publish (UNI-44)

**Date:** 2026-11-22
**Related tickets:** UNI-12 (publisher), UNI-20 (offline test), every ticket that sends a message
**Code:** `Shared.Persistence/Outbox/` (`OutboxMessage`, `IOutbox`, `EfOutbox`, `OutboxDispatcher`, `AddOutbox`), `Shared.Messaging/IMessagePublisher.cs`

### Decision

No feature publishes to RabbitMQ directly. It adds the message to the `OutboxMessages` table **in the same `SaveChangesAsync` as its data change**, and a background dispatcher sends it.

```
Service ──IOutbox.Add + SaveChangesAsync──▶ business tables + OutboxMessages   (one transaction)
                                                         │
OutboxDispatcher (every 2 s) ──reads unsent rows, oldest first──┘
        │
        └──IMessagePublisher.PublishAsync──▶ RabbitMQ ──▶ sets SentAt
```

### Why

Without it, "save then publish" has two ways to go wrong:

| What happens | Without the outbox | With the outbox |
|---|---|---|
| RabbitMQ is down when the customer orders | The order is saved but `OrderPlaced` is lost, or the checkout fails | The order and the outbox row are saved. The row is sent when RabbitMQ is back |
| The app crashes between the save and the publish | The message is lost | The row is still there and is sent after the restart |
| The save fails (e.g. a check constraint) | A message may already be sent for data that doesn't exist | Nothing is saved, so nothing is sent |

### Rules

- **Order:** rows are sent oldest `CreatedAt` first. If a row fails, the dispatcher stops for that round, so later rows (e.g. a `PaymentCompleted`) never overtake an earlier one (its `OrderPlaced`). The failed row keeps `SentAt = null`, `Attempts` goes up, and `LastError` holds the reason.
- **At least once:** if the app stops after RabbitMQ confirmed but before `SentAt` is saved, the row is sent again. Consumers already ignore duplicates by `MessageId` (UNI-18), so this is safe.
- **Only known messages:** `OutboxMessage.From` refuses a message type that has no queue in `RabbitMqTopology.Bindings`, so a message can't be saved that the dispatcher could never route.
- **Payload** is the message JSON from `JsonOptions.Default`, stored as `text` like `MessageLog.Payload`. `Type` is the class name (`OrderPlaced`), the same as the RabbitMQ `Type` header.
- **`IMessagePublisher.PublishAsync(MessageBase message)`** takes the base type, because the dispatcher only knows the type at runtime. The publisher finds the routing key with `RabbitMqTopology.RoutingKeyFor(message.GetType())`.
- `PollInterval` (2 s) and `BatchSize` (50) are in the optional `Outbox` settings section.

### Why it lives in `Shared.Persistence`

Every part of the outbox uses EF Core (the table, `EfOutbox`, the dispatcher's query). Only the dispatcher touches messaging, through the `IMessagePublisher` interface. Following DEC-003, EF Core code both apps share goes in `Shared.Persistence`, which now references `Shared.Messaging` (Persistence → Messaging → Contracts, no cycle). `Shared.Messaging` stays free of EF Core.