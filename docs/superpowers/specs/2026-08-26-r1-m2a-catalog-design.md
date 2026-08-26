# R1 · M2a — Catalog

**Date:** 2026-08-26
**Status:** approved, awaiting implementation plan
**Predecessor:** R1 · M1 (tag `r1-m1`) — schema, auth, app shell, dashboard

---

## What this milestone is

Products, variants and categories: the ability to record what SlingKing sells.
It ends with a working Products screen that the owner can enter her real bag
catalogue into.

It is deliberately **not** the stock ledger. M2 was split in two after the
survey below showed the two halves are separable and that catalog is a hard
prerequisite for the ledger. M2b covers stock levels, the append-only ledger,
manual movements, FIFO layers and reconciliation.

## What M1 already built, and why that shrinks this milestone

The single most important finding of the design survey: **M1 built the entire
schema, not just the part M1 used.** All 27 tables exist, along with their
domain entities. The invariants originally scoped as M2 work are already
enforced by the database:

| Invariant | Already enforced by |
|---|---|
| Stock can never go negative | `ck_physical_non_negative`, `ck_reserved_within_physical` |
| Product codes and SKUs are unique | `ux_products_code`, `ux_product_variants_sku` |
| FIFO consumes oldest-first | `ix_layers_fifo` on `(variant_id, location_id, received_at, id) WHERE remaining_qty > 0` |
| A cost layer cannot be over-consumed | `ck_remaining_within_original`, `ck_reversed_within_quantity` |
| A consumption cannot be over-reversed | `ck_consumption_reversed_non_negative`, `ck_reversed_within_quantity` |

Also already present and reusable, so M2a builds none of it:

- `IAppDbContext` exposing `Categories`, `Products`, `ProductVariants`,
  `StockLevels`, `Locations`
- `INumberSequenceAllocator.NextAsync(key)` — gap-free under concurrency,
  proven by `NumberSequenceTests` with 20 concurrent callers
- `IClock`
- `locations` seeded with one row: `id = 1`, `Main`, `is_default = true`
- `number_sequences` seeded with `product` → prefix `BAG`, padding 6

Facts settled by reading the schema rather than by asking:

- Money is `numeric(18,2)` for prices, `numeric(18,4)` for unit costs.
  **Rupees with decimals, not paise.**
- Column limits: `products.code` 32, `product_variants.sku` 48,
  `products.name` 256, `categories.name` 128, `colour` and `size` 64 each.

## Decisions taken

| Decision | Choice | Reasoning |
|---|---|---|
| Milestone shape | Split; catalog first | Catalog is a prerequisite for the ledger, and the FIFO engine is the riskiest code in Release 1. Shipping catalog first means the owner can enter her real catalogue while the hard part is still being built. |
| Product ↔ colour | One product, many variants | The owner thinks "12 slings — 5 tan, 7 black", not "two products". Matches the schema's design intent. |
| Categories | Flat | `parent_id` exists so nesting is possible later with no migration, but a tree UI is real work a bags catalogue does not need. |
| Photos | None | The catalogue's job is knowing what is owned, what it cost and what is left; Instagram already holds the photography. Adding an image column later is a small migration. Images are also what actually fills a free Supabase tier. |
| Product with no variations | Auto-creates one default variant | Stock exists only at variant level, so a product with zero variants can never hold stock. The owner should not have to know that. |
| Deletion | None, anywhere | Standing constraint: prefer status changes and soft deletes for transactional data. |
| Stock rows | Created eagerly, qty 0 | Approach A below. |

## Architecture

Two services in `SlingKing.Application/Catalog/`, following the `AuthService`
pattern — interface plus implementation, `IAppDbContext` as the only database
surface, no repository layer.

| Service | Methods |
|---|---|
| `CategoryService` | `ListAsync`, `CreateAsync` |
| `ProductService` | `ListAsync`, `GetAsync`, `CreateAsync`, `UpdateAsync`, `DeactivateAsync` |

Variants have no independent life — a variant is part of the product
aggregate — so they are owned by `ProductService` and have no service of their
own.

`CategoryService` stays a service rather than letting the controller query
`IAppDbContext` directly, even at two methods. M1's layering is worth more than
the file it would save.

### Approach A: eager stock rows (chosen)

When a variant is created, a `stock_levels` row is written immediately at the
default location with `physical_qty = 0`.

Costs one insert per variant. Buys three things:

1. The Products list is a plain join rather than `LEFT JOIN … COALESCE`
2. "0 in stock" is a recorded fact, not an absent row — the "no row versus
   zero" ambiguity never enters the codebase
3. M2b's reconciliation has a row to compare against from day one

The rejected alternative — lazy rows created on first movement — is a small
convenience now paid for by a `COALESCE(physical_qty, 0)` in every inventory
query M2b and M5 will ever write.

### Create is one transaction

1. `INumberSequenceAllocator.NextAsync("product")` → `BAG-000001`
2. Insert the product
3. Each variant takes `{code}-V{ordinal:00}` with the ordinal from the
   in-memory list → `BAG-000001-V01`, `BAG-000001-V02`
4. Insert a `stock_levels` row per variant at the default location, qty 0
5. Commit

An empty variant list is legitimate and yields one variant with null colour
and null size.

### Update takes the whole product

`PUT /api/v1/products/{id}` accepts name, category, description **and the
complete variant list**. The server diffs it:

- row with an `id` → update in place
- row with `id: null` → create, allocating the next SKU from the count of
  existing variants
- `isActive: false` → deactivate

All inside one transaction.

This replaces three separate endpoints (`POST /products/{id}/variants`,
`PUT /variants/{id}`, `POST /variants/{id}/deactivate`). The reason is not
tidiness. The product form is one page with one Save button, so those three
endpoints forced the client to issue up to four independent calls per save —
and a failure on the third leaves a renamed product with a new price, a
missing variant and a stale one still active. A half-saved product with no
compensating path back. One transaction cannot half-apply, which is also what
the standing constraint about transactions for critical operations requires.

**`isActive` is explicit and never inferred from absence.** A variant missing
from the array does not mean "delete it" — that is the version of this pattern
that silently destroys data the first time a bug drops a row from the list.

### Variant ordinals

The ordinal for a new variant is the count of existing variants for that
product, plus one. Because variants are soft-deleted and never removed, that
count only ever grows, so ordinals are never reused.

`ux_product_variants_sku` is the backstop. **There is deliberately no retry on
collision** — a collision requires two requests adding a variant to the same
product simultaneously, which one owner with one browser will not produce. The
unique index turns it into a loud 409 rather than corruption. Add the retry
when it actually happens.

## Migration

One migration, both halves database-enforced invariants:

```sql
create unique index ux_variant_combination
    on product_variants (product_id, coalesce(colour, ''), coalesce(size, ''))
 where is_active;

alter table product_variants
  add constraint ck_variant_threshold_non_negative
  check (low_stock_threshold >= 0);

create unique index ux_categories_name on categories (lower(name));
```

The `coalesce` is load-bearing. A plain unique index on
`(product_id, colour, size)` would **not** catch a duplicate, because Postgres
treats each `NULL` as distinct — so `(1, 'Tan', NULL)` could be inserted twice.

**The `where is_active` predicate is load-bearing too**, and was added while
writing the implementation plan. Without it, retiring "Tan" would permanently
forbid ever selling a Tan of that product again — the retired row keeps
occupying the combination for ever. The invariant that actually matches the
business is *at most one **active** variant per colour and size*, which is what
the partial index expresses.

`ux_categories_name` is on `lower(name)` so "Slings" and "slings" cannot both
exist. Without it, the duplicate check in `CategoryService` would be the only
thing holding that invariant.

`low_stock_threshold` currently has no check constraint at all, so a negative
threshold is possible today.

No new tables. The table count stays at 28.

## API

```
GET   /api/v1/categories
POST  /api/v1/categories
GET   /api/v1/products?search=&includeInactive=
GET   /api/v1/products/{id}
POST  /api/v1/products
PUT   /api/v1/products/{id}
POST  /api/v1/products/{id}/deactivate
```

All `[Authorize]`. **No delete endpoint exists anywhere.**

`search` matches product name, product code and variant SKU.

### Semantics fixed here so they cannot be read two ways

- **`includeInactive` applies to products only.** Variants are always returned
  with the product, each carrying its own `isActive`; the screen marks the
  inactive ones. There is no separate flag for variants.
- **Deactivating a product does not touch its variant rows.** The product's
  `is_active` is authoritative for whether the product is available;
  reactivating it therefore restores exactly the variant states it had, rather
  than silently reviving variants that were retired individually.
- **`code` and `sku` are never editable.** `PUT /products/{id}` ignores them if
  present rather than erroring, because the form round-trips the values it was
  given. They are generated once and are immutable thereafter.
- **`totalStock` on the list is always 0 in M2a.** Nothing can move stock until
  M2b, so the field is truthful rather than absent — and the eager
  `stock_levels` rows mean it is read from a real row, not synthesised.

### Errors

| Status | When |
|---|---|
| 400 | Input validation, via DataAnnotations on the request DTOs |
| 404 | Unknown product or category id |
| 409 | Duplicate colour/size combination within a product |

Validation stays on DataAnnotations, matching `LoginRequest`. Cross-entity
rules — category exists and is active, combination is unique — sit in the
service. FluentValidation is installed but unused and stays that way: a second
validation idiom for these rules buys nothing.

## Frontend

**Products** (`/products`) — product rows showing code, name, category and
total stock, expanding to variants with SKU, colour/size, price and per-variant
stock. Search box; include-inactive toggle. Empty state in the dashboard's
voice.

**Product form** — one page for create and edit. Name, category, description,
then a variant table edited inline. Code and SKU are shown but unwritable,
generated on save. An empty variant table is legitimate.

**Categories** — no screen. A dropdown on the product form with an inline
**+ New category**.

Top rail gains nothing; **Products** stops being a dead link. Inventory and
Purchases keep the not-found page until M2b and M3.

TanStack Query — installed in M1 and currently unused — handles fetching: list
queries keyed by their filters, mutations invalidating the list on success.
Every number carries the `.num` class, so prices and counts render in tabular
JetBrains Mono.

### Deliberately cut

| Cut | Add when |
|---|---|
| Categories screen, rename, deactivate | The owner wants to fix a typo. `PUT /categories/{id}` is roughly twenty minutes. **This is the one genuinely arguable cut: until it exists, a mistyped category name can only be corrected in SQL.** |
| Category filter on the list | The catalogue outgrows what search over name/code/SKU can handle |
| SKU collision retry | A collision actually occurs |
| Pagination | The list feels slow — a few hundred rows in a browser will not |

## Testing

Test-first, matching M1.

**Unit** — SKU formatting, ordinal computation, validation rules. No database.

**Integration**, against the guarded `test` schema:

- Creating a product allocates `BAG-00000N`; variants get `-V01`/`-V02`; one
  `stock_levels` row appears per variant at the default location with qty 0
- Two sequential creates yield `BAG-000001` then `BAG-000002` — this proves
  `ProductService` is wired to the allocator. It does **not** re-prove
  concurrency: `NumberSequenceTests` already banked that property with 20
  concurrent callers, and re-testing it through the API is duplicate coverage.
- A duplicate colour/size is rejected **by the database**, proven with a direct
  SQL insert rather than by the service refusing first
- Deactivation leaves the row present with `is_active = false`
- A negative `low_stock_threshold` is rejected
- An unknown or inactive category is rejected

**Browser** — the screens get a Playwright walkthrough against the real API,
as M1's did, beginning with a stub probe.

## Gate

Each step passes or it does not.

1. Create a product with three variants → codes and SKUs exactly as specified;
   three `stock_levels` rows at qty 0 at the default location
2. Two sequential creates → `BAG-000001`, `BAG-000002`, no gap
3. A duplicate `(product_id, colour, size)` inserted **directly in SQL** is
   rejected by the database
4. No route anywhere deletes a catalog row — asserted by enumerating the
   application's routes, not by reading the controllers
5. Migrations apply to an empty schema; the table count is still 28
6. Full suite green, zero warnings under `TreatWarningsAsErrors`, `tsc -b`
   clean
7. Browser: create a product with two colours, find it via search, edit a
   price, deactivate it — all against the real API

## Out of scope

- Stock movements of any kind, the ledger, FIFO consumption, reconciliation — **M2b**
- Suppliers, purchases, cost layer creation — **M3**
- Bulk opening-stock import — **M4**
- Blocking deactivation of a variant that still holds stock — **M2b**, because
  there is no stock in M2a for the rule to check
- Product photographs — revisit if a storefront is ever built

## Standing constraints this milestone must honour

- Environment variables for connection strings, JWT secrets, API and CORS
  configuration; no hardcoded secrets
- No internal exceptions reach the frontend
- No deletion of transactional data; status changes and soft deletes instead
- Database transactions for critical operations
- Everything free: no paid tooling or licences
