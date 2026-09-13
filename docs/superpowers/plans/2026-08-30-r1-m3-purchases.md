# SlingKing R1 · M3 Purchases — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Recording what was bought, from whom and at what cost — so real cost enters the FIFO layers instead of being typed by hand on the Inventory screen.

**Architecture:** `PurchaseService` and a static, database-free `PurchaseTotals` in `SlingKing.Application/Purchasing/`. `PurchaseService` owns purchases, lines and suppliers and never touches stock; for that it calls **one** new method on `StockService`, which keeps its monopoly on `physical_qty`, the ledger and the cost layers. Every table already exists and is already constrained: **there is no migration**.

**Tech Stack:** .NET 8.0.408 (`net8.0`), EF Core 8.0.30, Npgsql 8.0.11, xUnit 2.9.3 + Shouldly 4.3.0; React 19.2.8, TypeScript 5.9.3, Vite 8.2.2, TanStack Query 5, React Router 7, Tailwind 4, axios, vitest.

**Spec:** `docs/superpowers/specs/2026-08-30-r1-m3-purchases-design.md` — normative. Read it alongside this plan.

---

## Global Constraints

These apply to every task. Do not restate them per task; do not violate them in any task.

- **Target framework is `net8.0` everywhere.** `global.json` pins the SDK to 8.0.408.
- **`SlingKing.Domain` has zero `<ProjectReference>` and zero `<PackageReference>`.** M3 changes exactly one doc comment there and nothing else.
- **`SlingKing.Application` must not reference any database-provider package.** It has `Microsoft.EntityFrameworkCore.Relational` and the Domain project, and nothing else. A Postgres error is matched by a **string** on the constraint name, never by `PostgresException` or a SqlState — see `ProductService.UpdateAsync`. Adding `Npgsql.EntityFrameworkCore.PostgreSQL` to Application will fail review.
- **Dependency direction is `Api → Application → Domain`, and `Infrastructure → Application → Domain`.** `Application` never references `Infrastructure`.
- **No secrets in source, ever.** All configuration comes from environment variables. **Never print, echo, or `cat` `backend/.env`.**
- **Money is `numeric(18,2)` for prices, line totals and purchase totals; `numeric(18,4)` for unit costs; quantities are `integer`; timestamps are `timestamptz` in UTC; enums are `smallint`.** Prices are **rupees with decimals, not paise**.
- **Cost is optional. Null means *unknown*, never zero.** No code may substitute `0m` for an absent cost, and **no total may be a partial sum** — this is the rule M3's purchase total exists to obey.
- **Table and column names are `snake_case`;** C# properties stay `PascalCase`. `EFCore.NamingConventions` does the mapping — never hand-write `HasColumnName`.
- **Integration tests run only against the `test` schema.** `SlingKingApiFactory` throws if the connection string does not target it. Never point a test at `public`.
- **The Supabase session pooler caps this project at 15 client connections.** `SlingKingApiFactory` bounds itself to 5. Never add parallelism or raise pool sizes.
- **Never use the direct connection host (`db.<ref>.supabase.co`)** — it is IPv6-only and unreachable from this machine.
- **There is no migration in this milestone.** The table count stays at 28 (`SchemaConstraintTests.cs:209`). A migration appearing here means something has been designed the schema did not anticipate — stop and raise it.
- **Business logic never lives in a controller.** Controllers validate shape, delegate, and map to a response.
- **Internal exceptions never reach the client.** Everything becomes RFC 7807 `ProblemDetails` via `AppException`.
- **No deletion, anywhere, and no update of a row committed by a previous transaction.** No `DELETE` endpoint exists; `NoDeleteRoutesTests` enforces it.
- **Only `StockService` may write `physical_qty`, `inventory_transactions`, `inventory_cost_layers` or `inventory_cost_consumptions`.** `PurchaseService` calls into it and never writes those tables itself.
- **Locks on `stock_levels` are always taken in ascending variant id.** Out-of-order locking across a multi-line purchase deadlocks in Postgres.
- **Validation attributes on a `record` bind to the primary-constructor parameters, never with a `[property:]` target.** A `[property:]` target compiles and then throws at request time. See `Contracts/Auth/AuthContracts.cs`.
- **Every number in the UI carries the `.num` class.** Design tokens only — no raw hex.
- **Enums serialize as strings** — `JsonStringEnumConverter` is registered globally in `Program.cs`.
- **Commit at the end of every task**, with the message given in that task's final step.

---

## File Structure

```
backend/src/SlingKing.Application/
├─ Catalog/
│  └─ VariantRules.cs             MOVED from StockService: the shared IsReachable expression
├─ Inventory/
│  └─ StockService.cs             gains ApplyPurchaseAsync + ReversePurchaseAsync; AppendAsync gains a context
└─ Purchasing/
   ├─ PurchaseModels.cs           input and view records
   ├─ PurchaseTotals.cs           static, pure, no IAppDbContext
   ├─ ISupplierService.cs
   ├─ SupplierService.cs          list, create, rename — mirrors CategoryService
   ├─ IPurchaseService.cs
   └─ PurchaseService.cs          record, cancel, list

backend/src/SlingKing.Api/
├─ Contracts/Purchasing/PurchasingContracts.cs
├─ Controllers/SuppliersController.cs
└─ Controllers/PurchasesController.cs

backend/tests/SlingKing.UnitTests/Purchasing/
└─ PurchaseTotalsTests.cs         the money arithmetic, no database

backend/tests/SlingKing.IntegrationTests/Api/
├─ SupplierEndpointTests.cs
└─ PurchaseEndpointTests.cs       recording, cancelling, listing, back-dating, invariants
backend/tests/SlingKing.IntegrationTests/Persistence/
└─ PurchaseConcurrencyTests.cs    lock ordering, with an honest caveat

frontend/src/
├─ api/purchasing.ts
├─ lib/purchaseTotals.ts          pure: line totals and the running total
├─ lib/purchaseTotals.test.ts
└─ features/purchasing/
   ├─ PurchasesPage.tsx           the list, rows expand to lines
   ├─ PurchaseForm.tsx            record a purchase
   ├─ SupplierSelect.tsx          mirrors CategorySelect
   └─ VariantPicker.tsx           searchable, every colour of every bag
```

**`PurchaseTotals` and `frontend/src/lib/purchaseTotals.ts` are database-free and DOM-free on purpose**, following `FifoCostEngine` and `lib/recount.ts`. The moment either learns what an `IAppDbContext` or a React hook is, the money arithmetic loses its millisecond test loop.

**`lib/purchaseTotals.ts` goes in `lib/`, not in the feature folder.** `recount.ts` had to be moved there mid-milestone because `Recount.tsx` and `recount.ts` collided on a case-insensitive filesystem. Do not recreate that collision — pure helpers live in `lib/`.

---

## Task 1: Shared variant rule, purchase cost source, and one corrected comment

Three small preparatory changes with no new behaviour. The existing suite is the test: it must come out with exactly the same pass count it went in with.

**Files:**
- Create: `backend/src/SlingKing.Application/Catalog/VariantRules.cs`
- Modify: `backend/src/SlingKing.Application/Inventory/StockService.cs`
- Modify: `backend/src/SlingKing.Domain/Purchasing/Purchase.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `SlingKing.Application.Catalog.VariantRules.IsReachable`, a
  `public static readonly Expression<Func<ProductVariant, bool>>`. Tasks 4 and 6
  use it to reject purchases against retired variants.
  `StockService.SourceOf` maps `InventoryTransactionType.Purchase` to
  `CostLayerSourceType.Purchase`.

- [x] **Step 1: Capture the current test count**

Run: `cd backend; dotnet test 2>&1 | Select-String -Pattern "Passed!|Failed!"`

Write the numbers down. Expect roughly 118 integration and 78 unit. Task 1 must not change either. If the suite is not green before you start, stop and report that — do not begin a refactor on a red suite.

- [x] **Step 2: Create the shared rule**

Create `backend/src/SlingKing.Application/Catalog/VariantRules.cs`:

```csharp
using System.Linq.Expressions;
using SlingKing.Domain.Catalog;

namespace SlingKing.Application.Catalog;

/// <summary>
/// Rules about variants that more than one module needs. Shared rather than copied: two
/// definitions of "which variants can be used" would drift, and the first symptom would be
/// one screen offering something another screen refuses.
/// </summary>
public static class VariantRules
{
    /// <summary>
    /// A variant may be used only while it AND its parent product are active. Deactivating a
    /// product deliberately leaves its variant rows untouched, so checking the variant alone
    /// lets a retired product's colours keep appearing.
    /// </summary>
    /// <remarks>
    /// An <see cref="Expression"/> rather than a <c>Func</c> so it composes into
    /// <c>.Where()</c> and translates to SQL. Invoking it as a delegate inside another lambda
    /// silently evaluates client-side.
    /// </remarks>
    public static readonly Expression<Func<ProductVariant, bool>> IsReachable =
        v => v.IsActive && v.Product!.IsActive;
}
```

- [x] **Step 3: Point StockService at it**

In `backend/src/SlingKing.Application/Inventory/StockService.cs`:

- Delete the `private static readonly Expression<Func<ProductVariant, bool>> IsReachable = ...` declaration (around line 70) and its comment.
- Add `using SlingKing.Application.Catalog;` to the usings.
- Replace every `.Where(IsReachable)` with `.Where(VariantRules.IsReachable)`. There are three call sites — `RecordMovementAsync`, `RecountAsync` and `GetAsync` — plus one in `ListAsync`. Verify with a grep that none is missed.
- Remove `using System.Linq.Expressions;` if nothing else in the file needs it. If the build warns about an unused using under `-warnaserror`, that is the signal.

- [x] **Step 4: Teach SourceOf about purchases**

Still in `StockService.cs`, replace `SourceOf` with:

```csharp
    private static CostLayerSourceType SourceOf(InventoryTransactionType type) => type switch
    {
        InventoryTransactionType.OpeningStock => CostLayerSourceType.OpeningStock,
        InventoryTransactionType.Purchase => CostLayerSourceType.Purchase,
        // Everything else that lays a layer is a recount adjusting upward.
        _ => CostLayerSourceType.AdjustmentIn
    };
```

- [x] **Step 5: Correct the misleading domain comment**

In `backend/src/SlingKing.Domain/Purchasing/Purchase.cs`, the `TotalAmount` property carries:

```csharp
    /// <summary>Sum of the lines that have a cost. Null when no line is costed.</summary>
```

That describes a partial sum, which understates what was actually paid and reads on screen as a fact — the defect the optional-cost rule exists to prevent. Replace it with:

```csharp
    /// <summary>
    /// What the purchase cost in total. Null when ANY line has an unknown cost — never a
    /// partial sum of the lines that happen to be costed, because a total that omits a line
    /// understates what was paid while still looking like a figure you can trust.
    /// </summary>
```

Change nothing else in `SlingKing.Domain`.

- [x] **Step 6: Verify nothing still names the old private rule**

Run: `cd backend; Select-String -Path "src\*\**\*.cs" -Pattern "IsReachable" | Select-String -NotMatch "VariantRules"`

Expected: no matches outside `VariantRules.cs` itself. Hits under `bin/` or `obj/` are stale build artefacts and do not count.

- [x] **Step 7: Build and run the full suite**

Run: `cd backend; dotnet build -warnaserror; if ($?) { dotnet test }`

Expected: build clean with zero warnings, and **exactly the same pass count as Step 1**. A changed count means this was not a pure refactor.

- [x] **Step 8: Commit**

```bash
git add backend/src
git commit -m "refactor: share the variant reachability rule, teach SourceOf about purchases

PurchaseService needs the same 'which variants can be used' rule StockService
applies, and a copy would drift. Also corrects Purchase.TotalAmount's comment,
which described a partial sum.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 2: `PurchaseTotals` — the money arithmetic, no database

The rule this task exists to enforce: **a purchase total is null when any line's cost is unknown.** Built in isolation where its tests run in milliseconds.

**Files:**
- Create: `backend/src/SlingKing.Application/Purchasing/PurchaseModels.cs`
- Create: `backend/src/SlingKing.Application/Purchasing/PurchaseTotals.cs`
- Test: `backend/tests/SlingKing.UnitTests/Purchasing/PurchaseTotalsTests.cs`

**Interfaces:**
- Consumes: `SlingKing.Application.Common.AppException` — `NotFound(string)`, `Conflict(string, Exception? inner = null)`, `Invalid(string)`.
- Produces:
  - `record PurchaseLineInput(long VariantId, int Quantity, decimal? UnitCost)`
  - `static decimal? PurchaseTotals.LineTotal(int quantity, decimal? unitCost)`
  - `static decimal? PurchaseTotals.Total(IReadOnlyList<PurchaseLineInput> lines)`
  - `static void PurchaseTotals.RejectDuplicateVariants(IReadOnlyList<PurchaseLineInput> lines, IReadOnlyDictionary<long, string> labels)`

- [x] **Step 1: Write the models**

Create `backend/src/SlingKing.Application/Purchasing/PurchaseModels.cs`:

```csharp
namespace SlingKing.Application.Purchasing;

/// <summary>
/// One line as the client submits it. <c>UnitCost</c> null means the cost is genuinely
/// unknown — a mixed lot bought for one price is real, and inventing a number is the one
/// thing this design forbids everywhere else.
/// </summary>
public record PurchaseLineInput(long VariantId, int Quantity, decimal? UnitCost);
```

- [x] **Step 2: Write the failing tests**

Create `backend/tests/SlingKing.UnitTests/Purchasing/PurchaseTotalsTests.cs`:

```csharp
using Shouldly;
using SlingKing.Application.Common;
using SlingKing.Application.Purchasing;
using Xunit;

namespace SlingKing.UnitTests.Purchasing;

public class PurchaseTotalsTests
{
    private static PurchaseLineInput Line(long variantId, int quantity, decimal? unitCost) =>
        new(variantId, quantity, unitCost);

    [Fact]
    public void A_line_total_is_quantity_times_cost()
    {
        PurchaseTotals.LineTotal(12, 450m).ShouldBe(5400m);
    }

    [Fact]
    public void A_line_with_an_unknown_cost_has_an_unknown_total()
    {
        // Not zero. Zero would claim the bags were free.
        PurchaseTotals.LineTotal(12, null).ShouldBeNull();
    }

    [Fact]
    public void A_fully_costed_purchase_totals_its_lines()
    {
        var total = PurchaseTotals.Total([Line(1, 12, 450m), Line(2, 8, 480m)]);

        total.ShouldBe(9240m);
    }

    [Fact]
    public void One_unknown_cost_makes_the_whole_total_unknown()
    {
        var total = PurchaseTotals.Total([Line(1, 12, 450m), Line(2, 8, null)]);

        // 5400 is the sum of the line we happen to know. Reporting it would understate what
        // was actually paid by whatever the second line cost, while looking like a real
        // figure — the same defect the FIFO engine's TotalOf refuses to produce.
        total.ShouldBeNull();
    }

    [Fact]
    public void A_purchase_with_no_costs_at_all_has_no_total()
    {
        PurchaseTotals.Total([Line(1, 12, null), Line(2, 8, null)]).ShouldBeNull();
    }

    [Fact]
    public void An_empty_purchase_has_no_total()
    {
        PurchaseTotals.Total([]).ShouldBeNull();
    }

    [Fact]
    public void A_variant_appearing_twice_is_refused_by_name()
    {
        var labels = new Dictionary<long, string> { [1] = "Tan", [2] = "Black" };

        var refusal = Should.Throw<AppException>(() =>
            PurchaseTotals.RejectDuplicateVariants(
                [Line(1, 12, 450m), Line(2, 8, 480m), Line(1, 3, 450m)], labels));

        refusal.StatusCode.ShouldBe(400);
        refusal.Message.ShouldBe("Tan appears twice. Put the whole quantity on one line.");
    }

    [Fact]
    public void Distinct_variants_are_accepted()
    {
        var labels = new Dictionary<long, string> { [1] = "Tan", [2] = "Black" };

        Should.NotThrow(() => PurchaseTotals.RejectDuplicateVariants(
            [Line(1, 12, 450m), Line(2, 8, 480m)], labels));
    }

    [Fact]
    public void A_duplicate_with_no_label_still_refuses_readably()
    {
        // The label lookup is built from the variants the caller resolved. If one is missing
        // the refusal must still be a sentence, not a crash or a bare id.
        var refusal = Should.Throw<AppException>(() =>
            PurchaseTotals.RejectDuplicateVariants(
                [Line(9, 1, null), Line(9, 1, null)], new Dictionary<long, string>()));

        refusal.StatusCode.ShouldBe(400);
        refusal.Message.ShouldBe("That item appears twice. Put the whole quantity on one line.");
    }
}
```

- [x] **Step 3: Run the tests to verify they fail**

Run: `cd backend; dotnet test tests/SlingKing.UnitTests --filter FullyQualifiedName~PurchaseTotalsTests`

Expected: the project does not compile — `PurchaseTotals` does not exist. That is the correct failure. Do not proceed until you have seen it.

- [x] **Step 4: Write the helper**

Create `backend/src/SlingKing.Application/Purchasing/PurchaseTotals.cs`:

```csharp
using SlingKing.Application.Common;

namespace SlingKing.Application.Purchasing;

/// <summary>
/// The purchase money arithmetic, and nothing else. Static and database-free on purpose: it
/// keeps a millisecond test loop only for as long as it never learns what an IAppDbContext is.
/// </summary>
public static class PurchaseTotals
{
    /// <summary>Null when the cost is unknown — never zero, which would claim the bags were free.</summary>
    public static decimal? LineTotal(int quantity, decimal? unitCost) => unitCost * quantity;

    /// <summary>
    /// Null when ANY line has an unknown cost, and null for an empty purchase. Never a partial
    /// sum: a total that silently omits a line understates what was paid while still reading
    /// as a figure you can trust.
    /// </summary>
    public static decimal? Total(IReadOnlyList<PurchaseLineInput> lines)
    {
        if (lines.Count == 0 || lines.Any(l => l.UnitCost is null))
        {
            return null;
        }

        return lines.Sum(l => l.UnitCost!.Value * l.Quantity);
    }

    /// <summary>
    /// Two lines for the same colour is a typing mistake, not a quantity of two.
    /// <paramref name="labels"/> maps variant id to a human name so the refusal can say which
    /// one; a missing entry falls back to a sentence that still reads.
    /// </summary>
    public static void RejectDuplicateVariants(
        IReadOnlyList<PurchaseLineInput> lines, IReadOnlyDictionary<long, string> labels)
    {
        var seen = new HashSet<long>();

        foreach (var line in lines)
        {
            if (seen.Add(line.VariantId))
            {
                continue;
            }

            var name = labels.TryGetValue(line.VariantId, out var label) ? label : "That item";

            throw AppException.Invalid(
                $"{name} appears twice. Put the whole quantity on one line.");
        }
    }
}
```

- [x] **Step 5: Run the tests to verify they pass**

Run: `cd backend; dotnet test tests/SlingKing.UnitTests --filter FullyQualifiedName~PurchaseTotalsTests`

Expected: 9 passed.

- [x] **Step 6: Prove the all-or-nothing total is load-bearing**

Temporarily change `Total` to sum only the lines it knows:

```csharp
        return lines.Where(l => l.UnitCost is not null).Sum(l => l.UnitCost!.Value * l.Quantity);
```

Run the same filter. Expected: `One_unknown_cost_makes_the_whole_total_unknown` FAILS with `5400` where null was expected, and `A_purchase_with_no_costs_at_all_has_no_total` and `An_empty_purchase_has_no_total` FAIL with `0` where null was expected.

**Then restore `Total` exactly as written above and re-run — 9 passed.** This is the rule the whole milestone's honesty about money rests on, and a guard never seen to fail is decoration.

- [x] **Step 7: Commit**

```bash
git add backend/src/SlingKing.Application/Purchasing backend/tests/SlingKing.UnitTests/Purchasing
git commit -m "feat: purchase totals, pure and database-free

A purchase total is null when any line's cost is unknown -- never a partial sum,
which understates what was paid while reading as a figure you can trust.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 3: Suppliers end to end

Three endpoints that are the category endpoints with a different noun. Build them the same way, because the frontend picker in Task 8 is the category picker with a different noun too.

**Files:**
- Modify: `backend/src/SlingKing.Application/Purchasing/PurchaseModels.cs`
- Create: `backend/src/SlingKing.Application/Purchasing/ISupplierService.cs`
- Create: `backend/src/SlingKing.Application/Purchasing/SupplierService.cs`
- Create: `backend/src/SlingKing.Api/Contracts/Purchasing/PurchasingContracts.cs`
- Create: `backend/src/SlingKing.Api/Controllers/SuppliersController.cs`
- Modify: `backend/src/SlingKing.Infrastructure/DependencyInjection.cs`
- Test: `backend/tests/SlingKing.IntegrationTests/Api/SupplierEndpointTests.cs`

**Interfaces:**
- Consumes: `AppException`, `IAppDbContext`, `IClock`.
- Produces:
  - `record SupplierOption(int Id, string Name, string? Phone)`
  - `Task<IReadOnlyList<SupplierOption>> ISupplierService.ListAsync(CancellationToken)`
  - `Task<SupplierOption> ISupplierService.CreateAsync(string name, string? phone, CancellationToken)`
  - `Task<SupplierOption> ISupplierService.UpdateAsync(int id, string name, string? phone, CancellationToken)`
  - `GET/POST /api/v1/suppliers`, `PUT /api/v1/suppliers/{id}`

- [x] **Step 1: Write the failing tests**

Create `backend/tests/SlingKing.IntegrationTests/Api/SupplierEndpointTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SlingKing.Infrastructure.Persistence.Seed;
using SlingKing.IntegrationTests.Fixtures;
using Xunit;

namespace SlingKing.IntegrationTests.Api;

[Collection("database")]
public class SupplierEndpointTests : IClassFixture<SlingKingApiFactory>, IAsyncLifetime
{
    private readonly SlingKingApiFactory _factory;
    private readonly HttpClient _client;

    public SupplierEndpointTests(SlingKingApiFactory factory)
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
    private record SupplierOption(int Id, string Name, string? Phone);
    private record SupplierRequest(string Name, string? Phone);

    private async Task AuthenticateAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest(SlingKingApiFactory.SeedEmail, SlingKingApiFactory.SeedPassword));

        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", body!.AccessToken);
    }

    private static string UniqueName() => $"Sup {Guid.NewGuid():N}"[..14];

    [Fact]
    public async Task Listing_suppliers_requires_a_token()
    {
        using var anonymous = _factory.CreateClient();

        (await anonymous.GetAsync("/api/v1/suppliers")).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_created_supplier_appears_in_the_list()
    {
        var name = UniqueName();

        var created = await _client.PostAsJsonAsync(
            "/api/v1/suppliers", new SupplierRequest(name, "98xxxxxx45"));
        created.StatusCode.ShouldBe(HttpStatusCode.Created);

        var option = await created.Content.ReadFromJsonAsync<SupplierOption>();
        option!.Name.ShouldBe(name);
        option.Phone.ShouldBe("98xxxxxx45");
        option.Id.ShouldBeGreaterThan(0);

        var list = await _client.GetFromJsonAsync<List<SupplierOption>>("/api/v1/suppliers");
        list!.ShouldContain(s => s.Id == option.Id && s.Name == name);
    }

    [Fact]
    public async Task A_supplier_may_have_no_phone_number()
    {
        var created = await _client.PostAsJsonAsync(
            "/api/v1/suppliers", new SupplierRequest(UniqueName(), null));

        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await created.Content.ReadFromJsonAsync<SupplierOption>())!.Phone.ShouldBeNull();
    }

    [Fact]
    public async Task A_duplicate_name_is_rejected_as_a_conflict()
    {
        var name = UniqueName();

        await _client.PostAsJsonAsync("/api/v1/suppliers", new SupplierRequest(name, null));
        var again = await _client.PostAsJsonAsync(
            "/api/v1/suppliers", new SupplierRequest(name, null));

        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var problem = await again.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        problem!["title"].ToString().ShouldBe($"A supplier named \"{name}\" already exists.");
    }

    [Fact]
    public async Task A_blank_name_is_rejected()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/v1/suppliers", new SupplierRequest("   ", null));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_supplier_can_be_renamed_and_keeps_its_id()
    {
        var created = await _client.PostAsJsonAsync(
            "/api/v1/suppliers", new SupplierRequest(UniqueName(), "1111"));
        var option = await created.Content.ReadFromJsonAsync<SupplierOption>();

        var newName = UniqueName();
        var renamed = await _client.PutAsJsonAsync(
            $"/api/v1/suppliers/{option!.Id}", new SupplierRequest(newName, "2222"));

        renamed.StatusCode.ShouldBe(HttpStatusCode.OK);

        var updated = await renamed.Content.ReadFromJsonAsync<SupplierOption>();
        updated!.Id.ShouldBe(option.Id);
        updated.Name.ShouldBe(newName);
        updated.Phone.ShouldBe("2222");
    }

    [Fact]
    public async Task Correcting_only_the_capitalisation_of_a_name_is_allowed()
    {
        // The row being renamed must be excluded from the duplicate check, or fixing
        // capitalisation collides with its own existing name.
        var name = UniqueName();
        var created = await _client.PostAsJsonAsync(
            "/api/v1/suppliers", new SupplierRequest(name, null));
        var option = await created.Content.ReadFromJsonAsync<SupplierOption>();

        var response = await _client.PutAsJsonAsync(
            $"/api/v1/suppliers/{option!.Id}", new SupplierRequest(name.ToUpperInvariant(), null));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Renaming_to_a_name_another_supplier_holds_is_a_conflict()
    {
        var first = UniqueName();
        await _client.PostAsJsonAsync("/api/v1/suppliers", new SupplierRequest(first, null));

        var created = await _client.PostAsJsonAsync(
            "/api/v1/suppliers", new SupplierRequest(UniqueName(), null));
        var second = await created.Content.ReadFromJsonAsync<SupplierOption>();

        var response = await _client.PutAsJsonAsync(
            $"/api/v1/suppliers/{second!.Id}", new SupplierRequest(first, null));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Renaming_an_unknown_supplier_is_not_found()
    {
        var response = await _client.PutAsJsonAsync(
            "/api/v1/suppliers/999999", new SupplierRequest(UniqueName(), null));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // A missing route is also a 404, so assert the authored body — otherwise this test
        // passes just as well with no endpoint at all.
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");

        var problem = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        problem!["title"].ToString().ShouldBe("That supplier no longer exists.");
    }
}
```

- [x] **Step 2: Run the tests to verify they fail**

Run: `cd backend; dotnet test tests/SlingKing.IntegrationTests --filter FullyQualifiedName~SupplierEndpointTests`

Expected: compile failure or 404s — there is no endpoint yet.

- [x] **Step 3: Add the option model**

Append to `backend/src/SlingKing.Application/Purchasing/PurchaseModels.cs`:

```csharp
public record SupplierOption(int Id, string Name, string? Phone);
```

- [x] **Step 4: Write the interface**

Create `backend/src/SlingKing.Application/Purchasing/ISupplierService.cs`:

```csharp
using SlingKing.Application.Common;

namespace SlingKing.Application.Purchasing;

public interface ISupplierService
{
    Task<IReadOnlyList<SupplierOption>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Throws <see cref="AppException"/> with 409 if the name is already taken.</summary>
    Task<SupplierOption> CreateAsync(
        string name, string? phone, CancellationToken cancellationToken = default);

    /// <summary>
    /// Corrects a supplier's name or phone. Throws 404 if no supplier has that id, and 409 if
    /// ANOTHER supplier already holds the name — the row being edited is excluded from that
    /// check, so fixing capitalisation does not collide with itself.
    /// </summary>
    Task<SupplierOption> UpdateAsync(
        int id, string name, string? phone, CancellationToken cancellationToken = default);
}
```

- [x] **Step 5: Write the service**

Create `backend/src/SlingKing.Application/Purchasing/SupplierService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SlingKing.Application.Common;
using SlingKing.Application.Common.Abstractions;
using SlingKing.Domain.Purchasing;

namespace SlingKing.Application.Purchasing;

public sealed class SupplierService : ISupplierService
{
    private readonly IAppDbContext _db;
    private readonly IClock _clock;

    public SupplierService(IAppDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    private static string? Normalise(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public async Task<IReadOnlyList<SupplierOption>> ListAsync(
        CancellationToken cancellationToken = default) =>
        await _db.Suppliers
            .Where(s => s.IsActive)
            .OrderBy(s => s.Name)
            .Select(s => new SupplierOption(s.Id, s.Name, s.Phone))
            .ToListAsync(cancellationToken);

    public async Task<SupplierOption> CreateAsync(
        string name, string? phone, CancellationToken cancellationToken = default)
    {
        var trimmed = name.Trim();

        var taken = await _db.Suppliers
            .AnyAsync(s => s.Name.ToLower() == trimmed.ToLower(), cancellationToken);

        if (taken)
        {
            throw AppException.Conflict($"A supplier named \"{trimmed}\" already exists.");
        }

        var now = _clock.UtcNow;

        var supplier = new Supplier
        {
            Name = trimmed,
            Phone = Normalise(phone),
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        };

        _db.Suppliers.Add(supplier);
        await _db.SaveChangesAsync(cancellationToken);

        return new SupplierOption(supplier.Id, supplier.Name, supplier.Phone);
    }

    public async Task<SupplierOption> UpdateAsync(
        int id, string name, string? phone, CancellationToken cancellationToken = default)
    {
        var supplier = await _db.Suppliers
            .SingleOrDefaultAsync(s => s.Id == id, cancellationToken)
            ?? throw AppException.NotFound("That supplier no longer exists.");

        var trimmed = name.Trim();

        // s.Id != id is load-bearing. Correcting capitalisation is the likeliest reason to
        // edit a supplier at all, and without excluding the row being edited it collides with
        // its own existing name and is refused.
        var taken = await _db.Suppliers
            .AnyAsync(s => s.Id != id && s.Name.ToLower() == trimmed.ToLower(), cancellationToken);

        if (taken)
        {
            throw AppException.Conflict($"A supplier named \"{trimmed}\" already exists.");
        }

        supplier.Name = trimmed;
        supplier.Phone = Normalise(phone);
        supplier.UpdatedAt = _clock.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);

        return new SupplierOption(supplier.Id, supplier.Name, supplier.Phone);
    }
}
```

- [x] **Step 6: Write the request contract**

Create `backend/src/SlingKing.Api/Contracts/Purchasing/PurchasingContracts.cs`:

```csharp
using System.ComponentModel.DataAnnotations;

namespace SlingKing.Api.Contracts.Purchasing;

// Attributes bind to the primary-constructor parameters. A [property:] target compiles and
// then throws at request time — see Contracts/Auth/AuthContracts.cs. Do not "fix" it.
public record SupplierRequest(
    [Required, MinLength(1), MaxLength(256)] string Name,
    [MaxLength(32)] string? Phone);
```

- [x] **Step 7: Write the controller**

Create `backend/src/SlingKing.Api/Controllers/SuppliersController.cs`:

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SlingKing.Api.Contracts.Purchasing;
using SlingKing.Application.Purchasing;

namespace SlingKing.Api.Controllers;

[ApiController]
[Route("api/v1/suppliers")]
[Authorize]
public class SuppliersController : ControllerBase
{
    private readonly ISupplierService _suppliers;

    public SuppliersController(ISupplierService suppliers) => _suppliers = suppliers;

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<SupplierOption>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken cancellationToken) =>
        Ok(await _suppliers.ListAsync(cancellationToken));

    [HttpPost]
    [ProducesResponseType(typeof(SupplierOption), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        SupplierRequest request, CancellationToken cancellationToken)
    {
        // MinLength(1) accepts "   ", which would create a supplier with a blank name.
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            ModelState.AddModelError(nameof(request.Name), "The Name field is required.");
            return ValidationProblem(ModelState);
        }

        var created = await _suppliers.CreateAsync(
            request.Name, request.Phone, cancellationToken);

        return CreatedAtAction(nameof(List), new { id = created.Id }, created);
    }

    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(SupplierOption), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(
        int id, SupplierRequest request, CancellationToken cancellationToken)
    {
        // As with Create: MinLength(1) accepts "   ", which would blank the name.
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            ModelState.AddModelError(nameof(request.Name), "The Name field is required.");
            return ValidationProblem(ModelState);
        }

        return Ok(await _suppliers.UpdateAsync(
            id, request.Name, request.Phone, cancellationToken));
    }
}
```

- [x] **Step 8: Register the service**

In `backend/src/SlingKing.Infrastructure/DependencyInjection.cs`, immediately after the `services.AddScoped<IStockService, StockService>();` line, add:

```csharp
        services.AddScoped<ISupplierService, SupplierService>();
```

Add `using SlingKing.Application.Purchasing;` to the file's usings.

- [x] **Step 9: Run the tests to verify they pass**

Run: `cd backend; dotnet test tests/SlingKing.IntegrationTests --filter FullyQualifiedName~SupplierEndpointTests`

Expected: 9 passed.

- [x] **Step 10: Prove the self-collision guard is load-bearing**

Temporarily delete `s.Id != id &&` from `UpdateAsync`'s duplicate check and re-run.

Expected: `Correcting_only_the_capitalisation_of_a_name_is_allowed` and `A_supplier_can_be_renamed_and_keeps_its_id` FAIL with 409 — the row collides with its own name.

**Then restore it and re-run — 9 passed.**

- [x] **Step 11: Commit**

```bash
git add backend/src backend/tests
git commit -m "feat: suppliers, list create and rename

The category endpoints with a different noun, deliberately: the picker in the
purchase form is the category picker with a different noun too.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 4: Record a purchase end to end

The write path. `PurchaseService` owns the transaction and the purchase rows; `StockService` gains one method and keeps its monopoly on stock. Cancellation is Task 6 — this task ships recording only.

**Files:**
- Modify: `backend/src/SlingKing.Application/Inventory/InventoryModels.cs`
- Modify: `backend/src/SlingKing.Application/Inventory/IStockService.cs`
- Modify: `backend/src/SlingKing.Application/Inventory/StockService.cs`
- Modify: `backend/src/SlingKing.Application/Purchasing/PurchaseModels.cs`
- Create: `backend/src/SlingKing.Application/Purchasing/IPurchaseService.cs`
- Create: `backend/src/SlingKing.Application/Purchasing/PurchaseService.cs`
- Modify: `backend/src/SlingKing.Api/Contracts/Purchasing/PurchasingContracts.cs`
- Create: `backend/src/SlingKing.Api/Controllers/PurchasesController.cs`
- Modify: `backend/src/SlingKing.Infrastructure/DependencyInjection.cs`
- Test: `backend/tests/SlingKing.IntegrationTests/Api/PurchaseEndpointTests.cs`

**Interfaces:**
- Consumes: `PurchaseTotals.{LineTotal,Total,RejectDuplicateVariants}` and `PurchaseLineInput` (Task 2); `VariantRules.IsReachable` and the `SourceOf` purchase case (Task 1); `ISupplierService` (Task 3); `INumberSequenceAllocator.NextAsync("purchase")`.
- Produces:
  - `sealed record MovementContext(InventoryReferenceType ReferenceType, long ReferenceId, long? SourceId = null, long? TargetLayerId = null)`
  - `record PurchaseStockLine(long PurchaseItemId, long VariantId, int Quantity, decimal? UnitCost)`
  - `Task IStockService.ApplyPurchaseAsync(long purchaseId, IReadOnlyList<PurchaseStockLine> lines, CancellationToken)`
  - `record PurchaseInput(int SupplierId, DateTimeOffset PurchaseDate, string? Notes, IReadOnlyList<PurchaseLineInput> Lines)`
  - `record PurchaseLineView(long Id, long VariantId, string Sku, string ProductName, string? Colour, string? Size, int Quantity, decimal? UnitCost, decimal? LineTotal)`
  - `record PurchaseView(long Id, string Number, int SupplierId, string SupplierName, DateTimeOffset PurchaseDate, PurchaseStatus Status, decimal? TotalAmount, string? Notes, IReadOnlyList<PurchaseLineView> Lines)`
  - `Task<PurchaseView> IPurchaseService.RecordAsync(PurchaseInput, CancellationToken)`
  - `POST /api/v1/purchases`

- [x] **Step 1: Write the failing tests**

Create `backend/tests/SlingKing.IntegrationTests/Api/PurchaseEndpointTests.cs`. Later tasks add to this file; this step establishes it with its helpers.

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SlingKing.Domain.Common;
using SlingKing.Infrastructure.Persistence;
using SlingKing.Infrastructure.Persistence.Seed;
using SlingKing.IntegrationTests.Fixtures;
using Xunit;

namespace SlingKing.IntegrationTests.Api;

[Collection("database")]
public class PurchaseEndpointTests : IClassFixture<SlingKingApiFactory>, IAsyncLifetime
{
    private readonly SlingKingApiFactory _factory;
    private readonly HttpClient _client;

    public PurchaseEndpointTests(SlingKingApiFactory factory)
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

    private record SupplierOption(int Id, string Name, string? Phone);
    private record SupplierRequest(string Name, string? Phone);

    private record VariantRequest(
        long? Id, string? Colour, string? Size, decimal SellingPrice,
        int LowStockThreshold, bool IsActive);
    private record ProductRequest(
        string Name, int? CategoryId, string? Description, List<VariantRequest> Variants);
    private record VariantView(long Id, string Sku);
    private record ProductView(long Id, string Code, List<VariantView> Variants);

    private record PurchaseLineRequest(long VariantId, int Quantity, decimal? UnitCost);
    private record PurchaseRequest(
        int SupplierId, DateTimeOffset PurchaseDate, string? Notes,
        List<PurchaseLineRequest> Lines);

    private record PurchaseLineView(
        long Id, long VariantId, string Sku, string ProductName, string? Colour, string? Size,
        int Quantity, decimal? UnitCost, decimal? LineTotal);
    private record PurchaseView(
        long Id, string Number, int SupplierId, string SupplierName,
        DateTimeOffset PurchaseDate, string Status, decimal? TotalAmount, string? Notes,
        List<PurchaseLineView> Lines);

    private record MovementRequest(
        long VariantId, string Type, int Quantity, decimal? UnitCost, string? Reason);

    private async Task AuthenticateAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest(SlingKingApiFactory.SeedEmail, SlingKingApiFactory.SeedPassword));

        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", body!.AccessToken);
    }

    private async Task<int> NewSupplierAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/suppliers",
            new SupplierRequest($"Sup {Guid.NewGuid():N}"[..14], null));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<SupplierOption>())!.Id;
    }

    /// <summary>
    /// A fresh product with <paramref name="colours"/> variants, so every test starts from
    /// stock nothing else has touched. Sharing variants across tests would make the running
    /// totals depend on test order.
    /// </summary>
    private async Task<(long ProductId, List<long> VariantIds)> NewProductAsync(
        params string[] colours)
    {
        var variants = colours
            .Select(c => new VariantRequest(null, c, "Standard", 4500m, 5, true))
            .ToList();

        var response = await _client.PostAsJsonAsync("/api/v1/products", new ProductRequest(
            $"Buy {Guid.NewGuid():N}"[..14], null, null, variants));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        var product = await response.Content.ReadFromJsonAsync<ProductView>();
        return (product!.Id, product.Variants.Select(v => v.Id).ToList());
    }

    private async Task<T> QueryAsync<T>(Func<AppDbContext, Task<T>> read)
    {
        using var scope = _factory.Services.CreateScope();
        return await read(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private static DateTimeOffset Today => DateTimeOffset.UtcNow;

    [Fact]
    public async Task Recording_a_purchase_requires_a_token()
    {
        using var anonymous = _factory.CreateClient();

        var response = await anonymous.PostAsJsonAsync("/api/v1/purchases",
            new PurchaseRequest(1, Today, null, [new PurchaseLineRequest(1, 1, null)]));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_purchase_raises_stock_and_opens_one_layer_per_line()
    {
        var supplierId = await NewSupplierAsync();
        var (_, variantIds) = await NewProductAsync("Tan", "Black");

        var response = await _client.PostAsJsonAsync("/api/v1/purchases",
            new PurchaseRequest(supplierId, Today, "Karol Bagh trip",
            [
                new PurchaseLineRequest(variantIds[0], 12, 450m),
                new PurchaseLineRequest(variantIds[1], 8, 480m)
            ]));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        var purchase = await response.Content.ReadFromJsonAsync<PurchaseView>();
        purchase!.Number.ShouldStartWith("PUR-");
        purchase.Status.ShouldBe("Completed");
        purchase.TotalAmount.ShouldBe(9240m);
        purchase.Lines.Count.ShouldBe(2);
        purchase.Lines.Single(l => l.VariantId == variantIds[0]).LineTotal.ShouldBe(5400m);

        var levels = await QueryAsync(db => db.StockLevels
            .Where(s => variantIds.Contains(s.VariantId))
            .OrderBy(s => s.VariantId)
            .ToListAsync());

        levels.Select(l => l.PhysicalQty).ShouldBe([12, 8]);

        var layers = await QueryAsync(db => db.InventoryCostLayers
            .Where(l => variantIds.Contains(l.VariantId))
            .OrderBy(l => l.VariantId)
            .ToListAsync());

        layers.Count.ShouldBe(2);
        layers.ShouldAllBe(l => l.SourceType == CostLayerSourceType.Purchase);
        layers.Select(l => l.UnitCost).ShouldBe([450m, 480m]);
        layers.ShouldAllBe(l => l.RemainingQty == l.OriginalQty);
    }

    [Fact]
    public async Task Every_ledger_row_points_back_at_the_purchase()
    {
        var supplierId = await NewSupplierAsync();
        var (_, variantIds) = await NewProductAsync("Tan");

        var response = await _client.PostAsJsonAsync("/api/v1/purchases",
            new PurchaseRequest(supplierId, Today, null,
                [new PurchaseLineRequest(variantIds[0], 5, 100m)]));

        var purchase = await response.Content.ReadFromJsonAsync<PurchaseView>();

        var ledger = await QueryAsync(db => db.InventoryTransactions
            .SingleAsync(t => t.VariantId == variantIds[0]));

        ledger.Type.ShouldBe(InventoryTransactionType.Purchase);
        ledger.PhysicalDelta.ShouldBe(5);
        ledger.UnitCost.ShouldBe(100m);
        ledger.TotalCost.ShouldBe(500m);

        // Without these an item's history cannot explain where the bags came from.
        ledger.ReferenceType.ShouldBe(InventoryReferenceType.Purchase);
        ledger.ReferenceId.ShouldBe(purchase!.Id);
    }

    [Fact]
    public async Task A_layer_records_which_purchase_line_made_it()
    {
        var supplierId = await NewSupplierAsync();
        var (_, variantIds) = await NewProductAsync("Tan");

        var response = await _client.PostAsJsonAsync("/api/v1/purchases",
            new PurchaseRequest(supplierId, Today, null,
                [new PurchaseLineRequest(variantIds[0], 5, 100m)]));

        var purchase = await response.Content.ReadFromJsonAsync<PurchaseView>();
        var lineId = purchase!.Lines.Single().Id;

        var layer = await QueryAsync(db => db.InventoryCostLayers
            .SingleAsync(l => l.VariantId == variantIds[0]));

        // Cancellation finds its own layers through this. Pointing at the ledger row instead
        // would work too, but a layer that names its purchase line is self-describing.
        layer.SourceId.ShouldBe(lineId);
    }

    [Fact]
    public async Task Two_purchases_take_consecutive_numbers()
    {
        var supplierId = await NewSupplierAsync();
        var (_, variantIds) = await NewProductAsync("Tan");

        var first = await _client.PostAsJsonAsync("/api/v1/purchases",
            new PurchaseRequest(supplierId, Today, null,
                [new PurchaseLineRequest(variantIds[0], 1, 10m)]));
        var second = await _client.PostAsJsonAsync("/api/v1/purchases",
            new PurchaseRequest(supplierId, Today, null,
                [new PurchaseLineRequest(variantIds[0], 1, 10m)]));

        var a = await first.Content.ReadFromJsonAsync<PurchaseView>();
        var b = await second.Content.ReadFromJsonAsync<PurchaseView>();

        var firstNumber = int.Parse(a!.Number["PUR-".Length..]);
        var secondNumber = int.Parse(b!.Number["PUR-".Length..]);

        secondNumber.ShouldBe(firstNumber + 1);
    }

    [Fact]
    public async Task An_uncosted_line_makes_the_whole_purchase_total_unknown()
    {
        var supplierId = await NewSupplierAsync();
        var (_, variantIds) = await NewProductAsync("Tan", "Black");

        var response = await _client.PostAsJsonAsync("/api/v1/purchases",
            new PurchaseRequest(supplierId, Today, null,
            [
                new PurchaseLineRequest(variantIds[0], 12, 450m),
                new PurchaseLineRequest(variantIds[1], 8, null)
            ]));

        var purchase = await response.Content.ReadFromJsonAsync<PurchaseView>();

        // 5400 is the sum of the line we know. Reporting it would understate what was paid.
        purchase!.TotalAmount.ShouldBeNull();
        purchase.Lines.Single(l => l.VariantId == variantIds[1]).LineTotal.ShouldBeNull();

        var uncosted = await QueryAsync(db => db.InventoryCostLayers
            .SingleAsync(l => l.VariantId == variantIds[1]));

        uncosted.UnitCost.ShouldBeNull();
    }

    [Fact]
    public async Task The_same_variant_twice_is_refused_by_name()
    {
        var supplierId = await NewSupplierAsync();
        var (_, variantIds) = await NewProductAsync("Tan");

        var response = await _client.PostAsJsonAsync("/api/v1/purchases",
            new PurchaseRequest(supplierId, Today, null,
            [
                new PurchaseLineRequest(variantIds[0], 12, 450m),
                new PurchaseLineRequest(variantIds[0], 3, 450m)
            ]));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var problem = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        problem!["title"].ToString()
            .ShouldBe("Tan appears twice. Put the whole quantity on one line.");
    }

    [Fact]
    public async Task A_purchase_with_no_lines_is_refused()
    {
        var supplierId = await NewSupplierAsync();

        var response = await _client.PostAsJsonAsync("/api/v1/purchases",
            new PurchaseRequest(supplierId, Today, null, []));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task An_unknown_supplier_is_not_found()
    {
        var (_, variantIds) = await NewProductAsync("Tan");

        var response = await _client.PostAsJsonAsync("/api/v1/purchases",
            new PurchaseRequest(999_999, Today, null,
                [new PurchaseLineRequest(variantIds[0], 1, 10m)]));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");

        var problem = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        problem!["title"].ToString().ShouldBe("That supplier no longer exists.");
    }

    [Fact]
    public async Task A_purchase_against_a_retired_product_is_not_found()
    {
        var supplierId = await NewSupplierAsync();
        var (productId, variantIds) = await NewProductAsync("Tan");

        (await _client.PostAsync($"/api/v1/products/{productId}/deactivate", null))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var response = await _client.PostAsJsonAsync("/api/v1/purchases",
            new PurchaseRequest(supplierId, Today, null,
                [new PurchaseLineRequest(variantIds[0], 1, 10m)]));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var problem = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        problem!["title"].ToString().ShouldBe("That item no longer exists.");
    }

    [Fact]
    public async Task Nothing_is_written_when_a_purchase_is_refused()
    {
        var supplierId = await NewSupplierAsync();
        var (_, variantIds) = await NewProductAsync("Tan");

        var before = await QueryAsync(db => db.Purchases.CountAsync());

        await _client.PostAsJsonAsync("/api/v1/purchases",
            new PurchaseRequest(supplierId, Today, null,
            [
                new PurchaseLineRequest(variantIds[0], 12, 450m),
                new PurchaseLineRequest(variantIds[0], 3, 450m)
            ]));

        (await QueryAsync(db => db.Purchases.CountAsync())).ShouldBe(before);

        var level = await QueryAsync(db => db.StockLevels
            .SingleAsync(s => s.VariantId == variantIds[0]));
        level.PhysicalQty.ShouldBe(0);
    }

    [Fact]
    public async Task Back_dating_a_purchase_does_not_move_it_ahead_in_the_cost_queue()
    {
        var supplierId = await NewSupplierAsync();
        var (_, variantIds) = await NewProductAsync("Tan");

        // Stock already on hand at a known cost.
        var opening = await _client.PostAsJsonAsync("/api/v1/inventory/movements",
            new MovementRequest(variantIds[0], "OpeningStock", 4, 100m, null));
        opening.StatusCode.ShouldBe(HttpStatusCode.Created);

        // A purchase dated a month ago, recorded now.
        var purchase = await _client.PostAsJsonAsync("/api/v1/purchases",
            new PurchaseRequest(supplierId, DateTimeOffset.UtcNow.AddMonths(-1), null,
                [new PurchaseLineRequest(variantIds[0], 4, 900m)]));
        purchase.StatusCode.ShouldBe(HttpStatusCode.Created);

        // FIFO must still take the opening stock first. If received_at had followed the
        // purchase date, this damage would cost 900 apiece and every cost already recorded
        // against this variant would silently have become wrong.
        var damage = await _client.PostAsJsonAsync("/api/v1/inventory/movements",
            new MovementRequest(variantIds[0], "Damage", 4, null, "Water damaged in transit"));

        damage.StatusCode.ShouldBe(HttpStatusCode.Created);

        var ledger = await QueryAsync(db => db.InventoryTransactions
            .Where(t => t.VariantId == variantIds[0]
                     && t.Type == InventoryTransactionType.Damage)
            .SingleAsync());

        ledger.TotalCost.ShouldBe(400m);
    }
}
```

- [x] **Step 2: Run the tests to verify they fail**

Run: `cd backend; dotnet test tests/SlingKing.IntegrationTests --filter FullyQualifiedName~PurchaseEndpointTests`

Expected: compile failure or 404s — there is no purchases endpoint yet.

- [x] **Step 3: Add the movement context**

Append to `backend/src/SlingKing.Application/Inventory/InventoryModels.cs`:

```csharp
/// <summary>
/// What caused a movement, for the paths that have a cause worth recording. Purchases set it
/// so an item's history can be traced back to what brought the bags in; the manual movement
/// endpoint passes null, because "you typed it" is not a reference.
/// </summary>
/// <param name="SourceId">
/// Overrides the cost layer's source id, which otherwise points at the ledger row. A purchase
/// points its layer at the purchase LINE, so a layer names the thing that bought it.
/// </param>
/// <param name="TargetLayerId">
/// Consume this exact layer rather than walking FIFO. Only a purchase cancellation sets it:
/// it must take back its own bags, and the oldest layer is usually somebody else's.
/// </param>
public sealed record MovementContext(
    InventoryReferenceType ReferenceType,
    long ReferenceId,
    long? SourceId = null,
    long? TargetLayerId = null);

/// <summary>One line of a purchase, as StockService needs it: enough to move stock, no more.</summary>
public record PurchaseStockLine(
    long PurchaseItemId, long VariantId, int Quantity, decimal? UnitCost);
```

- [x] **Step 4: Thread the context through `AppendAsync`**

In `backend/src/SlingKing.Application/Inventory/StockService.cs`, change `AppendAsync`'s signature to take a context immediately before the cancellation token:

```csharp
    private async Task<MovementView> AppendAsync(
        StockLevel level,
        InventoryTransactionType type,
        int signedQuantity,
        decimal? unitCost,
        string? reason,
        bool allowShortfall,
        MovementContext? context,
        CancellationToken cancellationToken)
```

Inside it, add the two reference columns to the ledger row initialiser:

```csharp
            ReferenceType = context?.ReferenceType,
            ReferenceId = context?.ReferenceId,
```

and change the layer's source id so a caller can override it:

```csharp
            _db.InventoryCostLayers.Add(FifoCostEngine.AddLayer(
                level.VariantId, level.LocationId, SourceOf(type),
                context?.SourceId ?? ledgerRow.Id,
                signedQuantity, unitCost, now));
```

Both existing callers — `RecordMovementAsync` and `RecountAsync` — pass `context: null`. Add that argument to both; do not change anything else about them.

- [x] **Step 5: Add `ApplyPurchaseAsync` to the interface**

Add to `backend/src/SlingKing.Application/Inventory/IStockService.cs`:

```csharp
    /// <summary>
    /// Raises stock for every line of a purchase, appending one ledger row and opening one
    /// cost layer each.
    /// </summary>
    /// <remarks>
    /// Assumes the caller has an open transaction and commits nothing itself, so a purchase
    /// and the stock it moves land or fail together. This is the ONLY way purchases reach the
    /// ledger: nothing outside StockService may write physical_qty.
    /// </remarks>
    Task ApplyPurchaseAsync(
        long purchaseId,
        IReadOnlyList<PurchaseStockLine> lines,
        CancellationToken cancellationToken = default);
```

- [x] **Step 6: Implement it**

Add to `StockService`, after `RecountAsync`:

```csharp
    public async Task ApplyPurchaseAsync(
        long purchaseId,
        IReadOnlyList<PurchaseStockLine> lines,
        CancellationToken cancellationToken = default)
    {
        var locationId = await DefaultLocationIdAsync(cancellationToken);

        // Ascending variant id, ALWAYS. A multi-line purchase locks several stock rows, and
        // two purchases touching the same variants in opposite orders deadlock in Postgres —
        // one waits on a row the other holds, in both directions. The ordering is the whole
        // defence, and nothing about the code looks wrong when it is missing.
        foreach (var line in lines.OrderBy(l => l.VariantId))
        {
            var level = await LockLevelAsync(line.VariantId, locationId, cancellationToken);

            await AppendAsync(
                level, InventoryTransactionType.Purchase, line.Quantity, line.UnitCost,
                reason: null, allowShortfall: false,
                new MovementContext(
                    InventoryReferenceType.Purchase, purchaseId, SourceId: line.PurchaseItemId),
                cancellationToken);
        }
    }
```

- [x] **Step 7: Add the purchase models**

Append to `backend/src/SlingKing.Application/Purchasing/PurchaseModels.cs`:

```csharp
public record PurchaseInput(
    int SupplierId,
    DateTimeOffset PurchaseDate,
    string? Notes,
    IReadOnlyList<PurchaseLineInput> Lines);

public record PurchaseLineView(
    long Id,
    long VariantId,
    string Sku,
    string ProductName,
    string? Colour,
    string? Size,
    int Quantity,
    decimal? UnitCost,
    decimal? LineTotal);

/// <summary>
/// <c>TotalAmount</c> is null when ANY line has an unknown cost — never a partial sum.
/// </summary>
public record PurchaseView(
    long Id,
    string Number,
    int SupplierId,
    string SupplierName,
    DateTimeOffset PurchaseDate,
    PurchaseStatus Status,
    decimal? TotalAmount,
    string? Notes,
    IReadOnlyList<PurchaseLineView> Lines);
```

Add `using SlingKing.Domain.Common;` at the top of the file for `PurchaseStatus`.

- [x] **Step 8: Write the service interface**

Create `backend/src/SlingKing.Application/Purchasing/IPurchaseService.cs`:

```csharp
using SlingKing.Application.Common;

namespace SlingKing.Application.Purchasing;

public interface IPurchaseService
{
    /// <summary>
    /// Records a purchase, already complete, and raises the stock it brought in — all in one
    /// transaction. There is no draft: the owner buys at the market and carries the bags home,
    /// so the purchase and the arrival are one event.
    /// </summary>
    /// <exception cref="AppException">
    /// 400 for no lines, a quantity below one, a negative cost, or the same variant twice.
    /// 404 for an unknown supplier or a retired variant.
    /// </exception>
    Task<PurchaseView> RecordAsync(
        PurchaseInput input, CancellationToken cancellationToken = default);
}
```

- [x] **Step 9: Write the service**

Create `backend/src/SlingKing.Application/Purchasing/PurchaseService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SlingKing.Application.Catalog;
using SlingKing.Application.Common;
using SlingKing.Application.Common.Abstractions;
using SlingKing.Application.Inventory;
using SlingKing.Domain.Common;
using SlingKing.Domain.Purchasing;

namespace SlingKing.Application.Purchasing;

public sealed class PurchaseService : IPurchaseService
{
    private readonly IAppDbContext _db;
    private readonly IStockService _stock;
    private readonly INumberSequenceAllocator _sequences;
    private readonly IClock _clock;

    public PurchaseService(
        IAppDbContext db,
        IStockService stock,
        INumberSequenceAllocator sequences,
        IClock clock)
    {
        _db = db;
        _stock = stock;
        _sequences = sequences;
        _clock = clock;
    }

    /// <summary>
    /// Resolves every line's variant and returns a label per id for readable refusals. Throws
    /// 404 if any line names a variant that does not exist or whose product is retired.
    /// </summary>
    private async Task<Dictionary<long, string>> ResolveVariantLabelsAsync(
        IReadOnlyList<PurchaseLineInput> lines, CancellationToken cancellationToken)
    {
        var ids = lines.Select(l => l.VariantId).Distinct().ToList();

        var found = await _db.ProductVariants
            .AsNoTracking()
            .Where(VariantRules.IsReachable)
            .Where(v => ids.Contains(v.Id))
            .Select(v => new { v.Id, v.Colour, v.Size, v.Sku })
            .ToListAsync(cancellationToken);

        if (found.Count != ids.Count)
        {
            throw AppException.NotFound("That item no longer exists.");
        }

        return found.ToDictionary(
            v => v.Id,
            v => string.Join(" · ", new[] { v.Colour, v.Size }.Where(s => s is not null))
                is { Length: > 0 } label ? label : v.Sku);
    }

    public async Task<PurchaseView> RecordAsync(
        PurchaseInput input, CancellationToken cancellationToken = default)
    {
        if (input.Lines.Count == 0)
        {
            throw AppException.Invalid("Add at least one bag to this purchase.");
        }

        foreach (var line in input.Lines)
        {
            if (line.Quantity <= 0)
            {
                throw AppException.Invalid("Enter how many, as a number above zero.");
            }

            if (line.UnitCost < 0)
            {
                throw AppException.Invalid("A cost cannot be negative.");
            }
        }

        var supplierExists = await _db.Suppliers
            .AnyAsync(s => s.Id == input.SupplierId && s.IsActive, cancellationToken);

        if (!supplierExists)
        {
            throw AppException.NotFound("That supplier no longer exists.");
        }

        var labels = await ResolveVariantLabelsAsync(input.Lines, cancellationToken);
        PurchaseTotals.RejectDuplicateVariants(input.Lines, labels);

        var now = _clock.UtcNow;

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        // Inside the transaction on purpose: the allocator's UPDATE rolls back with everything
        // else, so a failed purchase returns its number to the sequence.
        var number = await _sequences.NextAsync("purchase", cancellationToken);

        var purchase = new Purchase
        {
            Number = number,
            SupplierId = input.SupplierId,
            // The owner's record of when she bought. It may be back-dated; the cost layers are
            // still stamped now, so a back-dated purchase never reorders the FIFO queue and
            // never rewrites a cost that has already been recorded.
            PurchaseDate = input.PurchaseDate,
            Status = PurchaseStatus.Completed,
            TotalAmount = PurchaseTotals.Total(input.Lines),
            Notes = string.IsNullOrWhiteSpace(input.Notes) ? null : input.Notes.Trim(),
            CreatedAt = now,
            CompletedAt = now,
            UpdatedAt = now
        };

        _db.Purchases.Add(purchase);

        var items = input.Lines
            .Select(l => new PurchaseItem
            {
                Purchase = purchase,
                VariantId = l.VariantId,
                Quantity = l.Quantity,
                UnitCost = l.UnitCost,
                LineTotal = PurchaseTotals.LineTotal(l.Quantity, l.UnitCost)
            })
            .ToList();

        _db.PurchaseItems.AddRange(items);

        // The lines need ids before their layers can point at them.
        await _db.SaveChangesAsync(cancellationToken);

        await _stock.ApplyPurchaseAsync(
            purchase.Id,
            items.Select(i => new PurchaseStockLine(
                i.Id, i.VariantId, i.Quantity, i.UnitCost)).ToList(),
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return await ViewAsync(purchase.Id, cancellationToken);
    }

    private async Task<PurchaseView> ViewAsync(long id, CancellationToken cancellationToken)
    {
        var header = await _db.Purchases
            .AsNoTracking()
            .Where(p => p.Id == id)
            .Select(p => new
            {
                p.Id, p.Number, p.SupplierId, SupplierName = p.Supplier!.Name,
                p.PurchaseDate, p.Status, p.TotalAmount, p.Notes
            })
            .SingleAsync(cancellationToken);

        var lines = await _db.PurchaseItems
            .AsNoTracking()
            .Where(i => i.PurchaseId == id)
            .Join(_db.ProductVariants, i => i.VariantId, v => v.Id, (i, v) => new PurchaseLineView(
                i.Id, v.Id, v.Sku, v.Product!.Name, v.Colour, v.Size,
                i.Quantity, i.UnitCost, i.LineTotal))
            .OrderBy(l => l.Sku)
            .ToListAsync(cancellationToken);

        return new PurchaseView(
            header.Id, header.Number, header.SupplierId, header.SupplierName,
            header.PurchaseDate, header.Status, header.TotalAmount, header.Notes, lines);
    }
}
```

- [x] **Step 10: Add the request contracts**

Append to `backend/src/SlingKing.Api/Contracts/Purchasing/PurchasingContracts.cs`:

```csharp
public record PurchaseLineRequest(
    [Required] long? VariantId,
    [Required, Range(1, 1_000_000)] int? Quantity,
    [Range(0, 9_999_999)] decimal? UnitCost);

public record PurchaseRequest(
    [Required] int? SupplierId,
    [Required] DateTimeOffset? PurchaseDate,
    [MaxLength(1024)] string? Notes,
    List<PurchaseLineRequest>? Lines);
```

- [x] **Step 11: Write the controller**

Create `backend/src/SlingKing.Api/Controllers/PurchasesController.cs`:

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SlingKing.Api.Contracts.Purchasing;
using SlingKing.Application.Purchasing;

namespace SlingKing.Api.Controllers;

[ApiController]
[Route("api/v1/purchases")]
[Authorize]
public class PurchasesController : ControllerBase
{
    private readonly IPurchaseService _purchases;

    public PurchasesController(IPurchaseService purchases) => _purchases = purchases;

    private static PurchaseInput ToInput(PurchaseRequest request) => new(
        request.SupplierId!.Value,
        request.PurchaseDate!.Value,
        request.Notes,
        (request.Lines ?? [])
            .Select(l => new PurchaseLineInput(
                l.VariantId!.Value, l.Quantity!.Value, l.UnitCost))
            .ToList());

    [HttpPost]
    [ProducesResponseType(typeof(PurchaseView), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Record(
        PurchaseRequest request, CancellationToken cancellationToken)
    {
        var created = await _purchases.RecordAsync(ToInput(request), cancellationToken);

        // No GET for a single purchase — the list carries the lines — so this points at the
        // list the caller will actually go and look at.
        return Created("/api/v1/purchases", created);
    }
}
```

- [x] **Step 12: Register the service**

In `backend/src/SlingKing.Infrastructure/DependencyInjection.cs`, after the `ISupplierService` registration, add:

```csharp
        services.AddScoped<IPurchaseService, PurchaseService>();
```

- [x] **Step 13: Run the tests to verify they pass**

Run: `cd backend; dotnet test tests/SlingKing.IntegrationTests --filter FullyQualifiedName~PurchaseEndpointTests`

Expected: 12 passed.

- [x] **Step 14: Prove the back-dating rule is load-bearing**

Temporarily change `AppendAsync` to stamp the layer's `ReceivedAt` from the purchase date. The quickest way: in `ApplyPurchaseAsync`, there is no purchase date available — which is itself the point. To simulate the bug, temporarily change `FifoCostEngine.AddLayer`'s `ReceivedAt = now` to `ReceivedAt = now.AddMonths(-2)` and re-run.

Expected: `Back_dating_a_purchase_does_not_move_it_ahead_in_the_cost_queue` FAILS — the damage costs 3600 instead of 400, because the purchase's layer jumped the queue ahead of the opening stock.

**Then restore `ReceivedAt = now` and re-run — 12 passed.** This is the rule that keeps insertion order equal to FIFO order, and with it the guarantee that no recorded cost is ever retroactively wrong.

- [x] **Step 15: Run the whole backend suite**

Run: `cd backend; dotnet build -warnaserror; if ($?) { dotnet test }`

Expected: everything green, zero warnings. The two existing `AppendAsync` callers now pass `context: null`; nothing about M2b's behaviour should have moved.

- [x] **Step 16: Commit**

```bash
git add backend/src backend/tests
git commit -m "feat: record a purchase, raising stock and opening cost layers

One transaction: allocate the number, write the purchase and its lines, then
hand them to StockService, which stays the only writer of physical_qty. Locks
are taken in ascending variant id -- out of order, two purchases touching the
same variants deadlock.

A back-dated purchase does not reorder the FIFO queue: purchase_date is the
owner's record, received_at is when the system learned of it.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 5: The lock ordering, tested — with an honest caveat

The equivalent of M2b's row-lock test, and weaker by nature. Read the caveat in Step 3 before deciding what to report.

**Files:**
- Create: `backend/tests/SlingKing.IntegrationTests/Persistence/PurchaseConcurrencyTests.cs`

**Interfaces:**
- Consumes: `POST /api/v1/purchases` (Task 4), `POST /api/v1/suppliers` (Task 3), `SlingKingApiFactory`.
- Produces: nothing.

- [x] **Step 1: Write the test**

Create `backend/tests/SlingKing.IntegrationTests/Persistence/PurchaseConcurrencyTests.cs`:

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
/// Two purchases touching the same two variants in opposite line orders. Without a consistent
/// lock order each holds the row the other wants and Postgres kills one as a deadlock.
/// </summary>
[Collection("database")]
public class PurchaseConcurrencyTests : IClassFixture<SlingKingApiFactory>, IAsyncLifetime
{
    private readonly SlingKingApiFactory _factory;
    private readonly HttpClient _first;
    private readonly HttpClient _second;

    public PurchaseConcurrencyTests(SlingKingApiFactory factory)
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
    private record SupplierOption(int Id, string Name, string? Phone);
    private record SupplierRequest(string Name, string? Phone);
    private record VariantRequest(
        long? Id, string? Colour, string? Size, decimal SellingPrice,
        int LowStockThreshold, bool IsActive);
    private record ProductRequest(
        string Name, int? CategoryId, string? Description, List<VariantRequest> Variants);
    private record VariantView(long Id, string Sku);
    private record ProductView(long Id, string Code, List<VariantView> Variants);
    private record PurchaseLineRequest(long VariantId, int Quantity, decimal? UnitCost);
    private record PurchaseRequest(
        int SupplierId, DateTimeOffset PurchaseDate, string? Notes,
        List<PurchaseLineRequest> Lines);

    private async Task AuthenticateAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest(SlingKingApiFactory.SeedEmail, SlingKingApiFactory.SeedPassword));

        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", body!.AccessToken);
    }

    [Fact]
    public async Task Two_purchases_touching_the_same_variants_in_opposite_orders_both_complete()
    {
        var supplierResponse = await _first.PostAsJsonAsync("/api/v1/suppliers",
            new SupplierRequest($"Sup {Guid.NewGuid():N}"[..14], null));
        var supplierId = (await supplierResponse.Content
            .ReadFromJsonAsync<SupplierOption>())!.Id;

        var productResponse = await _first.PostAsJsonAsync("/api/v1/products", new ProductRequest(
            $"Race {Guid.NewGuid():N}"[..15], null, null,
            [
                new VariantRequest(null, "Tan", "Standard", 4500m, 5, true),
                new VariantRequest(null, "Black", "Standard", 4500m, 5, true)
            ]));

        var product = await productResponse.Content.ReadFromJsonAsync<ProductView>();
        var a = product!.Variants[0].Id;
        var b = product.Variants[1].Id;

        // The orders are deliberately opposite. The service sorts by variant id before
        // locking, so the request order must not matter.
        var ascending = _first.PostAsJsonAsync("/api/v1/purchases", new PurchaseRequest(
            supplierId, DateTimeOffset.UtcNow, null,
            [new PurchaseLineRequest(a, 3, 100m), new PurchaseLineRequest(b, 3, 100m)]));

        var descending = _second.PostAsJsonAsync("/api/v1/purchases", new PurchaseRequest(
            supplierId, DateTimeOffset.UtcNow, null,
            [new PurchaseLineRequest(b, 5, 200m), new PurchaseLineRequest(a, 5, 200m)]));

        var results = await Task.WhenAll(ascending, descending);

        results.ShouldAllBe(r => r.StatusCode == HttpStatusCode.Created);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Both purchases landed in full, so both variants hold 3 + 5.
        var levels = await db.StockLevels.AsNoTracking()
            .Where(s => s.VariantId == a || s.VariantId == b)
            .OrderBy(s => s.VariantId)
            .ToListAsync();

        levels.Select(l => l.PhysicalQty).ShouldBe([8, 8]);
    }
}
```

- [x] **Step 2: Run the test to verify it passes**

Run: `cd backend; dotnet test tests/SlingKing.IntegrationTests --filter FullyQualifiedName~PurchaseConcurrencyTests`

Expected: 1 passed.

- [x] **Step 3: Try to prove the ordering is load-bearing — and report honestly**

In `StockService.ApplyPurchaseAsync`, temporarily remove the `.OrderBy(l => l.VariantId)` so lines lock in the order they arrive. Run the test **at least five times**.

You are looking for one request to fail — Postgres detects a deadlock and terminates one transaction, which surfaces as a 500.

**This test is weaker than M2b's lost-update test and you must not pretend otherwise.** That one failed 3 of 3 runs because a lost update is deterministic once the reads interleave. A deadlock needs both transactions to hold one row and want the other *at the same moment*, and the window here is a few milliseconds inside a fast local request. It may never lose the race.

Report exactly what happened, in these words or their equivalent:

- If it deadlocked at least once: *"Ordering proved: N of 5 runs deadlocked without it."*
- If it never deadlocked: *"Ordering NOT proved by test. The test passes with and without the sort across 5 runs; the sort rests on the argument that two transactions locking the same rows in opposite orders deadlock in Postgres, which is documented behaviour but not demonstrated here."*

The second outcome is an acceptable result. A quietly green test implying proof it does not have is not.

**Then restore `.OrderBy(l => l.VariantId)` and re-run — 1 passed.**

- [x] **Step 4: Commit**

```bash
git add backend/tests
git commit -m "test: two purchases locking the same variants in opposite orders

Weaker than the M2b lock test by nature -- a deadlock must lose a race to fail,
where a lost update is deterministic. The commit body records what the
break-and-observe run actually showed rather than implying proof.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

Replace the second paragraph of that message with what Step 3 actually observed.

---

## Task 6: Cancel a purchase

The subtle one. Cancellation must take back its own bags, and its layers are usually not the oldest.

**Files:**
- Modify: `backend/src/SlingKing.Application/Inventory/IStockService.cs`
- Modify: `backend/src/SlingKing.Application/Inventory/StockService.cs`
- Modify: `backend/src/SlingKing.Application/Purchasing/IPurchaseService.cs`
- Modify: `backend/src/SlingKing.Application/Purchasing/PurchaseService.cs`
- Modify: `backend/src/SlingKing.Api/Controllers/PurchasesController.cs`
- Test: `backend/tests/SlingKing.IntegrationTests/Api/PurchaseEndpointTests.cs` (append)

**Interfaces:**
- Consumes: `MovementContext.TargetLayerId` and `ApplyPurchaseAsync` (Task 4).
- Produces:
  - `Task IStockService.ReversePurchaseAsync(long purchaseId, IReadOnlyList<PurchaseStockLine> lines, CancellationToken)`
  - `Task<PurchaseView> IPurchaseService.CancelAsync(long id, CancellationToken)`
  - `POST /api/v1/purchases/{id}/cancel`

- [x] **Step 1: Write the failing tests**

Append to `PurchaseEndpointTests.cs`, inside the class:

```csharp
    private async Task<PurchaseView> RecordAsync(
        int supplierId, long variantId, int quantity, decimal? unitCost)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/purchases",
            new PurchaseRequest(supplierId, Today, null,
                [new PurchaseLineRequest(variantId, quantity, unitCost)]));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<PurchaseView>())!;
    }

    [Fact]
    public async Task Cancelling_takes_the_stock_back_out_and_closes_the_layer()
    {
        var supplierId = await NewSupplierAsync();
        var (_, variantIds) = await NewProductAsync("Tan");
        var purchase = await RecordAsync(supplierId, variantIds[0], 12, 450m);

        var response = await _client.PostAsync(
            $"/api/v1/purchases/{purchase.Id}/cancel", null);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<PurchaseView>())!.Status
            .ShouldBe("Cancelled");

        var level = await QueryAsync(db => db.StockLevels
            .SingleAsync(s => s.VariantId == variantIds[0]));
        level.PhysicalQty.ShouldBe(0);

        var layer = await QueryAsync(db => db.InventoryCostLayers
            .SingleAsync(l => l.VariantId == variantIds[0]));
        layer.RemainingQty.ShouldBe(0);

        // The reversal is a Purchase-type row with a negative delta, referencing the purchase.
        var reversal = await QueryAsync(db => db.InventoryTransactions
            .Where(t => t.VariantId == variantIds[0] && t.PhysicalDelta < 0)
            .SingleAsync());

        reversal.Type.ShouldBe(InventoryTransactionType.Purchase);
        reversal.PhysicalDelta.ShouldBe(-12);
        reversal.ReferenceType.ShouldBe(InventoryReferenceType.Purchase);
        reversal.ReferenceId.ShouldBe(purchase.Id);
    }

    [Fact]
    public async Task Cancelling_takes_back_its_own_layer_not_the_oldest()
    {
        var supplierId = await NewSupplierAsync();
        var (_, variantIds) = await NewProductAsync("Tan");

        // Two purchases of the same variant at different costs. The FIRST is older.
        await RecordAsync(supplierId, variantIds[0], 5, 100m);
        var second = await RecordAsync(supplierId, variantIds[0], 5, 900m);

        (await _client.PostAsync($"/api/v1/purchases/{second.Id}/cancel", null))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var layers = await QueryAsync(db => db.InventoryCostLayers
            .Where(l => l.VariantId == variantIds[0])
            .OrderBy(l => l.Id)
            .ToListAsync());

        // Run through FIFO this passes on quantity -- 5 left either way -- while silently
        // unwinding the FIRST purchase's cost. The assertion that catches it is which layer
        // still holds stock, and at what price.
        layers[0].UnitCost.ShouldBe(100m);
        layers[0].RemainingQty.ShouldBe(5);
        layers[1].UnitCost.ShouldBe(900m);
        layers[1].RemainingQty.ShouldBe(0);

        var level = await QueryAsync(db => db.StockLevels
            .SingleAsync(s => s.VariantId == variantIds[0]));
        level.PhysicalQty.ShouldBe(5);
    }

    [Fact]
    public async Task Cancelling_is_refused_once_any_of_that_stock_has_left()
    {
        var supplierId = await NewSupplierAsync();
        var (_, variantIds) = await NewProductAsync("Tan");
        var purchase = await RecordAsync(supplierId, variantIds[0], 12, 450m);

        var damage = await _client.PostAsJsonAsync("/api/v1/inventory/movements",
            new MovementRequest(variantIds[0], "Damage", 1, null, "Torn strap"));
        damage.StatusCode.ShouldBe(HttpStatusCode.Created);

        var response = await _client.PostAsync(
            $"/api/v1/purchases/{purchase.Id}/cancel", null);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var problem = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        problem!["title"].ToString().ShouldBe(
            "Some of these bags have already left. Record what actually happened on the Inventory screen instead.");

        // And nothing was written: the purchase is still Completed and the stock unchanged.
        var stored = await QueryAsync(db => db.Purchases.SingleAsync(p => p.Id == purchase.Id));
        stored.Status.ShouldBe(PurchaseStatus.Completed);

        var level = await QueryAsync(db => db.StockLevels
            .SingleAsync(s => s.VariantId == variantIds[0]));
        level.PhysicalQty.ShouldBe(11);
    }

    [Fact]
    public async Task Cancelling_twice_is_refused()
    {
        var supplierId = await NewSupplierAsync();
        var (_, variantIds) = await NewProductAsync("Tan");
        var purchase = await RecordAsync(supplierId, variantIds[0], 3, 100m);

        (await _client.PostAsync($"/api/v1/purchases/{purchase.Id}/cancel", null))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var again = await _client.PostAsync($"/api/v1/purchases/{purchase.Id}/cancel", null);
        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var problem = await again.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        problem!["title"].ToString().ShouldBe("That purchase is already cancelled.");
    }

    [Fact]
    public async Task Cancelling_an_unknown_purchase_is_not_found()
    {
        var response = await _client.PostAsync("/api/v1/purchases/999999999/cancel", null);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");

        var problem = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        problem!["title"].ToString().ShouldBe("That purchase does not exist.");
    }

    [Fact]
    public async Task The_ledger_and_the_layers_still_agree_after_a_purchase_and_a_cancellation()
    {
        var supplierId = await NewSupplierAsync();
        var (_, variantIds) = await NewProductAsync("Tan");

        await RecordAsync(supplierId, variantIds[0], 10, 120m);
        var second = await RecordAsync(supplierId, variantIds[0], 6, 200m);
        (await _client.PostAsync($"/api/v1/purchases/{second.Id}/cancel", null))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        await RecordAsync(supplierId, variantIds[0], 4, 300m);

        var level = await QueryAsync(db => db.StockLevels
            .SingleAsync(s => s.VariantId == variantIds[0]));

        var summed = await QueryAsync(db => db.InventoryTransactions
            .Where(t => t.VariantId == variantIds[0])
            .SumAsync(t => t.PhysicalDelta));

        var openLayers = await QueryAsync(db => db.InventoryCostLayers
            .Where(l => l.VariantId == variantIds[0] && l.RemainingQty > 0)
            .SumAsync(l => l.RemainingQty));

        // The M2b spec predicted purchases would break these. They do not, and this is the
        // test that checks the claim rather than trusting it.
        summed.ShouldBe(level.PhysicalQty);
        openLayers.ShouldBe(level.PhysicalQty);
        level.PhysicalQty.ShouldBe(14);
    }
```

- [x] **Step 2: Run the tests to verify they fail**

Run: `cd backend; dotnet test tests/SlingKing.IntegrationTests --filter FullyQualifiedName~PurchaseEndpointTests`

Expected: the six new tests fail with 404 — there is no cancel route.

- [x] **Step 3: Add targeted consumption to `StockService`**

In `StockService.cs`, add a method beside `ConsumeAsync`:

```csharp
    /// <summary>
    /// Takes <paramref name="quantity"/> out of ONE named layer instead of walking FIFO. Only
    /// a purchase cancellation uses this: it must take back the bags that purchase brought in,
    /// and the oldest layer is usually a different purchase's. Running a cancellation through
    /// the FIFO path leaves every quantity correct and every cost quietly wrong.
    /// </summary>
    private async Task ConsumeLayerAsync(
        InventoryTransaction ledgerRow,
        long layerId,
        int quantity,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var layer = await _db.InventoryCostLayers
            .SingleAsync(l => l.Id == layerId, cancellationToken);

        if (layer.RemainingQty != layer.OriginalQty)
        {
            throw AppException.Conflict(
                "Some of these bags have already left. " +
                "Record what actually happened on the Inventory screen instead.");
        }

        layer.RemainingQty -= quantity;

        _db.InventoryCostConsumptions.Add(new InventoryCostConsumption
        {
            CostLayerId = layer.Id,
            InventoryTransactionId = ledgerRow.Id,
            Quantity = quantity,
            ReversedQty = 0,
            UnitCost = layer.UnitCost,
            TotalCost = layer.UnitCost * quantity,
            ConsumedAt = now
        });

        ledgerRow.UnitCost = layer.UnitCost;
        ledgerRow.TotalCost = layer.UnitCost * quantity;
    }
```

Then, in `AppendAsync`, replace the outflow branch so a targeted layer wins over FIFO:

```csharp
        if (signedQuantity > 0)
        {
            _db.InventoryCostLayers.Add(FifoCostEngine.AddLayer(
                level.VariantId, level.LocationId, SourceOf(type),
                context?.SourceId ?? ledgerRow.Id,
                signedQuantity, unitCost, now));
        }
        else if (context?.TargetLayerId is long targetLayerId)
        {
            await ConsumeLayerAsync(
                ledgerRow, targetLayerId, -signedQuantity, now, cancellationToken);
        }
        else
        {
            await ConsumeAsync(
                level, ledgerRow, -signedQuantity, allowShortfall, now, cancellationToken);
        }
```

- [x] **Step 4: Add `ReversePurchaseAsync`**

Add to `IStockService`:

```csharp
    /// <summary>
    /// Takes back everything a purchase brought in: one outflow ledger row per line, each
    /// consuming that line's OWN cost layer rather than the oldest.
    /// </summary>
    /// <remarks>
    /// Assumes an open transaction and commits nothing. Throws 409 if any of the purchase's
    /// layers has been drawn from — a bag that has been sold, damaged or lost cannot be
    /// un-bought.
    /// </remarks>
    Task ReversePurchaseAsync(
        long purchaseId,
        IReadOnlyList<PurchaseStockLine> lines,
        CancellationToken cancellationToken = default);
```

And to `StockService`, after `ApplyPurchaseAsync`:

```csharp
    public async Task ReversePurchaseAsync(
        long purchaseId,
        IReadOnlyList<PurchaseStockLine> lines,
        CancellationToken cancellationToken = default)
    {
        var locationId = await DefaultLocationIdAsync(cancellationToken);

        // Same ascending order as ApplyPurchaseAsync, for the same deadlock reason.
        foreach (var line in lines.OrderBy(l => l.VariantId))
        {
            var layer = await _db.InventoryCostLayers.SingleOrDefaultAsync(
                l => l.SourceType == CostLayerSourceType.Purchase
                  && l.SourceId == line.PurchaseItemId,
                cancellationToken)
                ?? throw AppException.Conflict(
                    "This purchase's cost records are missing, so it cannot be cancelled. " +
                    "Record what actually happened on the Inventory screen instead.");

            var level = await LockLevelAsync(line.VariantId, locationId, cancellationToken);

            await AppendAsync(
                level, InventoryTransactionType.Purchase, -line.Quantity,
                unitCost: null, reason: null, allowShortfall: false,
                new MovementContext(
                    InventoryReferenceType.Purchase, purchaseId, TargetLayerId: layer.Id),
                cancellationToken);
        }
    }
```

- [x] **Step 5: Add `CancelAsync`**

Add to `IPurchaseService`:

```csharp
    /// <summary>
    /// Reverses a purchase: its stock goes back out and its cost layers close. The purchase
    /// stays visible with its reversal in the ledger, because a history that shows bags
    /// appearing and vanishing for no stated reason is worse than one that shows the mistake.
    /// </summary>
    /// <exception cref="AppException">
    /// 404 if no purchase has that id. 409 if it is already cancelled, or if any of its stock
    /// has already been sold, damaged or lost.
    /// </exception>
    Task<PurchaseView> CancelAsync(long id, CancellationToken cancellationToken = default);
```

And to `PurchaseService`:

```csharp
    public async Task<PurchaseView> CancelAsync(
        long id, CancellationToken cancellationToken = default)
    {
        var purchase = await _db.Purchases
            .SingleOrDefaultAsync(p => p.Id == id, cancellationToken)
            ?? throw AppException.NotFound("That purchase does not exist.");

        if (purchase.Status == PurchaseStatus.Cancelled)
        {
            throw AppException.Conflict("That purchase is already cancelled.");
        }

        var items = await _db.PurchaseItems
            .Where(i => i.PurchaseId == id)
            .Select(i => new PurchaseStockLine(i.Id, i.VariantId, i.Quantity, i.UnitCost))
            .ToListAsync(cancellationToken);

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        await _stock.ReversePurchaseAsync(id, items, cancellationToken);

        purchase.Status = PurchaseStatus.Cancelled;
        purchase.UpdatedAt = _clock.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await ViewAsync(id, cancellationToken);
    }
```

- [x] **Step 6: Add the route**

Add to `PurchasesController`:

```csharp
    [HttpPost("{id:long}/cancel")]
    [ProducesResponseType(typeof(PurchaseView), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Cancel(long id, CancellationToken cancellationToken) =>
        Ok(await _purchases.CancelAsync(id, cancellationToken));
```

- [x] **Step 7: Run the tests to verify they pass**

Run: `cd backend; dotnet test tests/SlingKing.IntegrationTests --filter FullyQualifiedName~PurchaseEndpointTests`

Expected: 18 passed.

- [x] **Step 8: Prove the targeted consumption is load-bearing**

In `AppendAsync`, temporarily delete the `else if (context?.TargetLayerId is long targetLayerId)` branch so cancellation falls through to the FIFO path. Re-run.

Expected: `Cancelling_takes_back_its_own_layer_not_the_oldest` FAILS — the older ₹100 layer is emptied and the ₹900 layer keeps its 5, so the surviving stock is valued at ₹900 apiece instead of ₹100. Note that `level.PhysicalQty` is still 5 in that failure: the quantity assertion alone would have passed.

**Then restore the branch and re-run — 18 passed.**

- [x] **Step 9: Run the whole backend suite**

Run: `cd backend; dotnet build -warnaserror; if ($?) { dotnet test }`

Expected: everything green, zero warnings.

- [x] **Step 10: Commit**

```bash
git add backend/src backend/tests
git commit -m "feat: cancel a purchase, taking back its own layers

Cancellation consumes the layers that purchase created, not the oldest. Through
FIFO it passes on quantity while silently unwinding a different purchase's cost
-- so the test asserts which layer still holds stock and at what price, not just
how many are left.

Refuses outright once any of that stock has been sold, damaged or lost.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 7: List purchases

A pure read. The list carries each purchase's lines, so there is no detail endpoint and no detail screen.

**Files:**
- Modify: `backend/src/SlingKing.Application/Purchasing/IPurchaseService.cs`
- Modify: `backend/src/SlingKing.Application/Purchasing/PurchaseService.cs`
- Modify: `backend/src/SlingKing.Api/Controllers/PurchasesController.cs`
- Test: `backend/tests/SlingKing.IntegrationTests/Api/PurchaseEndpointTests.cs` (append)

**Interfaces:**
- Consumes: `ViewAsync` and `PurchaseView` (Task 4).
- Produces: `Task<IReadOnlyList<PurchaseView>> IPurchaseService.ListAsync(string? search, bool includeCancelled, CancellationToken)` and `GET /api/v1/purchases?search=&includeCancelled=`.

- [x] **Step 1: Write the failing tests**

Append to `PurchaseEndpointTests.cs`:

```csharp
    [Fact]
    public async Task Reading_the_list_requires_a_token()
    {
        using var anonymous = _factory.CreateClient();

        (await anonymous.GetAsync("/api/v1/purchases")).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task The_list_carries_each_purchase_with_its_lines()
    {
        var supplierId = await NewSupplierAsync();
        var (_, variantIds) = await NewProductAsync("Tan", "Black");

        var response = await _client.PostAsJsonAsync("/api/v1/purchases",
            new PurchaseRequest(supplierId, Today, "Karol Bagh trip",
            [
                new PurchaseLineRequest(variantIds[0], 12, 450m),
                new PurchaseLineRequest(variantIds[1], 8, 480m)
            ]));

        var created = await response.Content.ReadFromJsonAsync<PurchaseView>();

        var list = await _client.GetFromJsonAsync<List<PurchaseView>>("/api/v1/purchases");

        var found = list!.Single(p => p.Id == created!.Id);
        found.Lines.Count.ShouldBe(2);
        found.TotalAmount.ShouldBe(9240m);
        found.SupplierName.ShouldNotBeNullOrWhiteSpace();
        found.Notes.ShouldBe("Karol Bagh trip");
    }

    [Fact]
    public async Task The_list_can_be_searched_by_purchase_number()
    {
        var supplierId = await NewSupplierAsync();
        var (_, variantIds) = await NewProductAsync("Tan");
        var created = await RecordAsync(supplierId, variantIds[0], 3, 100m);

        var found = await _client.GetFromJsonAsync<List<PurchaseView>>(
            $"/api/v1/purchases?search={created.Number}");

        found!.Count.ShouldBe(1);
        found[0].Id.ShouldBe(created.Id);
    }

    [Fact]
    public async Task The_list_can_be_searched_by_supplier_name()
    {
        var name = $"Sadar {Guid.NewGuid():N}"[..16];
        var supplierResponse = await _client.PostAsJsonAsync("/api/v1/suppliers",
            new SupplierRequest(name, null));
        var supplierId = (await supplierResponse.Content
            .ReadFromJsonAsync<SupplierOption>())!.Id;

        var (_, variantIds) = await NewProductAsync("Tan");
        var created = await RecordAsync(supplierId, variantIds[0], 3, 100m);

        var found = await _client.GetFromJsonAsync<List<PurchaseView>>(
            $"/api/v1/purchases?search={name}");

        found!.ShouldContain(p => p.Id == created.Id);
    }

    [Fact]
    public async Task Cancelled_purchases_are_hidden_unless_asked_for()
    {
        var supplierId = await NewSupplierAsync();
        var (_, variantIds) = await NewProductAsync("Tan");
        var created = await RecordAsync(supplierId, variantIds[0], 3, 100m);

        (await _client.PostAsync($"/api/v1/purchases/{created.Id}/cancel", null))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var hidden = await _client.GetFromJsonAsync<List<PurchaseView>>("/api/v1/purchases");
        hidden!.ShouldNotContain(p => p.Id == created.Id);

        var shown = await _client.GetFromJsonAsync<List<PurchaseView>>(
            "/api/v1/purchases?includeCancelled=true");
        shown!.ShouldContain(p => p.Id == created.Id && p.Status == "Cancelled");
    }

    [Fact]
    public async Task The_list_is_newest_first()
    {
        var supplierId = await NewSupplierAsync();
        var (_, variantIds) = await NewProductAsync("Tan");

        var first = await RecordAsync(supplierId, variantIds[0], 1, 10m);
        var second = await RecordAsync(supplierId, variantIds[0], 1, 10m);

        var list = await _client.GetFromJsonAsync<List<PurchaseView>>("/api/v1/purchases");

        var firstIndex = list!.FindIndex(p => p.Id == first.Id);
        var secondIndex = list.FindIndex(p => p.Id == second.Id);

        // Both were recorded in the same second, so a purchase_date sort alone would be a
        // coin toss. The id tiebreak is what makes the order deterministic.
        secondIndex.ShouldBeLessThan(firstIndex);
    }
```

- [x] **Step 2: Run the tests to verify they fail**

Run: `cd backend; dotnet test tests/SlingKing.IntegrationTests --filter FullyQualifiedName~PurchaseEndpointTests`

Expected: the six new tests fail — there is no list route.

- [x] **Step 3: Add the interface method**

Add to `IPurchaseService`:

```csharp
    /// <summary>
    /// Newest first, each with its lines. <paramref name="search"/> matches the purchase
    /// number and the supplier name.
    /// </summary>
    /// <param name="includeCancelled">
    /// False by default, mirroring the products list's includeInactive. A cancelled purchase
    /// is history, not something to scroll past every time.
    /// </param>
    Task<IReadOnlyList<PurchaseView>> ListAsync(
        string? search, bool includeCancelled, CancellationToken cancellationToken = default);
```

- [x] **Step 4: Implement it**

Add to `PurchaseService`:

```csharp
    public async Task<IReadOnlyList<PurchaseView>> ListAsync(
        string? search, bool includeCancelled, CancellationToken cancellationToken = default)
    {
        var query = _db.Purchases.AsNoTracking().AsQueryable();

        if (!includeCancelled)
        {
            query = query.Where(p => p.Status != PurchaseStatus.Cancelled);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            // ToLowerInvariant has no EF Core translation. The local must be lowered with a
            // culture-independent call; the in-query side must stay ToLower(). Do not "make
            // them consistent" — one of the two will stop compiling to SQL.
            var term = search.Trim().ToLowerInvariant();

            query = query.Where(p =>
                p.Number.ToLower().Contains(term) ||
                p.Supplier!.Name.ToLower().Contains(term));
        }

        // Newest first, with the id as tiebreak: two purchases recorded in the same second
        // would otherwise come back in an arbitrary order.
        var ids = await query
            .OrderByDescending(p => p.PurchaseDate).ThenByDescending(p => p.Id)
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);

        var views = new List<PurchaseView>(ids.Count);

        // ponytail: one round trip per purchase, the same trade ProductService.ListAsync
        // already takes. A few purchases a month is tens of rows; batch the line query if this
        // ever reaches thousands.
        foreach (var id in ids)
        {
            views.Add(await ViewAsync(id, cancellationToken));
        }

        return views;
    }
```

- [x] **Step 5: Add the route**

Add to `PurchasesController`:

```csharp
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<PurchaseView>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] string? search,
        [FromQuery] bool includeCancelled,
        CancellationToken cancellationToken) =>
        Ok(await _purchases.ListAsync(search, includeCancelled, cancellationToken));
```

- [x] **Step 6: Run the tests to verify they pass**

Run: `cd backend; dotnet test tests/SlingKing.IntegrationTests --filter FullyQualifiedName~PurchaseEndpointTests`

Expected: 24 passed.

- [x] **Step 7: Run the whole backend suite**

Run: `cd backend; dotnet build -warnaserror; if ($?) { dotnet test }`

Expected: everything green, zero warnings.

- [x] **Step 8: Commit**

```bash
git add backend/src backend/tests
git commit -m "feat: list purchases with their lines

No detail endpoint: the list carries the lines and rows expand in place, the
way the products list expands to its variants. Accepts the same round trip per
row, with a ponytail comment naming the ceiling.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 8: Frontend API layer, pure totals, and the supplier picker

**Files:**
- Create: `frontend/src/api/purchasing.ts`
- Create: `frontend/src/lib/purchaseTotals.ts`
- Create: `frontend/src/lib/purchaseTotals.test.ts`
- Create: `frontend/src/features/purchasing/SupplierSelect.tsx`

**Interfaces:**
- Consumes: `api` and `serverMessage` from `frontend/src/api/client.ts`; `formatInr` from `frontend/src/lib/money.ts`.
- Produces: `listSuppliers`, `createSupplier`, `updateSupplier`, `listPurchases`, `recordPurchase`, `cancelPurchase`, the types `SupplierOption`, `PurchaseLineView`, `PurchaseView`, `PurchasePayload`; `parseQuantity`, `parseCost`, `lineTotal`, `purchaseTotal`; and the `SupplierSelect` component.

- [x] **Step 1: Write the failing tests**

Create `frontend/src/lib/purchaseTotals.test.ts`:

```ts
import { describe, expect, it } from "vitest";
import { lineTotal, parseCost, parseQuantity, purchaseTotal } from "./purchaseTotals";

describe("parseQuantity", () => {
  it("reads a whole number", () => {
    expect(parseQuantity("12")).toBe(12);
  });

  it("treats an empty field as no answer, NOT as zero", () => {
    // Number("") is 0 in JavaScript. Taking that at face value repriced a bag to nothing in
    // M2a and would silently record a purchase of zero bags here.
    expect(parseQuantity("")).toBeNull();
    expect(parseQuantity("   ")).toBeNull();
  });

  it("refuses zero, negatives and fractions", () => {
    expect(parseQuantity("0")).toBeNull();
    expect(parseQuantity("-3")).toBeNull();
    expect(parseQuantity("2.5")).toBeNull();
    expect(parseQuantity("twelve")).toBeNull();
  });
});

describe("parseCost", () => {
  it("reads a decimal cost", () => {
    expect(parseCost("450.50")).toBe(450.5);
  });

  it("reads zero, which is a real cost", () => {
    // A genuinely free sample is not the same as an unknown cost.
    expect(parseCost("0")).toBe(0);
  });

  it("treats an empty field as unknown, not as zero", () => {
    expect(parseCost("")).toBeNull();
    expect(parseCost("   ")).toBeNull();
  });

  it("refuses negatives and nonsense", () => {
    expect(parseCost("-1")).toBeNull();
    expect(parseCost("four fifty")).toBeNull();
  });
});

describe("lineTotal", () => {
  it("multiplies quantity by cost", () => {
    expect(lineTotal(12, 450)).toBe(5400);
  });

  it("is unknown when the cost is unknown", () => {
    expect(lineTotal(12, null)).toBeNull();
  });

  it("is unknown when the quantity is not yet entered", () => {
    expect(lineTotal(null, 450)).toBeNull();
  });
});

describe("purchaseTotal", () => {
  it("sums a fully costed purchase", () => {
    expect(purchaseTotal([
      { quantity: 12, unitCost: 450 },
      { quantity: 8, unitCost: 480 },
    ])).toBe(9240);
  });

  it("is unknown when any line's cost is unknown", () => {
    // 5400 is the sum of the line we know. Showing it would understate what is about to be
    // paid, against a bill the owner is holding in her hand.
    expect(purchaseTotal([
      { quantity: 12, unitCost: 450 },
      { quantity: 8, unitCost: null },
    ])).toBeNull();
  });

  it("is unknown for an empty purchase", () => {
    expect(purchaseTotal([])).toBeNull();
  });

  it("ignores lines with no quantity yet", () => {
    // A half-typed row must not blank the running total the owner is watching.
    expect(purchaseTotal([
      { quantity: 12, unitCost: 450 },
      { quantity: null, unitCost: null },
    ])).toBe(5400);
  });
});
```

- [x] **Step 2: Run the tests to verify they fail**

Run: `cd frontend; npx vitest run src/lib/purchaseTotals.test.ts`

Expected: failure resolving `./purchaseTotals` — the module does not exist.

- [x] **Step 3: Write the pure helpers**

Create `frontend/src/lib/purchaseTotals.ts`:

```ts
/** One line as the form holds it while being typed: both fields may still be empty. */
export interface DraftLine {
  quantity: number | null;
  unitCost: number | null;
}

/**
 * Null means "not entered yet", never zero. Number("") is 0 in JavaScript, and taking that at
 * face value would record a purchase of nothing without the owner typing a digit.
 */
export function parseQuantity(raw: string): number | null {
  const trimmed = raw.trim();
  if (trimmed === "") return null;

  const value = Number(trimmed);
  return Number.isInteger(value) && value > 0 ? value : null;
}

/**
 * Null means the cost is unknown. Zero is a real answer — a free sample is not the same as a
 * cost nobody recorded, which is the distinction the whole optional-cost design rests on.
 */
export function parseCost(raw: string): number | null {
  const trimmed = raw.trim();
  if (trimmed === "") return null;

  const value = Number(trimmed);
  return Number.isFinite(value) && value >= 0 ? value : null;
}

export function lineTotal(quantity: number | null, unitCost: number | null): number | null {
  if (quantity === null || unitCost === null) return null;
  return quantity * unitCost;
}

/**
 * Null when any line that has a quantity lacks a cost, and null when nothing is entered.
 * Never a partial sum: a total that omits a line understates what is about to be paid, against
 * a bill the owner is checking it against.
 *
 * Lines with no quantity yet are skipped rather than counted as unknown — a half-typed row
 * must not blank the number she is watching.
 */
export function purchaseTotal(lines: DraftLine[]): number | null {
  const entered = lines.filter((l) => l.quantity !== null);

  if (entered.length === 0 || entered.some((l) => l.unitCost === null)) return null;

  return entered.reduce((sum, l) => sum + l.quantity! * l.unitCost!, 0);
}
```

- [x] **Step 4: Run the tests to verify they pass**

Run: `cd frontend; npx vitest run src/lib/purchaseTotals.test.ts`

Expected: 14 passed.

- [x] **Step 5: Write the API layer**

Create `frontend/src/api/purchasing.ts`:

```ts
import { api } from "./client";

export interface SupplierOption {
  id: number;
  name: string;
  phone: string | null;
}

export interface PurchaseLineView {
  id: number;
  variantId: number;
  sku: string;
  productName: string;
  colour: string | null;
  size: string | null;
  quantity: number;
  unitCost: number | null;
  lineTotal: number | null;
}

export interface PurchaseView {
  id: number;
  number: string;
  supplierId: number;
  supplierName: string;
  purchaseDate: string;
  status: "Completed" | "Cancelled" | "Draft";
  totalAmount: number | null;
  notes: string | null;
  lines: PurchaseLineView[];
}

export interface PurchasePayload {
  supplierId: number;
  purchaseDate: string;
  notes: string | null;
  lines: { variantId: number; quantity: number; unitCost: number | null }[];
}

export async function listSuppliers(): Promise<SupplierOption[]> {
  const { data } = await api.get<SupplierOption[]>("/api/v1/suppliers");
  return data;
}

export async function createSupplier(
  name: string,
  phone: string | null,
): Promise<SupplierOption> {
  const { data } = await api.post<SupplierOption>("/api/v1/suppliers", { name, phone });
  return data;
}

export async function updateSupplier(
  id: number,
  name: string,
  phone: string | null,
): Promise<SupplierOption> {
  const { data } = await api.put<SupplierOption>(`/api/v1/suppliers/${id}`, { name, phone });
  return data;
}

export async function listPurchases(
  search: string,
  includeCancelled: boolean,
): Promise<PurchaseView[]> {
  const { data } = await api.get<PurchaseView[]>("/api/v1/purchases", {
    params: {
      search: search || undefined,
      includeCancelled: includeCancelled || undefined,
    },
  });
  return data;
}

export async function recordPurchase(payload: PurchasePayload): Promise<PurchaseView> {
  const { data } = await api.post<PurchaseView>("/api/v1/purchases", payload);
  return data;
}

export async function cancelPurchase(id: number): Promise<PurchaseView> {
  const { data } = await api.post<PurchaseView>(`/api/v1/purchases/${id}/cancel`);
  return data;
}
```

- [x] **Step 6: Write the supplier picker**

Create `frontend/src/features/purchasing/SupplierSelect.tsx`. This is `CategorySelect` with a phone field — read `frontend/src/features/catalog/CategorySelect.tsx` first and keep its shape:

```tsx
import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { createSupplier, listSuppliers, updateSupplier } from "../../api/purchasing";
import { serverMessage } from "../../api/client";

const field =
  "rounded-stamp border-rule bg-panel focus:border-saddle border px-3 py-2.5 text-[14px]";

/**
 * Suppliers get no screen of their own — the owner will have about five and then mostly stop
 * thinking about them. Creating and correcting one inline is the whole management interface,
 * exactly as it is for categories.
 */
export function SupplierSelect({
  value,
  onChange,
}: {
  value: number | null;
  onChange: (id: number | null) => void;
}) {
  const [mode, setMode] = useState<"idle" | "adding" | "editing">("idle");
  const [name, setName] = useState("");
  const [phone, setPhone] = useState("");
  const [error, setError] = useState<string | null>(null);
  const queryClient = useQueryClient();

  const suppliers = useQuery({ queryKey: ["suppliers"], queryFn: listSuppliers });

  const selected = suppliers.data?.find((s) => s.id === value) ?? null;

  function close() {
    setMode("idle");
    setName("");
    setPhone("");
    setError(null);
  }

  const create = useMutation({
    mutationFn: () => createSupplier(name.trim(), phone.trim() || null),
    onSuccess: (created) => {
      queryClient.invalidateQueries({ queryKey: ["suppliers"] });
      onChange(created.id);
      close();
    },
    onError: (err) =>
      setError(serverMessage(err, "That supplier could not be added. It may already exist.")),
  });

  const update = useMutation({
    mutationFn: () => updateSupplier(value!, name.trim(), phone.trim() || null),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["suppliers"] });
      // The id does not change, so the current selection stays correct.
      close();
    },
    onError: (err) =>
      setError(serverMessage(err, "That supplier could not be saved. The name may be taken.")),
  });

  if (mode !== "idle") {
    const isEditing = mode === "editing";
    const busy = create.isPending || update.isPending;

    return (
      <div className="mt-1.5">
        <div className="flex flex-wrap gap-2">
          <input
            value={name}
            onChange={(e) => setName(e.target.value)}
            placeholder={isEditing ? "Supplier name" : "New supplier name"}
            aria-label={isEditing ? "Supplier name" : "New supplier name"}
            className={`${field} flex-1`}
          />
          <input
            value={phone}
            onChange={(e) => setPhone(e.target.value)}
            placeholder="Phone (optional)"
            aria-label="Supplier phone"
            className={`num ${field} w-40`}
          />
          <button
            type="button"
            disabled={!name.trim() || busy}
            onClick={() => (isEditing ? update.mutate() : create.mutate())}
            className="font-display rounded-stamp bg-saddle px-4 text-[13px] font-semibold text-white disabled:opacity-55"
          >
            {isEditing ? "Save" : "Add"}
          </button>
          <button type="button" onClick={close} className="text-muted text-[13px]">
            Cancel
          </button>
        </div>
        {error && (
          <p role="alert" className="text-flag mt-2 text-[13px]">
            {error}
          </p>
        )}
      </div>
    );
  }

  return (
    <div className="mt-1.5 flex gap-2">
      <select
        value={value ?? ""}
        onChange={(e) => onChange(e.target.value ? Number(e.target.value) : null)}
        aria-label="Supplier"
        className={`${field} flex-1`}
      >
        <option value="">Choose a supplier</option>
        {suppliers.data?.map((supplier) => (
          <option key={supplier.id} value={supplier.id}>
            {supplier.name}
          </option>
        ))}
      </select>
      {/* Only offered for a supplier that actually exists — there is nothing to edit while
          no supplier is chosen. */}
      {selected && (
        <button
          type="button"
          onClick={() => {
            setName(selected.name);
            setPhone(selected.phone ?? "");
            setMode("editing");
          }}
          className="text-saddle text-[13px] font-medium"
        >
          Edit
        </button>
      )}
      <button
        type="button"
        onClick={() => {
          setName("");
          setPhone("");
          setMode("adding");
        }}
        className="text-saddle text-[13px] font-medium"
      >
        + New
      </button>
    </div>
  );
}
```

- [x] **Step 7: Type-check and lint**

Run: `cd frontend; npx tsc -b; if ($?) { npm run lint }`

Expected: both clean. `SupplierSelect` is not yet rendered anywhere; that is fine, Task 10 uses it.

- [x] **Step 8: Commit**

```bash
git add frontend/src
git commit -m "feat: purchasing API layer, pure totals, and the supplier picker

purchaseTotal is null when any entered line lacks a cost -- never a partial sum,
against a bill the owner is checking it against. Lines with no quantity yet are
skipped so a half-typed row does not blank the running total.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 9: The purchases list screen

**Files:**
- Create: `frontend/src/features/purchasing/PurchasesPage.tsx`
- Modify: `frontend/src/app/router.tsx`

**Interfaces:**
- Consumes: `listPurchases`, `cancelPurchase`, `PurchaseView` (Task 8); `formatInr` from `frontend/src/lib/money.ts`; `formatIstDate` from `frontend/src/lib/dates.ts`; `serverMessage`.
- Produces: the `/purchases` route.

- [x] **Step 1: Write the screen**

Create `frontend/src/features/purchasing/PurchasesPage.tsx`. Read `frontend/src/features/catalog/ProductsPage.tsx` first — this is the same shape, including the expand-on-click row and the confirmation before a destructive-looking action.

```tsx
import { Fragment, useState } from "react";
import { Link } from "react-router-dom";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { cancelPurchase, listPurchases, type PurchaseView } from "../../api/purchasing";
import { serverMessage } from "../../api/client";
import { formatInr } from "../../lib/money";
import { formatIstDate } from "../../lib/dates";

function LineRow({ line }: { line: PurchaseView["lines"][number] }) {
  return (
    <tr>
      <td className="text-muted py-2 pl-10 text-[13px]">
        {line.productName}
        <span className="block text-[12px]">
          {[line.colour, line.size].filter(Boolean).join(" · ") || "No variation"}
        </span>
      </td>
      <td className="num text-muted py-2 text-[13px]">{line.sku}</td>
      <td className="num py-2 text-right text-[13px]">{line.quantity}</td>
      <td className="num py-2 text-right text-[13px]">{formatInr(line.unitCost)}</td>
      <td className="num py-2 text-right text-[13px]">{formatInr(line.lineTotal)}</td>
      <td />
    </tr>
  );
}

export function PurchasesPage() {
  const [search, setSearch] = useState("");
  const [includeCancelled, setIncludeCancelled] = useState(false);
  const [expanded, setExpanded] = useState<number | null>(null);
  const [error, setError] = useState<string | null>(null);
  const queryClient = useQueryClient();

  const purchases = useQuery({
    queryKey: ["purchases", search, includeCancelled],
    queryFn: () => listPurchases(search, includeCancelled),
  });

  const cancel = useMutation({
    mutationFn: cancelPurchase,
    onSuccess: () => {
      setError(null);
      queryClient.invalidateQueries({ queryKey: ["purchases"] });
      // Cancelling puts stock back out, so both stock screens are now stale.
      queryClient.invalidateQueries({ queryKey: ["stock"] });
      queryClient.invalidateQueries({ queryKey: ["products"] });
    },
    onError: (err) => setError(serverMessage(err, "That purchase could not be cancelled.")),
  });

  return (
    <div className="mx-auto max-w-6xl px-6 py-10">
      <div className="flex flex-wrap items-end justify-between gap-4">
        <div>
          <h1 className="font-display text-[28px] font-bold tracking-tight">Purchases</h1>
          <p className="text-muted mt-1 text-[13px]">
            What you bought, from whom, and what it cost.
          </p>
        </div>
        <Link
          to="/purchases/new"
          className="font-display rounded-stamp bg-saddle px-4 py-2.5 text-[14px] font-semibold tracking-wide text-white transition-opacity hover:opacity-90"
        >
          Record a purchase
        </Link>
      </div>

      <div className="topstitch mt-6" />

      <div className="mt-6 flex flex-wrap items-center gap-4">
        <input
          type="search"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          placeholder="Search number or supplier"
          aria-label="Search purchases"
          className="rounded-stamp border-rule bg-panel placeholder:text-muted/60 focus:border-saddle border px-3 py-2 text-[14px] transition-colors sm:w-80"
        />
        <label className="text-muted flex items-center gap-2 text-[13px]">
          <input
            type="checkbox"
            checked={includeCancelled}
            onChange={(e) => setIncludeCancelled(e.target.checked)}
          />
          Show cancelled
        </label>
      </div>

      {purchases.isPending && <p className="text-muted mt-8 text-[13px]">Loading</p>}

      {purchases.isError && (
        <p role="alert" className="text-flag mt-8 text-[13px]">
          Could not load purchases. Check that the API is running and try again.
        </p>
      )}

      {error && (
        <p role="alert" className="text-flag mt-8 text-[13px]">
          {error}
        </p>
      )}

      {purchases.data?.length === 0 && (
        <p className="text-muted mt-8 text-[13px]">
          {search || includeCancelled
            ? "Nothing matches that."
            : "Nothing bought yet. Record a purchase when you next come back from the market."}
        </p>
      )}

      {purchases.data && purchases.data.length > 0 && (
        <table className="mt-6 w-full border-collapse">
          <thead>
            <tr className="border-rule text-muted border-b text-left text-[12px] tracking-wide uppercase">
              <th className="pb-2 font-medium">Purchase</th>
              <th className="pb-2 font-medium">Supplier</th>
              <th className="pb-2 text-right font-medium">Items</th>
              <th className="pb-2 text-right font-medium" />
              <th className="pb-2 text-right font-medium">Total</th>
              <th className="pb-2" />
            </tr>
          </thead>
          <tbody>
            {purchases.data.map((purchase) => {
              const cancelled = purchase.status === "Cancelled";
              const open = expanded === purchase.id;

              return (
                <Fragment key={purchase.id}>
                  <tr className={`border-rule border-b ${cancelled ? "opacity-55" : ""}`}>
                    <td className="py-2.5">
                      <button
                        type="button"
                        onClick={() => setExpanded(open ? null : purchase.id)}
                        className="text-left text-[14px] font-medium"
                      >
                        {open ? "▾" : "▸"} <span className="num">{purchase.number}</span>
                      </button>
                      <span className="num text-muted block text-[12px]">
                        {formatIstDate(purchase.purchaseDate)}
                        {cancelled && " · cancelled"}
                      </span>
                    </td>
                    <td className="py-2.5 text-[13px]">{purchase.supplierName}</td>
                    <td className="num py-2.5 text-right text-[13px]">
                      {purchase.lines.reduce((sum, l) => sum + l.quantity, 0)}
                    </td>
                    <td />
                    <td
                      className={`num py-2.5 text-right text-[14px] ${cancelled ? "line-through" : ""}`}
                    >
                      {formatInr(purchase.totalAmount)}
                    </td>
                    <td className="py-2.5 text-right">
                      {!cancelled && (
                        <button
                          type="button"
                          disabled={cancel.isPending}
                          onClick={() => {
                            const items = purchase.lines.reduce((s, l) => s + l.quantity, 0);
                            if (
                              window.confirm(
                                `Cancel ${purchase.number}? This takes ${items} back out of stock. ` +
                                  `It is refused if any of them have already been sold, damaged or lost.`,
                              )
                            ) {
                              cancel.mutate(purchase.id);
                            }
                          }}
                          className="text-muted hover:text-flag text-[13px] transition-colors disabled:opacity-55"
                        >
                          Cancel
                        </button>
                      )}
                    </td>
                  </tr>
                  {open &&
                    purchase.lines.map((line) => <LineRow key={line.id} line={line} />)}
                  {open && purchase.notes && (
                    <tr>
                      <td colSpan={6} className="text-muted py-2 pl-10 text-[12px]">
                        {purchase.notes}
                      </td>
                    </tr>
                  )}
                </Fragment>
              );
            })}
          </tbody>
        </table>
      )}
    </div>
  );
}
```

- [x] **Step 2: Add the route**

In `frontend/src/app/router.tsx`, add the import and the route **above** the `path: "*"` catch-all — anything after it is unreachable:

```tsx
import { PurchasesPage } from "../features/purchasing/PurchasesPage";
```

```tsx
      { path: "purchases", element: <PurchasesPage /> },
```

- [x] **Step 3: Type-check and lint**

Run: `cd frontend; npx tsc -b; if ($?) { npm run lint; if ($?) { npx vitest run } }`

Expected: all clean; 32 vitest tests (18 existing plus the 14 from Task 8).

- [x] **Step 4: Commit**

```bash
git add frontend/src
git commit -m "feat: purchases list screen

Rows expand to their lines the way products expand to variants. A cancelled
purchase reads muted with its total struck through -- the money did not happen,
and the row stays so the history explains itself.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 10: Record a purchase — the form and the variant picker

**Files:**
- Create: `frontend/src/features/purchasing/VariantPicker.tsx`
- Create: `frontend/src/features/purchasing/PurchaseForm.tsx`
- Modify: `frontend/src/app/router.tsx`

**Interfaces:**
- Consumes: `recordPurchase`, `PurchasePayload` (Task 8); `SupplierSelect` (Task 8); `parseQuantity`, `parseCost`, `lineTotal`, `purchaseTotal` (Task 8); `listStock` and `StockRow` from `frontend/src/api/inventory.ts`; `formatInr`; `serverMessage`.
- Produces: the `/purchases/new` route.

**The variant picker needs no new endpoint.** `GET /api/v1/inventory` already returns exactly one row per usable variant, carrying `variantId`, `sku`, `productName`, `colour` and `size` — and it already filters to variants whose product is active, which is the same set a purchase may reference. Reuse `listStock("", false)` rather than adding a variants endpoint.

- [x] **Step 1: Write the variant picker**

Create `frontend/src/features/purchasing/VariantPicker.tsx`:

```tsx
import { useMemo, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { listStock } from "../../api/inventory";

/**
 * The product form's category dropdown picks from about five things; this picks from every
 * colour of every bag in the catalogue, so it is searchable rather than a bare select.
 *
 * It reads the inventory list because that endpoint already returns exactly one row per
 * usable variant — same product-active filter a purchase needs — so no new endpoint exists
 * just to feed a dropdown.
 */
export function VariantPicker({
  value,
  onChange,
  label,
}: {
  value: number | null;
  onChange: (variantId: number | null) => void;
  label: string;
}) {
  const [term, setTerm] = useState("");
  const [open, setOpen] = useState(false);

  const stock = useQuery({ queryKey: ["stock", "", false], queryFn: () => listStock("", false) });

  const chosen = stock.data?.find((row) => row.variantId === value) ?? null;

  const matches = useMemo(() => {
    const needle = term.trim().toLowerCase();
    const rows = stock.data ?? [];

    if (needle === "") return rows.slice(0, 8);

    return rows
      .filter((row) =>
        [row.productName, row.sku, row.colour, row.size]
          .filter(Boolean)
          .join(" ")
          .toLowerCase()
          .includes(needle),
      )
      .slice(0, 8);
  }, [stock.data, term]);

  if (chosen && !open) {
    return (
      <button
        type="button"
        aria-label={label}
        onClick={() => {
          setTerm("");
          setOpen(true);
        }}
        className="rounded-stamp border-rule bg-panel w-full border px-2 py-1.5 text-left text-[14px]"
      >
        {chosen.productName}
        <span className="text-muted block text-[12px]">
          {[chosen.colour, chosen.size].filter(Boolean).join(" · ") || "No variation"} ·{" "}
          <span className="num">{chosen.sku}</span>
        </span>
      </button>
    );
  }

  return (
    <div className="relative">
      <input
        aria-label={label}
        value={term}
        autoFocus={open}
        placeholder="Search a bag"
        onChange={(e) => {
          setTerm(e.target.value);
          setOpen(true);
        }}
        onFocus={() => setOpen(true)}
        className="rounded-stamp border-rule bg-panel focus:border-saddle w-full border px-2 py-1.5 text-[14px]"
      />
      {open && (
        <ul className="border-rule bg-panel absolute z-10 mt-1 max-h-64 w-full overflow-auto border shadow-sm">
          {matches.length === 0 && (
            <li className="text-muted px-3 py-2 text-[13px]">
              Nothing matches. Add the bag on the Products screen first.
            </li>
          )}
          {matches.map((row) => (
            <li key={row.variantId}>
              <button
                type="button"
                onClick={() => {
                  onChange(row.variantId);
                  setOpen(false);
                }}
                className="hover:bg-canvas w-full px-3 py-2 text-left text-[13px]"
              >
                {row.productName}
                <span className="text-muted block text-[12px]">
                  {[row.colour, row.size].filter(Boolean).join(" · ") || "No variation"} ·{" "}
                  <span className="num">{row.sku}</span>
                </span>
              </button>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
```

- [x] **Step 2: Write the form**

Create `frontend/src/features/purchasing/PurchaseForm.tsx`. Read `frontend/src/features/catalog/ProductForm.tsx` first — the shared `field` class, `role="alert"` errors, and hooks before any early return all come from there.

```tsx
import { useState, type FormEvent } from "react";
import { useNavigate } from "react-router-dom";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { recordPurchase } from "../../api/purchasing";
import { serverMessage } from "../../api/client";
import { formatInr } from "../../lib/money";
import { lineTotal, parseCost, parseQuantity, purchaseTotal } from "../../lib/purchaseTotals";
import { SupplierSelect } from "./SupplierSelect";
import { VariantPicker } from "./VariantPicker";

const field =
  "mt-1.5 w-full rounded-stamp border border-rule bg-panel px-3 py-2.5 text-[14px] " +
  "transition-colors placeholder:text-muted/60 focus:border-saddle";

interface DraftRow {
  variantId: number | null;
  quantity: string;
  unitCost: string;
}

const blankRow: DraftRow = { variantId: null, quantity: "", unitCost: "" };

function today(): string {
  return new Date().toISOString().slice(0, 10);
}

export function PurchaseForm() {
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  const [supplierId, setSupplierId] = useState<number | null>(null);
  const [purchaseDate, setPurchaseDate] = useState(today());
  const [notes, setNotes] = useState("");
  const [rows, setRows] = useState<DraftRow[]>([{ ...blankRow }]);
  const [error, setError] = useState<string | null>(null);

  const save = useMutation({
    mutationFn: recordPurchase,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["purchases"] });
      // The purchase raised stock, so both stock screens are now stale.
      queryClient.invalidateQueries({ queryKey: ["stock"] });
      queryClient.invalidateQueries({ queryKey: ["products"] });
      navigate("/purchases");
    },
    onError: (err) => setError(serverMessage(err, "That purchase could not be recorded.")),
  });

  function patchRow(index: number, patch: Partial<DraftRow>) {
    setRows((current) => current.map((r, i) => (i === index ? { ...r, ...patch } : r)));
  }

  const draft = rows.map((r) => ({
    quantity: parseQuantity(r.quantity),
    unitCost: parseCost(r.unitCost),
  }));

  const runningTotal = purchaseTotal(draft);

  function handleSubmit(event: FormEvent) {
    event.preventDefault();
    setError(null);

    if (supplierId === null) {
      setError("Choose who you bought from.");
      return;
    }

    const lines = rows
      .map((row, i) => ({ row, quantity: draft[i].quantity, unitCost: draft[i].unitCost }))
      .filter((l) => l.row.variantId !== null || l.quantity !== null);

    if (lines.length === 0) {
      setError("Add at least one bag to this purchase.");
      return;
    }

    if (lines.some((l) => l.row.variantId === null)) {
      setError("Every line needs a bag. Pick one, or clear the line.");
      return;
    }

    if (lines.some((l) => l.quantity === null)) {
      setError("Enter how many, as a number above zero.");
      return;
    }

    const chosen = lines.map((l) => l.row.variantId);
    if (new Set(chosen).size !== chosen.length) {
      setError("That bag appears twice. Put the whole quantity on one line.");
      return;
    }

    save.mutate({
      supplierId,
      purchaseDate: new Date(purchaseDate).toISOString(),
      notes: notes.trim() || null,
      lines: lines.map((l) => ({
        variantId: l.row.variantId!,
        quantity: l.quantity!,
        unitCost: l.unitCost,
      })),
    });
  }

  return (
    <div className="mx-auto max-w-4xl px-6 py-10">
      <h1 className="font-display text-[28px] font-bold tracking-tight">Record a purchase</h1>
      <p className="text-muted mt-1 text-[13px]">
        What you brought back, and what you paid for it.
      </p>

      <div className="topstitch mt-6" />

      <form onSubmit={handleSubmit} className="mt-6">
        <span className="text-[12px] font-medium tracking-wide">Supplier</span>
        <SupplierSelect value={supplierId} onChange={setSupplierId} />

        <div className="mt-4 flex flex-wrap gap-4">
          <div>
            <label className="text-[12px] font-medium tracking-wide" htmlFor="purchase-date">
              Date
            </label>
            {/* A native date input: the platform already knows how to enter a date on a
                phone, and it back-dates without ceremony. */}
            <input
              id="purchase-date"
              type="date"
              value={purchaseDate}
              onChange={(e) => setPurchaseDate(e.target.value)}
              className={`num ${field} w-48`}
            />
          </div>
          <div className="flex-1">
            <label className="text-[12px] font-medium tracking-wide" htmlFor="purchase-notes">
              Notes <span className="text-muted font-normal">(optional)</span>
            </label>
            <input
              id="purchase-notes"
              value={notes}
              onChange={(e) => setNotes(e.target.value)}
              className={field}
            />
          </div>
        </div>

        <div className="topstitch mt-8" />

        <h2 className="font-display mt-6 text-[16px] font-bold">What you bought</h2>
        <p className="text-muted mt-1 text-[13px]">
          Leave a cost blank if you do not know it. It stays unknown rather than becoming zero.
        </p>

        <table className="mt-4 w-full border-collapse">
          <thead>
            <tr className="border-rule text-muted border-b text-left text-[12px] tracking-wide uppercase">
              <th className="pb-2 font-medium">Bag</th>
              <th className="pb-2 font-medium">How many</th>
              <th className="pb-2 font-medium">Cost each</th>
              <th className="pb-2 text-right font-medium">Line total</th>
              <th className="pb-2" />
            </tr>
          </thead>
          <tbody>
            {rows.map((row, index) => (
              <tr key={index} className="border-rule border-b">
                <td className="w-1/2 py-2 pr-2 align-top">
                  <VariantPicker
                    value={row.variantId}
                    onChange={(variantId) => patchRow(index, { variantId })}
                    label={`Bag ${index + 1}`}
                  />
                </td>
                <td className="py-2 pr-2 align-top">
                  <input
                    inputMode="numeric"
                    aria-label={`How many ${index + 1}`}
                    value={row.quantity}
                    onChange={(e) => patchRow(index, { quantity: e.target.value })}
                    className="num rounded-stamp border-rule w-20 border px-2 py-1.5 text-[14px]"
                  />
                </td>
                <td className="py-2 pr-2 align-top">
                  <input
                    inputMode="decimal"
                    aria-label={`Cost each ${index + 1}`}
                    value={row.unitCost}
                    onChange={(e) => patchRow(index, { unitCost: e.target.value })}
                    className="num rounded-stamp border-rule w-28 border px-2 py-1.5 text-[14px]"
                  />
                </td>
                <td className="num py-3.5 text-right text-[14px] align-top">
                  {formatInr(lineTotal(draft[index].quantity, draft[index].unitCost))}
                </td>
                <td className="py-3.5 text-right align-top">
                  {rows.length > 1 && (
                    <button
                      type="button"
                      aria-label={`Remove line ${index + 1}`}
                      onClick={() => setRows(rows.filter((_, i) => i !== index))}
                      className="text-muted hover:text-flag text-[13px] transition-colors"
                    >
                      Remove
                    </button>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>

        <div className="mt-3 flex flex-wrap items-center justify-between gap-4">
          <button
            type="button"
            onClick={() => setRows([...rows, { ...blankRow }])}
            className="text-saddle text-[13px] font-medium"
          >
            + Add a line
          </button>

          {/* Visible while lines are entered, because this is the number being checked
              against the bill in hand. A total that appears only after saving arrives too
              late to catch the mistake. */}
          <p className="text-[14px]">
            Total <span className="num font-display text-[18px] font-bold">
              {formatInr(runningTotal)}
            </span>
          </p>
        </div>

        {error && (
          <p role="alert" className="text-flag mt-6 text-[13px]">
            {error}
          </p>
        )}

        <div className="mt-8 flex gap-3">
          <button
            type="submit"
            disabled={save.isPending}
            className="font-display rounded-stamp bg-saddle px-5 py-2.5 text-[14px] font-semibold tracking-wide text-white transition-opacity hover:opacity-90 disabled:opacity-55"
          >
            {save.isPending ? "Recording" : "Record the purchase"}
          </button>
          <button
            type="button"
            onClick={() => navigate("/purchases")}
            className="text-muted text-[14px]"
          >
            Cancel
          </button>
        </div>
      </form>
    </div>
  );
}
```

- [x] **Step 3: Add the route**

In `frontend/src/app/router.tsx`, add the import and the route **above** the `path: "*"` catch-all:

```tsx
import { PurchaseForm } from "../features/purchasing/PurchaseForm";
```

```tsx
      { path: "purchases/new", element: <PurchaseForm /> },
```

- [x] **Step 4: Run the frontend checks**

Run: `cd frontend; npx tsc -b; if ($?) { npm run lint; if ($?) { npx vitest run } }`

Expected: type-check clean, lint clean, 32 vitest tests passing.

- [x] **Step 5: Commit**

```bash
git add frontend/src
git commit -m "feat: record-a-purchase form with a searchable variant picker

The running total stays visible while lines are entered, because it is the
number being checked against the bill in hand. The picker reads the inventory
list rather than adding an endpoint that exists only to feed a dropdown.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 11: Wire it up, walk it, and gate it

**Files:**
- Modify: `frontend/src/app/NotFound.tsx`
- Test: the whole suite, plus a browser walkthrough

**Interfaces:**
- Consumes: everything.
- Produces: the milestone.

- [x] **Step 1: Retire the last promise on the not-found page**

`frontend/src/app/NotFound.tsx` currently says Purchases is on its way and points at Products and Inventory as where the daily work happens. Purchases now exists, so the page has nothing left to promise. Rewrite that sentence as a plain not-found line in the page's existing voice — calm, second person, no exclamation marks. Change nothing else: not the heading, not the "Not here" label, not the markup, not the link.

- [x] **Step 2: Run everything**

Run: `cd backend; dotnet build -warnaserror; if ($?) { dotnet test }`

Expected: green, zero warnings. Expect roughly 151 integration and 87 unit — M2b's 118 and 78, plus 24 purchase, 9 supplier and 1 concurrency integration tests and 9 unit tests. An exact match is not the gate; a green run with no warnings is.

Run: `cd frontend; npx tsc -b; if ($?) { npm run lint; if ($?) { npx vitest run } }`

Expected: all clean, 32 tests passing.

- [x] **Step 3: Confirm no migration crept in**

Run: `git diff --name-only main -- backend/src/SlingKing.Infrastructure/Migrations`

Expected: no output. The schema was complete before this milestone started, and `SchemaConstraintTests` still asserts 28 tables — which Step 2 has already shown.

- [x] **Step 4: Start the app**

Run these in two background shells:

```
cd backend/src/SlingKing.Api && dotnet run --launch-profile http
cd frontend && npm run dev
```

The API listens on `http://localhost:5197`; Vite serves `http://localhost:5173`. **Vite must be on 5173** — it is the only origin in the API's CORS allowlist, and if a stale process holds the port Vite silently moves to 5174 and every request fails as a CORS error that looks like the server being down. Check the port it prints before going further.

- [x] **Step 5: Probe the stub before trusting the browser**

Run:

```
curl.exe -s -o /dev/null -w "%{http_code}\n" -H "Authorization: Bearer garbage" http://localhost:5197/api/v1/purchases
curl.exe -s -o /dev/null -w "%{http_code}\n" -H "Authorization: Bearer garbage" http://localhost:5197/api/v1/suppliers
```

Expected: `401` from both. If either returns 200, the session you are about to test is not authenticated the way you think it is, and every result after it is worthless.

- [x] **Step 6: Walk it in Playwright**

Sign in with a **throwaway** account: start the API with `SLINGKING_SEED_ADMIN_EMAIL` and `SLINGKING_SEED_ADMIN_PASSWORD` set to values you invent for this run. The seeder keys on email, so this adds an account without touching the owner's. **Never type the owner's real password into a tool call, and never print `backend/.env`.**

**Use a disposable product, not the owner's real catalogue.** A purchase writes to an append-only ledger; recording test quantities against real bags leaves permanent fabricated history that can only be compensated, never removed.

Then, against the real API:

1. Open **Purchases** from the top rail. It must resolve to a real screen, not the not-found page.
2. Create a supplier inline from the purchase form, with a phone number.
3. Record a two-line purchase — 12 at ₹450 and 8 at ₹480. Watch the running total read ₹9,240.00 **before** you save.
4. Check both items on the Inventory screen: stock has risen by 12 and 8, and each is worth what you paid.
5. Back on Purchases, expand the row. Both lines, their costs and their line totals are there.
6. Record a second purchase of one of the same variants at a different cost. Cancel **that** one. The first purchase's stock and value must be untouched — this is the FIFO trap, seen in the UI.
7. Try to cancel a purchase after damaging one of its bags on the Inventory screen. The refusal names the reason.
8. Clear a cost box on a new purchase and watch the total read `—` rather than a smaller number.
9. Reload. Every number survives.

Take a screenshot of the expanded purchases list and keep it with the task notes.

- [x] **Step 7: Check the browser console**

Read the console for the whole walkthrough. Expected: no errors, no React warnings, no unhandled rejections. The 409s from the deliberate refusals in steps 6 and 7 are expected and correct.

- [x] **Step 8: Clean up**

Stop both background shells — leaving them running holds Supabase pooler connections out of a cap of fifteen. Retire the disposable product, delete the throwaway user and its refresh tokens, and remove any screenshots from the repository root.

- [x] **Step 9: Request a whole-branch review**

Use `superpowers:requesting-code-review` against the full diff from `main`, not task-by-task, on the most capable model available. This is not a formality: M2a's whole-branch review found a Critical that six per-task reviews structurally could not, and M2b's found four Importants including a hole in the design's own reasoning.

Point it at the seams:

- Can any path write `physical_qty`, a ledger row or a cost layer without going through `StockService`?
- Can `ApplyPurchaseAsync` or `ReversePurchaseAsync` be reached without an open transaction?
- Is there any path that locks stock rows out of ascending variant id order?
- Can a cancellation consume a layer that is not its own?
- Does any code substitute `0` for an unknown cost, or present a partial sum as a total, in either language?
- If the second `SaveChangesAsync` inside `RecordAsync` throws, what state is left?
- Do the client-side guards and the server-side refusals agree, especially on duplicate variants?

- [x] **Step 10: Address the findings, then merge**

Use `superpowers:receiving-code-review` on whatever comes back — verify each finding before implementing it; do not agree performatively. Then `superpowers:finishing-a-development-branch`. Tag the merge `r1-m3`.

- [x] **Step 11: Tick this plan's boxes and commit it**

Mark every checkbox in this file, then:

```bash
git add docs/superpowers/plans/2026-08-30-r1-m3-purchases.md
git commit -m "docs: mark M3 plan tasks complete

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Self-Review

**Spec coverage.** Every section of the spec maps to a task:

| Spec section | Task |
|---|---|
| Shared variant reachability rule | 1 |
| `SourceOf` learns about purchases | 1 |
| `Purchase.TotalAmount` comment corrected | 1 |
| Purchase total null when any line is uncosted | 2, and asserted end to end in 4 |
| Duplicate variant refused by name | 2, 4 |
| Suppliers: list, create, rename, inline | 3, 8 |
| Recording: one transaction, number, lines, stock | 4 |
| Ascending-id lock order | 4, tested in 5 |
| Ledger rows carry `ReferenceType`/`ReferenceId` | 4 |
| Layers carry the purchase line as `SourceId` | 4 |
| Back-dating does not reorder the queue | 4 |
| Cancellation reverses with type `Purchase`, negative delta | 6 |
| Cancellation targets its own layers | 6 |
| Cancellation refuses once stock has moved | 6 |
| Both M2b invariants still hold | 6 |
| List with search and `includeCancelled` | 7 |
| Purchases list screen, expandable rows | 9 |
| Purchase form, running total, variant picker | 10 |
| Not-found page, browser walkthrough, gate | 11 |
| No migration | Global constraint, checked in Task 11 Step 3 |

**Two deliberate deviations from the spec, flagged rather than applied silently:**

1. **The variant picker uses `GET /api/v1/inventory` instead of a new endpoint.** The spec calls the picker "the one new control this milestone needs" without saying where its data comes from. The inventory list already returns one row per usable variant with exactly the fields the picker shows, and applies the same product-active filter a purchase requires. Adding a variants endpoint to feed a dropdown would be a second way to ask the same question.
2. **`purchaseTotal` on the client skips lines with no quantity entered**, where the server's `PurchaseTotals.Total` considers every line. They are not the same function: the server sees a submitted purchase, the client sees a form mid-typing, and blanking the running total because a fresh empty row exists would make the number useless exactly while it is being watched. The submitted payload only ever contains completed lines, so the two agree on everything that reaches the server.

**Placeholder scan.** No `TBD`, no "add error handling", no "similar to Task N". Every code step carries the actual code; every test step carries the actual test.

**Type consistency.** `PurchaseView` is defined once in Task 4 and reused verbatim in Tasks 6, 7, 8 and 9 — same nine fields, same order, with `Status` as the enum server-side and a string union client-side because enums serialize as strings. `PurchaseStockLine` and `MovementContext` are defined in Task 4 and used by name in Task 6. `PurchaseLineInput`, `PurchaseTotals.LineTotal`, `.Total` and `.RejectDuplicateVariants` are defined in Task 2 and called in Task 4. `VariantRules.IsReachable` is created in Task 1 and used in Task 4. `parseQuantity`, `parseCost`, `lineTotal` and `purchaseTotal` are defined in Task 8 Step 3 and used in Task 10.

**One thing an executor will hit.** Task 4 changes `AppendAsync`'s signature, which both M2b callers use. The plan says to pass `context: null` from each; if the build fails with "no overload takes 7 arguments", that is the step being half-applied — both call sites need the new argument, not one.
