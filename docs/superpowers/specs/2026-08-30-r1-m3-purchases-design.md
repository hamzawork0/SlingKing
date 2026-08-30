# R1 · M3 — Purchases and suppliers

**Date:** 2026-08-30
**Status:** approved, awaiting implementation plan
**Predecessor:** R1 · M2b (tag `r1-m2b`) — stock ledger, FIFO cost, recount

---

## What this milestone is

Recording what you bought, from whom, and what it cost — so that real cost
enters the FIFO layers instead of being typed by hand on the Inventory screen.

It ends with a Purchases screen the owner uses after a trip to the wholesale
market, and a **Purchases** link in the top rail that finally goes somewhere.

## What M1 already built, and why that shrinks this milestone

As with M2a and M2b: the schema is complete and constrained. This milestone
adds **no tables and no columns**.

| Table | Already present |
|---|---|
| `suppliers` | Name (256), contact person, phone, email, address, notes, `is_active` |
| `purchases` | `number` (32) with `ux_purchases_number`, `supplier_id` FK `ON DELETE RESTRICT`, `purchase_date`, `status` as smallint, `total_amount numeric(18,2)` nullable, `notes`, `completed_at` |
| `purchase_items` | `purchase_id` FK `ON DELETE CASCADE`, `variant_id`, `quantity` with `ck_purchase_item_quantity_positive`, `unit_cost numeric(18,4)` nullable, `line_total numeric(18,2)` nullable |

Also present and reusable:

- `number_sequences` seeded with `purchase` → prefix `PUR`, padding 6
- `INumberSequenceAllocator.NextAsync("purchase")` — gap-free under
  concurrency, banked by `NumberSequenceTests` with 20 concurrent callers
- `InventoryTransactionType.Purchase`, `CostLayerSourceType.Purchase`,
  `InventoryReferenceType.Purchase`, `PurchaseStatus.{Draft,Completed,Cancelled}`
- `InventoryTransaction.ReferenceType` / `ReferenceId` — written for the first
  time in this milestone; every movement M2b produced left them null
- The whole M2b write path: the row lock, `AppendAsync`, `FifoCostEngine`

Facts settled by reading the schema rather than by asking:

- `purchases.total_amount` is `numeric(18,2)`; `purchase_items.unit_cost` is
  `numeric(18,4)`; `line_total` is `numeric(18,2)`
- `suppliers.name` is capped at 256, `phone` at 32
- The supplier FK is `RESTRICT`, so a supplier with purchases can never be
  deleted even if a delete path were ever written

## Decisions taken

| Decision | Choice | Reasoning |
|---|---|---|
| Buying flow | Buy and receive together; purchases are created **already Completed** | The owner walks into a wholesale market, pays, and carries the bags home. Purchase and arrival are one event. `Draft` stays in the enum unused — ordering ahead later costs a status change, not a migration. |
| Corrections | **Cancel and re-record.** No editing. | Editing rewrites what a past cost layer said, and if any of those bags are already sold it changes profit that was already recorded. Consistent with the append-only ledger: a correction is a new fact, not an edit. |
| New designs | Product first, then purchase | Naming and pricing a bag is different thinking from recording what you spent. Letting the purchase form create products turns it into the product form with extra fields. |
| Suppliers | Name and phone, inline picker | No Suppliers screen. A dropdown with **+ New** and **Rename**, exactly the `CategorySelect` pattern from M2a. There will be about five suppliers. |
| Service shape | `PurchaseService` writes purchases; `StockService` keeps its monopoly on stock | Approach A. Nothing outside `StockService` may touch `physical_qty`, a ledger row or a cost layer. A second writer is exactly the seam M2a's Critical lived in. |
| Back-dating | The **purchase date** may be back-dated; the **cost layer** is stamped now | See "The subtle semantic" below. |
| Purchase total | Null when **any** line lacks a cost | Corrects the schema's own doc comment. See "One correction" below. |

## Architecture

`PurchaseService` and a static `PurchaseTotals` in
`SlingKing.Application/Purchasing/`, following the established pattern —
interface plus sealed implementation, `IAppDbContext` as the only database
surface, no repository layer.

| Class | Responsibility |
|---|---|
| `PurchaseService` | Purchases and their lines. Reads and writes `purchases`, `purchase_items`, `suppliers`. Never touches stock. |
| `PurchaseTotals` | Static, pure. Line totals, the purchase total, and duplicate-variant detection. No database. |
| `StockService` | Gains **one** new method. Still the only code that touches `physical_qty`, `inventory_transactions` or `inventory_cost_layers`. |

`PurchaseTotals` being static and database-free is deliberate, and follows
`FifoCostEngine`: the money arithmetic keeps a millisecond test loop only for
as long as it never learns what an `IAppDbContext` is.

### Recording one purchase

One transaction:

1. Allocate `PUR-000001` from `INumberSequenceAllocator`
2. Insert the `purchases` row (status `Completed`, `completed_at` = now) and
   its `purchase_items`
3. Save, so the lines have ids
4. Apply the stock — for every line, in **ascending variant id**: take the row
   lock, append an `InventoryTransaction` of type `Purchase` carrying
   `ReferenceType = Purchase` and `ReferenceId = purchase.Id`, raise
   `physical_qty`, and open a cost layer sourced `CostLayerSourceType.Purchase`
   with `SourceId = purchaseItem.Id`
5. Commit

**Locks are taken in ascending variant id, always.** A three-line purchase
locks three stock rows. Two purchases touching the same variants in opposite
orders deadlock in Postgres. This rule is invisible until it bites in
production at the worst possible moment, so it belongs in a comment at the
lock site, not only in this document.

**A variant may appear only once per purchase.** Two lines for the same colour
is a typing mistake, not a quantity of two.

**Cost per line is optional.** A blank cost opens an uncosted layer, exactly as
`PurchaseItem.UnitCost`'s own comment says. You will normally know what you
paid, but a mixed lot bought for one price is real, and inventing a number is
the one thing this design forbids everywhere else.

### Cancelling one purchase

Each line gets an outflow ledger row referencing the purchase, and the layer
that line created is closed.

**The reversal row is type `Purchase` with a negative `physical_delta`.** The
type names what caused the movement; the sign says which way the bags went.
M2b's rule that "direction comes from the type, never from the sign" governs
the **movements API**, where a signed quantity would make "five damaged" and
"five arriving" indistinguishable to a human. Inside the ledger,
`physical_delta` has always been signed, and `Adjustment` already runs both
ways. `Purchase` is the second bidirectional type, and like `Adjustment` it is
**not postable**: `RecordMovementAsync` already refuses it by name, and
`PurchaseService` is its only producer.

**Cancellation consumes that purchase's own layers, never the oldest.** This is
the trap in this milestone. Ordinary outflows walk FIFO. Running cancellation
through the same path would take stock back out of whichever layer happened to
be oldest — leaving every quantity correct, the costs silently wrong, and
nothing on screen looking odd. Cancellation targets layers by `source_id`.

**It refuses if any of those layers has been drawn from at all.** The check is
exact: `remaining_qty` must still equal `original_qty` on every layer the
purchase created. Partly consumed and fully consumed both refuse.

> Some of these bags have already left. Record what actually happened on the
> Inventory screen instead.

A cancelled purchase stays visible with its reversal in the ledger. That is
what makes the history explain itself, rather than showing bags appearing and
vanishing for no stated reason.

### One correction, carried from the design discussion

The schema's doc comment on `Purchase.TotalAmount` reads *"Sum of the lines
that have a cost. Null when no line is costed."* That is wrong by this
project's own rules.

A purchase with three lines where two costs are known and one is not would, by
that comment, report the sum of the two — a total that **understates what was
actually paid** and prints on screen as a fact. It is the same defect
`ConsumptionPlan.TotalCost` and `stockValue` exist to prevent, arriving through
a new door.

**`TotalAmount` is null when *any* line lacks a cost.** The doc comment is
corrected to match. That comment is the only change M3 makes under
`SlingKing.Domain` — no behaviour, no schema, but the misleading sentence
should not survive this milestone.

### Two predictions from the M2b spec, corrected

**Purchases do not make the recount-shortfall divergence reachable.** M2b's
spec predicted they would, and deferred a fix to this milestone on that basis.
Tracing it: a purchase adds stock and layers in equal measure, and cancellation
removes them in equal measure or refuses outright. The invariant holds. The
deferred fix does not come due here, and an integration test checks that claim
rather than trusting it.

**Backfilling an unknown cost stays cut, for good.** M2b deferred it to "M3,
where the backfill has a real source." That was wishful: a purchase recorded
without a cost provides no source, and an opening-stock layer from before the
records began has none either. Editing a past cost is the one mutation the
optional-cost design exists to prevent. It stops being promised.

## Migration

**None.** No tables, no columns, no indexes, no constraints. The table count
stays at 28. Any migration appearing in this milestone means something has been
designed that the schema did not anticipate, and should be questioned before it
is written.

## API

```
GET   /api/v1/suppliers
POST  /api/v1/suppliers
PUT   /api/v1/suppliers/{id}
GET   /api/v1/purchases?search=&includeCancelled=
POST  /api/v1/purchases
POST  /api/v1/purchases/{id}/cancel
```

All `[Authorize]`. **No delete endpoint anywhere, and no edit endpoint for a
purchase.**

The three supplier routes are the category routes with a different noun — same
shape, same inline picker, same rename, same duplicate-name conflict.

There is deliberately **no `GET /purchases/{id}`**: the list carries each
purchase's lines and rows expand in place, exactly as Products expands to its
variants. That is one fewer endpoint and one fewer screen for a page opened a
few times a month. It accepts the same N+1 the products list already accepts,
with a `ponytail:` comment naming the ceiling.

`search` matches the purchase number and the supplier name.

### Request

```
{ supplierId, purchaseDate, notes?, lines: [{ variantId, quantity, unitCost? }] }
```

### The subtle semantic, stated so nobody "fixes" it

**A purchase date may be back-dated. It does not move those bags' place in the
cost queue.**

You come back from the market on Thursday and record it on Saturday, dated
Thursday. That works. But the cost layer's `received_at` is stamped **now**,
not Thursday.

M2b bought a large simplification by making insertion order *be* FIFO order: no
past cost can ever need recalculating. If a back-dated purchase slotted into the
queue ahead of stock already sold on Friday, Friday's recorded cost would become
wrong — and it is immutable, so it would stay wrong while continuing to look
right. `purchase_date` is the owner's record of when she bought;
`received_at` is when the system learned of it.

`InventoryCostLayer.ReceivedAt`'s own comment says a back-dated purchase "takes
its rightful place at the front". That is the future this deliberately does not
build, and the reason is recorded here so the comment does not read as an
unimplemented requirement.

### Semantics fixed here so they cannot be read two ways

- **A purchase is created `Completed`.** No draft, no receive step, no status
  transition other than cancellation.
- **`includeCancelled` defaults to false**, mirroring Products' `includeInactive`.
- **Cancelling is not idempotent.** Cancelling an already-cancelled purchase is
  a 409, not a silent success — the caller believes something is happening.
- **A retired variant may still appear on an existing purchase.** History does
  not change because a product was retired later. Recording a *new* purchase
  against a retired variant is refused, by the same rule `StockService` applies.

  That rule currently lives as `private static readonly
  Expression<Func<ProductVariant, bool>> IsReachable` inside `StockService`,
  where `PurchaseService` cannot reach it. Copying it would create two
  definitions of "which variants can be used" that must not drift — the exact
  objection that moved `AppException` out of the catalog module in M2b. **Move
  it to a shared public static** in `SlingKing.Application/Catalog/`, and have
  both services use that one expression. It stays an `Expression`, not a
  predicate, so it keeps composing into `.Where()` and translating to SQL.
- **`TotalAmount` is null when any line is uncosted** — never a partial sum.

### Errors

| Status | When | Sentence |
|---|---|---|
| 400 | No lines | Add at least one bag to this purchase. |
| 400 | Quantity below one | Enter how many, as a number above zero. |
| 400 | Same variant twice | Tan appears twice. Put the whole quantity on one line. |
| 400 | Negative cost | A cost cannot be negative. |
| 404 | Unknown or inactive supplier | That supplier no longer exists. |
| 404 | Unknown or retired variant | That item no longer exists. |
| 404 | Unknown purchase | That purchase does not exist. |
| 409 | Cancelling a cancelled purchase | That purchase is already cancelled. |
| 409 | Cancelling after stock has moved | Some of these bags have already left. Record what actually happened on the Inventory screen instead. |
| 409 | Duplicate supplier name | A supplier named "Sadar Bazaar" already exists. |

Validation stays on DataAnnotations for shape; rules needing the database sit
in `PurchaseService` and surface as `AppException`.

## Frontend

**Purchases** (`/purchases`) — number, date, supplier, total, status. Rows
expand to their lines: item, quantity, cost each, line total. Search box;
**Show cancelled** toggle, off by default. Cancelled rows read muted with the
total struck through — the money did not happen. Empty state: *"Nothing bought
yet. Record a purchase when you next come back from the market."*

**Record a purchase** (`/purchases/new`) — supplier picker with inline **+ New**
and **Rename**, date defaulting to today, notes, then a line table edited in
place: variant, quantity, cost each, line total. **The running total stays
visible while lines are entered**, because it is the number being checked
against the bill in hand; a total that appears only after saving is a total
that arrives too late to catch the mistake.

**The variant picker** is the one genuinely new control. The product form's
category dropdown picks from about five things; this picks from every colour of
every bag in the catalogue. It shows product name, colour and size together and
is searchable rather than a bare `<select>`.

**Cancel** lives on the expanded row, behind a confirmation naming what it will
undo, in the voice of Products' retire confirmation.

Top rail: **Purchases** stops being the last dead link. `NotFound.tsx` loses
its last promise and becomes a plain not-found page.

Every number carries the `.num` class.

### Deliberately cut

| Cut | Add when |
|---|---|
| A Suppliers screen | Looking a supplier up becomes a thing she does, rather than picking one from five |
| `GET /purchases/{id}` and a detail page | The list rows stop being enough |
| Purchase list pagination | The list feels slow — a few hundred rows in a browser will not |
| Supplier email, address, contact person | She wants them; the columns already exist |
| Photographs of bills | A storefront or an accountant needs them |

## Testing

Test-first, matching M2a and M2b.

**Unit — `PurchaseTotals`, no database.**

- A line total is quantity × cost, and null when the cost is unknown
- The purchase total is null when **any** line is uncosted — proved
  load-bearing by making it sum only the known lines and watching the test fail
- A variant appearing twice is detected

**Integration**, against the guarded `test` schema:

- Recording allocates `PUR-000001`, moves every line's stock, and opens one
  layer per line sourced `Purchase`
- Every ledger row carries `ReferenceType = Purchase` and the purchase's id
- Two sequential purchases yield `PUR-000001` then `PUR-000002`
- An uncosted line opens a null-cost layer, and `TotalAmount` is null
- Duplicate variant → 400; unknown supplier → 404; no lines → 400; a purchase
  against a retired variant → 404
- **Cancellation takes back its own layers.** Two purchases of the same variant
  at different costs; cancel the *second*; the first's layer must be untouched
  and the surviving stock must still be worth the first purchase's price.
  Asserted on the surviving layer's **cost**, not merely on quantities — run
  through FIFO this passes on quantity while unwinding the wrong money.
- Cancelling after any of that stock has been damaged → 409, nothing written
- Cancelling twice → 409
- **Back-dating does not reorder the queue.** A purchase dated last month but
  recorded now must sit *behind* stock already on hand, proved by consuming
  afterwards and checking which cost came out.
- **Both M2b invariants still hold** after a purchase, a cancellation and
  another purchase — the test that checks this spec's claim rather than
  trusting it.

**Concurrency — with an honest caveat.** Two purchases touching the same two
variants in opposite line orders, submitted together; both must complete. Then
the proof: remove the ascending-id ordering and run repeatedly, expecting a
deadlock. Unlike M2b's lost-update test, which failed 3 of 3 reliably, this one
must *lose* a race to fail. **If it cannot be made to fail within a reasonable
number of runs, the honest report is that the ordering is unproven by test and
rests on the argument alone** — not a quietly green test implying otherwise.

**Frontend — vitest.** The form's running total: quantity × cost summed across
lines, `—` when any line is uncosted, and an empty cost box read as unknown
rather than as zero. The `Number("") === 0` trap has produced one real defect
and been caught twice; it gets a test every time it appears.

**Browser** — a Playwright walkthrough against the real API, beginning with a
stub probe: a garbage bearer must return 401 before any result is trusted.

## Gate

The tests are the gate. What is left is what no test covers:

1. Full suite green, zero warnings under `TreatWarningsAsErrors`, `tsc -b` clean
2. Migrations apply to an empty schema; the table count is still 28
3. The lock-ordering result reported honestly, whichever way it lands
4. Browser: record a two-line purchase, watch both items' stock rise, cancel it,
   watch it fall back and the layers close — against the real API
5. A whole-branch review before merge. M2a's found a Critical that per-task
   reviews structurally could not; M2b's found four Importants including one
   hole in the design's own reasoning.

## Out of scope

- Partial receipts — the buying flow is buy-and-carry-home
- Returns to a supplier
- Payments, credit, or what is owed
- Editing a purchase — cancel and re-record is the mechanism
- Backfilling an unknown cost — cut for good, not deferred again
- Sales, orders, customers — **R2**
- Multiple locations — the column is carried everywhere; the UI is not built

## Standing constraints this milestone must honour

- Environment variables for connection strings, JWT secrets, API and CORS
  configuration; no hardcoded secrets
- No internal exceptions reach the frontend
- No deletion of transactional data; status changes and soft deletes instead
- Database transactions for critical operations involving inventory and
  purchases
- Everything free: no paid tooling or licences
