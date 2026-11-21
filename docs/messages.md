# RabbitMQ Messages

How Client.Web and Admin.Web talk to each other. Both apps must follow this document exactly. The C# classes in `Shared.Contracts` (UNI-10) are written from it.

**Related:** UNI-56 / [DEC-001](decisions-log.md) (order statuses), UNI-10 (contracts), UNI-54/55 (ERDs)

## Overview

| Message | Sent by | Received by | When |
|---|---|---|---|
| `OrderPlaced` | Client | Admin | Customer places an order |
| `PaymentCompleted` | Client | Admin | An online payment succeeds |
| `OrderCancelled` | Client | Admin | Customer cancels an order that is still `Placed` |
| `DishRated` | Client | Admin | Customer rates a dish from a delivered order |
| `OrderStatusChanged` | Admin | Client | Staff change an order's status |
| `MenuItemUpdated` | Admin | Client | Staff create, edit or delete a menu item |

## Exchange, routing keys and queues (UNI-11)

All messages go through **one durable topic exchange: `food.events`**. The publisher sends each message to the exchange with its routing key. The exchange copies it into every queue bound to that key. Why topic: see [DEC-002](decisions-log.md).

| Message | Direction | Routing key | Queue (durable) | Consumed by |
|---|---|---|---|---|
| `OrderPlaced` | Client → Admin | `order.placed` | `admin.order-placed` | Admin |
| `PaymentCompleted` | Client → Admin | `payment.completed` | `admin.payment-completed` | Admin |
| `OrderCancelled` | Client → Admin | `order.cancelled` | `admin.order-cancelled` | Admin |
| `DishRated` | Client → Admin | `dish.rated` | `admin.dish-rated` | Admin |
| `OrderStatusChanged` | Admin → Client | `order.status.changed` | `client.order-status-changed` | Client |
| `MenuItemUpdated` | Admin → Client | `menu.item.updated` | `client.menu-item-updated` | Client |

- **Naming:** routing keys are `<subject>.<event>` with dots (dots are what topic wildcards split on). Queues are `<receiving app>.<message>`, so the prefix shows who reads it.
- **In code:** never type these names by hand. Use `RoutingKeys`, `QueueNames` and `RabbitMqTopology.Bindings` in `Shared.Contracts/Messaging`. `RabbitMqTopology.RoutingKeyFor(typeof(OrderPlaced))` gives the key for a message.
- **Setup:** both apps call `RabbitMqTopology.DeclareAsync` at startup. It creates the exchange, the 6 queues and their bindings. Declaring something that already exists with the same settings does nothing, so it is safe to run every time and in both apps. If RabbitMQ is not reachable yet, it retries `RabbitMq:RetryCount` times (2s, 4s, 8s) before the app stops.
- **Durable** exchange and queues survive a RabbitMQ restart. Messages also survive when they are published as persistent (UNI-12).
- **Dead letters:** queue arguments are created in one place (`QueueArguments()`), where UNI-17 adds `x-dead-letter-exchange`.
- **Check it:** http://localhost:15672 → **Exchanges** → `food.events` → **Bindings**.

## Rules for every message

### Base class: `MessageBase`

Every message inherits these three fields.

| Field | Type | Rule |
|---|---|---|
| `messageId` | Guid | A new Guid for **every** message, even when the same thing is sent again. The receiver uses it to skip duplicates. |
| `createdAt` | DateTime (UTC) | When the message was created. |
| `correlationId` | Guid? | The `OrderId` for all order messages. `null` for `MenuItemUpdated`. For `DishRated` it is the `OrderId` of the rated order. |

### JSON rules

- **Serializer:** `System.Text.Json` only.
- **Property names:** camelCase (`orderId`, not `OrderId`).
- **Enums:** written as strings, exactly as named in C# (`"Delivery"`, `"OutForDelivery"`), never as numbers.
- **Dates:** ISO 8601 in **UTC**, ending in `Z` (`"2026-11-18T12:30:00Z"`).
- **Money:** JSON number, C# `decimal`, 2 decimal places (`12.50`).
- **Ids:** Guid strings.
- **Empty values:** written as `null`, not left out. Empty lists are written as `[]`.
- **One shared options object:** `Shared.Contracts` has a `JsonOptions` class with these settings. Both apps use it for sending and receiving, and never create their own `JsonSerializerOptions`.

### Message type

The message type goes in RabbitMQ's **`BasicProperties.Type`** field, set to the class name (`"OrderPlaced"`, `"OrderStatusChanged"`, ...). The consumer reads `Type` first, then deserializes the body into that class. The JSON body itself has no type field.

Other `BasicProperties`:

| Property | Value |
|---|---|
| `Type` | Message class name |
| `MessageId` | Same as `messageId` in the body |
| `CorrelationId` | Same as `correlationId` in the body (empty when `null`) |
| `ContentType` | `application/json` |
| `Persistent` | `true`, so messages survive a RabbitMQ restart |

A message with an unknown `Type`, or a body that can't be deserialized, is logged and rejected (not requeued), so it can't block the queue.

### Shared enums

From `Shared.Contracts.Enums` (UNI-56): `OrderStatus`, `PaymentStatus`, `DeliveryType`, `PaymentMethod`.

One new enum is needed for UNI-10: **`MenuItemAction`**, with the values `Created`, `Updated` and `Deleted`.

---

## 1. `OrderPlaced` (Client → Admin)

Sent once when the customer confirms an order. Admin creates its own copy of the order from it, so it has to contain **everything** the restaurant needs to see.

| Field | Type | Required | Notes |
|---|---|---|---|
| `orderId` | Guid | yes | Created by Client. Used by every later message about this order. |
| `orderNumber` | string | yes | Short number for people, e.g. `"FO-1042"`. Created by Client. |
| `customerId` | Guid | yes | Id in the Client database. |
| `customerName` | string | yes | |
| `customerPhone` | string | yes | So staff or the driver can call. |
| `deliveryType` | DeliveryType | yes | `Delivery` or `Pickup` |
| `address` | string? | only for `Delivery` | `null` for `Pickup` |
| `notes` | string? | no | e.g. "no onions", "ring twice" |
| `paymentMethod` | PaymentMethod | yes | `Online` or `Cash` |
| `items` | list | yes, at least 1 | See below |
| `total` | decimal | yes | Sum of all items with extras |
| `placedAt` | DateTime (UTC) | yes | |

Each item in `items`:

| Field | Type | Notes |
|---|---|---|
| `menuItemId` | Guid | Id from `MenuItemUpdated` |
| `name` | string | Name **at the time of ordering**, in case the menu changes later |
| `unitPrice` | decimal | Price **at the time of ordering**, without extras |
| `quantity` | int | 1 or more |
| `extras` | list of `{ name, price }` | Can be `[]` |

```json
{
  "messageId": "6f1c2b8e-3d4a-4f7b-9c1e-2a5d8e7f9b10",
  "createdAt": "2026-11-18T12:30:00Z",
  "correlationId": "a3e1c9d2-7b4f-4c8a-9e2d-1f6b5a4c3d21",
  "orderId": "a3e1c9d2-7b4f-4c8a-9e2d-1f6b5a4c3d21",
  "orderNumber": "FO-1042",
  "customerId": "c7d8e9f0-1a2b-4c3d-8e4f-5a6b7c8d9e01",
  "customerName": "Sara Haddad",
  "customerPhone": "+961 70 123 456",
  "deliveryType": "Delivery",
  "address": "Hamra Street, Building 12, 3rd floor, Beirut",
  "notes": "Please ring twice",
  "paymentMethod": "Online",
  "items": [
    {
      "menuItemId": "11111111-2222-4333-8444-555555555555",
      "name": "Chicken Burger",
      "unitPrice": 9.50,
      "quantity": 2,
      "extras": [
        { "name": "Extra cheese", "price": 1.00 },
        { "name": "Bacon", "price": 1.50 }
      ]
    },
    {
      "menuItemId": "66666666-7777-4888-9999-000000000000",
      "name": "Fries",
      "unitPrice": 3.00,
      "quantity": 1,
      "extras": []
    }
  ],
  "total": 27.00,
  "placedAt": "2026-11-18T12:30:00Z"
}
```

`total` = (9.50 + 1.00 + 1.50) × 2 + 3.00 × 1 = **27.00**

**Can Admin do its job?** ✅ It can show the order with all items and extras, call the customer, know where to deliver, and know how the order is paid. Admin sets the starting payment status itself: `Cash` → `CashOnDelivery`, `Online` → `Unpaid` until `PaymentCompleted` arrives.

---

## 2. `PaymentCompleted` (Client → Admin)

Sent when an online payment succeeds. Not sent for cash orders: Admin marks cash orders `Paid` itself on delivery (DEC-001).

| Field | Type | Required | Notes |
|---|---|---|---|
| `orderId` | Guid | yes | |
| `paymentId` | Guid | yes | Id of the payment in the Client database |
| `amount` | decimal | yes | Should equal the order `total` |
| `method` | PaymentMethod | yes | Always `Online` for now |
| `paidAt` | DateTime (UTC) | yes | |

```json
{
  "messageId": "0b9a8c7d-6e5f-4a3b-8c2d-1e0f9a8b7c6d",
  "createdAt": "2026-11-18T12:30:20Z",
  "correlationId": "a3e1c9d2-7b4f-4c8a-9e2d-1f6b5a4c3d21",
  "orderId": "a3e1c9d2-7b4f-4c8a-9e2d-1f6b5a4c3d21",
  "paymentId": "d4c3b2a1-9f8e-4d7c-8b6a-5f4e3d2c1b0a",
  "amount": 27.00,
  "method": "Online",
  "paidAt": "2026-11-18T12:30:18Z"
}
```

**Can Admin do its job?** ✅ It sets the order's payment status to `Paid`, so staff know they can accept it. If `amount` is different from the order total, Admin logs a warning.

**Order of arrival:** `PaymentCompleted` can arrive before `OrderPlaced`. Admin then keeps it and applies it once the order exists, or Client sends `OrderPlaced` only after the payment step. We need to pick one when coding (UNI-10).

---

## 3. `OrderCancelled` (Client → Admin)

Sent when the customer cancels. Allowed only while the order is `Placed` (DEC-001).

| Field | Type | Required | Notes |
|---|---|---|---|
| `orderId` | Guid | yes | |
| `reason` | string? | no | What the customer typed, if anything |
| `cancelledAt` | DateTime (UTC) | yes | |

```json
{
  "messageId": "9e8d7c6b-5a4f-4e3d-9c2b-1a0f9e8d7c6b",
  "createdAt": "2026-11-18T12:32:05Z",
  "correlationId": "a3e1c9d2-7b4f-4c8a-9e2d-1f6b5a4c3d21",
  "orderId": "a3e1c9d2-7b4f-4c8a-9e2d-1f6b5a4c3d21",
  "reason": "Ordered by mistake",
  "cancelledAt": "2026-11-18T12:32:05Z"
}
```

**Can Admin do its job?** ✅ If the order is still `Placed`, Admin sets `Cancelled`. If it is already `Accepted` or later, Admin follows the race rule in DEC-001: ignore, log, and send `OrderStatusChanged` with the current status.

---

## 4. `DishRated` (Client → Admin)

Sent when the customer rates a dish from one of their delivered orders.

| Field | Type | Required | Notes |
|---|---|---|---|
| `ratingId` | Guid | yes | Id in the Client database |
| `orderId` | Guid | yes | The order the dish came from |
| `menuItemId` | Guid | yes | The rated dish |
| `customerName` | string | yes | Shown next to the review in Admin |
| `stars` | int | yes | 1 to 5 |
| `comment` | string? | no | |
| `ratedAt` | DateTime (UTC) | yes | |

```json
{
  "messageId": "5c4b3a29-1807-4f6e-8d5c-4b3a29180706",
  "createdAt": "2026-11-18T14:10:00Z",
  "correlationId": "a3e1c9d2-7b4f-4c8a-9e2d-1f6b5a4c3d21",
  "ratingId": "e1f2a3b4-c5d6-4e7f-8a9b-0c1d2e3f4a5b",
  "orderId": "a3e1c9d2-7b4f-4c8a-9e2d-1f6b5a4c3d21",
  "menuItemId": "11111111-2222-4333-8444-555555555555",
  "customerName": "Sara Haddad",
  "stars": 5,
  "comment": "Best burger in town!",
  "ratedAt": "2026-11-18T14:10:00Z"
}
```

**Can Admin do its job?** ✅ It can show the review under the dish and work out the dish's average rating. `menuItemId` matches Admin's own menu, so no dish name is needed.

---

## 5. `OrderStatusChanged` (Admin → Client)

Sent every time staff change an order's status, and again when Admin ignores a late cancel (DEC-001 race rule).

| Field | Type | Required | Notes |
|---|---|---|---|
| `orderId` | Guid | yes | |
| `newStatus` | OrderStatus | yes | |
| `estimatedDeliveryTime` | DateTime? (UTC) | only for `Accepted` | When the order should arrive (Delivery) or be ready (Pickup). A time, not a number of minutes. |
| `reason` | string? | only for `Rejected` | Shown to the customer. Also used for the note when a late cancel is refused. |
| `changedAt` | DateTime (UTC) | yes | |

Accepted:

```json
{
  "messageId": "7a6b5c4d-3e2f-4a1b-9c8d-7e6f5a4b3c2d",
  "createdAt": "2026-11-18T12:31:40Z",
  "correlationId": "a3e1c9d2-7b4f-4c8a-9e2d-1f6b5a4c3d21",
  "orderId": "a3e1c9d2-7b4f-4c8a-9e2d-1f6b5a4c3d21",
  "newStatus": "Accepted",
  "estimatedDeliveryTime": "2026-11-18T13:15:00Z",
  "reason": null,
  "changedAt": "2026-11-18T12:31:40Z"
}
```

Rejected:

```json
{
  "messageId": "2d3c4b5a-6f7e-4d8c-9b0a-1f2e3d4c5b6a",
  "createdAt": "2026-11-18T12:31:40Z",
  "correlationId": "a3e1c9d2-7b4f-4c8a-9e2d-1f6b5a4c3d21",
  "orderId": "a3e1c9d2-7b4f-4c8a-9e2d-1f6b5a4c3d21",
  "newStatus": "Rejected",
  "estimatedDeliveryTime": null,
  "reason": "Sorry, we are out of chicken today.",
  "changedAt": "2026-11-18T12:31:40Z"
}
```

**Can Client do its job?** ✅ It updates the order the customer sees, shows the ETA after Accepted and the reason after Rejected, and turns off the Cancel button once the status is not `Placed`. Client can also work out payment for cash orders: `Delivered` means paid.

---

## 6. `MenuItemUpdated` (Admin → Client)

Sent when staff create, edit or delete a menu item. Client keeps its own copy of the menu from these messages, so `Created` and `Updated` always carry the **full** item, not only the fields that changed.

| Field | Type | Required | Notes |
|---|---|---|---|
| `action` | MenuItemAction | yes | `Created`, `Updated` or `Deleted` |
| `menuItemId` | Guid | yes | Id in the Admin database |
| `name` | string? | not for `Deleted` | |
| `description` | string? | no | |
| `price` | decimal? | not for `Deleted` | |
| `photoUrl` | string? | no | Full URL Client can load directly |
| `isVegetarian` | bool | yes | |
| `isAvailable` | bool | yes | `false` = shown as "sold out", cannot be ordered |
| `category` | `{ id, name }`? | not for `Deleted` | Client creates the category if it doesn't have it yet |
| `extras` | list of `{ id, name, price }` | yes | The **full** list. It replaces Client's list. `[]` if none. |
| `updatedAt` | DateTime (UTC) | yes | Client ignores a message older than the `updatedAt` it already has for this item |

Created / Updated:

```json
{
  "messageId": "3f4e5d6c-7b8a-4c9d-8e0f-1a2b3c4d5e6f",
  "createdAt": "2026-11-18T09:00:00Z",
  "correlationId": null,
  "action": "Updated",
  "menuItemId": "11111111-2222-4333-8444-555555555555",
  "name": "Chicken Burger",
  "description": "Grilled chicken breast, lettuce, tomato and garlic sauce",
  "price": 9.50,
  "photoUrl": "https://localhost:7101/images/menu/chicken-burger.jpg",
  "isVegetarian": false,
  "isAvailable": true,
  "category": {
    "id": "aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee",
    "name": "Burgers"
  },
  "extras": [
    { "id": "12345678-90ab-4cde-8f01-23456789abcd", "name": "Extra cheese", "price": 1.00 },
    { "id": "87654321-ba09-4edc-8f10-dcba98765432", "name": "Bacon", "price": 1.50 }
  ],
  "updatedAt": "2026-11-18T09:00:00Z"
}
```

Deleted:

```json
{
  "messageId": "8b7a6c5d-4e3f-4a2b-9c1d-0e9f8a7b6c5d",
  "createdAt": "2026-11-18T10:00:00Z",
  "correlationId": null,
  "action": "Deleted",
  "menuItemId": "11111111-2222-4333-8444-555555555555",
  "name": null,
  "description": null,
  "price": null,
  "photoUrl": null,
  "isVegetarian": false,
  "isAvailable": false,
  "category": null,
  "extras": [],
  "updatedAt": "2026-11-18T10:00:00Z"
}
```

**Can Client do its job?** ✅ It can show the full menu with categories, photos, prices, the vegetarian badge and extras, and block sold-out dishes, without ever calling Admin. On `Deleted`, Client hides the item but keeps the row, because old orders and ratings still point to it.

---

## Points to agree on

Both of you should confirm these before UNI-10:

1. **All ids are Guids**, including menu items and categories. The ERDs (UNI-54/55) need to use the same types.
2. **`orderNumber` is created by Client**, format `FO-<number>`.
3. **Enum values are sent as written in C#** (`"OutForDelivery"`), not in camelCase.
4. **`estimatedDeliveryTime` is a UTC date/time**, not "30 minutes".
5. **`PaymentCompleted` arriving before `OrderPlaced`:** Admin holds it until the order arrives, or Client only sends `OrderPlaced` after payment. Pick one.
6. **New enum `MenuItemAction`** (Created, Updated, Deleted) is added to `Shared.Contracts.Enums` in UNI-10.
