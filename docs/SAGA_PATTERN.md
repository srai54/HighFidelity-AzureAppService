# The Saga Pattern — Distributed Transactions Across Services

Relates to `docs/SERVICE_BUS.md` (the messaging that carries a saga) and
`docs/DURABLE_FUNCTIONS.md` (one way to orchestrate one).

## The problem it solves

In a single database you can wrap several writes in **one ACID transaction** —
all commit or all roll back. But once a business operation spans **multiple
services**, each with its **own database** (place order → charge payment → reserve
inventory → arrange shipping, all separate services), there's no shared
transaction you can wrap around them. You can't hold a lock across four services
and the network for the seconds/minutes the whole thing takes. (The old answer,
two-phase commit / distributed transactions, doesn't scale and couples everything
together.)

A **Saga** is the pattern for keeping data consistent across services **without** a
distributed transaction: model the operation as a **sequence of local
transactions**, one per service, where each step publishes an event/message that
triggers the next. If a step **fails**, you don't roll back (you can't — earlier
steps already committed in their own databases); instead you run **compensating
transactions** that semantically undo the completed steps.

## The analogy that makes it click

Booking a vacation: flight, hotel, and car, each booked through a different company
— there's no single "undo the whole trip" button. So you book them one at a time.
If the car rental falls through after the flight and hotel are already booked, you
don't magically rewind — you **cancel the hotel** and **cancel the flight** (each a
deliberate compensating action) to get back to "nothing booked." A saga is exactly
that: forward steps that each commit independently, and explicit "cancel" steps to
walk it back when something later fails.

## Compensating transactions — the core idea

A compensating transaction is the *semantic* inverse of a step, not a literal
rollback:
- Charged the card → **refund** it.
- Reserved inventory → **release** the reservation.
- Created the order → **mark it cancelled**.

They must be **idempotent** (safe to run more than once — retries happen) and they
account for the fact that the original action really did happen and may have had
side effects (a refund is a new transaction, not an erasure of the charge).

## Two ways to coordinate a saga

### 1. Choreography (event-driven, no central coordinator)
Each service listens for events and reacts, publishing its own event when done. No
one is "in charge" — the workflow emerges from services reacting to each other.
- **Order Service** commits the order → publishes `OrderCreated`.
- **Payment Service** hears `OrderCreated` → charges → publishes `PaymentCompleted`
  (or `PaymentFailed`).
- **Inventory Service** hears `PaymentCompleted` → reserves stock → publishes
  `StockReserved`, and so on.
- On a failure event, the relevant services run their compensations.

**This is where Service Bus fits directly** — topics/subscriptions (or queues) are
the transport carrying those events (see `docs/SERVICE_BUS.md`). Pros: loose
coupling, no central bottleneck. Cons: the overall flow is implicit and spread
across services — hard to see "what's the whole process" or debug where it stalled.

### 2. Orchestration (a central coordinator drives it)
One component (the **orchestrator**) explicitly tells each service what to do next
and tracks progress: "call Payment; if ok, call Inventory; if that fails, call
Payment's compensation." The workflow lives in one place.

**This is where Durable Functions fits** (`docs/DURABLE_FUNCTIONS.md`) — a durable
orchestrator function calling activities in sequence, with try/catch to invoke
compensations, is a textbook saga orchestrator: it survives restarts, remembers how
far it got, and can drive the compensation path on failure. Pros: the whole flow is
explicit and centrally visible/debuggable. Cons: the orchestrator is a component you
own and must keep available; it's a (soft) central point.

## Choreography vs. orchestration — how to choose

- **Choreography** for simple sagas with few steps and teams that want maximum
  decoupling — the events *are* the workflow.
- **Orchestration** when the flow is complex, has many branches/compensations, or
  you need one place to see and reason about the whole process. Most non-trivial
  sagas trend toward orchestration for exactly that visibility.

## How it connects to this repo

- The **Service Bus** publisher/receiver (`OrdersController` → `OrderCreatedFunction`)
  is the messaging substrate a **choreography** saga would ride on — each service
  reacting to the previous one's event over queues/topics.
- The **Durable Functions** order-batch orchestrator (`OrderBatchOrchestration.cs`)
  is the shape an **orchestration** saga takes — sequential activity calls with a
  central coordinator; adding try/catch + compensation activities turns it into a
  saga orchestrator.
- **Cross-entity transactions** (see `docs/SERVICE_BUS.md`) are *not* a saga — they
  give atomicity within Service Bus (e.g. complete-and-forward), but a saga exists
  precisely because you **can't** get one transaction across independent services'
  databases. Knowing that boundary is a common interview distinction.

## What's real vs. reference-only in this repo

This is an architectural pattern doc — there's no dedicated saga implementation in
the repo. It's explained in terms of the Service Bus and Durable Functions pieces
that *are* here, which are the two building blocks a real saga is built from.

---

## Interview Questions

**Q: What is the Saga pattern and what problem does it solve?**
It maintains data consistency across a business operation that spans multiple
services (each with its own database) without a distributed transaction. The
operation is broken into a sequence of local transactions, each triggering the
next; if a step fails, previously completed steps are undone with compensating
transactions rather than a rollback (which isn't possible across independent
databases).

**Q: What's a compensating transaction?**
The semantic inverse of a completed step — refund a charge, release a reservation,
cancel an order. It doesn't erase the original (that already happened); it performs
a new action that undoes the effect. Compensations must be idempotent because
they'll be retried.

**Q: Choreography vs. orchestration for a saga — what's the difference?**
Choreography has no central coordinator: each service reacts to events and emits
its own, so the workflow is emergent (Service Bus topics/queues carry the events) —
loosely coupled but hard to see/debug as a whole. Orchestration has a central
coordinator (e.g. a Durable Functions orchestrator) that explicitly calls each step
and drives compensations on failure — the whole flow is visible in one place, at the
cost of owning that coordinator.

**Q: Why not just use a distributed transaction (two-phase commit) instead of a saga?**
2PC holds locks across all participants for the duration and requires a coordinator
all of them trust and stay connected to — it doesn't scale, hurts availability
(a slow/failed participant blocks everyone), and tightly couples services. Sagas
trade strict atomicity for availability and loose coupling, accepting eventual
consistency plus compensations instead.

**Q: How does a saga differ from Service Bus cross-entity transactions?**
Cross-entity transactions give atomicity *within Service Bus* (e.g. complete a
message and send another as one unit). A saga spans *different services and
databases*, where no shared transaction exists at all — which is the whole reason
the pattern (local transactions + compensations) is needed. One is a messaging
feature; the other is a distributed-systems consistency pattern.
