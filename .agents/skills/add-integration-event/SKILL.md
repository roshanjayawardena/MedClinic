---
name: add-integration-event
description: >
  Use when one module needs to react to something that happened in another module.
  Triggers on "publish an event", "notify another module", "cross-module event",
  "appointment booked event", "patient registered integration event".
---

# Add a cross-module integration event

Read first: `eventing.md`, `phi-and-tenancy.md`.

## Step 1 — Define the event in the publisher's Contracts

```csharp
// src/Modules/<Publisher>/<Publisher>.Contracts/Events/<Name>IntegrationEvent.cs
namespace <Publisher>.Contracts.Events;

public sealed record <Name>IntegrationEvent(
    Guid <Entity>Id,         // surrogate Id — no PHI
    Guid ClinicId,           // tenant — required for Outbox relay
    DateTimeOffset OccurredAt);
```

Rules:
- No PHI in the event payload — carry Ids and metadata only.
- Include `ClinicId` so the relay can restore tenant context.
- `OccurredAt` is the business time (from `TimeProvider.GetUtcNow()`), not the message-sent time.

## Step 2 — Publish inside the handler (same SaveChanges)

```csharp
// Inside the publishing handler, BEFORE SaveChangesAsync
outbox.Publish(new <Name>IntegrationEvent(entity.Id, tenant.ClinicId, now));
await db.SaveChangesAsync(ct).ConfigureAwait(false);
// Outbox row and business row commit together — atomically
```

Never publish after `SaveChanges`. A crash between save and publish silently drops the event.

## Step 3 — Reference the Contracts in the subscriber module

Add a project reference from `<Subscriber>` (runtime) → `<Publisher>.Contracts` only.
Never reference the publisher's runtime project.

```xml
<!-- <Subscriber>/<Subscriber>.csproj -->
<ProjectReference Include="..\..\<Publisher>\<Publisher>.Contracts\<Publisher>.Contracts.csproj" />
```

## Step 4 — Write the subscriber handler (idempotent)

```csharp
// src/Modules/<Subscriber>/<Subscriber>/Features/On<Name>/On<Name>Handler.cs
public sealed class On<Name>Handler(<Subscriber>DbContext db)
{
    public async Task Handle(<Name>IntegrationEvent e, CancellationToken ct)
    {
        // Idempotency guard — check before acting
        if (await db.<Entities>.AnyAsync(x => x.<Publisher>Id == e.<Entity>Id, ct))
            return;

        // ... subscriber logic ...
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
```

## Step 5 — Register the subscriber

Wire the subscriber in the subscriber module's `RegisterServices()`.
Exact registration API depends on the Outbox/bus implementation in BuildingBlocks.

## Step 6 — Update the event catalogue in eventing.md

Add a row to the catalogue table in `.agents/rules/eventing.md`:
```
| <Publisher> | `<Name>IntegrationEvent` | <Subscriber> (what it does) |
```

## Step 7 — Verify

- `dotnet build` — both publisher and subscriber projects.
- Write an integration test: publish the business command, assert the subscriber's side effect.
- Run `phi-review` if the event payload touches clinical data.
