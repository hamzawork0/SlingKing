# SlingKing R1 · M2b Stock Ledger — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Knowing how many of each bag exist and what they cost — an Inventory screen the owner can put real counts into, and a movement history that answers "where did those four go?".

**Architecture:** One service and one pure helper in `SlingKing.Application/Inventory/`. `StockService` owns the write path, the transaction and the row lock; `FifoCostEngine` is a **static class over in-memory layers** that returns a consumption plan and touches no database at all — which is what keeps the cost arithmetic unit-testable without Postgres. Every table this milestone needs already exists and is already constrained by M1: **there is no migration**.

**Tech Stack:** .NET 8.0.408 (`net8.0`), EF Core 8.0.30, Npgsql 8.0.11, xUnit 2.9.3 + Shouldly 4.3.0; React 19.2.8, TypeScript 5.9.3, Vite 8.2.2, TanStack Query 5, React Router 7, Tailwind 4, axios, vitest.

**Spec:** `docs/superpowers/specs/2026-08-30-r1-m2b-stock-ledger-design.md` — normative. Read it alongside this plan.

---

## Global Constraints

These apply to every task. Do not restate them per task; do not violate them in any task.

- **Target framework is `net8.0` everywhere.** `global.json` pins the SDK to 8.0.408.
- **`SlingKing.Domain` has zero `<ProjectReference>` and zero `<PackageReference>`.** An architecture test fails the build if this is violated. M2b adds no domain code.
- **`SlingKing.Application` must not reference any database-provider package.** It has `Microsoft.EntityFrameworkCore.Relational` and the Domain project, and nothing else. This is why a Postgres error is matched by a **string** on the constraint name, never by `PostgresException` or a SqlState — see `ProductService.UpdateAsync`. Do not add `Npgsql.EntityFrameworkCore.PostgreSQL` to Application to "do it properly"; it will fail review.
- **Dependency direction is `Api → Application → Domain`, and `Infrastructure → Application → Domain`.** `Application` never references `Infrastructure`.
- **No secrets in source, ever.** All configuration comes from environment variables.
- **Money is `numeric(18,2)`; cost unit costs are `numeric(18,4)`; quantities are `integer`; timestamps are `timestamptz` in UTC; enums are `smallint`.** Prices are **rupees with decimals, not paise** — `formatInr` takes rupees.
- **Cost is optional. Null means *unknown*, never zero.** No code in this milestone may substitute `0m` for an absent cost, and no total may be a partial sum.
- **Table and column names are `snake_case`;** C# properties stay `PascalCase`. `EFCore.NamingConventions` does the mapping — never hand-write `HasColumnName`.
- **Integration tests run only against the `test` schema.** `SlingKingApiFactory` throws if the connection string does not target it. Never point a test at `public`.
- **The Supabase session pooler caps this project at 15 client connections.** `SlingKingApiFactory` bounds itself to 5. A test opening its own connections must bound its pool via `MaxPoolSize`, never by reducing concurrency.
- **Never use the direct connection host (`db.<ref>.supabase.co`)** — it is IPv6-only and unreachable from this machine.
- **There is no migration in this milestone.** The table count stays at 28 (`SchemaConstraintTests.cs:209`). If a task finds itself writing a migration, stop and raise it — the schema already anticipated everything here.
- **Business logic never lives in a controller.** Controllers validate shape, delegate, and map to a response.
- **Internal exceptions never reach the client.** Everything becomes RFC 7807 `ProblemDetails`.
- **No deletion, anywhere, and no update of a historical row.** `NoDeleteRoutesTests` proves the first. Nothing in this milestone may `Remove()` or mutate an `InventoryTransaction`, `InventoryCostConsumption`, or a layer's `OriginalQty`/`UnitCost`. A layer's `RemainingQty` is the one field that legitimately changes.
- **Validation attributes on a `record` bind to the primary-constructor parameters, not with a `[property:]` target.** A `[property:]` target compiles and then throws at request time. See `Contracts/Auth/AuthContracts.cs`. Do not "fix" it.
- **Every number in the UI carries the `.num` class**, so counts and money render in tabular JetBrains Mono.
- **Commit at the end of every task**, with the message given in that task's final step.

---

## File Structure

```
backend/src/SlingKing.Application/
├─ Common/
│  └─ AppException.cs               MOVED from Catalog/CatalogModels.cs, renamed
└─ Inventory/
   ├─ InventoryModels.cs            input and view records
   ├─ FifoCostEngine.cs             static, pure, no IAppDbContext
   ├─ IStockService.cs
   └─ StockService.cs               write path, reads, the transaction and the lock

backend/src/SlingKing.Api/
├─ Contracts/Inventory/InventoryContracts.cs
└─ Controllers/InventoryController.cs

backend/tests/SlingKing.UnitTests/Inventory/
└─ FifoCostEngineTests.cs           the whole cost arithmetic, no database

backend/tests/SlingKing.IntegrationTests/Api/
└─ InventoryEndpointTests.cs        one file: movements, refusals, recount, reads
backend/tests/SlingKing.IntegrationTests/Persistence/
└─ StockConcurrencyTests.cs         its own connections, its own pool bound

frontend/src/
├─ api/inventory.ts
└─ features/inventory/
   ├─ InventoryPage.tsx             the list
   ├─ StockItemPage.tsx             history, worth, and the two forms
   ├─ RecordMovement.tsx            type / quantity / reason
   ├─ Recount.tsx                   one field, shows the arithmetic
   └─ recount.test.ts              the arithmetic sentence, in vitest
```

`FifoCostEngine` being static and database-free is the load-bearing decision in this
structure. The moment it takes an `IAppDbContext` its tests need Postgres, and the
riskiest arithmetic in Release 1 loses its fast feedback loop.

---

## Task 1: Rename `CatalogException` to `AppException`

Pure refactor. No behaviour changes, no new tests. The existing suite is the test:
it asserts status codes and message text, not type names, so it must stay green
untouched.

**Files:**
- Create: `backend/src/SlingKing.Application/Common/AppException.cs`
- Modify: `backend/src/SlingKing.Application/Catalog/CatalogModels.cs` (remove the class)
- Modify: `backend/src/SlingKing.Application/Catalog/CategoryService.cs`
- Modify: `backend/src/SlingKing.Application/Catalog/ProductService.cs`
- Modify: `backend/src/SlingKing.Application/Catalog/ICategoryService.cs`
- Modify: `backend/src/SlingKing.Application/Catalog/IProductService.cs`
- Modify: `backend/src/SlingKing.Api/Middleware/ProblemDetailsExceptionHandler.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `SlingKing.Application.Common.AppException` with `int StatusCode` and three
  static factories: `NotFound(string)`, `Conflict(string, Exception? inner = null)`,
  `Invalid(string)`. Every later task throws this and nothing else for caller-caused
  failures.

- [x] **Step 1: Capture the current test count**

Run: `cd backend && dotnet test 2>&1 | Select-String -Pattern "Passed!|Failed!"`

Write the two numbers down. Task 1 must not change either of them. If the suite is
not green before you start, stop and report that — do not begin a refactor on a red
suite.

- [x] **Step 2: Create the new file**

Create `backend/src/SlingKing.Application/Common/AppException.cs`:

```csharp
namespace SlingKing.Application.Common;

/// <summary>
/// A failure the caller caused and is allowed to read about. Every message on this type is
/// written by us and carries nothing from an inner exception, a SQL fragment or a type name,
/// which is what makes it safe for <c>ProblemDetailsExceptionHandler</c> to return verbatim.
/// Anything else thrown still becomes a generic 500.
/// </summary>
public sealed class AppException : Exception
{
    public int StatusCode { get; }

    private AppException(int statusCode, string message, Exception? inner = null)
        : base(message, inner) => StatusCode = statusCode;

    public static AppException NotFound(string message) => new(404, message);

    /// <summary>
    /// <paramref name="inner"/> is for a rejection we inferred from a database failure rather
    /// than checked ourselves. It never reaches the client — the handler logs it so an
    /// unexpected failure is not silently dressed up as an ordinary conflict.
    /// </summary>
    public static AppException Conflict(string message, Exception? inner = null) =>
        new(409, message, inner);

    public static AppException Invalid(string message) => new(400, message);
}
```

- [x] **Step 3: Delete the old class**

In `backend/src/SlingKing.Application/Catalog/CatalogModels.cs`, delete the entire
`CatalogException` class and its doc comment — from `/// <summary>` down to the closing
brace before `public record CategoryOption`. Leave every record in that file alone.

- [x] **Step 4: Repoint the six usages**

In each of `CategoryService.cs`, `ProductService.cs`, `ICategoryService.cs`,
`IProductService.cs`: replace every `CatalogException` with `AppException`. The service
files already have `using SlingKing.Application.Common.Abstractions;` — add
`using SlingKing.Application.Common;` above it. The two interface files reference the
type only in doc comments (`<exception cref="...">`); update those too, and add the
using if the file has none.

In `backend/src/SlingKing.Api/Middleware/ProblemDetailsExceptionHandler.cs`: change
`using SlingKing.Application.Catalog;` to `using SlingKing.Application.Common;`, and
change `exception is CatalogException rejected` to `exception is AppException rejected`.
Update the comment on the line above — it says "authored in CatalogException" — to say
`AppException`.

- [x] **Step 5: Verify nothing still names the old type**

Run: `cd backend && Select-String -Path "src\*\**\*.cs","tests\*\**\*.cs" -Pattern "CatalogException"`

Expected: no matches. Anything under `bin/` or `obj/` is a stale build artefact and does
not count — if the only hits are there, you are done.

- [x] **Step 6: Build and run the full suite**

Run: `cd backend && dotnet build -warnaserror; if ($?) { dotnet test }`

Expected: build clean with zero warnings, and **exactly the same passed count as Step 1**.
A changed count means this was not a pure rename — find out why before continuing.

- [x] **Step 7: Commit**

```bash
git add backend/src backend/tests
git commit -m "refactor: CatalogException becomes AppException in Application/Common

Status code plus message plus three factories, with nothing catalog-specific in
it. The inventory module needs exactly this type; a byte-identical
InventoryException beside it would be two copies that must not drift.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 2: `FifoCostEngine` — the cost arithmetic, no database

The riskiest code in Release 1, built in isolation where its tests run in milliseconds.
This task writes **no database code at all**. The engine takes a list of open layers,
returns a plan, and never saves anything; `StockService` applies the plan in Task 4.

**Files:**
- Create: `backend/src/SlingKing.Application/Inventory/InventoryModels.cs`
- Create: `backend/src/SlingKing.Application/Inventory/FifoCostEngine.cs`
- Test: `backend/tests/SlingKing.UnitTests/Inventory/FifoCostEngineTests.cs`

**Interfaces:**
- Consumes: `SlingKing.Domain.Inventory.InventoryCostLayer` (existing entity),
  `SlingKing.Application.Common.AppException` (Task 1).
- Produces:
  - `readonly record struct LayerDraw(long LayerId, int Quantity, decimal? UnitCost)`
  - `sealed record ConsumptionPlan(IReadOnlyList<LayerDraw> Draws, int Drawn, decimal? TotalCost)`
  - `static ConsumptionPlan FifoCostEngine.Consume(IReadOnlyList<InventoryCostLayer> openLayers, int quantity, bool allowShortfall)`
  - `static InventoryCostLayer FifoCostEngine.AddLayer(long variantId, int locationId, CostLayerSourceType sourceType, long sourceId, int quantity, decimal? unitCost, DateTimeOffset now)`

- [x] **Step 1: Write the models**

Create `backend/src/SlingKing.Application/Inventory/InventoryModels.cs`:

```csharp
using SlingKing.Domain.Common;

namespace SlingKing.Application.Inventory;

/// <summary>How much was taken from one layer, and at what rate. UnitCost null means the
/// layer's cost was never known — see <see cref="ConsumptionPlan.TotalCost"/>.</summary>
public readonly record struct LayerDraw(long LayerId, int Quantity, decimal? UnitCost);

/// <summary>
/// What an outflow would take, before anything is written. <c>Drawn</c> is less than the
/// requested quantity only when the caller allowed a shortfall — see
/// <c>FifoCostEngine.Consume</c>.
/// </summary>
/// <param name="TotalCost">
/// Null when ANY drawn layer has an unknown unit cost. Never a partial sum: a partial total
/// understates the real cost and reads on screen as a fact.
/// </param>
public sealed record ConsumptionPlan(
    IReadOnlyList<LayerDraw> Draws, int Drawn, decimal? TotalCost);
```

- [x] **Step 2: Write the failing tests**

Create `backend/tests/SlingKing.UnitTests/Inventory/FifoCostEngineTests.cs`:

```csharp
using Shouldly;
using SlingKing.Application.Common;
using SlingKing.Application.Inventory;
using SlingKing.Domain.Common;
using SlingKing.Domain.Inventory;
using Xunit;

namespace SlingKing.UnitTests.Inventory;

public class FifoCostEngineTests
{
    /// <summary>
    /// Layers as the database would hand them over: already filtered to remaining_qty > 0
    /// and already ordered by (received_at, id). The engine trusts that order and does not
    /// re-sort — re-sorting here would hide a caller that forgot the ORDER BY.
    /// </summary>
    private static InventoryCostLayer Layer(long id, int remaining, decimal? unitCost) => new()
    {
        Id = id,
        VariantId = 1,
        LocationId = 1,
        SourceType = CostLayerSourceType.OpeningStock,
        SourceId = id,
        ReceivedAt = DateTimeOffset.UnixEpoch.AddDays(id),
        OriginalQty = remaining,
        RemainingQty = remaining,
        UnitCost = unitCost
    };

    [Fact]
    public void A_consumption_inside_one_layer_draws_only_from_it()
    {
        var plan = FifoCostEngine.Consume([Layer(1, 10, 120m)], 4, allowShortfall: false);

        plan.Draws.Count.ShouldBe(1);
        plan.Draws[0].ShouldBe(new LayerDraw(1, 4, 120m));
        plan.Drawn.ShouldBe(4);
        plan.TotalCost.ShouldBe(480m);
    }

    [Fact]
    public void A_consumption_crossing_two_layers_apportions_at_each_rate()
    {
        var plan = FifoCostEngine.Consume(
            [Layer(1, 3, 100m), Layer(2, 5, 200m)], 5, allowShortfall: false);

        plan.Draws.ShouldBe([new LayerDraw(1, 3, 100m), new LayerDraw(2, 2, 200m)]);
        plan.Drawn.ShouldBe(5);
        plan.TotalCost.ShouldBe(700m);
    }

    [Fact]
    public void A_consumption_exhausting_a_layer_exactly_stops_there()
    {
        var plan = FifoCostEngine.Consume(
            [Layer(1, 3, 100m), Layer(2, 5, 200m)], 3, allowShortfall: false);

        // The second layer must not appear at all. A zero-quantity draw would violate
        // ck_consumption_quantity_positive the moment Task 4 writes it out.
        plan.Draws.ShouldBe([new LayerDraw(1, 3, 100m)]);
        plan.TotalCost.ShouldBe(300m);
    }

    [Fact]
    public void Crossing_a_layer_of_unknown_cost_yields_no_total_rather_than_a_partial_one()
    {
        var plan = FifoCostEngine.Consume(
            [Layer(1, 3, null), Layer(2, 5, 200m)], 5, allowShortfall: false);

        plan.Drawn.ShouldBe(5);
        plan.Draws[0].UnitCost.ShouldBeNull();
        plan.Draws[1].UnitCost.ShouldBe(200m);

        // 400.00 would be the partial sum of the layer we do know. Reporting it would
        // understate the cost by however much the first three actually cost.
        plan.TotalCost.ShouldBeNull();
    }

    [Fact]
    public void Layers_short_of_the_quantity_are_refused_when_a_shortfall_is_not_allowed()
    {
        var refusal = Should.Throw<AppException>(() =>
            FifoCostEngine.Consume([Layer(1, 3, 100m)], 10, allowShortfall: false));

        refusal.StatusCode.ShouldBe(409);
        refusal.Message.ShouldBe(
            "Stock and cost records disagree for this item — run a recount to correct it.");
    }

    [Fact]
    public void A_shortfall_is_permitted_when_the_caller_allows_it()
    {
        // This is the recount exemption. Without it the disagreement message names a fix
        // that hits the same wall and throws the same message.
        var plan = FifoCostEngine.Consume([Layer(1, 3, 100m)], 10, allowShortfall: true);

        plan.Draws.ShouldBe([new LayerDraw(1, 3, 100m)]);
        plan.Drawn.ShouldBe(3);
        plan.TotalCost.ShouldBe(300m);
    }

    [Fact]
    public void An_empty_layer_list_with_a_permitted_shortfall_draws_nothing()
    {
        var plan = FifoCostEngine.Consume([], 5, allowShortfall: true);

        plan.Draws.ShouldBeEmpty();
        plan.Drawn.ShouldBe(0);

        // Nothing was drawn, so nothing is known about what it cost. Zero would be a claim.
        plan.TotalCost.ShouldBeNull();
    }

    [Fact]
    public void A_non_positive_quantity_is_a_programming_error_not_a_rejection()
    {
        Should.Throw<ArgumentOutOfRangeException>(() =>
            FifoCostEngine.Consume([Layer(1, 3, 100m)], 0, allowShortfall: false));
    }

    [Fact]
    public void An_added_layer_starts_full_and_carries_its_cost()
    {
        var now = DateTimeOffset.UnixEpoch;

        var layer = FifoCostEngine.AddLayer(
            variantId: 7, locationId: 1, CostLayerSourceType.OpeningStock,
            sourceId: 42, quantity: 10, unitCost: 120m, now);

        layer.VariantId.ShouldBe(7L);
        layer.OriginalQty.ShouldBe(10);
        layer.RemainingQty.ShouldBe(10);
        layer.UnitCost.ShouldBe(120m);
        layer.SourceId.ShouldBe(42L);

        // ReceivedAt is the FIFO queue position. Stamped now, never back-dated in M2b, so
        // insertion order IS queue order.
        layer.ReceivedAt.ShouldBe(now);
    }

    [Fact]
    public void An_added_layer_may_have_no_cost_at_all()
    {
        var layer = FifoCostEngine.AddLayer(
            1, 1, CostLayerSourceType.AdjustmentIn, 1, 3, unitCost: null,
            DateTimeOffset.UnixEpoch);

        layer.UnitCost.ShouldBeNull();
    }
}
```

- [x] **Step 3: Run the tests to verify they fail**

Run: `cd backend && dotnet test tests/SlingKing.UnitTests --filter FullyQualifiedName~FifoCostEngineTests`

Expected: the project does not compile — `FifoCostEngine` does not exist. That is the
correct failure. Do not proceed until you have seen it.

- [x] **Step 4: Write the engine**

Create `backend/src/SlingKing.Application/Inventory/FifoCostEngine.cs`:

```csharp
using SlingKing.Application.Common;
using SlingKing.Domain.Common;
using SlingKing.Domain.Inventory;

namespace SlingKing.Application.Inventory;

/// <summary>
/// The FIFO cost arithmetic, and nothing else. Static and database-free on purpose: it is
/// the riskiest code in Release 1, and it only keeps a millisecond test loop for as long as
/// it never learns what an IAppDbContext is. It reads no stock levels and appends no ledger
/// rows — StockService applies what this returns.
/// </summary>
public static class FifoCostEngine
{
    /// <summary>
    /// Walks <paramref name="openLayers"/> in the order given — which the caller must have
    /// taken from ix_layers_fifo, oldest first — and reports what an outflow of
    /// <paramref name="quantity"/> would take from each.
    /// </summary>
    /// <param name="allowShortfall">
    /// False for every ordinary outflow: running out of layers means the cost records have
    /// diverged from the stock records, and the honest response is to refuse. True only for a
    /// recount, which is the one operation authorised to bring the two back into line — the
    /// counted number is authoritative by definition. Without that exemption the refusal
    /// message names a fix that hits this same wall and throws this same message.
    /// </param>
    public static ConsumptionPlan Consume(
        IReadOnlyList<InventoryCostLayer> openLayers, int quantity, bool allowShortfall)
    {
        if (quantity <= 0)
        {
            // Not an AppException: a caller reaching here with zero has a bug, and dressing
            // that up as a 400 would hide it behind a message the owner cannot act on.
            throw new ArgumentOutOfRangeException(
                nameof(quantity), quantity, "A consumption must take at least one unit.");
        }

        var draws = new List<LayerDraw>();
        var outstanding = quantity;

        foreach (var layer in openLayers)
        {
            if (outstanding == 0)
            {
                break;
            }

            var take = Math.Min(outstanding, layer.RemainingQty);

            if (take <= 0)
            {
                // A layer with nothing left should not have been passed in, but skipping it
                // is cheaper than a zero-quantity draw that ck_consumption_quantity_positive
                // would reject at the far end of the transaction.
                continue;
            }

            draws.Add(new LayerDraw(layer.Id, take, layer.UnitCost));
            outstanding -= take;
        }

        if (outstanding > 0 && !allowShortfall)
        {
            throw AppException.Conflict(
                "Stock and cost records disagree for this item — run a recount to correct it.");
        }

        return new ConsumptionPlan(draws, quantity - outstanding, TotalOf(draws));
    }

    /// <summary>
    /// Null when any draw has an unknown rate, and null when nothing was drawn. A partial sum
    /// understates the real cost and reads on screen as a fact rather than as a gap.
    /// </summary>
    private static decimal? TotalOf(IReadOnlyList<LayerDraw> draws)
    {
        if (draws.Count == 0 || draws.Any(d => d.UnitCost is null))
        {
            return null;
        }

        return draws.Sum(d => d.UnitCost!.Value * d.Quantity);
    }

    /// <summary>
    /// A new queue entry, full and unconsumed. <paramref name="unitCost"/> null means the cost
    /// is genuinely unknown — a recount, for instance, says how many were counted and nothing
    /// about what they cost.
    /// </summary>
    public static InventoryCostLayer AddLayer(
        long variantId,
        int locationId,
        CostLayerSourceType sourceType,
        long sourceId,
        int quantity,
        decimal? unitCost,
        DateTimeOffset now) => new()
    {
        VariantId = variantId,
        LocationId = locationId,
        SourceType = sourceType,
        SourceId = sourceId,
        ReceivedAt = now,
        OriginalQty = quantity,
        RemainingQty = quantity,
        UnitCost = unitCost,
        CreatedAt = now
    };
}
```

- [x] **Step 5: Run the tests to verify they pass**

Run: `cd backend && dotnet test tests/SlingKing.UnitTests --filter FullyQualifiedName~FifoCostEngineTests`

Expected: 10 passed.

- [x] **Step 6: Prove the unknown-cost rule is actually load-bearing**

Temporarily change `TotalOf` to ignore null rates:

```csharp
return draws.Where(d => d.UnitCost is not null).Sum(d => d.UnitCost!.Value * d.Quantity);
```

Run the same filter. Expected: `Crossing_a_layer_of_unknown_cost_yields_no_total_rather_than_a_partial_one`
FAILS with 400 where null was expected, and `An_empty_layer_list...` FAILS with 0 where
null was expected.

**Then put `TotalOf` back exactly as written above and re-run — 10 passed.** A test that
has never been seen to fail is decoration, and this is the rule the whole optional-cost
design rests on.

- [x] **Step 7: Commit**

```bash
git add backend/src/SlingKing.Application/Inventory backend/tests/SlingKing.UnitTests/Inventory
git commit -m "feat: FIFO cost engine, pure and database-free

Consume walks layers oldest-first and reports what an outflow would take.
Unknown cost stays unknown -- a partial total understates the real cost and
reads as a fact. Static over in-memory layers so the riskiest arithmetic in
Release 1 keeps a millisecond test loop.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 3: Opening stock, end to end

The inflow path, the transaction, and the row lock. Outflows arrive in Task 4 — this task
deliberately ships one movement type so the lock and the transaction can be reviewed
without consumption logic in the same diff.

**Files:**
- Create: `backend/src/SlingKing.Application/Inventory/IStockService.cs`
- Create: `backend/src/SlingKing.Application/Inventory/StockService.cs`
- Modify: `backend/src/SlingKing.Application/Inventory/InventoryModels.cs`
- Create: `backend/src/SlingKing.Api/Contracts/Inventory/InventoryContracts.cs`
- Create: `backend/src/SlingKing.Api/Controllers/InventoryController.cs`
- Modify: `backend/src/SlingKing.Infrastructure/DependencyInjection.cs:27` (add the registration below the `IProductService` one)
- Test: `backend/tests/SlingKing.IntegrationTests/Api/InventoryEndpointTests.cs`

**Interfaces:**
- Consumes: `FifoCostEngine.AddLayer` (Task 2), `AppException` (Task 1), `IAppDbContext`,
  `IClock`.
- Produces:
  - `record MovementInput(long VariantId, InventoryTransactionType Type, int Quantity, decimal? UnitCost, string? Reason)`
  - `record MovementView(long Id, DateTimeOffset OccurredAt, InventoryTransactionType Type, int PhysicalDelta, decimal? TotalCost, string? Reason)`
  - `Task<MovementView> IStockService.RecordMovementAsync(MovementInput, CancellationToken)`
  - `POST /api/v1/inventory/movements`

- [x] **Step 1: Write the failing tests**

Create `backend/tests/SlingKing.IntegrationTests/Api/InventoryEndpointTests.cs`. Later
tasks add to this file; this step establishes it with its helpers.

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SlingKing.Infrastructure.Persistence;
using SlingKing.Infrastructure.Persistence.Seed;
using SlingKing.IntegrationTests.Fixtures;
using Xunit;

namespace SlingKing.IntegrationTests.Api;

[Collection("database")]
public class InventoryEndpointTests : IClassFixture<SlingKingApiFactory>, IAsyncLifetime
{
    private readonly SlingKingApiFactory _factory;
    private readonly HttpClient _client;

    public InventoryEndpointTests(SlingKingApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>().SeedAsync();
        await AuthenticateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private record LoginRequest(string Email, string Password);
    private record LoginResponse(string AccessToken);

    private record VariantRequest(
        long? Id, string? Colour, string? Size, decimal SellingPrice,
        int LowStockThreshold, bool IsActive);

    private record ProductRequest(
        string Name, int? CategoryId, string? Description, List<VariantRequest> Variants);

    private record VariantView(long Id, string Sku);
    private record ProductView(long Id, string Code, List<VariantView> Variants);

    private record MovementRequest(
        long VariantId, string Type, int Quantity, decimal? UnitCost, string? Reason);

    private record MovementView(
        long Id, DateTimeOffset OccurredAt, string Type, int PhysicalDelta,
        decimal? TotalCost, string? Reason);

    private async Task AuthenticateAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest(SlingKingApiFactory.SeedEmail, SlingKingApiFactory.SeedPassword));

        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", body!.AccessToken);
    }

    /// <summary>
    /// A fresh product with one variant, so every test starts from a variant nothing else
    /// has touched. Sharing one variant across tests would make the running totals depend
    /// on test order.
    /// </summary>
    private async Task<long> NewVariantAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/products", new ProductRequest(
            $"Stock {Guid.NewGuid():N}"[..16], null, null,
            [new VariantRequest(null, "Tan", "Standard", 4500m, 5, true)]));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var product = await response.Content.ReadFromJsonAsync<ProductView>();
        return product!.Variants.Single().Id;
    }

    private async Task<T> QueryAsync<T>(Func<AppDbContext, Task<T>> read)
    {
        using var scope = _factory.Services.CreateScope();
        return await read(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    [Fact]
    public async Task Recording_a_movement_requires_a_token()
    {
        using var anonymous = _factory.CreateClient();

        var response = await anonymous.PostAsJsonAsync("/api/v1/inventory/movements",
            new MovementRequest(1, "OpeningStock", 1, null, null));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Opening_stock_raises_the_level_and_opens_one_cost_layer()
    {
        var variantId = await NewVariantAsync();

        var response = await _client.PostAsJsonAsync("/api/v1/inventory/movements",
            new MovementRequest(variantId, "OpeningStock", 10, 120m, null));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        var movement = await response.Content.ReadFromJsonAsync<MovementView>();
        movement!.PhysicalDelta.ShouldBe(10);
        movement.Type.ShouldBe("OpeningStock");

        var level = await QueryAsync(db => db.StockLevels
            .SingleAsync(s => s.VariantId == variantId));
        level.PhysicalQty.ShouldBe(10);

        // Reservations are R2. Nothing in this milestone may move this number.
        level.ReservedQty.ShouldBe(0);

        var layers = await QueryAsync(db => db.InventoryCostLayers
            .Where(l => l.VariantId == variantId).ToListAsync());
        layers.Count.ShouldBe(1);
        layers[0].OriginalQty.ShouldBe(10);
        layers[0].RemainingQty.ShouldBe(10);
        layers[0].UnitCost.ShouldBe(120m);
    }

    [Fact]
    public async Task Opening_stock_may_have_no_cost_at_all()
    {
        var variantId = await NewVariantAsync();

        var response = await _client.PostAsJsonAsync("/api/v1/inventory/movements",
            new MovementRequest(variantId, "OpeningStock", 4, null, null));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        var layer = await QueryAsync(db => db.InventoryCostLayers
            .SingleAsync(l => l.VariantId == variantId));

        // Unknown, not zero. Zero here would silently claim the bags were free.
        layer.UnitCost.ShouldBeNull();
    }

    [Fact]
    public async Task Two_openings_stack_into_two_layers_in_arrival_order()
    {
        var variantId = await NewVariantAsync();

        await _client.PostAsJsonAsync("/api/v1/inventory/movements",
            new MovementRequest(variantId, "OpeningStock", 3, 100m, null));
        await _client.PostAsJsonAsync("/api/v1/inventory/movements",
            new MovementRequest(variantId, "OpeningStock", 5, 200m, null));

        var level = await QueryAsync(db => db.StockLevels
            .SingleAsync(s => s.VariantId == variantId));
        level.PhysicalQty.ShouldBe(8);

        var layers = await QueryAsync(db => db.InventoryCostLayers
            .Where(l => l.VariantId == variantId)
            .OrderBy(l => l.ReceivedAt).ThenBy(l => l.Id)
            .ToListAsync());

        layers.Select(l => l.UnitCost).ShouldBe([100m, 200m]);
    }

    [Fact]
    public async Task A_quantity_of_zero_is_refused()
    {
        var variantId = await NewVariantAsync();

        var response = await _client.PostAsJsonAsync("/api/v1/inventory/movements",
            new MovementRequest(variantId, "OpeningStock", 0, null, null));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task An_unknown_variant_is_not_found()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/inventory/movements",
            new MovementRequest(999_999_999, "OpeningStock", 1, null, null));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // A missing route is also a 404, so assert the authored body rather than the status
        // alone — otherwise this test passes just as well with no endpoint at all.
        response.Content.Headers.ContentType?.MediaType
            .ShouldBe("application/problem+json");

        var problem = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        problem!["title"].ToString().ShouldBe("That item no longer exists.");
    }

    [Fact]
    public async Task A_type_this_endpoint_does_not_accept_is_refused_by_name()
    {
        var variantId = await NewVariantAsync();

        foreach (var type in new[] { "Purchase", "Sale", "Adjustment" })
        {
            var response = await _client.PostAsJsonAsync("/api/v1/inventory/movements",
                new MovementRequest(variantId, type, 1, null, null));

            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, $"type {type}");
        }
    }
}
```

- [x] **Step 2: Run the tests to verify they fail**

Run: `cd backend && dotnet test tests/SlingKing.IntegrationTests --filter FullyQualifiedName~InventoryEndpointTests`

Expected: compile failure or 404s — there is no endpoint yet.

- [x] **Step 3: Extend the models**

Append to `backend/src/SlingKing.Application/Inventory/InventoryModels.cs`:

```csharp
/// <summary>
/// <c>Quantity</c> is ALWAYS positive; direction comes from <c>Type</c>. A signed quantity
/// lets -5 on a Damage mean "five damaged" to a person and "five arriving" to the server,
/// and after the fact the two are indistinguishable.
/// </summary>
public record MovementInput(
    long VariantId,
    InventoryTransactionType Type,
    int Quantity,
    decimal? UnitCost,
    string? Reason);

public record MovementView(
    long Id,
    DateTimeOffset OccurredAt,
    InventoryTransactionType Type,
    int PhysicalDelta,
    decimal? TotalCost,
    string? Reason);
```

- [x] **Step 4: Write the service interface**

Create `backend/src/SlingKing.Application/Inventory/IStockService.cs`:

```csharp
namespace SlingKing.Application.Inventory;

public interface IStockService
{
    /// <summary>
    /// Appends one ledger row, moves the stock level, and lays down or consumes cost — all in
    /// one transaction, under a row lock on the variant's stock level.
    /// </summary>
    /// <exception cref="Common.AppException">
    /// 400 for a quantity below one, a type this endpoint does not accept, a cost on anything
    /// but an inflow, or a missing reason. 404 for an unknown or inactive variant.
    /// </exception>
    Task<MovementView> RecordMovementAsync(
        MovementInput input, CancellationToken cancellationToken = default);
}
```

- [x] **Step 5: Write the service**

Create `backend/src/SlingKing.Application/Inventory/StockService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SlingKing.Application.Common;
using SlingKing.Application.Common.Abstractions;
using SlingKing.Domain.Common;
using SlingKing.Domain.Inventory;

namespace SlingKing.Application.Inventory;

public sealed class StockService : IStockService
{
    private readonly IAppDbContext _db;
    private readonly IClock _clock;

    public StockService(IAppDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    /// <summary>
    /// The four types this API accepts, each with exactly one direction. Adjustment is
    /// deliberately absent: the recount endpoint is its only producer, because an adjustment
    /// a caller computed itself is computed from a number their screen loaded a minute ago.
    /// </summary>
    private static readonly Dictionary<InventoryTransactionType, int> PostableDirections = new()
    {
        [InventoryTransactionType.OpeningStock] = +1,
        [InventoryTransactionType.Damage] = -1,
        [InventoryTransactionType.Loss] = -1,
        [InventoryTransactionType.PersonalUse] = -1
    };

    private static void RejectBadInput(MovementInput input)
    {
        if (input.Quantity <= 0)
        {
            throw AppException.Invalid("Enter how many, as a number above zero.");
        }

        if (!PostableDirections.TryGetValue(input.Type, out var direction))
        {
            throw AppException.Invalid("That kind of movement cannot be recorded here yet.");
        }

        if (input.UnitCost is not null && direction < 0)
        {
            throw AppException.Invalid("A cost can only be set on stock coming in.");
        }

        if (input.UnitCost < 0)
        {
            throw AppException.Invalid("A cost cannot be negative.");
        }

        if (direction < 0 && string.IsNullOrWhiteSpace(input.Reason))
        {
            throw AppException.Invalid("Say briefly what happened, so this makes sense later.");
        }
    }

    private Task<int> DefaultLocationIdAsync(CancellationToken cancellationToken) =>
        _db.Locations.Where(l => l.IsDefault).Select(l => l.Id).SingleAsync(cancellationToken);

    /// <summary>
    /// Takes the row lock and returns the tracked level. Two statements rather than a
    /// FromSql that does both: the extra round trip inside an already-open transaction costs
    /// nothing, and a plain LINQ read cannot be broken by how EF materialises the generated
    /// available_qty column.
    /// </summary>
    private async Task<StockLevel> LockLevelAsync(
        long variantId, int locationId, CancellationToken cancellationToken)
    {
        await _db.Database.ExecuteSqlInterpolatedAsync(
            $"select 1 from stock_levels where variant_id = {variantId} and location_id = {locationId} for update",
            cancellationToken);

        return await _db.StockLevels.SingleAsync(
            s => s.VariantId == variantId && s.LocationId == locationId, cancellationToken);
    }

    public async Task<MovementView> RecordMovementAsync(
        MovementInput input, CancellationToken cancellationToken = default)
    {
        RejectBadInput(input);

        var variantExists = await _db.ProductVariants
            .AnyAsync(v => v.Id == input.VariantId && v.IsActive, cancellationToken);

        if (!variantExists)
        {
            throw AppException.NotFound("That item no longer exists.");
        }

        var locationId = await DefaultLocationIdAsync(cancellationToken);
        var direction = PostableDirections[input.Type];
        var now = _clock.UtcNow;

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        var level = await LockLevelAsync(input.VariantId, locationId, cancellationToken);

        var ledgerRow = new InventoryTransaction
        {
            VariantId = input.VariantId,
            LocationId = locationId,
            Type = input.Type,
            PhysicalDelta = direction * input.Quantity,
            ReservedDelta = 0,
            UnitCost = input.UnitCost,
            TotalCost = input.UnitCost * input.Quantity,
            Reason = input.Reason?.Trim(),
            OccurredAt = now,
            CreatedAt = now
        };

        _db.InventoryTransactions.Add(ledgerRow);
        level.PhysicalQty += ledgerRow.PhysicalDelta;
        level.UpdatedAt = now;

        // The ledger row must exist before anything can reference its id.
        await _db.SaveChangesAsync(cancellationToken);

        _db.InventoryCostLayers.Add(FifoCostEngine.AddLayer(
            input.VariantId, locationId, CostLayerSourceType.OpeningStock,
            ledgerRow.Id, input.Quantity, input.UnitCost, now));

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new MovementView(
            ledgerRow.Id, ledgerRow.OccurredAt, ledgerRow.Type,
            ledgerRow.PhysicalDelta, ledgerRow.TotalCost, ledgerRow.Reason);
    }
}
```

**`ponytail:` this task's write path only ever runs the inflow branch — the outflow
direction is rejected before it reaches the cost step, because `Damage` and friends have
no consumption code until Task 4. Task 4 replaces the unconditional `AddLayer` with a
branch on `direction`.**

- [x] **Step 6: Write the request contract**

Create `backend/src/SlingKing.Api/Contracts/Inventory/InventoryContracts.cs`:

```csharp
using System.ComponentModel.DataAnnotations;
using SlingKing.Domain.Common;

namespace SlingKing.Api.Contracts.Inventory;

// Attributes bind to the primary-constructor parameters. A [property:] target compiles and
// then throws at request time — see Contracts/Auth/AuthContracts.cs. Do not "fix" it.
public record MovementRequest(
    [Required] long? VariantId,
    [Required] InventoryTransactionType? Type,
    [Required, Range(1, 1_000_000)] int? Quantity,
    [Range(0, 9_999_999)] decimal? UnitCost,
    [MaxLength(256)] string? Reason);
```

- [x] **Step 7: Write the controller**

Create `backend/src/SlingKing.Api/Controllers/InventoryController.cs`:

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SlingKing.Api.Contracts.Inventory;
using SlingKing.Application.Inventory;

namespace SlingKing.Api.Controllers;

[ApiController]
[Route("api/v1/inventory")]
[Authorize]
public class InventoryController : ControllerBase
{
    private readonly IStockService _stock;

    public InventoryController(IStockService stock) => _stock = stock;

    [HttpPost("movements")]
    [ProducesResponseType(typeof(MovementView), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RecordMovement(
        MovementRequest request, CancellationToken cancellationToken)
    {
        var movement = await _stock.RecordMovementAsync(
            new MovementInput(
                request.VariantId!.Value, request.Type!.Value, request.Quantity!.Value,
                request.UnitCost, request.Reason),
            cancellationToken);

        // No GET for a single movement — the item page reads the whole history — so this
        // reports the location of what the caller would actually go and look at.
        return Created($"/api/v1/inventory/{request.VariantId}", movement);
    }
}
```

- [x] **Step 8: Register the service**

In `backend/src/SlingKing.Infrastructure/DependencyInjection.cs`, immediately after the
`services.AddScoped<IProductService, ProductService>();` line, add:

```csharp
        services.AddScoped<IStockService, StockService>();
```

Add `using SlingKing.Application.Inventory;` to the file's usings.

- [x] **Step 9: Run the tests to verify they pass**

Run: `cd backend && dotnet test tests/SlingKing.IntegrationTests --filter FullyQualifiedName~InventoryEndpointTests`

Expected: 7 passed.

- [x] **Step 10: Prove the lock statement actually runs**

Temporarily change the SQL in `LockLevelAsync` to `select 1 from stock_levels where
variant_id = {variantId} and location_id = {locationId} for update nowait` and re-run.
Expected: still 7 passed — the statement is valid and reaching the database. Then put
`for update` back. Task 6 is where the lock's *behaviour* gets a real test; this step
only proves the statement is not silently a no-op.

- [x] **Step 11: Commit**

```bash
git add backend/src backend/tests
git commit -m "feat: record opening stock

One transaction: row lock, ledger append, level update, cost layer. Quantity is
always positive and direction comes from the type -- a signed quantity makes
'five damaged' and 'five arriving' indistinguishable after the fact.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 4: Outflows — damage, loss and personal use

Wires `FifoCostEngine.Consume` into the write path, adds the two refusals that need the
database, and proves the ledger is append-only. This is where the two saves inside one
transaction start to matter: consumption rows reference the ledger row's id.

**Files:**
- Modify: `backend/src/SlingKing.Application/Inventory/StockService.cs`
- Test: `backend/tests/SlingKing.IntegrationTests/Api/InventoryEndpointTests.cs` (append)

**Interfaces:**
- Consumes: `FifoCostEngine.Consume` and `ConsumptionPlan` (Task 2),
  `RecordMovementAsync` (Task 3).
- Produces: no new public surface. Task 5 calls the private `AppendAsync` introduced here.

- [x] **Step 1: Write the failing tests**

Append these to `InventoryEndpointTests.cs`, inside the class:

```csharp
    /// <summary>Opening stock straight in, so a test can set up a starting position.</summary>
    private async Task OpenAsync(long variantId, int quantity, decimal? unitCost)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/inventory/movements",
            new MovementRequest(variantId, "OpeningStock", quantity, unitCost, null));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Damage_lowers_the_level_and_consumes_the_oldest_layer_first()
    {
        var variantId = await NewVariantAsync();
        await OpenAsync(variantId, 3, 100m);
        await OpenAsync(variantId, 5, 200m);

        var response = await _client.PostAsJsonAsync("/api/v1/inventory/movements",
            new MovementRequest(variantId, "Damage", 5, null, "Water damaged in transit"));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        var movement = await response.Content.ReadFromJsonAsync<MovementView>();
        movement!.PhysicalDelta.ShouldBe(-5);

        // 3 at 100 then 2 at 200. A last-in-first-out walk would say 1000.
        movement.TotalCost.ShouldBe(700m);

        var level = await QueryAsync(db => db.StockLevels
            .SingleAsync(s => s.VariantId == variantId));
        level.PhysicalQty.ShouldBe(3);

        var layers = await QueryAsync(db => db.InventoryCostLayers
            .Where(l => l.VariantId == variantId)
            .OrderBy(l => l.ReceivedAt).ThenBy(l => l.Id)
            .ToListAsync());

        layers.Select(l => l.RemainingQty).ShouldBe([0, 3]);

        var consumptions = await QueryAsync(db => db.InventoryCostConsumptions
            .Where(c => c.InventoryTransactionId == movement.Id)
            .OrderBy(c => c.Id)
            .ToListAsync());

        consumptions.Select(c => c.Quantity).ShouldBe([3, 2]);
        consumptions.Select(c => c.UnitCost).ShouldBe([100m, 200m]);

        // Copied at consumption time, never joined from the layer, so repricing later
        // cannot rewrite what this outflow cost.
        consumptions.Select(c => c.TotalCost).ShouldBe([300m, 400m]);
    }

    [Fact]
    public async Task An_outflow_crossing_a_layer_of_unknown_cost_reports_no_total()
    {
        var variantId = await NewVariantAsync();
        await OpenAsync(variantId, 3, null);
        await OpenAsync(variantId, 5, 200m);

        var response = await _client.PostAsJsonAsync("/api/v1/inventory/movements",
            new MovementRequest(variantId, "Loss", 5, null, "Left in a rickshaw"));

        var movement = await response.Content.ReadFromJsonAsync<MovementView>();

        // 400.00 is the partial sum of the half we know about. Reporting it would
        // understate the cost by whatever the first three actually cost.
        movement!.TotalCost.ShouldBeNull();
    }

    [Fact]
    public async Task An_outflow_larger_than_the_stock_is_refused_and_writes_nothing()
    {
        var variantId = await NewVariantAsync();
        await OpenAsync(variantId, 6, 100m);

        var before = await QueryAsync(db => db.InventoryTransactions
            .CountAsync(t => t.VariantId == variantId));

        var response = await _client.PostAsJsonAsync("/api/v1/inventory/movements",
            new MovementRequest(variantId, "Damage", 99, null, "Fire"));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var problem = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        problem!["title"].ToString()
            .ShouldBe("Only 6 in stock. Record a recount first if that is wrong.");

        var after = await QueryAsync(db => db.InventoryTransactions
            .CountAsync(t => t.VariantId == variantId));
        after.ShouldBe(before);

        var level = await QueryAsync(db => db.StockLevels
            .SingleAsync(s => s.VariantId == variantId));
        level.PhysicalQty.ShouldBe(6);
    }

    [Fact]
    public async Task An_outflow_without_a_reason_is_refused()
    {
        var variantId = await NewVariantAsync();
        await OpenAsync(variantId, 6, 100m);

        foreach (var reason in new string?[] { null, "", "   " })
        {
            var response = await _client.PostAsJsonAsync("/api/v1/inventory/movements",
                new MovementRequest(variantId, "PersonalUse", 1, null, reason));

            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, $"reason {reason ?? "null"}");
        }
    }

    [Fact]
    public async Task A_cost_may_not_be_attached_to_an_outflow()
    {
        var variantId = await NewVariantAsync();
        await OpenAsync(variantId, 6, 100m);

        var response = await _client.PostAsJsonAsync("/api/v1/inventory/movements",
            new MovementRequest(variantId, "Damage", 1, 50m, "Torn strap"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var problem = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        problem!["title"].ToString().ShouldBe("A cost can only be set on stock coming in.");
    }

    [Fact]
    public async Task A_ledger_row_never_changes_once_written()
    {
        var variantId = await NewVariantAsync();
        await OpenAsync(variantId, 10, 120m);

        var original = await QueryAsync(db => db.InventoryTransactions
            .AsNoTracking()
            .Where(t => t.VariantId == variantId)
            .OrderBy(t => t.Id)
            .FirstAsync());

        await _client.PostAsJsonAsync("/api/v1/inventory/movements",
            new MovementRequest(variantId, "Damage", 2, null, "Scuffed"));
        await _client.PostAsJsonAsync("/api/v1/inventory/movements",
            new MovementRequest(variantId, "Loss", 1, null, "Miscounted at the courier"));
        await _client.PostAsJsonAsync("/api/v1/inventory/movements",
            new MovementRequest(variantId, "PersonalUse", 1, null, "Gifted to my sister"));

        var reread = await QueryAsync(db => db.InventoryTransactions
            .AsNoTracking()
            .SingleAsync(t => t.Id == original.Id));

        // Route enumeration cannot prove this: a POST updates a row as easily as a PUT does.
        // Observation is the only proof that the ledger is genuinely append-only.
        reread.PhysicalDelta.ShouldBe(original.PhysicalDelta);
        reread.ReservedDelta.ShouldBe(original.ReservedDelta);
        reread.UnitCost.ShouldBe(original.UnitCost);
        reread.TotalCost.ShouldBe(original.TotalCost);
        reread.Type.ShouldBe(original.Type);
        reread.Reason.ShouldBe(original.Reason);
        reread.OccurredAt.ShouldBe(original.OccurredAt);
    }

    [Fact]
    public async Task The_ledger_and_the_stock_level_agree_after_a_run_of_movements()
    {
        var variantId = await NewVariantAsync();
        await OpenAsync(variantId, 10, 120m);

        await _client.PostAsJsonAsync("/api/v1/inventory/movements",
            new MovementRequest(variantId, "Damage", 4, null, "Water damaged in transit"));
        await _client.PostAsJsonAsync("/api/v1/inventory/movements",
            new MovementRequest(variantId, "PersonalUse", 1, null, "Gifted to my sister"));

        var summed = await QueryAsync(db => db.InventoryTransactions
            .Where(t => t.VariantId == variantId)
            .SumAsync(t => t.PhysicalDelta));

        var level = await QueryAsync(db => db.StockLevels
            .SingleAsync(s => s.VariantId == variantId));

        summed.ShouldBe(level.PhysicalQty);

        var openLayers = await QueryAsync(db => db.InventoryCostLayers
            .Where(l => l.VariantId == variantId && l.RemainingQty > 0)
            .SumAsync(l => l.RemainingQty));

        openLayers.ShouldBe(level.PhysicalQty);
    }
```

- [x] **Step 2: Run the tests to verify they fail**

Run: `cd backend && dotnet test tests/SlingKing.IntegrationTests --filter FullyQualifiedName~InventoryEndpointTests`

Expected: the seven new tests fail. `Damage_lowers_the_level...` fails on the layer's
`RemainingQty` still reading 3 and 5 — Task 3 lays down a layer for every movement
regardless of direction, so a Damage currently *adds* stock. That is exactly the branch
Step 3 replaces.

- [x] **Step 3: Split the write path into a reusable append**

In `StockService.cs`, replace the body of `RecordMovementAsync` from
`await using var transaction` to the end of the method with the following, and add the
three private methods after it. Everything above `await using var transaction` — the
validation, the variant check, the location lookup — stays exactly as it is.

```csharp
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        var level = await LockLevelAsync(input.VariantId, locationId, cancellationToken);

        if (direction < 0 && level.PhysicalQty < input.Quantity)
        {
            throw AppException.Conflict(
                $"Only {level.PhysicalQty} in stock. Record a recount first if that is wrong.");
        }

        var movement = await AppendAsync(
            level, input.Type, direction * input.Quantity, input.UnitCost, input.Reason,
            allowShortfall: false, cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return movement;
    }

    /// <summary>
    /// One ledger row, one level update, and the matching cost movement. Assumes the caller
    /// holds the lock on <paramref name="level"/> and has an open transaction — it commits
    /// nothing itself, so a recount can compute its delta and append inside the same one.
    /// </summary>
    /// <param name="signedQuantity">Positive for stock coming in, negative for stock going out.</param>
    private async Task<MovementView> AppendAsync(
        StockLevel level,
        InventoryTransactionType type,
        int signedQuantity,
        decimal? unitCost,
        string? reason,
        bool allowShortfall,
        CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;

        var ledgerRow = new InventoryTransaction
        {
            VariantId = level.VariantId,
            LocationId = level.LocationId,
            Type = type,
            PhysicalDelta = signedQuantity,
            ReservedDelta = 0,
            UnitCost = unitCost,
            TotalCost = unitCost * Math.Abs(signedQuantity),
            Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
            OccurredAt = now,
            CreatedAt = now
        };

        _db.InventoryTransactions.Add(ledgerRow);
        level.PhysicalQty += signedQuantity;
        level.UpdatedAt = now;

        // The consumption rows reference this row's id, so it has to exist first. Still one
        // transaction — two saves inside it, not two transactions.
        await _db.SaveChangesAsync(cancellationToken);

        if (signedQuantity > 0)
        {
            _db.InventoryCostLayers.Add(FifoCostEngine.AddLayer(
                level.VariantId, level.LocationId, SourceOf(type), ledgerRow.Id,
                signedQuantity, unitCost, now));
        }
        else
        {
            await ConsumeAsync(
                level, ledgerRow, -signedQuantity, allowShortfall, now, cancellationToken);
        }

        await _db.SaveChangesAsync(cancellationToken);

        return new MovementView(
            ledgerRow.Id, ledgerRow.OccurredAt, ledgerRow.Type,
            ledgerRow.PhysicalDelta, ledgerRow.TotalCost, ledgerRow.Reason);
    }

    private static CostLayerSourceType SourceOf(InventoryTransactionType type) =>
        type == InventoryTransactionType.OpeningStock
            ? CostLayerSourceType.OpeningStock
            : CostLayerSourceType.AdjustmentIn;

    private async Task ConsumeAsync(
        StockLevel level,
        InventoryTransaction ledgerRow,
        int quantity,
        bool allowShortfall,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // The order here IS the FIFO order, and ix_layers_fifo is the index that serves it.
        // The engine trusts this order and does not re-sort.
        var openLayers = await _db.InventoryCostLayers
            .Where(l => l.VariantId == level.VariantId
                     && l.LocationId == level.LocationId
                     && l.RemainingQty > 0)
            .OrderBy(l => l.ReceivedAt).ThenBy(l => l.Id)
            .ToListAsync(cancellationToken);

        var plan = FifoCostEngine.Consume(openLayers, quantity, allowShortfall);
        var byId = openLayers.ToDictionary(l => l.Id);

        foreach (var draw in plan.Draws)
        {
            byId[draw.LayerId].RemainingQty -= draw.Quantity;

            _db.InventoryCostConsumptions.Add(new InventoryCostConsumption
            {
                CostLayerId = draw.LayerId,
                InventoryTransactionId = ledgerRow.Id,
                Quantity = draw.Quantity,
                ReversedQty = 0,
                UnitCost = draw.UnitCost,
                TotalCost = draw.UnitCost * draw.Quantity,
                ConsumedAt = now
            });
        }

        ledgerRow.TotalCost = plan.TotalCost;

        // A single rate only when every layer drawn shared one. Averaging two rates would
        // print a per-unit figure that no bag was ever bought at.
        ledgerRow.UnitCost = plan.Draws.Select(d => d.UnitCost).Distinct().Count() == 1
            ? plan.Draws[0].UnitCost
            : null;
    }
```

Delete the `ponytail:` comment left at the end of Task 3 — the branch it described now
exists.

- [x] **Step 4: Run the tests to verify they pass**

Run: `cd backend && dotnet test tests/SlingKing.IntegrationTests --filter FullyQualifiedName~InventoryEndpointTests`

Expected: 14 passed.

- [x] **Step 5: Prove the FIFO ordering is load-bearing**

Temporarily change the `.OrderBy(l => l.ReceivedAt).ThenBy(l => l.Id)` in `ConsumeAsync`
to `.OrderByDescending(l => l.ReceivedAt).ThenByDescending(l => l.Id)` and re-run.

Expected: `Damage_lowers_the_level_and_consumes_the_oldest_layer_first` FAILS — the total
comes back as 1000 instead of 700, and the remaining quantities as `[3, 0]`.

**Then put the ascending order back and re-run — 14 passed.** A first-in-first-out test
that passes under last-in-first-out is not testing the ordering.

- [x] **Step 6: Run the whole backend suite**

Run: `cd backend && dotnet build -warnaserror; if ($?) { dotnet test }`

Expected: everything green, zero warnings. Nothing in Tasks 1–4 should have moved a
catalog test.

- [x] **Step 7: Commit**

```bash
git add backend/src backend/tests
git commit -m "feat: damage, loss and personal use consume cost oldest-first

Consumption rows reference the ledger row, so the ledger row is saved first --
still one transaction, two saves inside it. Cost is copied at consumption time,
never joined, so repricing later cannot rewrite what an outflow cost.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 5: Recount

The endpoint that reads the current quantity under the lock and does the arithmetic for
you — and the one path allowed to bring cost records back into line with stock.

**Files:**
- Modify: `backend/src/SlingKing.Application/Inventory/InventoryModels.cs`
- Modify: `backend/src/SlingKing.Application/Inventory/IStockService.cs`
- Modify: `backend/src/SlingKing.Application/Inventory/StockService.cs`
- Modify: `backend/src/SlingKing.Api/Contracts/Inventory/InventoryContracts.cs`
- Modify: `backend/src/SlingKing.Api/Controllers/InventoryController.cs`
- Test: `backend/tests/SlingKing.IntegrationTests/Api/InventoryEndpointTests.cs` (append)

**Interfaces:**
- Consumes: `AppendAsync`, `LockLevelAsync` (Task 4).
- Produces:
  - `record RecountResult(int Previous, int Counted, int Difference, MovementView? Movement)`
  - `Task<RecountResult> IStockService.RecountAsync(long variantId, int countedQuantity, CancellationToken)`
  - `POST /api/v1/inventory/{variantId}/recount`

- [x] **Step 1: Write the failing tests**

Append to `InventoryEndpointTests.cs`:

```csharp
    private record RecountRequest(int CountedQuantity);

    private record RecountResult(
        int Previous, int Counted, int Difference, MovementView? Movement);

    [Fact]
    public async Task Counting_more_than_the_system_says_adjusts_up_at_an_unknown_cost()
    {
        var variantId = await NewVariantAsync();
        await OpenAsync(variantId, 6, 100m);

        var response = await _client.PostAsJsonAsync(
            $"/api/v1/inventory/{variantId}/recount", new RecountRequest(9));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<RecountResult>();
        result!.Previous.ShouldBe(6);
        result.Counted.ShouldBe(9);
        result.Difference.ShouldBe(3);
        result.Movement!.Type.ShouldBe("Adjustment");
        result.Movement.PhysicalDelta.ShouldBe(3);

        var level = await QueryAsync(db => db.StockLevels
            .SingleAsync(s => s.VariantId == variantId));
        level.PhysicalQty.ShouldBe(9);

        var newest = await QueryAsync(db => db.InventoryCostLayers
            .Where(l => l.VariantId == variantId)
            .OrderByDescending(l => l.Id)
            .FirstAsync());

        newest.OriginalQty.ShouldBe(3);

        // A recount says how many were counted and nothing about what they cost.
        // Zero here would claim three free bags.
        newest.UnitCost.ShouldBeNull();
    }

    [Fact]
    public async Task Counting_fewer_consumes_the_difference()
    {
        var variantId = await NewVariantAsync();
        await OpenAsync(variantId, 6, 100m);

        var response = await _client.PostAsJsonAsync(
            $"/api/v1/inventory/{variantId}/recount", new RecountRequest(4));

        var result = await response.Content.ReadFromJsonAsync<RecountResult>();
        result!.Difference.ShouldBe(-2);
        result.Movement!.PhysicalDelta.ShouldBe(-2);
        result.Movement.TotalCost.ShouldBe(200m);

        var layer = await QueryAsync(db => db.InventoryCostLayers
            .SingleAsync(l => l.VariantId == variantId));
        layer.RemainingQty.ShouldBe(4);
    }

    [Fact]
    public async Task Counting_exactly_what_the_system_says_writes_nothing()
    {
        var variantId = await NewVariantAsync();
        await OpenAsync(variantId, 6, 100m);

        var before = await QueryAsync(db => db.InventoryTransactions
            .CountAsync(t => t.VariantId == variantId));

        var response = await _client.PostAsJsonAsync(
            $"/api/v1/inventory/{variantId}/recount", new RecountRequest(6));

        var result = await response.Content.ReadFromJsonAsync<RecountResult>();
        result!.Difference.ShouldBe(0);
        result.Movement.ShouldBeNull();

        var after = await QueryAsync(db => db.InventoryTransactions
            .CountAsync(t => t.VariantId == variantId));
        after.ShouldBe(before);
    }

    [Fact]
    public async Task A_negative_count_is_refused()
    {
        var variantId = await NewVariantAsync();

        var response = await _client.PostAsJsonAsync(
            $"/api/v1/inventory/{variantId}/recount", new RecountRequest(-1));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Recounting_an_unknown_variant_is_not_found()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/v1/inventory/999999999/recount", new RecountRequest(1));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");

        var problem = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        problem!["title"].ToString().ShouldBe("That item no longer exists.");
    }

    [Fact]
    public async Task An_ordinary_outflow_refuses_when_layers_run_short_but_a_recount_does_not()
    {
        var variantId = await NewVariantAsync();
        await OpenAsync(variantId, 12, 100m);

        // Force the divergence the guard exists for: stock says 12, layers hold 8. Doing it
        // in SQL rather than through the API is the point — no endpoint can produce this
        // state, which is why the guard needs a test that manufactures it.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var layer = await db.InventoryCostLayers.SingleAsync(l => l.VariantId == variantId);
            layer.RemainingQty = 8;
            await db.SaveChangesAsync();
        }

        var refused = await _client.PostAsJsonAsync("/api/v1/inventory/movements",
            new MovementRequest(variantId, "Damage", 10, null, "Flooded"));

        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var problem = await refused.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        problem!["title"].ToString().ShouldBe(
            "Stock and cost records disagree for this item — run a recount to correct it.");

        // The same shortfall, through the recount, must succeed. If it did not, the message
        // above would name a fix that hits this same wall and returns this same message.
        var recount = await _client.PostAsJsonAsync(
            $"/api/v1/inventory/{variantId}/recount", new RecountRequest(2));

        recount.StatusCode.ShouldBe(HttpStatusCode.OK);

        var level = await QueryAsync(db => db.StockLevels
            .SingleAsync(s => s.VariantId == variantId));
        level.PhysicalQty.ShouldBe(2);
    }
```

- [x] **Step 2: Run the tests to verify they fail**

Run: `cd backend && dotnet test tests/SlingKing.IntegrationTests --filter FullyQualifiedName~InventoryEndpointTests`

Expected: the six new tests fail with 404 — there is no recount route.

- [x] **Step 3: Add the result model**

Append to `backend/src/SlingKing.Application/Inventory/InventoryModels.cs`:

```csharp
/// <summary>
/// What a recount did. <c>Movement</c> is null when the count matched — nothing happened,
/// and the ledger should not carry a row saying so.
/// </summary>
public record RecountResult(int Previous, int Counted, int Difference, MovementView? Movement);
```

- [x] **Step 4: Extend the interface**

Add to `IStockService`:

```csharp
    /// <summary>
    /// Reads the current quantity under the lock, works out the difference, and records it as
    /// an Adjustment. The current quantity is read inside the transaction on purpose: a caller
    /// computing the delta from a number its page loaded a minute ago posts a wrong adjustment
    /// whenever anything happened in between.
    /// </summary>
    /// <exception cref="Common.AppException">
    /// 400 for a negative count. 404 for an unknown or inactive variant.
    /// </exception>
    Task<RecountResult> RecountAsync(
        long variantId, int countedQuantity, CancellationToken cancellationToken = default);
```

- [x] **Step 5: Implement it**

Add to `StockService`, after `RecordMovementAsync`:

```csharp
    public async Task<RecountResult> RecountAsync(
        long variantId, int countedQuantity, CancellationToken cancellationToken = default)
    {
        if (countedQuantity < 0)
        {
            throw AppException.Invalid("A count cannot be negative.");
        }

        var variantExists = await _db.ProductVariants
            .AnyAsync(v => v.Id == variantId && v.IsActive, cancellationToken);

        if (!variantExists)
        {
            throw AppException.NotFound("That item no longer exists.");
        }

        var locationId = await DefaultLocationIdAsync(cancellationToken);

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        var level = await LockLevelAsync(variantId, locationId, cancellationToken);

        var previous = level.PhysicalQty;
        var difference = countedQuantity - previous;

        if (difference == 0)
        {
            // Nothing happened. A zero-delta ledger row would be noise in the one history
            // the owner actually reads.
            await transaction.CommitAsync(cancellationToken);
            return new RecountResult(previous, countedQuantity, 0, null);
        }

        var movement = await AppendAsync(
            level, InventoryTransactionType.Adjustment, difference,
            unitCost: null, reason: $"Recount: counted {countedQuantity}",
            // The exemption. A recount is the one operation authorised to bring cost records
            // into line with stock, because the counted number is authoritative by definition.
            // Without this, the disagreement message names a fix that hits the same wall.
            allowShortfall: true,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return new RecountResult(previous, countedQuantity, difference, movement);
    }
```

- [x] **Step 6: Add the contract and the route**

Append to `backend/src/SlingKing.Api/Contracts/Inventory/InventoryContracts.cs`:

```csharp
public record RecountRequest(
    [Required, Range(0, 1_000_000)] int? CountedQuantity);
```

Add to `InventoryController`:

```csharp
    [HttpPost("{variantId:long}/recount")]
    [ProducesResponseType(typeof(RecountResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Recount(
        long variantId, RecountRequest request, CancellationToken cancellationToken) =>
        Ok(await _stock.RecountAsync(
            variantId, request.CountedQuantity!.Value, cancellationToken));
```

- [x] **Step 7: Run the tests to verify they pass**

Run: `cd backend && dotnet test tests/SlingKing.IntegrationTests --filter FullyQualifiedName~InventoryEndpointTests`

Expected: 20 passed.

- [x] **Step 8: Prove the exemption is load-bearing**

Temporarily change `allowShortfall: true` in `RecountAsync` to `allowShortfall: false`
and re-run.

Expected: `An_ordinary_outflow_refuses_when_layers_run_short_but_a_recount_does_not`
FAILS — the recount comes back 409 with the same message, which is precisely the dead
end the exemption exists to avoid.

**Then put `true` back and re-run — 20 passed.**

- [x] **Step 9: Commit**

```bash
git add backend/src backend/tests
git commit -m "feat: recount reads the current quantity under the lock

The only producer of an Adjustment, and the only path allowed to bring cost
records into line with stock -- without that exemption the disagreement message
names a fix that hits the same wall and returns the same message.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 6: The lock, tested

The one test here that would pass by luck without the row lock. It gets its own file
because it needs two overlapping requests, which the single shared client of
`InventoryEndpointTests` cannot express.

**Files:**
- Create: `backend/tests/SlingKing.IntegrationTests/Persistence/StockConcurrencyTests.cs`

**Interfaces:**
- Consumes: `POST /api/v1/inventory/movements` (Task 4), `POST /api/v1/inventory/{id}/recount`
  (Task 5), `SlingKingApiFactory`.
- Produces: nothing.

- [x] **Step 1: Write the test**

Create `backend/tests/SlingKing.IntegrationTests/Persistence/StockConcurrencyTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SlingKing.Infrastructure.Persistence;
using SlingKing.Infrastructure.Persistence.Seed;
using SlingKing.IntegrationTests.Fixtures;
using Xunit;

namespace SlingKing.IntegrationTests.Persistence;

/// <summary>
/// You are one person, but you have two browser tabs. Both read "12 in stock", both post an
/// outflow of 8. Neither request is wrong on its own; the second must see the first's result.
/// </summary>
[Collection("database")]
public class StockConcurrencyTests : IClassFixture<SlingKingApiFactory>, IAsyncLifetime
{
    private readonly SlingKingApiFactory _factory;
    private readonly HttpClient _first;
    private readonly HttpClient _second;

    public StockConcurrencyTests(SlingKingApiFactory factory)
    {
        _factory = factory;
        _first = factory.CreateClient();
        _second = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>().SeedAsync();
        await AuthenticateAsync(_first);
        await AuthenticateAsync(_second);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private record LoginRequest(string Email, string Password);
    private record LoginResponse(string AccessToken);
    private record VariantRequest(
        long? Id, string? Colour, string? Size, decimal SellingPrice,
        int LowStockThreshold, bool IsActive);
    private record ProductRequest(
        string Name, int? CategoryId, string? Description, List<VariantRequest> Variants);
    private record VariantView(long Id, string Sku);
    private record ProductView(long Id, string Code, List<VariantView> Variants);
    private record MovementRequest(
        long VariantId, string Type, int Quantity, decimal? UnitCost, string? Reason);
    private record RecountRequest(int CountedQuantity);

    private async Task AuthenticateAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest(SlingKingApiFactory.SeedEmail, SlingKingApiFactory.SeedPassword));

        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", body!.AccessToken);
    }

    private async Task<long> NewVariantWithStockAsync(int quantity)
    {
        var created = await _first.PostAsJsonAsync("/api/v1/products", new ProductRequest(
            $"Race {Guid.NewGuid():N}"[..15], null, null,
            [new VariantRequest(null, "Tan", "Standard", 4500m, 5, true)]));

        var product = await created.Content.ReadFromJsonAsync<ProductView>();
        var variantId = product!.Variants.Single().Id;

        var opened = await _first.PostAsJsonAsync("/api/v1/inventory/movements",
            new MovementRequest(variantId, "OpeningStock", quantity, 100m, null));
        opened.StatusCode.ShouldBe(HttpStatusCode.Created);

        return variantId;
    }

    private async Task<int> LevelAsync(long variantId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var level = await db.StockLevels.AsNoTracking()
            .SingleAsync(s => s.VariantId == variantId);
        return level.PhysicalQty;
    }

    [Fact]
    public async Task Two_outflows_racing_for_the_same_stock_cannot_both_win()
    {
        var variantId = await NewVariantWithStockAsync(12);

        Task<HttpResponseMessage> Damage(HttpClient client) => client.PostAsJsonAsync(
            "/api/v1/inventory/movements",
            new MovementRequest(variantId, "Damage", 8, null, "Water damaged in transit"));

        var results = await Task.WhenAll(Damage(_first), Damage(_second));

        // One takes 8 of 12; the other must find 4 and refuse. Both succeeding would drive the
        // level to -4, which ck_physical_non_negative would abort anyway — this asserts the
        // refusal is a clean 409 rather than a 500 out of a constraint violation.
        results.Count(r => r.StatusCode == HttpStatusCode.Created).ShouldBe(1);
        results.Count(r => r.StatusCode == HttpStatusCode.Conflict).ShouldBe(1);

        (await LevelAsync(variantId)).ShouldBe(4);
    }

    [Fact]
    public async Task Two_recounts_racing_do_not_lose_one_of_them()
    {
        var variantId = await NewVariantWithStockAsync(12);

        // The case the check constraint cannot see. Both counts are non-negative and both are
        // legal, so nothing at the database refuses them — without the lock the second
        // computes its delta from the stale 12 and silently overwrites the first.
        var toNine = _second.PostAsJsonAsync(
            $"/api/v1/inventory/{variantId}/recount", new RecountRequest(9));
        var toFive = _first.PostAsJsonAsync(
            $"/api/v1/inventory/{variantId}/recount", new RecountRequest(5));

        var results = await Task.WhenAll(toNine, toFive);
        results.ShouldAllBe(r => r.StatusCode == HttpStatusCode.OK);

        // Whichever ran second is authoritative, so the level is 9 or 5 — but the deltas must
        // add up to it from 12. A lost update leaves the ledger summing to something else.
        var level = await LevelAsync(variantId);
        level.ShouldBeOneOf(9, 5);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var summed = await db.InventoryTransactions
            .Where(t => t.VariantId == variantId)
            .SumAsync(t => t.PhysicalDelta);

        summed.ShouldBe(level);
    }
}
```

- [x] **Step 2: Run the tests to verify they pass**

Run: `cd backend && dotnet test tests/SlingKing.IntegrationTests --filter FullyQualifiedName~StockConcurrencyTests`

Expected: 2 passed.

- [x] **Step 3: Prove the lock is what makes them pass**

This is the whole point of the task. In `StockService.LockLevelAsync`, comment out the
`ExecuteSqlInterpolatedAsync` call so only the LINQ read remains, and re-run.

Expected: `Two_recounts_racing_do_not_lose_one_of_them` FAILS — the summed deltas do not
match the level, because both recounts computed their difference from 12.

`Two_outflows_racing...` may well still pass: the check constraint catches that one on
its own, which is exactly why it is not sufficient evidence and why the recount test
exists. If it does fail, expect a 500 out of a constraint violation rather than a clean
409.

**Then restore the lock statement and re-run — 2 passed.** Say in the commit message that
the test was seen to fail; the gate asks for that evidence.

- [x] **Step 4: Commit**

```bash
git add backend/tests
git commit -m "test: the row lock, seen to fail without it

Two racing recounts are the case ck_physical_non_negative cannot see -- both
counts are legal, and without the lock the second computes its delta from a
stale read and silently overwrites the first. Verified failing with the lock
statement removed, then passing with it restored.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 7: The read endpoints

The list and the item. Both are pure reads — no transaction, no lock.

**Files:**
- Modify: `backend/src/SlingKing.Application/Inventory/InventoryModels.cs`
- Modify: `backend/src/SlingKing.Application/Inventory/IStockService.cs`
- Modify: `backend/src/SlingKing.Application/Inventory/StockService.cs`
- Modify: `backend/src/SlingKing.Api/Controllers/InventoryController.cs`
- Test: `backend/tests/SlingKing.IntegrationTests/Api/InventoryEndpointTests.cs` (append)

**Interfaces:**
- Consumes: everything from Tasks 3–5.
- Produces:
  - `record StockRow(long VariantId, string Sku, string ProductName, string? Colour, string? Size, int PhysicalQty, int LowStockThreshold)`
  - `record StockItemView(long VariantId, string Sku, string ProductName, string? Colour, string? Size, int PhysicalQty, int LowStockThreshold, decimal? StockValue, IReadOnlyList<MovementView> Movements)`
  - `Task<IReadOnlyList<StockRow>> IStockService.ListAsync(string? search, bool lowOnly, CancellationToken)`
  - `Task<StockItemView?> IStockService.GetAsync(long variantId, CancellationToken)`
  - `GET /api/v1/inventory?search=&lowOnly=` and `GET /api/v1/inventory/{variantId}`

**The running total is not on the API.** The item page receives the whole history and
accumulates it in one `reduce`; sending a precomputed column would be the same arithmetic
done twice, in two languages, with two chances to disagree.

- [x] **Step 1: Write the failing tests**

Append to `InventoryEndpointTests.cs`:

```csharp
    private record StockRow(
        long VariantId, string Sku, string ProductName, string? Colour, string? Size,
        int PhysicalQty, int LowStockThreshold);

    private record StockItemView(
        long VariantId, string Sku, string ProductName, string? Colour, string? Size,
        int PhysicalQty, int LowStockThreshold, decimal? StockValue,
        List<MovementView> Movements);

    [Fact]
    public async Task The_list_shows_one_row_per_variant_with_its_level()
    {
        var variantId = await NewVariantAsync();
        await OpenAsync(variantId, 7, 100m);

        var rows = await _client.GetFromJsonAsync<List<StockRow>>("/api/v1/inventory");

        var row = rows!.Single(r => r.VariantId == variantId);
        row.PhysicalQty.ShouldBe(7);
        row.Colour.ShouldBe("Tan");
        row.Size.ShouldBe("Standard");
        row.LowStockThreshold.ShouldBe(5);
        row.Sku.ShouldEndWith("-V01");
    }

    [Fact]
    public async Task The_list_can_be_searched_by_sku()
    {
        var variantId = await NewVariantAsync();
        await OpenAsync(variantId, 1, null);

        var all = await _client.GetFromJsonAsync<List<StockRow>>("/api/v1/inventory");
        var sku = all!.Single(r => r.VariantId == variantId).Sku;

        var found = await _client.GetFromJsonAsync<List<StockRow>>(
            $"/api/v1/inventory?search={sku}");

        found!.Count.ShouldBe(1);
        found[0].VariantId.ShouldBe(variantId);
    }

    [Fact]
    public async Task Low_only_shows_a_variant_at_or_under_its_threshold()
    {
        var variantId = await NewVariantAsync();

        // Threshold is 5. Four is low; six is not.
        await OpenAsync(variantId, 4, null);

        var low = await _client.GetFromJsonAsync<List<StockRow>>(
            "/api/v1/inventory?lowOnly=true");
        low!.ShouldContain(r => r.VariantId == variantId);

        await OpenAsync(variantId, 2, null);

        var notLow = await _client.GetFromJsonAsync<List<StockRow>>(
            "/api/v1/inventory?lowOnly=true");
        notLow!.ShouldNotContain(r => r.VariantId == variantId);
    }

    [Fact]
    public async Task Low_only_ignores_a_variant_with_no_threshold_set()
    {
        // A threshold of zero would otherwise flag every empty variant forever, which makes
        // the filter useless on a catalogue that is still being entered.
        var created = await _client.PostAsJsonAsync("/api/v1/products", new ProductRequest(
            $"NoThresh {Guid.NewGuid():N}"[..16], null, null,
            [new VariantRequest(null, "Black", null, 4500m, 0, true)]));

        var product = await created.Content.ReadFromJsonAsync<ProductView>();
        var variantId = product!.Variants.Single().Id;

        var low = await _client.GetFromJsonAsync<List<StockRow>>(
            "/api/v1/inventory?lowOnly=true");

        low!.ShouldNotContain(r => r.VariantId == variantId);
    }

    [Fact]
    public async Task The_item_returns_its_history_newest_first_and_what_it_is_worth()
    {
        var variantId = await NewVariantAsync();
        await OpenAsync(variantId, 10, 120m);

        await _client.PostAsJsonAsync("/api/v1/inventory/movements",
            new MovementRequest(variantId, "Damage", 4, null, "Water damaged in transit"));

        var item = await _client.GetFromJsonAsync<StockItemView>(
            $"/api/v1/inventory/{variantId}");

        item!.PhysicalQty.ShouldBe(6);
        item.Movements.Count.ShouldBe(2);
        item.Movements[0].Type.ShouldBe("Damage");
        item.Movements[0].Reason.ShouldBe("Water damaged in transit");
        item.Movements[1].Type.ShouldBe("OpeningStock");

        // Six left in a layer bought at 120.
        item.StockValue.ShouldBe(720m);
    }

    [Fact]
    public async Task An_item_holding_stock_of_unknown_cost_is_worth_an_unknown_amount()
    {
        var variantId = await NewVariantAsync();
        await OpenAsync(variantId, 3, null);
        await OpenAsync(variantId, 5, 200m);

        var item = await _client.GetFromJsonAsync<StockItemView>(
            $"/api/v1/inventory/{variantId}");

        // 1000.00 is what the layer we know about is worth. Printing it as the total would
        // understate the stock by whatever the other three cost.
        item!.StockValue.ShouldBeNull();
    }

    [Fact]
    public async Task An_unknown_item_is_not_found()
    {
        var response = await _client.GetAsync("/api/v1/inventory/999999999");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Reading_the_list_requires_a_token()
    {
        using var anonymous = _factory.CreateClient();

        (await anonymous.GetAsync("/api/v1/inventory")).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);
    }
```

- [x] **Step 2: Run the tests to verify they fail**

Run: `cd backend && dotnet test tests/SlingKing.IntegrationTests --filter FullyQualifiedName~InventoryEndpointTests`

Expected: the eight new tests fail — no read routes exist.

- [x] **Step 3: Add the view models**

Append to `backend/src/SlingKing.Application/Inventory/InventoryModels.cs`:

```csharp
/// <summary>
/// One line of the stock list. Carries no money: a value column would make every list
/// render touch the cost layers for a figure the item page already shows.
/// </summary>
public record StockRow(
    long VariantId,
    string Sku,
    string ProductName,
    string? Colour,
    string? Size,
    int PhysicalQty,
    int LowStockThreshold);

/// <summary>
/// <c>Movements</c> is newest-first, which is the order the page reads them in. There is no
/// running-total column: the client has the whole list and accumulates it itself, rather than
/// the same arithmetic being done twice in two languages.
/// </summary>
/// <param name="StockValue">
/// Null when any open layer has an unknown cost. A partial total would understate the stock.
/// </param>
public record StockItemView(
    long VariantId,
    string Sku,
    string ProductName,
    string? Colour,
    string? Size,
    int PhysicalQty,
    int LowStockThreshold,
    decimal? StockValue,
    IReadOnlyList<MovementView> Movements);
```

- [x] **Step 4: Extend the interface**

Add to `IStockService`:

```csharp
    /// <summary>
    /// One row per ACTIVE variant. <paramref name="search"/> matches product name, product
    /// code and SKU — the same fields the products list searches, so the two screens behave
    /// the same way.
    /// </summary>
    /// <param name="lowOnly">
    /// Physical quantity at or under the threshold. Variants with a threshold of zero are
    /// excluded: they would otherwise flag forever and make the filter useless.
    /// </param>
    Task<IReadOnlyList<StockRow>> ListAsync(
        string? search, bool lowOnly, CancellationToken cancellationToken = default);

    /// <summary>Null when the variant does not exist or is retired.</summary>
    Task<StockItemView?> GetAsync(long variantId, CancellationToken cancellationToken = default);
```

- [x] **Step 5: Implement the reads**

Add to `StockService`:

```csharp
    public async Task<IReadOnlyList<StockRow>> ListAsync(
        string? search, bool lowOnly, CancellationToken cancellationToken = default)
    {
        var locationId = await DefaultLocationIdAsync(cancellationToken);

        var query = _db.ProductVariants
            .AsNoTracking()
            .Where(v => v.IsActive && v.Product!.IsActive);

        if (!string.IsNullOrWhiteSpace(search))
        {
            // ToLowerInvariant has no EF Core translation. The local must be lowered with a
            // culture-independent call; the in-query side must stay ToLower(). Do not
            // "make them consistent" — one of the two will stop compiling to SQL.
            var term = search.Trim().ToLowerInvariant();

            query = query.Where(v =>
                v.Sku.ToLower().Contains(term) ||
                v.Product!.Name.ToLower().Contains(term) ||
                v.Product.Code.ToLower().Contains(term));
        }

        var rows = query.Select(v => new StockRow(
            v.Id,
            v.Sku,
            v.Product!.Name,
            v.Colour,
            v.Size,
            _db.StockLevels
                .Where(s => s.VariantId == v.Id && s.LocationId == locationId)
                .Sum(s => (int?)s.PhysicalQty) ?? 0,
            v.LowStockThreshold));

        if (lowOnly)
        {
            rows = rows.Where(r => r.LowStockThreshold > 0
                                && r.PhysicalQty <= r.LowStockThreshold);
        }

        return await rows.OrderBy(r => r.Sku).ToListAsync(cancellationToken);
    }

    public async Task<StockItemView?> GetAsync(
        long variantId, CancellationToken cancellationToken = default)
    {
        var locationId = await DefaultLocationIdAsync(cancellationToken);

        var header = await _db.ProductVariants
            .AsNoTracking()
            .Where(v => v.Id == variantId && v.IsActive)
            .Select(v => new StockRow(
                v.Id,
                v.Sku,
                v.Product!.Name,
                v.Colour,
                v.Size,
                _db.StockLevels
                    .Where(s => s.VariantId == v.Id && s.LocationId == locationId)
                    .Sum(s => (int?)s.PhysicalQty) ?? 0,
                v.LowStockThreshold))
            .SingleOrDefaultAsync(cancellationToken);

        if (header is null)
        {
            return null;
        }

        var movements = await _db.InventoryTransactions
            .AsNoTracking()
            .Where(t => t.VariantId == variantId && t.LocationId == locationId)
            .OrderByDescending(t => t.OccurredAt).ThenByDescending(t => t.Id)
            .Select(t => new MovementView(
                t.Id, t.OccurredAt, t.Type, t.PhysicalDelta, t.TotalCost, t.Reason))
            .ToListAsync(cancellationToken);

        var openLayers = await _db.InventoryCostLayers
            .AsNoTracking()
            .Where(l => l.VariantId == variantId
                     && l.LocationId == locationId
                     && l.RemainingQty > 0)
            .Select(l => new { l.RemainingQty, l.UnitCost })
            .ToListAsync(cancellationToken);

        // Unknown, not partial, and unknown for empty stock too — nothing is held, so nothing
        // is known about what it cost. Summed in memory because the null rule is not a SUM.
        var stockValue = openLayers.Count > 0 && openLayers.All(l => l.UnitCost is not null)
            ? openLayers.Sum(l => l.UnitCost!.Value * l.RemainingQty)
            : (decimal?)null;

        return new StockItemView(
            header.VariantId, header.Sku, header.ProductName, header.Colour, header.Size,
            header.PhysicalQty, header.LowStockThreshold, stockValue, movements);
    }
```

- [x] **Step 6: Add the routes**

Add to `InventoryController`:

```csharp
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<StockRow>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] string? search,
        [FromQuery] bool lowOnly,
        CancellationToken cancellationToken) =>
        Ok(await _stock.ListAsync(search, lowOnly, cancellationToken));

    [HttpGet("{variantId:long}")]
    [ProducesResponseType(typeof(StockItemView), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(long variantId, CancellationToken cancellationToken)
    {
        var item = await _stock.GetAsync(variantId, cancellationToken);

        return item is null ? NotFound() : Ok(item);
    }
```

- [x] **Step 7: Run the tests to verify they pass**

Run: `cd backend && dotnet test tests/SlingKing.IntegrationTests --filter FullyQualifiedName~InventoryEndpointTests`

Expected: 28 passed.

- [x] **Step 8: Run the whole backend suite**

Run: `cd backend && dotnet build -warnaserror; if ($?) { dotnet test }`

Expected: everything green, zero warnings.

- [x] **Step 9: Commit**

```bash
git add backend/src backend/tests
git commit -m "feat: read the stock list and one item's history

The list carries no money -- a value column would make every render touch the
cost layers for a figure the item page already shows. lowOnly skips variants
with no threshold set, which would otherwise flag forever.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 8: Frontend API layer and the stock list

**Files:**
- Create: `frontend/src/api/inventory.ts`
- Create: `frontend/src/features/inventory/InventoryPage.tsx`
- Modify: `frontend/src/app/router.tsx`

**Interfaces:**
- Consumes: `GET /api/v1/inventory` (Task 7), `api` and `serverMessage` from
  `frontend/src/api/client.ts`.
- Produces: `listStock`, `getStockItem`, `recordMovement`, `recount`, and the types
  `StockRow`, `StockItemView`, `MovementView`, `MovementType`, `RecountResult`. Task 9
  imports all of them.

- [x] **Step 1: Write the API layer**

Create `frontend/src/api/inventory.ts`:

```ts
import { api } from "./client";

/**
 * The four types this API accepts. Adjustment is missing on purpose — the recount endpoint
 * is its only producer, so offering it here would offer a second way to change a quantity
 * where the caller does the arithmetic from a number the screen loaded a minute ago.
 */
export const movementTypes = ["OpeningStock", "Damage", "Loss", "PersonalUse"] as const;

export type MovementType = (typeof movementTypes)[number];

export const movementLabels: Record<string, string> = {
  OpeningStock: "Opening stock",
  Damage: "Damaged",
  Loss: "Lost",
  PersonalUse: "Personal use",
  Adjustment: "Recount",
};

/** Outflows must say what happened; the server refuses them otherwise. */
export const outflowTypes: MovementType[] = ["Damage", "Loss", "PersonalUse"];

export interface StockRow {
  variantId: number;
  sku: string;
  productName: string;
  colour: string | null;
  size: string | null;
  physicalQty: number;
  lowStockThreshold: number;
}

export interface MovementView {
  id: number;
  occurredAt: string;
  type: string;
  physicalDelta: number;
  totalCost: number | null;
  reason: string | null;
}

export interface StockItemView extends StockRow {
  stockValue: number | null;
  movements: MovementView[];
}

export interface RecountResult {
  previous: number;
  counted: number;
  difference: number;
  movement: MovementView | null;
}

export interface MovementPayload {
  variantId: number;
  type: MovementType;
  quantity: number;
  unitCost: number | null;
  reason: string | null;
}

export async function listStock(search: string, lowOnly: boolean): Promise<StockRow[]> {
  const { data } = await api.get<StockRow[]>("/api/v1/inventory", {
    params: { search: search || undefined, lowOnly: lowOnly || undefined },
  });
  return data;
}

export async function getStockItem(variantId: number): Promise<StockItemView> {
  const { data } = await api.get<StockItemView>(`/api/v1/inventory/${variantId}`);
  return data;
}

export async function recordMovement(payload: MovementPayload): Promise<MovementView> {
  const { data } = await api.post<MovementView>("/api/v1/inventory/movements", payload);
  return data;
}

export async function recount(
  variantId: number,
  countedQuantity: number,
): Promise<RecountResult> {
  const { data } = await api.post<RecountResult>(
    `/api/v1/inventory/${variantId}/recount`,
    { countedQuantity },
  );
  return data;
}
```

- [x] **Step 2: Write the list screen**

Create `frontend/src/features/inventory/InventoryPage.tsx`:

```tsx
import { useState } from "react";
import { Link } from "react-router-dom";
import { useQuery } from "@tanstack/react-query";
import { listStock } from "../../api/inventory";

export function InventoryPage() {
  const [search, setSearch] = useState("");
  const [lowOnly, setLowOnly] = useState(false);

  const stock = useQuery({
    queryKey: ["stock", search, lowOnly],
    queryFn: () => listStock(search, lowOnly),
  });

  const nothingCounted =
    stock.data !== undefined &&
    stock.data.length > 0 &&
    stock.data.every((row) => row.physicalQty === 0);

  return (
    <div className="mx-auto max-w-6xl px-6 py-10">
      <h1 className="font-display text-[28px] font-bold tracking-tight">Inventory</h1>
      <p className="mt-1 text-[13px] text-muted">
        How many of each bag you have, and where they went.
      </p>

      <div className="topstitch mt-6" />

      <div className="mt-6 flex flex-wrap items-center gap-4">
        <input
          type="search"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          placeholder="Search name, code or SKU"
          aria-label="Search stock"
          className="rounded-stamp border border-rule bg-panel px-3 py-2 text-[14px] transition-colors placeholder:text-muted/60 focus:border-saddle sm:w-80"
        />
        <label className="flex items-center gap-2 text-[13px] text-muted">
          <input
            type="checkbox"
            checked={lowOnly}
            onChange={(e) => setLowOnly(e.target.checked)}
          />
          Low stock only
        </label>
      </div>

      {stock.isPending && <p className="mt-8 text-[13px] text-muted">Loading</p>}

      {stock.isError && (
        <p role="alert" className="mt-8 text-[13px] text-flag">
          Could not load stock. Check that the API is running and try again.
        </p>
      )}

      {stock.data?.length === 0 && (
        <p className="mt-8 text-[13px] text-muted">
          {search || lowOnly
            ? "Nothing matches that."
            : "No items yet. Add a product first, then come back and count it."}
        </p>
      )}

      {nothingCounted && (
        <p className="mt-8 text-[13px] text-muted">
          Nothing counted yet. Open an item and record its opening stock.
        </p>
      )}

      {stock.data && stock.data.length > 0 && (
        <table className="mt-6 w-full border-collapse">
          <thead>
            <tr className="border-b border-rule text-left text-[12px] tracking-wide text-muted uppercase">
              <th className="pb-2 font-medium">Item</th>
              <th className="pb-2 font-medium">SKU</th>
              <th className="pb-2 text-right font-medium">In stock</th>
              <th className="pb-2 text-right font-medium">Low at</th>
            </tr>
          </thead>
          <tbody>
            {stock.data.map((row) => {
              // A threshold of zero means none was set, so it can never be low. Matches
              // exactly what lowOnly filters on at the server.
              const low = row.lowStockThreshold > 0 && row.physicalQty <= row.lowStockThreshold;

              return (
                <tr key={row.variantId} className="border-b border-rule">
                  <td className="py-2.5">
                    <Link
                      to={`/inventory/${row.variantId}`}
                      className="text-[14px] font-medium hover:text-saddle"
                    >
                      {row.productName}
                    </Link>
                    <span className="block text-[12px] text-muted">
                      {[row.colour, row.size].filter(Boolean).join(" · ") || "No variation"}
                    </span>
                  </td>
                  <td className="num py-2.5 text-[13px] text-muted">{row.sku}</td>
                  <td
                    className={`num py-2.5 text-right text-[14px] ${low ? "font-semibold text-flag" : ""}`}
                  >
                    {row.physicalQty}
                  </td>
                  <td className="num py-2.5 text-right text-[13px] text-muted">
                    {row.lowStockThreshold > 0 ? row.lowStockThreshold : "—"}
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      )}
    </div>
  );
}
```

- [x] **Step 3: Add the route**

In `frontend/src/app/router.tsx`, add the import and the route. The route goes above the
`path: "*"` catch-all — anything after it is unreachable.

```tsx
import { InventoryPage } from "../features/inventory/InventoryPage";
```

```tsx
      { path: "inventory", element: <InventoryPage /> },
```

- [x] **Step 4: Type-check and lint**

Run: `cd frontend && npx tsc -b; if ($?) { npm run lint }`

Expected: both clean.

- [x] **Step 5: Commit**

```bash
git add frontend/src
git commit -m "feat: inventory list screen

One row per active variant, because stock is a property of a colour-and-size
rather than of a bag. Low rows carry the flag colour -- the one place on this
screen that earns any.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 9: The item screen — history, worth, and the two forms

**Files:**
- Create: `frontend/src/features/inventory/recount.ts`
- Create: `frontend/src/features/inventory/recount.test.ts`
- Create: `frontend/src/features/inventory/RecordMovement.tsx`
- Create: `frontend/src/features/inventory/Recount.tsx`
- Create: `frontend/src/features/inventory/StockItemPage.tsx`
- Modify: `frontend/src/app/router.tsx`

**Interfaces:**
- Consumes: everything exported from `frontend/src/api/inventory.ts` (Task 8),
  `formatInr` from `frontend/src/lib/money.ts`, `formatIstDateTime` from
  `frontend/src/lib/dates.ts`, `serverMessage` from `frontend/src/api/client.ts`.
- Produces: `parseCount(raw: string): number | null` and
  `recountSentence(counted: number | null, current: number): string`.

- [x] **Step 1: Write the failing tests**

Create `frontend/src/features/inventory/recount.test.ts`:

```ts
import { describe, expect, it } from "vitest";
import { parseCount, recountSentence } from "./recount";

describe("parseCount", () => {
  it("reads a whole number", () => {
    expect(parseCount("9")).toBe(9);
  });

  it("reads zero, which is a real count", () => {
    expect(parseCount("0")).toBe(0);
  });

  it("treats an empty field as no answer, NOT as zero", () => {
    // Number("") is 0 in JavaScript. Taking that at face value is what repriced a bag to
    // nothing in M2a; here it would silently record a count of zero and wipe the stock.
    expect(parseCount("")).toBeNull();
    expect(parseCount("   ")).toBeNull();
  });

  it("refuses anything that is not a whole non-negative number", () => {
    expect(parseCount("-1")).toBeNull();
    expect(parseCount("2.5")).toBeNull();
    expect(parseCount("nine")).toBeNull();
  });
});

describe("recountSentence", () => {
  it("says nothing at all until a number is entered", () => {
    expect(recountSentence(null, 12)).toBe("");
  });

  it("names the loss when fewer were counted", () => {
    expect(recountSentence(9, 12)).toBe(
      "Counted 9, system says 12 — this records a loss of 3.",
    );
  });

  it("names the gain when more were counted", () => {
    expect(recountSentence(15, 12)).toBe(
      "Counted 15, system says 12 — this records a gain of 3.",
    );
  });

  it("says plainly that nothing happens when the count matches", () => {
    expect(recountSentence(12, 12)).toBe("Counted 12, system says 12 — nothing to record.");
  });
});
```

- [x] **Step 2: Run the tests to verify they fail**

Run: `cd frontend && npx vitest run src/features/inventory/recount.test.ts`

Expected: failure resolving `./recount` — the module does not exist.

- [x] **Step 3: Write the arithmetic**

Create `frontend/src/features/inventory/recount.ts`:

```ts
/**
 * Null means "no answer yet", never zero. Number("") is 0 in JavaScript, and taking that at
 * face value is what repriced a bag to nothing in M2a — here it would record a count of zero
 * and wipe the stock without the owner typing a digit.
 */
export function parseCount(raw: string): number | null {
  const trimmed = raw.trim();
  if (trimmed === "") return null;

  const value = Number(trimmed);
  return Number.isInteger(value) && value >= 0 ? value : null;
}

/** The sentence the owner confirms, instead of confirming a bare number. */
export function recountSentence(counted: number | null, current: number): string {
  if (counted === null) return "";

  const difference = counted - current;
  const preamble = `Counted ${counted}, system says ${current} — `;

  if (difference === 0) return `${preamble}nothing to record.`;

  return difference > 0
    ? `${preamble}this records a gain of ${difference}.`
    : `${preamble}this records a loss of ${-difference}.`;
}
```

- [x] **Step 4: Run the tests to verify they pass**

Run: `cd frontend && npx vitest run src/features/inventory/recount.test.ts`

Expected: 8 passed.

- [x] **Step 5: Write the movement form**

Create `frontend/src/features/inventory/RecordMovement.tsx`:

```tsx
import { useState, type FormEvent } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import {
  movementLabels,
  movementTypes,
  outflowTypes,
  recordMovement,
  type MovementType,
} from "../../api/inventory";
import { serverMessage } from "../../api/client";
import { parseCount } from "./recount";

const field =
  "mt-1.5 w-full rounded-stamp border border-rule bg-panel px-3 py-2.5 text-[14px] " +
  "transition-colors placeholder:text-muted/60 focus:border-saddle";

export function RecordMovement({ variantId }: { variantId: number }) {
  const [type, setType] = useState<MovementType>("OpeningStock");
  const [quantity, setQuantity] = useState("");
  const [unitCost, setUnitCost] = useState("");
  const [reason, setReason] = useState("");
  const [error, setError] = useState<string | null>(null);

  const queryClient = useQueryClient();
  const isOutflow = outflowTypes.includes(type);

  const save = useMutation({
    mutationFn: recordMovement,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["stockItem", variantId] });
      queryClient.invalidateQueries({ queryKey: ["stock"] });
      setQuantity("");
      setUnitCost("");
      setReason("");
      setError(null);
    },
    onError: (err) => setError(serverMessage(err, "That movement could not be recorded.")),
  });

  function handleSubmit(event: FormEvent) {
    event.preventDefault();
    setError(null);

    const parsed = parseCount(quantity);

    if (parsed === null || parsed < 1) {
      setError("Enter how many, as a number above zero.");
      return;
    }

    if (isOutflow && !reason.trim()) {
      setError("Say briefly what happened, so this makes sense later.");
      return;
    }

    // An empty cost box means unknown, never zero. Number("") would say the bags were free.
    const cost = unitCost.trim() === "" ? null : Number(unitCost);

    if (cost !== null && (!Number.isFinite(cost) || cost < 0)) {
      setError("A cost cannot be negative.");
      return;
    }

    save.mutate({
      variantId,
      type,
      quantity: parsed,
      unitCost: isOutflow ? null : cost,
      reason: reason.trim() || null,
    });
  }

  return (
    <form onSubmit={handleSubmit} className="mt-4">
      <label className="text-[12px] font-medium tracking-wide" htmlFor="movement-type">
        What happened
      </label>
      <select
        id="movement-type"
        value={type}
        onChange={(e) => setType(e.target.value as MovementType)}
        className={field}
      >
        {movementTypes.map((option) => (
          <option key={option} value={option}>
            {movementLabels[option]}
          </option>
        ))}
      </select>

      <div className="mt-4 flex gap-3">
        <div className="flex-1">
          <label className="text-[12px] font-medium tracking-wide" htmlFor="movement-quantity">
            How many
          </label>
          <input
            id="movement-quantity"
            inputMode="numeric"
            value={quantity}
            onChange={(e) => setQuantity(e.target.value)}
            className={`num ${field}`}
          />
        </div>

        {/* Only an inflow can carry a cost. The server refuses one on an outflow, so
            offering the box there would be offering a field that cannot be submitted. */}
        {!isOutflow && (
          <div className="flex-1">
            <label className="text-[12px] font-medium tracking-wide" htmlFor="movement-cost">
              Cost each <span className="font-normal text-muted">(optional)</span>
            </label>
            <input
              id="movement-cost"
              inputMode="decimal"
              placeholder="Leave blank if unknown"
              value={unitCost}
              onChange={(e) => setUnitCost(e.target.value)}
              className={`num ${field}`}
            />
          </div>
        )}
      </div>

      <div className="mt-4">
        <label className="text-[12px] font-medium tracking-wide" htmlFor="movement-reason">
          Why {isOutflow ? "" : <span className="font-normal text-muted">(optional)</span>}
        </label>
        <input
          id="movement-reason"
          value={reason}
          onChange={(e) => setReason(e.target.value)}
          placeholder={isOutflow ? "Water damaged in transit" : ""}
          className={field}
        />
      </div>

      {error && (
        <p role="alert" className="mt-3 text-[13px] text-flag">
          {error}
        </p>
      )}

      <button
        type="submit"
        disabled={save.isPending}
        className="font-display mt-4 rounded-stamp bg-saddle px-5 py-2.5 text-[14px] font-semibold tracking-wide text-white transition-opacity hover:opacity-90 disabled:opacity-55"
      >
        {save.isPending ? "Recording" : "Record"}
      </button>
    </form>
  );
}
```

- [x] **Step 6: Write the recount form**

Create `frontend/src/features/inventory/Recount.tsx`:

```tsx
import { useState, type FormEvent } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { recount } from "../../api/inventory";
import { serverMessage } from "../../api/client";
import { parseCount, recountSentence } from "./recount";

export function Recount({ variantId, current }: { variantId: number; current: number }) {
  const [counted, setCounted] = useState("");
  const [error, setError] = useState<string | null>(null);

  const queryClient = useQueryClient();
  const parsed = parseCount(counted);
  const sentence = recountSentence(parsed, current);

  const save = useMutation({
    mutationFn: (value: number) => recount(variantId, value),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["stockItem", variantId] });
      queryClient.invalidateQueries({ queryKey: ["stock"] });
      setCounted("");
      setError(null);
    },
    onError: (err) => setError(serverMessage(err, "That recount could not be saved.")),
  });

  function handleSubmit(event: FormEvent) {
    event.preventDefault();
    setError(null);

    if (parsed === null) {
      setError("Enter the number you counted.");
      return;
    }

    save.mutate(parsed);
  }

  return (
    <form onSubmit={handleSubmit} className="mt-4">
      <label className="text-[12px] font-medium tracking-wide" htmlFor="recount-counted">
        How many did you count
      </label>
      <input
        id="recount-counted"
        inputMode="numeric"
        value={counted}
        onChange={(e) => setCounted(e.target.value)}
        className="num mt-1.5 w-full rounded-stamp border border-rule bg-panel px-3 py-2.5 text-[14px] transition-colors focus:border-saddle"
      />

      {/* You confirm the sentence, not the number. The two forms on this page take numbers
          that mean opposite things, and this is the line that says which one this is. */}
      {sentence && <p className="mt-3 text-[13px] text-muted">{sentence}</p>}

      {error && (
        <p role="alert" className="mt-3 text-[13px] text-flag">
          {error}
        </p>
      )}

      <button
        type="submit"
        disabled={save.isPending || parsed === null || parsed === current}
        className="font-display mt-4 rounded-stamp bg-saddle px-5 py-2.5 text-[14px] font-semibold tracking-wide text-white transition-opacity hover:opacity-90 disabled:opacity-55"
      >
        {save.isPending ? "Saving" : "Save the count"}
      </button>
    </form>
  );
}
```

- [x] **Step 7: Write the item page**

Create `frontend/src/features/inventory/StockItemPage.tsx`:

```tsx
import { useNavigate, useParams } from "react-router-dom";
import { useQuery } from "@tanstack/react-query";
import { getStockItem, movementLabels } from "../../api/inventory";
import { formatInr } from "../../lib/money";
import { formatIstDateTime } from "../../lib/dates";
import { RecordMovement } from "./RecordMovement";
import { Recount } from "./Recount";

export function StockItemPage() {
  const { variantId } = useParams();
  const id = Number(variantId);
  const navigate = useNavigate();

  const item = useQuery({
    queryKey: ["stockItem", id],
    queryFn: () => getStockItem(id),
    enabled: Number.isInteger(id),
  });

  if (item.isPending) {
    return <p className="mx-auto max-w-3xl px-6 py-10 text-[13px] text-muted">Loading</p>;
  }

  if (item.isError || !item.data) {
    return (
      <div className="mx-auto max-w-3xl px-6 py-10">
        <p role="alert" className="text-[13px] text-flag">
          That item could not be loaded. It may have been retired, or the server may be
          unreachable.
        </p>
        <button
          type="button"
          onClick={() => navigate("/inventory")}
          className="mt-4 text-[14px] text-saddle"
        >
          Back to inventory
        </button>
      </div>
    );
  }

  const stock = item.data;

  // Oldest-first, accumulating, then flipped back — the API sends newest-first because that
  // is the reading order, but a running total only makes sense counted forwards.
  const oldestFirst = [...stock.movements].reverse();
  let carried = 0;
  const withTotals = oldestFirst.map((movement) => {
    carried += movement.physicalDelta;
    return { movement, runningTotal: carried };
  });
  withTotals.reverse();

  return (
    <div className="mx-auto max-w-3xl px-6 py-10">
      <button
        type="button"
        onClick={() => navigate("/inventory")}
        className="text-[13px] text-muted transition-colors hover:text-saddle"
      >
        ← Inventory
      </button>

      <h1 className="font-display mt-3 text-[28px] font-bold tracking-tight">
        {stock.productName}
      </h1>
      <p className="mt-1 text-[13px] text-muted">
        <span className="num">{stock.sku}</span>
        {" · "}
        {[stock.colour, stock.size].filter(Boolean).join(" · ") || "No variation"}
      </p>

      <p className="num font-display mt-6 text-[44px] leading-none font-bold">
        {stock.physicalQty}
      </p>
      <p className="mt-1 text-[13px] text-muted">
        in stock, worth <span className="num">{formatInr(stock.stockValue)}</span>
      </p>

      <div className="topstitch mt-8" />

      <div className="mt-6 grid gap-8 sm:grid-cols-2">
        <section>
          <h2 className="font-display text-[16px] font-bold">Record a movement</h2>
          <p className="mt-1 text-[13px] text-muted">How many came in or went out.</p>
          <RecordMovement variantId={stock.variantId} />
        </section>

        <section>
          <h2 className="font-display text-[16px] font-bold">Recount</h2>
          <p className="mt-1 text-[13px] text-muted">
            How many there are in total, right now.
          </p>
          <Recount variantId={stock.variantId} current={stock.physicalQty} />
        </section>
      </div>

      <div className="topstitch mt-8" />

      <h2 className="font-display mt-6 text-[16px] font-bold">History</h2>
      <p className="mt-1 text-[13px] text-muted">
        Entries cannot be edited or removed. Correct a mistake by recording its opposite —
        both stay here.
      </p>

      {withTotals.length === 0 ? (
        <p className="mt-6 text-[13px] text-muted">
          Nothing recorded yet. Start with the opening stock.
        </p>
      ) : (
        <table className="mt-4 w-full border-collapse">
          <thead>
            <tr className="border-b border-rule text-left text-[12px] tracking-wide text-muted uppercase">
              <th className="pb-2 font-medium">When</th>
              <th className="pb-2 font-medium">What</th>
              <th className="pb-2 text-right font-medium">Change</th>
              <th className="pb-2 text-right font-medium">Left</th>
              <th className="pb-2 text-right font-medium">Cost</th>
            </tr>
          </thead>
          <tbody>
            {withTotals.map(({ movement, runningTotal }) => (
              <tr key={movement.id} className="border-b border-rule align-top">
                <td className="num py-2.5 text-[13px] text-muted">
                  {formatIstDateTime(movement.occurredAt)}
                </td>
                <td className="py-2.5 text-[13px]">
                  {movementLabels[movement.type] ?? movement.type}
                  {movement.reason && (
                    <span className="block text-[12px] text-muted">{movement.reason}</span>
                  )}
                </td>
                <td
                  className={`num py-2.5 text-right text-[13px] ${
                    movement.physicalDelta < 0 ? "text-flag" : ""
                  }`}
                >
                  {movement.physicalDelta > 0 ? `+${movement.physicalDelta}` : movement.physicalDelta}
                </td>
                <td className="num py-2.5 text-right text-[13px]">{runningTotal}</td>
                <td className="num py-2.5 text-right text-[13px] text-muted">
                  {formatInr(movement.totalCost)}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  );
}
```

- [x] **Step 8: Add the route**

In `frontend/src/app/router.tsx`, add the import and the route — again above the `"*"`
catch-all:

```tsx
import { StockItemPage } from "../features/inventory/StockItemPage";
```

```tsx
      { path: "inventory/:variantId", element: <StockItemPage /> },
```

- [x] **Step 9: Run the frontend checks**

Run: `cd frontend && npx tsc -b; if ($?) { npm run lint; if ($?) { npx vitest run } }`

Expected: type-check clean, lint clean, all vitest tests passing (10 from M2a plus 8 new).

- [x] **Step 10: Commit**

```bash
git add frontend/src
git commit -m "feat: stock item screen with history, worth and the two forms

Record a movement and Recount take numbers that mean opposite things -- one is
what changed, one is the total -- so they are separate forms with separate
verbs, and the recount confirms a sentence rather than a bare number.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 10: Wire it up, walk it, and gate it

**Files:**
- Modify: `frontend/src/app/NotFound.tsx`
- Test: the whole suite, plus a browser walkthrough

**Interfaces:**
- Consumes: everything.
- Produces: the milestone.

- [x] **Step 1: Fix the stale not-found copy**

`frontend/src/app/NotFound.tsx` still promises that Products, Inventory and Purchases are
on their way. Two of the three now exist. Read the file and change that sentence to name
only Purchases — keep the surrounding voice and markup exactly as they are.

- [x] **Step 2: Run everything**

Run: `cd backend && dotnet build -warnaserror; if ($?) { dotnet test }`

Expected: green, zero warnings. Expect roughly 115 integration and 78 unit tests — the
M2a totals of 85 and 68 plus 30 and 10 from this milestone. An exact match is not the
gate; a green run with no warnings is.

Run: `cd frontend && npx tsc -b; if ($?) { npm run lint; if ($?) { npx vitest run } }`

Expected: all clean, 18 tests passing.

- [x] **Step 3: Confirm no migration crept in**

Run: `git diff --name-only main -- backend/src/SlingKing.Infrastructure/Migrations`

Expected: no output. The schema was complete before this milestone started, and
`SchemaConstraintTests` asserting 28 tables must still pass — which Step 2 has already
shown.

- [x] **Step 4: Start the app**

Run these in two background shells:

```
cd backend/src/SlingKing.Api && dotnet run
cd frontend && npm run dev
```

Wait for the API to report it is listening, and Vite to print its local URL.

- [x] **Step 5: Probe the stub before trusting the browser**

Before driving any UI, confirm the API actually rejects a bad token:

Run: `curl.exe -s -o NUL -w "%{http_code}" -H "Authorization: Bearer garbage" http://localhost:5000/api/v1/inventory`

Expected: `401`. If this returns 200, the session you are about to test is not
authenticated the way you think it is, and every result after it is worthless. Adjust the
port to whatever the API reported.

- [x] **Step 6: Walk it in Playwright**

Sign in with the throwaway seed account — `SLINGKING_SEED_ADMIN_EMAIL` and
`SLINGKING_SEED_ADMIN_PASSWORD` from the process environment. **Never type the owner's
real password into a tool call, and never print `backend/.env`.**

Then, against the real API:

1. Open **Inventory** from the top rail. The link must resolve to a real screen, not the
   not-found page. BOHO Style · Multicolor/Standard is there at 0.
2. Open the item. Record **Opening stock** of 10 at a cost of 120.
   The count reads 10; worth reads ₹1,200.00.
3. Record a **Damaged** of 4 with the reason "Water damaged in transit".
   The count reads 6; the history shows the damage with cost ₹480.00 and a running total
   of 6.
4. Try a **Damaged** of 99. The refusal names the number: "Only 6 in stock."
5. Try an outflow with the reason left blank. It is refused before anything is sent.
6. **Recount** to 9. The sentence reads "Counted 9, system says 12 — …" — check the
   arithmetic matches whatever the page currently shows, then save. The history gains a
   Recount row.
7. Recount to the same number again. The button is disabled and nothing is written.
8. Reload the page. Every number survives.

Take a screenshot of the item page with its history and keep it with the task notes.

- [x] **Step 7: Check the browser console**

Read the console for the whole walkthrough. Expected: no errors, no React warnings, no
unhandled rejections. A 409 or 400 logged by axios for the deliberate refusals in steps 4
and 5 is expected and correct.

- [x] **Step 8: Stop the servers**

Stop both background shells. Leaving them running holds Supabase pooler connections out
of a cap of fifteen.

- [x] **Step 9: Request a whole-branch review**

Use `superpowers:requesting-code-review` against the full diff from `main`, not
task-by-task. This is not optional and it is not a formality: M2a's whole-branch review
found a Critical that six per-task reviews structurally could not, because the bug lived
in the seams between individually reasonable pieces. This milestone has more seams.

Point the reviewer at the seams specifically:

- Does every path that changes `physical_qty` also write a ledger row, and vice versa?
- Can any path reach `AppendAsync` without holding the lock?
- Is there any way to get an `Adjustment` in without going through the recount?
- Does any code substitute `0` for an unknown cost, anywhere, in either language?
- Does the two-saves-in-one-transaction structure hold if the second save throws?

- [x] **Step 10: Address the findings, then merge**

Use `superpowers:receiving-code-review` on whatever comes back — verify each finding
before implementing it; do not agree performatively.

Then `superpowers:finishing-a-development-branch`. Tag the merge `r1-m2b`.

- [x] **Step 11: Tick this plan's boxes and commit it**

Mark every checkbox in this file, then:

```bash
git add docs/superpowers/plans/2026-08-30-r1-m2b-stock-ledger.md
git commit -m "docs: mark M2b plan tasks complete

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Self-Review

**Spec coverage.** Every section of the spec maps to a task:

| Spec section | Task |
|---|---|
| `CatalogException` → `AppException` | 1 |
| `FifoCostEngine`, the three cost rules | 2 |
| One movement is one transaction; the row lock | 3 |
| Postable types and their directions; the refusals | 3, 4 |
| FIFO consumption, cost copied not joined | 4 |
| Append-only, proven behaviourally | 4 |
| Recount, and the shortfall exemption | 5 |
| Concurrency | 6 |
| `GET /inventory`, `GET /inventory/{variantId}`, `lowOnly` semantics | 7 |
| Inventory list screen | 8 |
| Item screen, two controls, the append-only line | 9 |
| Shell wiring, not-found copy, browser walkthrough, gate | 10 |
| No migration | Global constraint, checked in Task 10 Step 3 |
| Both invariants | Task 4 Step 1, `The_ledger_and_the_stock_level_agree_after_a_run_of_movements` |

**Two deliberate deviations from the spec**, both simplifications found while writing the
tasks, both flagged here rather than applied silently:

1. **`FifoCostEngine` is a static class over in-memory layers**, not an injected service.
   The spec says it "touches neither `stock_levels` nor the ledger"; making it static over
   a `IReadOnlyList<InventoryCostLayer>` is the strongest possible form of that promise,
   and it is what lets Task 2's ten tests run without Postgres.
2. **No running-total field on the API.** The spec's item screen shows a running total;
   the client computes it from the history it already has. Sending it would be the same
   arithmetic in two languages with two chances to disagree.

**Placeholder scan.** No `TBD`, no "add error handling", no "similar to Task N". Every
code step carries the actual code; every test step carries the actual test.

**Type consistency.** `MovementView` is defined once in Task 3 and reused verbatim in
Tasks 5, 7, 8 and 9 — same six fields, same order. `StockRow` is defined in Task 7 and
`StockItemView` extends it in the TypeScript of Task 8, matching the C# record's field
order. `AppendAsync`, `LockLevelAsync`, `ConsumeAsync` and `SourceOf` are introduced in
Task 4 and called by name in Tasks 5 and 7. `parseCount` and `recountSentence` are defined
in Task 9 Step 3 and used in Steps 5 and 6 of the same task.

**One thing an executor will hit.** Task 3 writes a cost layer for *every* movement,
because outflows do not exist yet and the direction branch would be dead code. Task 4
Step 2 predicts the exact failure this causes and Step 3 replaces it. An executor running
Task 3 in isolation and reviewing it as finished work will correctly observe that a Damage
would add stock — it cannot, because Task 3 rejects every type but `OpeningStock`, and the
`ponytail:` comment at the end of Task 3 Step 5 says so explicitly.
