# R1 · M2b — Stock ledger

**Date:** 2026-08-30
**Status:** approved, awaiting implementation plan
**Predecessor:** R1 · M2a (tag `r1-m2a`) — products, variants, categories

---

## What this milestone is

Knowing how many of each bag exist, and what they cost. It ends with an
Inventory screen the owner can put real counts into, and a movement history
that answers "where did those four go?".

Quantities and cost tracking ship **together**. Splitting them would mean
building damage, loss and personal use with no cost attached and then
rewriting all three in M3 when purchases arrive. The FIFO engine is the
riskiest code in Release 1; it is better built once, against movements that
already exist, than retrofitted.

## What M1 already built, and why that shrinks this milestone

As with M2a: the schema is already complete and already constrained. This
milestone adds **no tables and no columns**. Everything below exists today.

| Table | Guarantees already enforced |
|---|---|
| `stock_levels` | `ck_physical_non_negative`, `ck_reserved_non_negative`, `ck_reserved_within_physical`; `available_qty` is a stored generated column — `physical_qty - reserved_qty` — with no code path able to compute it differently |
| `inventory_transactions` | Append-only by convention; `physical_delta` and `reserved_delta` on one row; nullable `unit_cost` / `total_cost`; `ix_inventory_transactions_variant_time` on `(variant_id, location_id, occurred_at)` |
| `inventory_cost_layers` | `ck_layer_original_positive`, `ck_layer_remaining_non_negative`, `ck_remaining_within_original`, `ck_layer_cost_non_negative`; `ix_layers_fifo` on `(variant_id, location_id, received_at, id) WHERE remaining_qty > 0` |
| `inventory_cost_consumptions` | `ck_consumption_quantity_positive`, `ck_consumption_reversed_non_negative`, `ck_reversed_within_quantity`; FK to the layer is `ON DELETE RESTRICT` |

Note that `ck_layer_cost_non_negative` is `unit_cost >= 0`, and a Postgres
CHECK evaluating to NULL passes. **A null unit cost is permitted by design** —
that is the mechanism the "unknown cost stays unknown" rule below rests on.

Also present and reusable, so M2b builds none of it:

- `IAppDbContext` already exposes `StockLevels`, `InventoryTransactions`,
  `InventoryCostLayers`, `InventoryCostConsumptions`, `Locations`, and
  `Database` for explicit transactions
- `IClock`
- `locations` seeded with one row: `id = 1`, `Main`, `is_default = true`
- M2a creates a `stock_levels` row per variant eagerly at qty 0, so every
  variant already has a row to update — there is no create-on-first-movement
  path to write

Facts settled by reading the schema rather than by asking:

- `inventory_transactions.unit_cost` and `inventory_cost_layers.unit_cost` are
  `numeric(18,4)`; `total_cost` is `numeric(18,2)`
- `inventory_transactions.reason` is capped at 256 characters
- Quantities are `int` throughout — no fractional stock

## Decisions taken

| Decision | Choice | Reasoning |
|---|---|---|
| Milestone shape | Quantities and FIFO cost together | Splitting means writing damage/loss/personal-use twice |
| Back-dating | Not supported; every movement is stamped now | **Insertion order is therefore FIFO order.** No past cost can ever need recalculating, which removes an entire class of bug from the engine. `received_at` still exists, so back-dated purchases remain possible in M3 without a migration. |
| Service shape | `StockService` and `FifoCostEngine`, separate | Quantity arithmetic and cost arithmetic fail differently and are tested differently. The engine is pure enough to unit-test without Postgres, and only stays that way if it never touches stock levels or the ledger. |
| Corrections | Compensating movements only | The ledger is append-only. No edit, no delete, no undo on any historical row. |
| Movement direction | Carried by `type`, never by the sign of `quantity` | A signed quantity lets `-5` on a Damage mean "five damaged" to a human and "five arriving" to the server, indistinguishably after the fact |
| Locations | Default location only, id resolved server-side | The column is carried everywhere; the UI never shows it while there is one location |
| Reservations | Not in M2b | `reserved_delta` stays 0 throughout. Reservations need orders, which are R2. |
| Exception type | `CatalogException` moves to `Application/Common/AppException.cs` | It is status code + message + three factories, with nothing catalog-specific in it. A byte-identical `InventoryException` beside it is two copies that must not drift. |

### Movement types in scope

**Four postable types, each with exactly one direction.**

| Type | Direction | Cost effect |
|---|---|---|
| `OpeningStock` | in | Creates a layer, `CostLayerSourceType.OpeningStock` |
| `Damage` | out | Consumes |
| `Loss` | out | Consumes |
| `PersonalUse` | out | Consumes |

`Adjustment` is a fifth type the ledger records, but **it cannot be posted.**
It is produced only by the recount endpoint, in whichever direction the count
implies. Offering it directly as well would mean offering two ways to change a
quantity — one where you say what you counted and the server does the
arithmetic under a lock, and one where you compute `+3` yourself from a number
your screen loaded a minute ago. The second is exactly the mistake the recount
endpoint exists to prevent, so it is not built.

The payoff runs through the whole design: every postable type has a fixed
direction, so there is no "either" branch, no direction inference, and
`OpeningStock` is the only type that carries a cost.

`Purchase` is M3; `Sale`, `Return`, `Reservation` and `ReservationRelease` are
R2. Posting any of those — or `Adjustment` — is a 400, refused by name rather
than silently accepted by an endpoint with no cost path for it.

## Architecture

Two classes in `SlingKing.Application/Inventory/`, following the M2a pattern —
interface plus implementation, `IAppDbContext` as the only database surface,
no repository layer.

| Class | Responsibility |
|---|---|
| `StockService` | The write path and all reads. Owns the transaction. |
| `FifoCostEngine` | `AddLayer` and `Consume`. Touches neither `stock_levels` nor `inventory_transactions`. |

### One movement is one transaction

`StockService.RecordMovementAsync` opens an explicit transaction and, in order:

1. Locks the variant's `stock_levels` row
2. Appends the `inventory_transactions` row
3. Updates `physical_qty` on the stock level
4. Delegates cost: inflow → `FifoCostEngine.AddLayer`; outflow →
   `FifoCostEngine.Consume`, whose result is stamped back onto the ledger
   row's `unit_cost` and `total_cost`
5. Commits

Steps 2 and 4 are in that order because the consumption rows reference the
transaction id — the ledger row must exist before they can point at it, and
its cost columns are filled before the same transaction commits. That is one
transaction containing two saves, not two transactions.

### Concurrency

The first statement inside the transaction takes a row lock:

```sql
select 1 from stock_levels
 where variant_id = @variantId and location_id = @locationId
   for update;
```

Two tabs both reading "12 in stock" and both posting an outflow of 8 is the
motivating case. For that one, `ck_physical_non_negative` would also catch it.
The lock exists for the case the check constraint cannot see: two recounts,
both landing on non-negative numbers, where the second silently overwrites the
first. One statement covers both.

`ponytail:` a row lock serialises all movement on one variant. Fine for one
owner; revisit only if a second writer ever exists.

### The three cost rules

**Unknown cost stays unknown.** If a consumption crosses any layer with a null
`unit_cost`, the resulting `total_cost` on both the consumption and the ledger
row is **null**, not a partial sum. A partial total understates the real cost
and reads on screen as a fact.

**Cost is copied, never joined.** `inventory_cost_consumptions.unit_cost` is
written at consumption time from the layer's rate. Editing a price later
cannot rewrite historical profit. The single permitted exception is a backfill
that fills a null, which is not in this milestone.

**If layers do not cover stock, refuse loudly.** `Consume` walking the FIFO
index and running out before satisfying the quantity means the two records
have diverged. It throws rather than consuming what it found:

> Stock and cost records disagree for this item — run a recount to correct it.

That sentence names the fix, because a recount genuinely is the fix — see the
exemption below, which is what makes the advice honest rather than circular.

### Recount

`RecountAsync(variantId, countedQuantity)` reads the current quantity **inside
the same transaction, after taking the lock**, computes the difference, and
writes an `Adjustment` down the same internal path as any other movement —
the same lock, the same ledger append, the same cost delegation. It is the
only producer of that type, which is why the type is not postable.

A difference of zero records nothing and says so.

**Recount is the one path exempt from "refuse loudly".** Every other outflow
throws when layers run short. A recount does not: it consumes oldest-first,
and if the layers are exhausted before the difference is satisfied it stops
there and commits anyway.

Without that exemption the design eats itself. Consider `physical_qty = 12`
with layers holding only 8. A damage of 10 throws and says "run a recount".
The recount then needs to consume 10 as well, hits the same wall, and throws
the same sentence — leaving the owner with an instruction that cannot be
followed. The counted number is authoritative by definition, so a recount is
allowed to bring the cost records into line with it. That is precisely what
makes it the fix.

**A recount-up creates a layer with a null `unit_cost`.** The endpoint takes
only a counted quantity; nobody has said what the extra stock cost, and the
"unknown stays unknown" rule forbids inventing zero. Cost can be attached to
those units in M3, where a backfill has a real source.

## Migration

**None.** No tables, no columns, no indexes, no constraints. The table count
stays at 28. Any migration appearing in this milestone is a signal that
something has been designed which the schema did not anticipate, and should be
questioned before it is written.

## API

```
GET   /api/v1/inventory?search=&lowOnly=
GET   /api/v1/inventory/{variantId}
POST  /api/v1/inventory/movements
POST  /api/v1/inventory/{variantId}/recount
```

All `[Authorize]`. **No delete endpoint, and no update endpoint for any
historical row.**

`search` matches product name, product code and variant SKU — the same fields
`/products` already searches, so the two screens behave the same way.

### Movement request

```
{ variantId, type, quantity, unitCost?, reason? }
```

- `quantity` is always positive. Direction comes from `type`.
- `unitCost` is accepted only on `OpeningStock`, the one inflow, and is
  **optional** — omitted means genuinely unknown.
- `reason` is required for `Damage`, `Loss` and `PersonalUse`. Those are the
  movements read back months later. It is the only free-text field: a second
  optional `notes` box beside it is one nobody fills, on a form this small.

### Semantics fixed here so they cannot be read two ways

- **The list is one row per active variant**, not per product. Stock is a
  property of a colour-and-size.
- **`lowOnly` compares `physical_qty <= low_stock_threshold`** and, because a
  threshold of 0 would otherwise flag every empty variant forever, excludes
  variants whose threshold is 0.
- **Outflows check `physical_qty`, not `available_qty`.** `reserved_qty` is
  always 0 in M2b; the distinction becomes real in R2 and the check moves then.
- **The list carries no money.** It reads `stock_levels` and `product_variants`
  and nothing else. A value column would make every list render touch the cost
  layers for a figure the item page already shows. If a total worth is wanted
  later it belongs at the top of the list as one figure — one query, not one
  per row.

### Errors

| Status | When | Sentence |
|---|---|---|
| 400 | Quantity ≤ 0 | Enter how many, as a number above zero. |
| 400 | Reason missing on Damage/Loss/Personal use | Say briefly what happened, so this makes sense later. |
| 400 | Type not one of the four postable ones | That kind of movement cannot be recorded here yet. |
| 400 | `unitCost` sent on anything but `OpeningStock` | A cost can only be set on stock coming in. |
| 404 | Unknown or inactive variant | That item no longer exists. |
| 409 | Outflow larger than stock | Only 3 in stock. Record a recount first if that is wrong. |
| 409 | Cost layers short of stock | Stock and cost records disagree for this item — run a recount to correct it. |

Validation stays on DataAnnotations for shape, matching M2a; the rules above
that need the database sit in `StockService` and surface as `AppException`.

## Frontend

**Inventory** (`/inventory`) — one row per active variant: item (product name,
colour/size beneath), SKU, in stock, low at. Search box; **Low stock only**
toggle. Low rows carry `--color-flag`, the one place on the screen that earns
colour. Empty-ish state: *"Nothing counted yet. Open an item and record its
opening stock."*

**Item** (`/inventory/:variantId`) — current quantity large at the top, then
movement history newest-first: date, what happened, the change, the running
total, the reason. Below it, one figure: what this stock is worth, or `—` if
any of it has an unknown cost. Not a per-layer breakdown — the FIFO queue is
machinery, and its internals are accountant UI for a one-person bag business.
History is the body of the page, because this page is the answer to "where did
those four go?".

Two controls, deliberately not one:

- **Record a movement** — type, quantity, reason. Quantity is what *changed*.
- **Recount** — one field. Quantity is what you *counted*. Shows the
  arithmetic before committing: *"Counted 9, system says 12 — this records a
  loss of 3."* You confirm the sentence, not the number.

A single form with a mode toggle puts "12" in the same box meaning "twelve
more" or "twelve total" depending on a control that may not be noticed.

One line under the history heading states that entries cannot be edited and
that a mistake is corrected by recording its opposite — so the missing Edit
button reads as deliberate rather than unbuilt.

Top rail: **Inventory** stops being a dead link. Purchases keeps the not-found
page until M3, and that page's copy is corrected to stop promising Products.

Every number carries the `.num` class.

### Deliberately cut

| Cut | Add when |
|---|---|
| A `/verify` reconciliation endpoint | Something other than `StockService` can write to the ledger |
| Bulk opening-stock entry | Entering counts one at a time actually hurts — **M4** covers import |
| Movement history pagination | A variant accumulates enough movements for the page to feel slow |
| Editing a layer's unknown cost | M3, where the backfill has a real source |
| Blocking variant deactivation while stock is held | Immediately after this milestone; it is an M2a deferral that only becomes checkable now |

## Testing

Test-first, matching M2a.

**Unit — `FifoCostEngine`, no database.**

- A consumption crossing two layers apportions correctly
- A consumption exhausting a layer exactly leaves `remaining_qty = 0` and
  moves on
- A consumption crossing a null-cost layer yields a null total, not a partial
- Layers short of the requested quantity throw rather than under-consuming

**Integration**, against the guarded `test` schema:

- Opening stock creates one layer and sets `physical_qty`
- Damage consumes oldest-first across two layers of different cost
- An outflow past stock is refused with the counted sentence, and nothing is
  written — the ledger row count is unchanged
- Recount up creates a layer; recount down consumes; recount to the same
  number records nothing
- A missing reason on Damage is a 400
- A `Purchase` type is a 400, and so is `Adjustment` — the recount endpoint is
  the only way to produce one
- `unitCost` on a `Damage` is a 400
- With layers deliberately short of `physical_qty`: an ordinary outflow is
  refused with the disagreement sentence, and a recount over the same
  shortfall **succeeds** — the exemption, proven both ways in one test
- **Append-only, behaviourally.** Record three movements, capture the first
  ledger row's every column, record three more, re-read it and assert nothing
  changed. Route enumeration cannot prove this — a POST can update a row as
  easily as a PUT can — so it is proven by observation rather than by shape.
- **Reconciliation script**: opening stock, damage, recount up, recount down,
  personal use — then assert both invariants below

**Concurrency — one test.** Two overlapping transactions against one variant;
the second must observe the first's result. It is the only test here that
would pass by luck without the row lock, so it is proven by removing the lock
and watching it go red before the lock goes back.

**Frontend — vitest.** The recount sentence renders correctly, including the
zero-difference case. An empty quantity field does not post a zero — the same
`Number("") === 0` trap that produced the ₹0.00 bag in M2a.

**Browser** — a Playwright walkthrough against the real API, beginning with a
stub probe: a garbage bearer must return 401 before any result from the
session is trusted.

### The invariants

1. `sum(physical_delta)` across the ledger for a variant equals
   `stock_levels.physical_qty`
2. `sum(remaining_qty)` across that variant's open layers equals
   `physical_qty` — whenever every open layer has a known cost

## Gate

The tests above are the gate. Restating them here as a numbered list would be
the same assertions with different punctuation, and two copies of a checklist
drift. What is left is the part no test covers:

1. Full suite green, zero warnings under `TreatWarningsAsErrors`, `tsc -b`
   clean
2. Migrations apply to an empty schema; the table count is still 28
3. The concurrency test has been **seen to fail** with the row lock removed,
   not merely seen to pass with it
4. Browser: record opening stock, record a damage with a reason, run a
   recount, read the history back — all against the real API, after a stub
   probe
5. A whole-branch review before merge. M2a's found a Critical that six
   per-task reviews structurally could not, and this milestone has more seams
   than that one did.

## Out of scope

- Purchase orders and suppliers — **M3**, where supplier cost enters
- Sales, returns, reservations — **R2**
- Multiple locations — the column is carried everywhere; the UI is not built
- Stock transfers between locations
- Barcode scanning
- Back-dated movements — `received_at` exists, so M3 can add them without a
  migration

## Standing constraints this milestone must honour

- Environment variables for connection strings, JWT secrets, API and CORS
  configuration; no hardcoded secrets
- No internal exceptions reach the frontend
- No deletion of transactional data; status changes and soft deletes instead
- Database transactions for critical operations involving inventory
- Everything free: no paid tooling or licences
