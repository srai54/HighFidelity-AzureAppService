# Service Bus — Azure Portal Walkthrough (create namespace → queue → send/receive)

A click-by-click guide to doing the whole Service Bus loop in the Azure Portal,
no code required. This is the portal counterpart to `scripts/05-service-bus.azcli`
(the CLI version) and `docs/SERVICE_BUS.md` (the theory). Great for a first look
or a live demo, because you can *see* a message go in and come back out.

> Cost: a **Basic** namespace (used here) is fractions of a cent for a demo —
> ~$0.05 per million operations. Everything below fits comfortably in a free
> credit. Delete the resource group when done (see `scripts/99-teardown.azcli`).

---

## Step 1 — Create the namespace

The namespace is the top-level container/endpoint that holds your queues (see
`docs/SERVICE_BUS.md` for the entity hierarchy).

1. In the [Azure Portal](https://portal.azure.com), click **Create a resource**
   (top-left **+**), search **"Service Bus"**, and choose **Service Bus** →
   **Create**.
2. Fill in the **Basics** tab:
   - **Subscription** — your subscription.
   - **Resource group** — pick your learning RG (e.g. `rg-highfidelity-learning`),
     or create one.
   - **Namespace name** — globally unique, e.g. `sb-highfid-demo` (Azure appends
     `.servicebus.windows.net`).
   - **Location** — a region near you.
   - **Pricing tier** — **Basic** is enough for a plain queue. (Choose **Standard**
     only if you also want *topics/subscriptions* — Basic doesn't support those.)
3. Click **Review + create** → **Create**. Wait for "Your deployment is complete"
   (about a minute), then **Go to resource**.

## Step 2 — Go to the resource and create the queue

1. On the namespace's **Overview** page, click **+ Queue** (top toolbar).
2. In the **Create queue** panel:
   - **Name** — `orders` (this repo's code publishes to a queue named exactly
     `orders`, so match it if you want the app to line up).
   - Leave the defaults for **Max queue size**, **Message time to live**, etc.
   - **Max delivery count** — default `10` is fine (after 10 failed deliveries a
     message is dead-lettered — see `docs/SERVICE_BUS.md`).
3. Click **Create**. The `orders` queue now appears in the namespace's
   **Queues** list.

## Step 3 — Open Service Bus Explorer

1. Click the **`orders`** queue to open it.
2. In the queue's left menu, click **Service Bus Explorer**. This is the built-in
   send/peek/receive tool — no code, no connection string to copy.

## Step 4 — Send messages

1. In Service Bus Explorer, select the **Send messages** tab (or **Send message**).
2. **Content Type** — leave as `text/plain` (or set `application/json`).
3. **Message body** — type a payload. To match this repo's `OrderCreatedMessage`
   shape, use JSON like:
   ```json
   { "OrderId": "ORD-1001", "Customer": "Ada Lovelace", "Amount": 42.50, "CreatedUtc": "2026-07-22T10:00:00Z" }
   ```
   (Any text works if you're just testing the queue mechanics — the shape only
   matters if a real receiver will deserialize it.)
4. Click **Send**. Send it a few times so there's a small backlog to look at.
5. Confirm it landed: on the queue's **Overview**, the **Active message count**
   goes up by however many you sent.

## Step 5 — Receive (or peek) messages

Back in **Service Bus Explorer**, use the **Receive** / **Peek** tab:

- **Peek** — shows messages *without* removing them (a read-only look). The active
  count stays the same. Good for "what's sitting in the queue right now."
- **Receive** — actually pulls messages. Choose the mode:
  - **ReceiveAndDelete** — reads and removes in one step (simple; the message is
    gone whether or not you did anything with it).
  - **PeekLock** — locks the message while you look; it's only removed when
    completed. This is the safe mode real consumers use (see the competing-consumers
    explanation in `docs/SERVICE_BUS.md`).

Click **Receive** and you'll see the message bodies you sent. The **Active message
count** on the Overview drops as messages are received/deleted.

> The **dead-letter** subqueue is also reachable here (a dropdown in Service Bus
> Explorer) — that's where messages land after exceeding max delivery count. Handy
> to know exactly where "messages that failed processing" go.

## Step 6 — Get the connection string (for the queue)

A connection string is what code/apps use to authenticate to Service Bus (the
portal's Service Bus Explorer didn't need one because it used your logged-in
identity). There are **two levels** you can get one at, and knowing the
difference is a common interview point:

### Namespace-level connection string (access to the whole namespace)
1. Open the **namespace** → left menu **Shared access policies**.
2. Click **RootManageSharedAccessKey** (the default policy, has Manage/Send/Listen).
3. Copy **Primary Connection String**. It looks like:
   ```
   Endpoint=sb://sb-highfid-demo.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=abc123...=
   ```
   This grants access to **every** queue/topic in the namespace — convenient for
   learning, but broad.

### Queue-level connection string (access to just this one queue)
This is the least-privilege, production-preferred option — a key scoped to a
**single queue** rather than the whole namespace:
1. Open the **`orders` queue** → left menu **Shared access policies**.
2. Click **+ Add**, give it a name (e.g. `sender-policy`), tick only the rights
   it needs — **Send** for a publisher, **Listen** for a receiver (rarely
   **Manage**) — and **Create**.
3. Click the new policy → copy **Primary Connection String**. It's the same
   shape but includes an **`EntityPath=orders`** segment, pinning it to that queue:
   ```
   Endpoint=sb://sb-highfid-demo.servicebus.windows.net/;SharedAccessKeyName=sender-policy;SharedAccessKey=xyz...=;EntityPath=orders
   ```

**Namespace vs. queue connection string — the rule of thumb:** use a
namespace-level string for convenience while learning; use **queue-level policies
with only Send or only Listen** in production, so a leaked publisher key can't
also drain (Listen) or reconfigure (Manage) your queues. The presence of
`EntityPath=` is the tell that a connection string is scoped to one entity.

> Best-of-all: skip connection strings entirely in Azure and use the app's
> **Managed Identity** + `DefaultAzureCredential` against the namespace, granting
> it the `Azure Service Bus Data Sender`/`Data Receiver` RBAC role — no key to
> leak or rotate (same pattern as Key Vault; see `docs/MANAGED_IDENTITY_ENTRA_ID.md`).

### Where to use it in this repo
- Set the namespace/queue connection string as `ServiceBus__ConnectionString` on
  the WebApp (publisher) and `ServiceBusConnection` on the Function App (receiver)
  — see `scripts/03-app-service.azcli` / `scripts/04-functions.azcli`.
- Then `POST /api/orders` publishes, and `OrderCreatedFunction` receives — the
  same loop you just did by hand in Service Bus Explorer, now driven by the app.
- Note: the .NET SDK's `ServiceBusClient` takes a **namespace-level** connection
  string and you name the queue in `CreateSender("orders")`; an `EntityPath`-scoped
  string is used with APIs that target a single entity. This repo uses the
  namespace form (see `ServiceBusPublisher.cs`).

---

## Quick recap

1. **Create a resource** → Service Bus → new **namespace** (Basic tier).
2. **Go to resource** → **+ Queue** → name it `orders`.
3. Open the queue → **Service Bus Explorer**.
4. **Send messages** (type JSON, click Send) → watch Active message count rise.
5. **Peek/Receive** → see them come back out → count drops.

That's the entire produce-store-consume loop of a queue, done in the portal. For
the *why* behind each piece (decoupling, load balancing, tiers, dead-lettering),
see `docs/SERVICE_BUS.md`.
