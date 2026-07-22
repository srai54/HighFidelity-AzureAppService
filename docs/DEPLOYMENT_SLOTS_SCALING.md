# App Service Deployment Slots & Scaling

## The plain-English version

Two separate but related questions come up here: **"how do you deploy without downtime/risk?"** and **"how does the app handle more load?"**

### Deployment slots

A **deployment slot** is a fully separate, live copy of your App Service — same infrastructure, its own URL, running independently — that you deploy new code to *before* it's serving real traffic. The standard flow:
1. Deploy the new version to a **staging slot** (not production).
2. Test it for real, against its own URL, running on real App Service infrastructure — not a local approximation.
3. **Swap** staging and production. Azure does this by re-pointing the routing, not by copying files — so it's fast (seconds, not a redeploy), and if something's wrong, you **swap back** just as fast.

This is **blue-green deployment**: two identical environments ("blue" and "green"), only one serving live traffic at a time, and a release is just flipping which one is live. The value: you eliminate the deploy-and-pray window where broken code is already live before anyone's tested it for real, and rollback is a swap, not a redeploy of the old version.

A detail worth knowing: some app settings can be marked "slot-specific" (they stay with the slot, not the swap) — this is exactly how a staging slot can point at a staging database/connection string while production points at the real one, without the swap causing staging to suddenly talk to production's database or vice versa.

### Scaling

App Service scaling splits into two independent axes:
- **Scale up (vertical)** — move to a bigger/more powerful App Service Plan tier (more CPU, more RAM, more instances allowed). You're changing *what size machine* you're running on.
- **Scale out (horizontal)** — run *more instances* of the same tier, with a load balancer spreading requests across them. This is what "autoscale rules" configure: e.g., "if average CPU > 70% for 10 minutes, add an instance; if < 20%, remove one," or scheduled rules ("scale out to 5 instances every weekday 9am–5pm").

The App Service Plan is the actual unit you pay for and scale — it's the compute (a set of VMs, abstracted away from you) that one or more App Services can share. Which pricing tier you're on determines what's even possible: the Free/Shared tiers can't autoscale or use deployment slots at all; you need at least the Standard tier for slots, and Standard-or-above for autoscale rules.

## The analogy that makes it click

**Deployment slots** are like a restaurant with a private test kitchen next door, built identically to the real kitchen. The new menu gets cooked and tested in the test kitchen first — for real, on real equipment — while the dining room keeps being served from the original kitchen. "Swapping" is switching which kitchen the waitstaff walks to, not moving any equipment — fast, and reversible by switching back if the new dish turns out wrong.

**Scaling out** is calling in more cooks to work the same-size kitchen in parallel when it gets busy; **scaling up** is moving the whole operation into a bigger kitchen with more stations. You can do either, or both, depending on whether the bottleneck is "not enough hands" or "not enough space to work in."

## How this repo relates to it

This is infrastructure/platform configuration, not application code — there's nothing to implement in `src/WebApp` or `src/Functions` for slots or scaling; it's a property of how the App Service *resource* is configured and deployed in Azure, orthogonal to what's actually running inside it. Nothing in this repo demonstrates it directly, which is why this doc is conceptual-only, same as `docs/MESSAGING_COMPARISON.md`.

## What's real vs. reference-only in this repo

Entirely conceptual — there's no local emulator or free-tier equivalent for deployment slots or autoscale rules (they're properties of a real App Service Plan resource, not something `dotnet run` can approximate), and no Azure subscription was available in this environment to exercise them for real.

---

## Interview Questions

**Q: Walk through a zero-downtime deployment using slots.**
Deploy the new build to a staging slot (a separate, fully live instance with its own URL, not yet receiving production traffic). Test against that slot directly. Once confirmed good, swap staging and production — Azure re-points routing rather than redeploying, so it's near-instant. If a problem shows up after the swap, swap back immediately to restore the previous version, which is still intact in what's now the staging slot.

**Q: Why is a swap so much faster and safer than redeploying?**
Because nothing is actually being copied or rebuilt at swap time — both slots already have their code running and warmed up; the swap just changes which slot sits behind the production hostname. That's also exactly why rollback is just as fast: swap back, and the previous version (still fully intact and running in what's now the staging slot) is live again immediately.

**Q: What's the difference between scaling up and scaling out?**
Scaling up (vertical) means moving to a more powerful App Service Plan tier — more CPU/RAM per instance. Scaling out (horizontal) means running more instances of the same tier, with load balanced across them. They're independent decisions: you can scale out on a modest tier, scale up without adding instances, or do both.

**Q: What triggers an autoscale rule, typically?**
A metric threshold sustained over a time window — most commonly average CPU or memory percentage over some minutes (e.g., "CPU > 70% for 10 minutes → add an instance"), but can also be a request queue length, a custom metric, or a fixed schedule (e.g., scale out ahead of a known daily traffic peak, scale back in overnight).

**Q: What's an App Service Plan, and why does its tier matter beyond just performance?**
It's the actual compute resource (a set of VM instances) that one or more App Services run on — the unit you're billed for and the unit that scales. The tier matters beyond raw performance because features are gated by tier: deployment slots require at least Standard; autoscale rules need Standard or above; Free/Shared tiers can't do either, regardless of how the app itself is configured.

**Q: Can staging and production slots safely use different connection strings/config?**
Yes — via "slot-specific" (a.k.a. "sticky") app settings, which are pinned to a slot and do *not* travel with the content on a swap. This is what lets a staging slot point at a staging database while production points at the real one: after a swap, each slot still points at whatever its sticky settings say, so you don't accidentally have "production" pointed at the staging database (or vice versa) right after a swap.
