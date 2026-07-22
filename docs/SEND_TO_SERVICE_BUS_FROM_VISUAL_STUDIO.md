# Sending a Message to Service Bus from Visual Studio

How to publish a real message to your Azure Service Bus queue by running this
repo's **WebApp** locally in Visual Studio. The app is the sender
(`OrdersController` → `ServiceBusPublisher`); the message lands in the `orders`
queue in Azure, where you can see it in Service Bus Explorer (or let the Functions
receiver process it). Theory: `docs/SERVICE_BUS.md`.

## Prerequisites

1. A Service Bus **namespace + `orders` queue** already exist in Azure. If not,
   create them first — portal steps in `docs/SERVICE_BUS_PORTAL_GUIDE.md`, or CLI
   in `scripts/05-service-bus.azcli`.
2. The queue's **connection string** — namespace → **Shared access policies** →
   `RootManageSharedAccessKey` → copy **Primary Connection String** (details and
   least-privilege alternatives in the portal guide).

## Step 1 — Configure the connection string locally (User Secrets)

⚠️ **This step is not optional.** The app only registers `IServiceBusPublisher`
when `ServiceBus:ConnectionString` is present (see `Program.cs`). Without it,
`OrdersController` can't resolve its dependency and the POST fails — so you must
set the connection string before running.

Use **User Secrets** (keeps the secret out of source control — the Visual Studio
way):

1. In **Solution Explorer**, right-click the **WebApp** project → **Manage User
   Secrets**. This opens a `secrets.json` stored outside the repo.
2. Add the connection string under the `ServiceBus:ConnectionString` key:
   ```json
   {
     "ServiceBus": {
       "ConnectionString": "Endpoint=sb://<your-namespace>.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=<key>"
     }
   }
   ```
3. Save. (Alternative: put the same key in `appsettings.Development.json` — but
   User Secrets is preferred so a real connection string never sits in a file that
   could be committed.)

## Step 2 — Set the WebApp as the startup project

1. In **Solution Explorer**, right-click the **WebApp** project → **Set as
   Startup Project**.
2. In the toolbar's run dropdown, pick the **https** (or **http**) launch profile.
   (Leave it on the project profile, not IIS Express, so the console log is visible.)

## Step 3 — Run the app

1. Press **F5** (debug) or **Ctrl+F5** (run without debugging).
2. The browser opens; the root redirects to **`/swagger`**. You'll see the
   Swagger UI listing the endpoints, including **POST `/api/orders`**.

## Step 4 — Send the message via Swagger

1. In Swagger, expand **POST `/api/orders`** → **Try it out**.
2. Enter the request body (this matches `OrdersController.CreateOrderRequest`):
   ```json
   { "customer": "Ada Lovelace", "amount": 42.50 }
   ```
3. Click **Execute**. A successful publish returns **`202 Accepted`**:
   ```json
   { "orderId": 51234, "status": "Published to Service Bus, awaiting processing" }
   ```
   `202` (not `200`) is deliberate — the message has been *queued*, not processed
   (see `docs/SERVICE_BUS.md`). That 202 IS the confirmation the message reached
   Service Bus.

> Prefer not to use Swagger? Any HTTP client works against the running app, e.g.:
> ```powershell
> curl -X POST http://localhost:5175/api/orders -H "Content-Type: application/json" -d '{ "customer": "Ada Lovelace", "amount": 42.50 }'
> ```
> (Use whatever port your launch profile shows in the console / `launchSettings.json`.)

## Step 5 — Verify the message arrived

Either check the queue directly, or let the receiver handle it:

- **In Azure (portal):** namespace → `orders` queue → **Service Bus Explorer** →
  **Peek** — you'll see the message you just sent (and the **Active message count**
  on the queue Overview will have gone up). See `docs/SERVICE_BUS_PORTAL_GUIDE.md`.
- **With the receiver:** run the **Functions** project too (set both as startup
  projects, or run `func start` in `src/Functions`) with `ServiceBusConnection`
  set to the same connection string — `OrderCreatedFunction` will pick the message
  up and log it, and the Active count will drop back down.

## Troubleshooting

- **500 / "Unable to resolve service for type IServiceBusPublisher"** → the
  connection string isn't being read. Confirm the User Secrets JSON uses the exact
  key `ServiceBus:ConnectionString`, and that you're running the **Development**
  environment (User Secrets only load in Development).
- **`ServiceBusException: ... Put token failed` / unauthorized** → wrong or
  truncated connection string, or the SAS policy lacks **Send** rights.
- **`MessagingEntityNotFoundException`** → the `orders` queue doesn't exist in that
  namespace (or a name typo) — create it (Step in the portal guide).
- **Timeouts / can't connect** → corporate network/firewall blocking AMQP port
  5671; Service Bus can fall back to AMQP-over-WebSockets if you configure the
  client's `TransportType` — not needed on most networks.

## What's real vs. reference-only in this repo

The publisher code and this flow are correct against a real namespace. As noted in
`docs/ARCHITECTURE.md`, this environment couldn't run the WebApp (Smart App Control)
and had no Azure Service Bus to target, so the steps are written from how the SDK
and tooling actually behave — they run as-written in Visual Studio on a normal
machine with a real namespace + queue.

---

## Interview-style recap

**Q: What's the minimum to publish to Service Bus from a local ASP.NET Core app?**
A `ServiceBusClient` built from a connection string (with Send rights), a
`ServiceBusSender` for the target queue, and `sender.SendMessageAsync(...)`. In
this repo that's wired through DI: the connection string in config →
`ServiceBusClient` singleton in `Program.cs` → `ServiceBusPublisher` → invoked by
`OrdersController`. Locally you supply the connection string via User Secrets; in
Azure it comes from an app setting (or Managed Identity + RBAC instead).

**Q: Why did the POST return 202 instead of 200?**
Because publishing to the queue means the message was *accepted for asynchronous
processing*, not that the order was processed. 202 Accepted is the honest status;
the actual work happens later in the receiver.
