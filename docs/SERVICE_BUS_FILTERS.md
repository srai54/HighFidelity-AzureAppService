# Service Bus Subscription Filters — Interview-Depth Guide

Everything about **topic subscription filters**: what they are, the three types,
rule actions, the multi-rule gotcha, and how to create them in the portal, CLI, and
SDK. Builds on the topic/subscription and message-property basics in
`docs/SERVICE_BUS.md`.

## The mental model: a subscription is a topic + a set of rules

A **topic** fans a published message out to its **subscriptions** — but *which*
subscriptions actually get a copy is decided by each subscription's **filter**.
Precisely, a subscription holds one or more **rules**, and a rule is:

> **rule = filter (a match condition) + optional action (mutate the message on match)**

When a message is published to the topic, Service Bus evaluates each subscription's
rules against the message's **properties** (system + application — *not* the body,
see `docs/SERVICE_BUS.md`). If a rule matches, a copy of the message is placed in
that subscription.

### The default rule (`$Default`)

Every subscription you create **without specifying a filter** gets a built-in rule
named **`$Default`** whose filter is a **TrueFilter** — it matches everything. That's
why a brand-new subscription receives *all* messages: it's not "no filter," it's a
filter that always says yes. To make a subscription selective you either create it
*with* a filter, or **delete `$Default` and add your own rule** (forgetting to remove
`$Default` is the #1 reason "my filter isn't filtering" — see the gotcha below).

## The three filter types

### 1. SQL filter — a boolean expression over properties
A SQL-92-like predicate evaluated against the message's system + application
properties:
```sql
amount > 1000 AND region = 'US'
```
- Operators: `AND/OR/NOT`, `=, <>, >, <, >=, <=`, `LIKE`, `IN`, `IS NULL`.
- References properties by name (`amount`, `region` are application properties;
  system props like `sys.Label`/`Subject` are also addressable).
- Most flexible; slightly more evaluation cost than a correlation filter.

### 2. Correlation filter — efficient equality matching
Matches on **exact equality** of specific system properties (commonly
`CorrelationId`, `Subject`/Label, `MessageId`, `ContentType`, `ReplyTo`) and/or exact
application-property values:
```
Subject = 'OrderCreated'  AND  region = 'US'   (all equality, ANDed)
```
- No ranges/operators — equality only. Because of that it's **indexed and faster**
  than a SQL filter, so **prefer it when simple equality is enough**.

### 3. Boolean filter — all or nothing
- **TrueFilter** — matches every message (what `$Default` is).
- **FalseFilter** — matches nothing (a subscription that receives only what you later
  add via other rules, or is temporarily "off").

## Rule actions — mutate the message on match (SQL rules only)

A SQL rule can carry an **action** that modifies the message copy delivered to that
subscription — set or remove properties:
```sql
-- filter:
amount > 1000
-- action:
SET priority = 'escalated'; SET auditRequired = true;
```
Only that subscription's copy is changed; other subscriptions see the original. Handy
for tagging/enriching messages per-route without the publisher knowing about it.

## The multi-rule gotcha (duplicate delivery)

A subscription can have **multiple rules**, and they combine with **OR** — a message
is delivered if *any* rule matches. The catch: **if two rules both match the same
message, that subscription can receive the message more than once** (one copy per
matching rule). So keep a subscription's rules mutually exclusive, or use a single
rule with an `OR` expression instead of two overlapping rules. And remember the
`$Default` interaction: if you *add* a selective rule but *leave* `$Default` in place,
`$Default` still matches everything, so the subscription keeps getting all
messages — delete `$Default` when you add your own.

## Creating filters — portal, CLI, SDK

### Portal
When adding a subscription (topic → **+ Subscription**), expand the filter options
and choose a SQL or correlation filter. For an existing subscription, open it →
**Filters** (or **Rules**) → add/edit/delete rules (including removing `$Default`).
See `docs/SERVICE_BUS_PORTAL_GUIDE.md`.

### CLI
```bash
# create a subscription (gets $Default = TrueFilter automatically)
az servicebus topic subscription create \
  --resource-group "$RG" --namespace-name "$SB" \
  --topic-name orders-topic --name large-orders

# add a SQL rule...
az servicebus topic subscription rule create \
  --resource-group "$RG" --namespace-name "$SB" \
  --topic-name orders-topic --subscription-name large-orders \
  --name large-orders-rule --filter-sql-expression "amount > 1000"

# ...then remove the default so only your rule applies
az servicebus topic subscription rule delete \
  --resource-group "$RG" --namespace-name "$SB" \
  --topic-name orders-topic --subscription-name large-orders --name '$Default'
```

### SDK (management via `ServiceBusAdministrationClient`)
Creating subscriptions/rules is a **management** operation — a different client from
`ServiceBusClient` (which is data-plane send/receive), but the **same NuGet package**
(`Azure.Messaging.ServiceBus`, namespace `Azure.Messaging.ServiceBus.Administration`):
```csharp
using Azure.Messaging.ServiceBus.Administration;

var admin = new ServiceBusAdministrationClient(connectionString);

// create a subscription WITH a filter in one call (no $Default created):
await admin.CreateSubscriptionAsync(
    new CreateSubscriptionOptions("orders-topic", "large-orders"),
    new CreateRuleOptions("large-orders-rule", new SqlRuleFilter("amount > 1000")));

// a correlation filter (equality, more efficient):
await admin.CreateRuleAsync("orders-topic", "us-orders",
    new CreateRuleOptions("us-rule",
        new CorrelationRuleFilter { Subject = "OrderCreated", ApplicationProperties = { ["region"] = "US" } }));

// a SQL rule WITH an action:
await admin.CreateRuleAsync("orders-topic", "vip",
    new CreateRuleOptions("vip-rule", new SqlRuleFilter("amount > 1000"))
    {
        Action = new SqlRuleAction("SET priority = 'escalated'")
    });

// remove the default catch-all rule so only your rule applies:
await admin.DeleteRuleAsync("orders-topic", "large-orders", "$Default");
```

## Worked example — one publish, three routes

Publish "order placed" (with `ApplicationProperties` `amount`, `region`) to
`orders-topic`, with three subscriptions:
- `all-orders` — keeps `$Default` (TrueFilter) → gets **every** order.
- `large-orders` — SQL filter `amount > 1000` → only big orders.
- `us-orders` — correlation filter `region = 'US'` → only US orders.

One publish; each subscription independently gets a copy only if its rule matches.
A US order over $1000 lands in **all three**; a $50 EU order lands only in
`all-orders`.

## What's real vs. reference-only in this repo

There's no dedicated filter sample project here — filters are a topic/subscription
management concept, and the repo uses a single **queue** (no topic), so this is a
reference/interview doc. The SDK/CLI/portal steps are accurate to the real APIs. To
try it live you'd create a Standard-tier namespace, a topic, and subscriptions with
these rules (portal steps in `docs/SERVICE_BUS_PORTAL_GUIDE.md`).

---

## Interview Questions

**Q: How do topic subscription filters actually work?**
Each subscription has one or more rules; a rule is a filter (match condition) plus an
optional action. When a message is published to the topic, Service Bus evaluates each
subscription's rules against the message's properties (system + application, not the
body); if a rule matches, a copy is delivered to that subscription. So the topic
fans out, and filters decide which subscriptions each message actually reaches.

**Q: What are the filter types and when do you use each?**
SQL filter — a boolean expression over properties (ranges, LIKE, IN, etc.), most
flexible. Correlation filter — equality-only matching on system props
(CorrelationId, Subject, …) and/or app props; indexed and more efficient, so use it
when simple equality suffices. Boolean — TrueFilter (all, the default) / FalseFilter
(none).

**Q: What is the $Default rule and why does it trip people up?**
Every subscription created without a filter gets a `$Default` rule that's a
TrueFilter (matches everything) — that's why new subscriptions receive all messages.
If you add a selective rule but forget to delete `$Default`, the subscription still
matches everything via `$Default`, so it looks like your filter "isn't working."

**Q: Can a subscription receive the same message twice from filters?**
Yes — a subscription's multiple rules combine with OR, and if two rules both match a
message, that subscription gets a copy per matching rule (duplicate delivery). Keep
rules mutually exclusive or use one rule with an OR expression to avoid it.

**Q: What's a rule action?**
A SQL rule can carry an action that mutates the delivered copy on match — e.g.
`SET priority = 'escalated'` — enriching/tagging the message for that subscription
only, without changing what other subscriptions receive or requiring the publisher
to set it.

**Q: Which client creates subscriptions and rules in code?**
`ServiceBusAdministrationClient` (management/control-plane), not `ServiceBusClient`
(data-plane send/receive) — same NuGet package, `Administration` namespace. You use
`CreateSubscriptionAsync`/`CreateRuleAsync` with `SqlRuleFilter`/`CorrelationRuleFilter`
and optional `SqlRuleAction`.
