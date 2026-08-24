# SlingKing R1 · M1 Foundation — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stand up the SlingKing solution end to end — four-layer .NET 8 modular monolith, the complete 27-table schema migrated onto Supabase, JWT auth with one seeded user, and a React shell that logs in — so that every later milestone adds features to a running system rather than building one.

**Architecture:** A modular monolith in four projects. `Domain` is a leaf with zero project references and zero NuGet packages. `Application` declares the interfaces it needs (`IAppDbContext`, `IClock`, `ISkuGenerator`). `Infrastructure` implements them over EF Core and Npgsql. `Api` is a thin HTTP shell. Modules are folders inside those projects, never separate assemblies. All 27 tables ship in M1's single migration, including tables no code touches until Release 2 — the schema is created once, so R2 needs no migration.

**Tech Stack:** .NET 8.0.408 SDK (`net8.0`), EF Core 8.0.30, Npgsql 8.0.11, Supabase-hosted PostgreSQL, Serilog 8.0.3, Swashbuckle 6.9.0, FluentValidation 11.12.0, BCrypt.Net-Next 4.2.1, xUnit 2.9.3 + Shouldly 4.3.0; React 19.2.8, TypeScript 5.9.3, Vite 8.2.2, TanStack Query 5, React Router 7, Tailwind 4, axios.

**Spec:** `docs/phase1-design.html` — the Phase 1 design brief. Sections 03 (solution layout), 05 (database design), 06 (FIFO), 09 (API surface), 10 (frontend) and 11 (milestones) are normative for this plan. Read it alongside this document.

---

## Global Constraints

These apply to every task. Do not restate them per task; do not violate them in any task.

- **Target framework is `net8.0` everywhere.** SDK 9.0.306 is also installed on this machine — `global.json` pins the build to 8.0.408 so it cannot drift.
- **`SlingKing.Domain` has zero `<ProjectReference>` and zero `<PackageReference>`.** Task 1 adds a test that fails the build if this is ever violated.
- **Dependency direction is `Api → Application → Domain`, and `Infrastructure → Application → Domain`.** `Application` never references `Infrastructure`.
- **No secrets in source, ever.** All configuration comes from environment variables. `.env` is gitignored in the very first commit, before any secret exists.
- **Every money column is `numeric(18,2)`; cost-layer unit costs are `numeric(18,4)`; quantities are `integer`; timestamps are `timestamptz` stored in UTC; enums are `smallint`.**
- **Cost is optional.** `inventory_cost_layers.unit_cost` and both cost columns on `inventory_cost_consumptions` are nullable. Null means *unknown*, never zero.
- **No GST or tax fields anywhere.** Not on purchases, purchase items, orders or order items.
- **Table and column names are `snake_case`;** C# properties stay `PascalCase`. `EFCore.NamingConventions` does the mapping — never hand-write a `HasColumnName` for it.
- **Integration tests run only against the `test` schema.** The fixture throws if the connection string does not target it. Never point a test at `public`.
- **Business logic never lives in a controller.** Controllers validate, delegate, and map to a response.
- **Internal exceptions never reach the client.** Everything becomes RFC 7807 `ProblemDetails`.
- **Commit at the end of every task**, with the message given in that task's final step.

### Environment variables

One name, one purpose. `backend/.env.example` documents them; `backend/.env` holds the real values and is gitignored.

| Variable | Purpose |
|---|---|
| `SLINGKING_DB_MIGRATION` | Supabase **direct** connection, port 5432. Migrations only. |
| `SLINGKING_DB_RUNTIME` | Supabase **transaction pooler**, port 6543. The running API. |
| `SLINGKING_DB_TEST` | Direct connection with `Search Path=test`. Integration tests only. |
| `SLINGKING_JWT_SECRET` | Signing key, 32+ bytes. |
| `SLINGKING_JWT_ISSUER` | Token issuer, e.g. `slingking-api`. |
| `SLINGKING_JWT_AUDIENCE` | Token audience, e.g. `slingking-web`. |
| `SLINGKING_CORS_ORIGINS` | Comma-separated allowed origins. |
| `SLINGKING_SEED_ADMIN_EMAIL` | Email of the single seeded user. |
| `SLINGKING_SEED_ADMIN_PASSWORD` | Initial password. Changed on first login. |

---

## File Structure

```
SlingKing/
├─ global.json                        SDK pinned to 8.0.408
├─ .gitignore                         .env, bin, obj, node_modules, *.sql dumps
├─ backup.ps1                         pg_dump of the live database
├─ docs/
│  ├─ phase1-design.html              the spec
│  └─ superpowers/plans/              this plan
├─ backend/
│  ├─ SlingKing.sln
│  ├─ Directory.Build.props           net8.0, nullable, warnings-as-errors
│  ├─ .env.example                    variable names, no values
│  ├─ .env                            gitignored
│  └─ src/
│     ├─ SlingKing.Domain/
│     │  ├─ DomainAssembly.cs         marker for the architecture test
│     │  ├─ Common/Enums.cs           every smallint enum in one file
│     │  ├─ Platform/                 User, RefreshToken, NumberSequence, AppSetting
│     │  ├─ Catalog/                  Category, Product, ProductVariant
│     │  ├─ Inventory/                Location, StockLevel, InventoryTransaction,
│     │  │                            InventoryCostLayer, InventoryCostConsumption
│     │  ├─ Purchasing/               Supplier, Purchase, PurchaseItem
│     │  ├─ Sales/                    SalesChannel, Customer, Order, OrderItem,
│     │  │                            OrderStatusHistory
│     │  ├─ Payments/                 Payment, Return, ReturnItem
│     │  └─ Operations/               ExpenseCategory, Expense, ImportBatch, ImportRow
│     ├─ SlingKing.Application/
│     │  └─ Common/Abstractions/      IAppDbContext, IClock, INumberSequenceAllocator,
│     │                               IPasswordHasher, ITokenService
│     ├─ SlingKing.Infrastructure/
│     │  ├─ Configuration/EnvFile.cs  .env loader, no third-party package
│     │  ├─ Persistence/AppDbContext.cs
│     │  ├─ Persistence/Configurations/  one file per module group
│     │  ├─ Persistence/NumberSequenceAllocator.cs
│     │  ├─ Persistence/Seed/DatabaseSeeder.cs
│     │  ├─ Auth/BcryptPasswordHasher.cs, JwtTokenService.cs
│     │  ├─ SystemClock.cs
│     │  └─ Migrations/
│     └─ SlingKing.Api/
│        ├─ Program.cs
│        ├─ Controllers/AuthController.cs, HealthController.cs
│        ├─ Contracts/Auth/           request and response DTOs
│        └─ Middleware/ProblemDetailsExceptionHandler.cs
│  └─ tests/
│     ├─ SlingKing.UnitTests/
│     └─ SlingKing.IntegrationTests/
│        └─ Fixtures/TestSchemaFixture.cs   the guard
└─ frontend/
   ├─ package.json, vite.config.ts, tsconfig.json
   └─ src/
      ├─ app/          router, providers, ProtectedRoute, AppShell
      ├─ api/          client.ts (axios + refresh interceptor), auth.ts
      ├─ features/auth/LoginPage.tsx
      ├─ features/dashboard/DashboardPage.tsx
      └─ lib/          money.ts, dates.ts
```

---

## Task 1: Repository skeleton and enforced layering

**Files:**
- Create: `global.json`, `.gitignore`, `backend/Directory.Build.props`, `backend/SlingKing.sln`
- Create: `backend/src/SlingKing.Domain/SlingKing.Domain.csproj`, `backend/src/SlingKing.Domain/DomainAssembly.cs`
- Create: `backend/src/SlingKing.Application/`, `SlingKing.Infrastructure/`, `SlingKing.Api/` projects
- Test: `backend/tests/SlingKing.UnitTests/Architecture/LayeringTests.cs`

**Interfaces:**
- Consumes: nothing — this is the first task.
- Produces: `SlingKing.Domain.DomainAssembly` (empty marker class used by every later reflection test); the six-project solution every later task adds files to.

- [ ] **Step 1: Initialise the repository and pin the SDK**

```bash
cd "C:/Hamza Projects/SlingKing"
git init
git branch -M main
```

Create `global.json`:

```json
{
  "sdk": {
    "version": "8.0.408",
    "rollForward": "latestPatch"
  }
}
```

Create `.gitignore`:

```gitignore
# Secrets — this entry must exist before any secret does
backend/.env
*.env.local

# Database dumps
backups/
*.sql.gz

# .NET
bin/
obj/
*.user

# Node
node_modules/
frontend/dist/

# IDE / OS
.vs/
.idea/
Thumbs.db
```

- [ ] **Step 2: Verify the SDK pin took effect**

Run: `dotnet --version`
Expected: `8.0.408` — **not** 9.0.306. If it prints 9.x, `global.json` is in the wrong directory.

- [ ] **Step 3: Create the solution and six projects**

```bash
cd backend
dotnet new sln -n SlingKing
dotnet new classlib -o src/SlingKing.Domain
dotnet new classlib -o src/SlingKing.Application
dotnet new classlib -o src/SlingKing.Infrastructure
dotnet new webapi -o src/SlingKing.Api --use-controllers
dotnet new xunit -o tests/SlingKing.UnitTests
dotnet new xunit -o tests/SlingKing.IntegrationTests

dotnet sln add src/SlingKing.Domain src/SlingKing.Application src/SlingKing.Infrastructure src/SlingKing.Api
dotnet sln add tests/SlingKing.UnitTests tests/SlingKing.IntegrationTests

rm src/SlingKing.Domain/Class1.cs src/SlingKing.Application/Class1.cs src/SlingKing.Infrastructure/Class1.cs
```

- [ ] **Step 4: Wire the dependency direction**

```bash
dotnet add src/SlingKing.Application reference src/SlingKing.Domain
dotnet add src/SlingKing.Infrastructure reference src/SlingKing.Application
dotnet add src/SlingKing.Api reference src/SlingKing.Application src/SlingKing.Infrastructure
dotnet add tests/SlingKing.UnitTests reference src/SlingKing.Domain src/SlingKing.Application
dotnet add tests/SlingKing.IntegrationTests reference src/SlingKing.Api src/SlingKing.Infrastructure
```

`SlingKing.Domain` gets no `reference` command. That is the point.

- [ ] **Step 5: Add the shared build properties**

Create `backend/Directory.Build.props`:

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
    <GenerateDocumentationFile>false</GenerateDocumentationFile>
  </PropertyGroup>
</Project>
```

- [ ] **Step 6: Create the Domain marker**

Create `backend/src/SlingKing.Domain/DomainAssembly.cs`:

```csharp
namespace SlingKing.Domain;

/// <summary>
/// Empty marker used by reflection-based tests to locate the Domain assembly
/// without taking a dependency on any particular entity type.
/// </summary>
public static class DomainAssembly
{
}
```

- [ ] **Step 7: Write the failing layering test**

Add Shouldly to the unit test project:

```bash
dotnet add tests/SlingKing.UnitTests package Shouldly --version 4.3.0
```

Create `backend/tests/SlingKing.UnitTests/Architecture/LayeringTests.cs`:

```csharp
using System.Reflection;
using Shouldly;
using Xunit;

namespace SlingKing.UnitTests.Architecture;

public class LayeringTests
{
    private static readonly string[] FrameworkPrefixes =
        { "System", "netstandard", "mscorlib", "Microsoft.CSharp" };

    [Fact]
    public void Domain_depends_on_nothing_but_the_base_class_library()
    {
        var domain = typeof(SlingKing.Domain.DomainAssembly).Assembly;

        var offenders = domain.GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(name => !FrameworkPrefixes.Any(p =>
                name.Equals(p, StringComparison.Ordinal) ||
                name.StartsWith(p + ".", StringComparison.Ordinal)))
            .ToArray();

        offenders.ShouldBeEmpty(
            "Domain must stay a leaf. Remove these references: " + string.Join(", ", offenders));
    }

    [Fact]
    public void Application_does_not_reference_Infrastructure()
    {
        var application = typeof(SlingKing.Application.ApplicationAssembly).Assembly;

        application.GetReferencedAssemblies()
            .Select(a => a.Name!)
            .ShouldNotContain("SlingKing.Infrastructure");
    }
}
```

- [ ] **Step 8: Run the test and watch it fail**

Run: `dotnet test tests/SlingKing.UnitTests`
Expected: FAIL — compile error, `ApplicationAssembly` does not exist.

- [ ] **Step 9: Add the Application marker**

Create `backend/src/SlingKing.Application/ApplicationAssembly.cs`:

```csharp
namespace SlingKing.Application;

/// <summary>Empty marker, mirroring <c>DomainAssembly</c>.</summary>
public static class ApplicationAssembly
{
}
```

- [ ] **Step 10: Run the tests and watch them pass**

Run: `dotnet build && dotnet test tests/SlingKing.UnitTests`
Expected: PASS, 2 tests.

- [ ] **Step 11: Commit**

```bash
cd "C:/Hamza Projects/SlingKing"
git add .
git commit -m "chore: scaffold solution with enforced layer boundaries"
```

---

## Task 2: Environment configuration without a third-party package

`.env` parsing is fifteen lines. Taking a dependency for it would add a licence to audit and a supply-chain surface for no benefit.

**Files:**
- Create: `backend/src/SlingKing.Infrastructure/Configuration/EnvFile.cs`
- Create: `backend/.env.example`
- Test: `backend/tests/SlingKing.UnitTests/Configuration/EnvFileTests.cs`

**Interfaces:**
- Consumes: the solution from Task 1.
- Produces: `EnvFile.Load(string path)` — reads a `.env` file and sets any variable **not already present** in the process environment. Returns the number of variables set. Missing file is not an error.

- [ ] **Step 1: Write the failing tests**

Create `backend/tests/SlingKing.UnitTests/Configuration/EnvFileTests.cs`:

```csharp
using Shouldly;
using SlingKing.Infrastructure.Configuration;
using Xunit;

namespace SlingKing.UnitTests.Configuration;

public class EnvFileTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"env-{Guid.NewGuid():N}");

    private void WriteFile(string contents) => File.WriteAllText(_path, contents);

    [Fact]
    public void Reads_simple_key_value_pairs()
    {
        WriteFile("SK_TEST_ONE=hello\nSK_TEST_TWO=world\n");

        EnvFile.Load(_path).ShouldBe(2);

        Environment.GetEnvironmentVariable("SK_TEST_ONE").ShouldBe("hello");
        Environment.GetEnvironmentVariable("SK_TEST_TWO").ShouldBe("world");
    }

    [Fact]
    public void Ignores_comments_and_blank_lines()
    {
        WriteFile("# a comment\n\n   \nSK_TEST_THREE=value\n");

        EnvFile.Load(_path).ShouldBe(1);

        Environment.GetEnvironmentVariable("SK_TEST_THREE").ShouldBe("value");
    }

    [Fact]
    public void Keeps_equals_signs_inside_the_value()
    {
        WriteFile("SK_TEST_CONN=Host=db;Password=a=b=c\n");

        EnvFile.Load(_path);

        Environment.GetEnvironmentVariable("SK_TEST_CONN").ShouldBe("Host=db;Password=a=b=c");
    }

    [Fact]
    public void Strips_surrounding_quotes()
    {
        WriteFile("SK_TEST_QUOTED=\"spaced value\"\n");

        EnvFile.Load(_path);

        Environment.GetEnvironmentVariable("SK_TEST_QUOTED").ShouldBe("spaced value");
    }

    [Fact]
    public void Never_overwrites_a_variable_already_set_in_the_process()
    {
        Environment.SetEnvironmentVariable("SK_TEST_EXISTING", "from-process");
        WriteFile("SK_TEST_EXISTING=from-file\n");

        EnvFile.Load(_path).ShouldBe(0);

        Environment.GetEnvironmentVariable("SK_TEST_EXISTING").ShouldBe("from-process");
    }

    [Fact]
    public void A_missing_file_is_not_an_error()
    {
        EnvFile.Load(Path.Combine(Path.GetTempPath(), "definitely-not-here")).ShouldBe(0);
    }

    public void Dispose()
    {
        foreach (var key in new[]
                 {
                     "SK_TEST_ONE", "SK_TEST_TWO", "SK_TEST_THREE",
                     "SK_TEST_CONN", "SK_TEST_QUOTED", "SK_TEST_EXISTING"
                 })
        {
            Environment.SetEnvironmentVariable(key, null);
        }

        if (File.Exists(_path)) File.Delete(_path);
    }
}
```

- [ ] **Step 2: Run the tests and watch them fail**

Run: `dotnet test tests/SlingKing.UnitTests --filter EnvFileTests`
Expected: FAIL — `EnvFile` does not exist.

- [ ] **Step 3: Reference Infrastructure from the unit tests, then implement**

```bash
dotnet add tests/SlingKing.UnitTests reference src/SlingKing.Infrastructure
```

Create `backend/src/SlingKing.Infrastructure/Configuration/EnvFile.cs`:

```csharp
namespace SlingKing.Infrastructure.Configuration;

/// <summary>
/// Minimal <c>.env</c> reader. Real environment variables always win, so a value
/// exported by the host (Render, CI) is never clobbered by a stray local file.
/// </summary>
public static class EnvFile
{
    public static int Load(string path)
    {
        if (!File.Exists(path)) return 0;

        var applied = 0;

        foreach (var rawLine in File.ReadAllLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;

            var separator = line.IndexOf('=');
            if (separator <= 0) continue;

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();

            if (value.Length >= 2 &&
                ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
            {
                value = value[1..^1];
            }

            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key))) continue;

            Environment.SetEnvironmentVariable(key, value);
            applied++;
        }

        return applied;
    }
}
```

- [ ] **Step 4: Run the tests and watch them pass**

Run: `dotnet test tests/SlingKing.UnitTests --filter EnvFileTests`
Expected: PASS, 6 tests.

- [ ] **Step 5: Write the example file**

Create `backend/.env.example` — names and shapes only, never real values:

```dotenv
# Copy to backend/.env and fill in. backend/.env is gitignored — keep it that way.
# Supabase → Project Settings → Database → Connection string

# Direct connection, port 5432. Migrations need prepared statements and advisory
# locks, which the transaction pooler does not support.
SLINGKING_DB_MIGRATION=Host=db.<ref>.supabase.co;Port=5432;Database=postgres;Username=postgres;Password=<password>;SSL Mode=Require;Trust Server Certificate=true

# Transaction pooler, port 6543. Used by the running API.
SLINGKING_DB_RUNTIME=Host=aws-0-ap-south-1.pooler.supabase.com;Port=6543;Database=postgres;Username=postgres.<ref>;Password=<password>;SSL Mode=Require;Trust Server Certificate=true;No Reset On Close=true;Max Auto Prepare=0

# Direct connection pinned to the throwaway test schema. Integration tests refuse
# to start unless this says Search Path=test.
SLINGKING_DB_TEST=Host=db.<ref>.supabase.co;Port=5432;Database=postgres;Username=postgres;Password=<password>;SSL Mode=Require;Trust Server Certificate=true;Search Path=test

# 32+ bytes. Generate with: openssl rand -base64 48
SLINGKING_JWT_SECRET=
SLINGKING_JWT_ISSUER=slingking-api
SLINGKING_JWT_AUDIENCE=slingking-web

SLINGKING_CORS_ORIGINS=http://localhost:5173

SLINGKING_SEED_ADMIN_EMAIL=
SLINGKING_SEED_ADMIN_PASSWORD=
```

- [ ] **Step 6: Prove the secret file cannot be committed**

Run:
```bash
cd "C:/Hamza Projects/SlingKing"
cp backend/.env.example backend/.env
git status --porcelain backend/.env
```
Expected: **no output.** Git does not see it. If a path appears, the `.gitignore` entry is wrong — fix it before continuing.

- [ ] **Step 7: Commit**

```bash
git add .
git commit -m "feat: env-file configuration loader with process-wins precedence"
```

---

## Task 3: Domain enums

Every `smallint` column in the spec maps to one of these. Values are assigned explicitly and **must never be renumbered** — they are persisted in the database.

**Files:**
- Create: `backend/src/SlingKing.Domain/Common/Enums.cs`
- Test: `backend/tests/SlingKing.UnitTests/Domain/EnumStabilityTests.cs`

**Interfaces:**
- Consumes: Task 1's Domain project.
- Produces: `InventoryTransactionType`, `CostLayerSourceType`, `InventoryReferenceType`, `PurchaseStatus`, `OrderStatus`, `PaymentStatus`, `PaymentDirection`, `PaymentMethod`, `ReturnStatus`, `ReturnItemCondition`, `ImportBatchStatus`, `ImportRowStatus` — all in namespace `SlingKing.Domain.Common`.

- [ ] **Step 1: Write the failing test**

This test exists because a renumbered enum silently corrupts every historical row. It pins the wire values.

Create `backend/tests/SlingKing.UnitTests/Domain/EnumStabilityTests.cs`:

```csharp
using Shouldly;
using SlingKing.Domain.Common;
using Xunit;

namespace SlingKing.UnitTests.Domain;

public class EnumStabilityTests
{
    [Fact]
    public void Inventory_transaction_type_values_are_frozen()
    {
        ((int)InventoryTransactionType.OpeningStock).ShouldBe(1);
        ((int)InventoryTransactionType.Purchase).ShouldBe(2);
        ((int)InventoryTransactionType.Sale).ShouldBe(3);
        ((int)InventoryTransactionType.Return).ShouldBe(4);
        ((int)InventoryTransactionType.Damage).ShouldBe(5);
        ((int)InventoryTransactionType.Loss).ShouldBe(6);
        ((int)InventoryTransactionType.PersonalUse).ShouldBe(7);
        ((int)InventoryTransactionType.Adjustment).ShouldBe(8);
        ((int)InventoryTransactionType.Reservation).ShouldBe(9);
        ((int)InventoryTransactionType.ReservationRelease).ShouldBe(10);
    }

    [Fact]
    public void Order_status_values_are_frozen()
    {
        ((int)OrderStatus.Draft).ShouldBe(1);
        ((int)OrderStatus.Confirmed).ShouldBe(2);
        ((int)OrderStatus.Packed).ShouldBe(3);
        ((int)OrderStatus.Shipped).ShouldBe(4);
        ((int)OrderStatus.Delivered).ShouldBe(5);
        ((int)OrderStatus.Completed).ShouldBe(6);
        ((int)OrderStatus.Cancelled).ShouldBe(7);
        ((int)OrderStatus.Returned).ShouldBe(8);
    }

    [Fact]
    public void Payment_status_carries_the_partial_refund_state()
    {
        ((int)PaymentStatus.Pending).ShouldBe(1);
        ((int)PaymentStatus.PartiallyPaid).ShouldBe(2);
        ((int)PaymentStatus.Paid).ShouldBe(3);
        ((int)PaymentStatus.PartiallyRefunded).ShouldBe(4);
        ((int)PaymentStatus.Refunded).ShouldBe(5);
    }

    [Fact]
    public void Cost_layer_sources_are_frozen()
    {
        ((int)CostLayerSourceType.OpeningStock).ShouldBe(1);
        ((int)CostLayerSourceType.Purchase).ShouldBe(2);
        ((int)CostLayerSourceType.ReturnRestock).ShouldBe(3);
        ((int)CostLayerSourceType.AdjustmentIn).ShouldBe(4);
    }

    [Fact]
    public void No_enum_uses_zero_so_a_default_struct_is_always_invalid()
    {
        var enums = typeof(InventoryTransactionType).Assembly
            .GetTypes()
            .Where(t => t.IsEnum && t.Namespace == "SlingKing.Domain.Common");

        foreach (var type in enums)
        {
            Enum.GetValues(type)
                .Cast<object>()
                .Select(v => Convert.ToInt32(v))
                .ShouldNotContain(0, $"{type.Name} must not define a zero value");
        }
    }
}
```

- [ ] **Step 2: Run the test and watch it fail**

Run: `dotnet test tests/SlingKing.UnitTests --filter EnumStabilityTests`
Expected: FAIL — namespace `SlingKing.Domain.Common` does not exist.

- [ ] **Step 3: Write the enums**

Create `backend/src/SlingKing.Domain/Common/Enums.cs`:

```csharp
namespace SlingKing.Domain.Common;

// Persisted as smallint. Values are permanent — add new members, never renumber.

public enum InventoryTransactionType
{
    OpeningStock = 1,
    Purchase = 2,
    Sale = 3,
    Return = 4,
    Damage = 5,
    Loss = 6,
    PersonalUse = 7,
    Adjustment = 8,
    Reservation = 9,
    ReservationRelease = 10
}

public enum CostLayerSourceType
{
    OpeningStock = 1,
    Purchase = 2,
    ReturnRestock = 3,
    AdjustmentIn = 4
}

public enum InventoryReferenceType
{
    Order = 1,
    OrderItem = 2,
    Purchase = 3,
    Return = 4,
    ImportBatch = 5
}

public enum PurchaseStatus
{
    Draft = 1,
    Completed = 2,
    Cancelled = 3
}

public enum OrderStatus
{
    Draft = 1,
    Confirmed = 2,
    Packed = 3,
    Shipped = 4,
    Delivered = 5,
    Completed = 6,
    Cancelled = 7,
    Returned = 8
}

public enum PaymentStatus
{
    Pending = 1,
    PartiallyPaid = 2,
    Paid = 3,
    PartiallyRefunded = 4,
    Refunded = 5
}

public enum PaymentDirection
{
    In = 1,
    Refund = 2
}

public enum PaymentMethod
{
    Cash = 1,
    Upi = 2,
    BankTransfer = 3,
    Card = 4,
    CashOnDelivery = 5,
    Other = 6
}

public enum ReturnStatus
{
    Draft = 1,
    Completed = 2,
    Cancelled = 3
}

public enum ReturnItemCondition
{
    Sellable = 1,
    Damaged = 2
}

public enum ImportBatchStatus
{
    Uploaded = 1,
    Validated = 2,
    Committed = 3,
    Failed = 4,
    Cancelled = 5
}

public enum ImportRowStatus
{
    Valid = 1,
    Invalid = 2,
    Committed = 3
}
```

- [ ] **Step 4: Run the tests and watch them pass**

Run: `dotnet test tests/SlingKing.UnitTests --filter EnumStabilityTests`
Expected: PASS, 5 tests.

- [ ] **Step 5: Commit**

```bash
git add .
git commit -m "feat: domain enums with frozen persisted values"
```

---
## Task 4: Domain entities — Platform, Catalog, Inventory

Twelve of the twenty-seven tables. These are plain persistence models: public settable properties, no behaviour. Business rules live in Application services, and the hard guarantees live in database constraints.

**Files:**
- Create: `backend/src/SlingKing.Domain/Platform/User.cs`, `RefreshToken.cs`, `NumberSequence.cs`, `AppSetting.cs`
- Create: `backend/src/SlingKing.Domain/Catalog/Category.cs`, `Product.cs`, `ProductVariant.cs`
- Create: `backend/src/SlingKing.Domain/Inventory/Location.cs`, `StockLevel.cs`, `InventoryTransaction.cs`, `InventoryCostLayer.cs`, `InventoryCostConsumption.cs`
- Test: `backend/tests/SlingKing.UnitTests/Domain/OptionalCostTests.cs`

**Interfaces:**
- Consumes: `SlingKing.Domain.Common` enums from Task 3.
- Produces: namespaces `SlingKing.Domain.Platform`, `.Catalog`, `.Inventory`. Later tasks rely on these exact property names: `User.Email`, `User.PasswordHash`, `NumberSequence.Key/Prefix/NextValue/Padding`, `StockLevel.PhysicalQty/ReservedQty/AvailableQty`, `InventoryCostLayer.RemainingQty/UnitCost`, `InventoryCostConsumption.ReversedQty/UnitCost/TotalCost`.

- [ ] **Step 1: Write the failing test**

The one thing worth asserting at this layer is that cost is genuinely optional and that nothing quietly defaults it to zero.

Create `backend/tests/SlingKing.UnitTests/Domain/OptionalCostTests.cs`:

```csharp
using Shouldly;
using SlingKing.Domain.Inventory;
using Xunit;

namespace SlingKing.UnitTests.Domain;

public class OptionalCostTests
{
    [Fact]
    public void A_new_cost_layer_has_an_unknown_cost_not_a_zero_cost()
    {
        var layer = new InventoryCostLayer();

        layer.UnitCost.ShouldBeNull();
    }

    [Fact]
    public void A_new_consumption_has_unknown_costs_not_zero_costs()
    {
        var consumption = new InventoryCostConsumption();

        consumption.UnitCost.ShouldBeNull();
        consumption.TotalCost.ShouldBeNull();
    }

    [Fact]
    public void Available_quantity_is_derived_and_has_no_public_setter()
    {
        typeof(StockLevel)
            .GetProperty(nameof(StockLevel.AvailableQty))!
            .SetMethod!.IsPublic
            .ShouldBeFalse("available_qty is a generated column — application code must never write it");
    }
}
```

- [ ] **Step 2: Run the test and watch it fail**

Run: `dotnet test tests/SlingKing.UnitTests --filter OptionalCostTests`
Expected: FAIL — `SlingKing.Domain.Inventory` does not exist.

- [ ] **Step 3: Write the Platform entities**

Create `backend/src/SlingKing.Domain/Platform/User.cs`:

```csharp
namespace SlingKing.Domain.Platform;

public class User
{
    public long Id { get; set; }
    public string Email { get; set; } = null!;
    public string PasswordHash { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
}
```

Create `backend/src/SlingKing.Domain/Platform/RefreshToken.cs`:

```csharp
namespace SlingKing.Domain.Platform;

public class RefreshToken
{
    public long Id { get; set; }
    public long UserId { get; set; }

    /// <summary>SHA-256 of the token. The raw token is never stored.</summary>
    public string TokenHash { get; set; } = null!;

    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? ReplacedByTokenHash { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public User? User { get; set; }
}
```

Create `backend/src/SlingKing.Domain/Platform/NumberSequence.cs`:

```csharp
namespace SlingKing.Domain.Platform;

/// <summary>
/// Gap-free document numbering. Allocated with a single UPDATE … RETURNING so two
/// concurrent callers can never receive the same number.
/// </summary>
public class NumberSequence
{
    public string Key { get; set; } = null!;      // "order", "purchase", "return", "product"
    public string Prefix { get; set; } = null!;   // "ORD", "PUR", "RET", "BAG"
    public long NextValue { get; set; } = 1;
    public int Padding { get; set; } = 6;
}
```

Create `backend/src/SlingKing.Domain/Platform/AppSetting.cs`:

```csharp
namespace SlingKing.Domain.Platform;

public class AppSetting
{
    public string Key { get; set; } = null!;
    public string Value { get; set; } = null!;
    public DateTimeOffset UpdatedAt { get; set; }
}
```

- [ ] **Step 4: Write the Catalog entities**

Create `backend/src/SlingKing.Domain/Catalog/Category.cs`:

```csharp
namespace SlingKing.Domain.Catalog;

public class Category
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public int? ParentId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }

    public Category? Parent { get; set; }
    public ICollection<Product> Products { get; set; } = new List<Product>();
}
```

Create `backend/src/SlingKing.Domain/Catalog/Product.cs`:

```csharp
namespace SlingKing.Domain.Catalog;

public class Product
{
    public long Id { get; set; }

    /// <summary>Auto-generated, e.g. BAG-000001. Never user-editable.</summary>
    public string Code { get; set; } = null!;

    public string Name { get; set; } = null!;
    public int? CategoryId { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Category? Category { get; set; }
    public ICollection<ProductVariant> Variants { get; set; } = new List<ProductVariant>();
}
```

Create `backend/src/SlingKing.Domain/Catalog/ProductVariant.cs`:

```csharp
namespace SlingKing.Domain.Catalog;

public class ProductVariant
{
    public long Id { get; set; }
    public long ProductId { get; set; }

    /// <summary>Auto-generated from the product code, e.g. BAG-000001-V01.</summary>
    public string Sku { get; set; } = null!;

    public string? Colour { get; set; }
    public string? Size { get; set; }

    /// <summary>Current list price. Orders snapshot their own price and never read this again.</summary>
    public decimal SellingPrice { get; set; }

    public int LowStockThreshold { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Product? Product { get; set; }
}
```

- [ ] **Step 5: Write the Inventory entities**

Create `backend/src/SlingKing.Domain/Inventory/Location.cs`:

```csharp
namespace SlingKing.Domain.Inventory;

/// <summary>
/// Present from day one and hidden in the UI while there is exactly one. Carrying the
/// column now costs nothing; adding it to a populated ledger later would be a migration
/// across every inventory row.
/// </summary>
public class Location
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
}
```

Create `backend/src/SlingKing.Domain/Inventory/StockLevel.cs`:

```csharp
namespace SlingKing.Domain.Inventory;

/// <summary>
/// Fast-read projection of the ledger. Composite key (VariantId, LocationId).
/// </summary>
public class StockLevel
{
    public long VariantId { get; set; }
    public int LocationId { get; set; }
    public int PhysicalQty { get; set; }
    public int ReservedQty { get; set; }

    /// <summary>
    /// Generated column: <c>physical_qty - reserved_qty</c>, stored. Read-only by design —
    /// there is no code path that can compute it differently from the database.
    /// </summary>
    public int AvailableQty { get; private set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
```

Create `backend/src/SlingKing.Domain/Inventory/InventoryTransaction.cs`:

```csharp
using SlingKing.Domain.Common;

namespace SlingKing.Domain.Inventory;

/// <summary>
/// The append-only ledger. Both deltas on one row is what keeps reservation and
/// consumption in a single timeline: a Reservation writes (0, +5); a Sale writes (-5, -5).
/// Rows are never updated and never deleted.
/// </summary>
public class InventoryTransaction
{
    public long Id { get; set; }
    public long VariantId { get; set; }
    public int LocationId { get; set; }
    public InventoryTransactionType Type { get; set; }

    public int PhysicalDelta { get; set; }
    public int ReservedDelta { get; set; }

    public decimal? UnitCost { get; set; }
    public decimal? TotalCost { get; set; }

    public InventoryReferenceType? ReferenceType { get; set; }
    public long? ReferenceId { get; set; }

    public string? Reason { get; set; }
    public string? Notes { get; set; }

    public DateTimeOffset OccurredAt { get; set; }
    public long? CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
```

Create `backend/src/SlingKing.Domain/Inventory/InventoryCostLayer.cs`:

```csharp
using SlingKing.Domain.Common;

namespace SlingKing.Domain.Inventory;

/// <summary>
/// One FIFO queue entry. Consumed oldest-first by (ReceivedAt, Id).
/// </summary>
public class InventoryCostLayer
{
    public long Id { get; set; }
    public long VariantId { get; set; }
    public int LocationId { get; set; }

    public CostLayerSourceType SourceType { get; set; }
    public long SourceId { get; set; }

    /// <summary>Queue position. A back-dated purchase takes its rightful place at the front.</summary>
    public DateTimeOffset ReceivedAt { get; set; }

    public int OriginalQty { get; set; }
    public int RemainingQty { get; set; }

    /// <summary>Null means the cost is unknown, never that it is zero. Fillable later, once.</summary>
    public decimal? UnitCost { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
```

Create `backend/src/SlingKing.Domain/Inventory/InventoryCostConsumption.cs`:

```csharp
namespace SlingKing.Domain.Inventory;

/// <summary>
/// Which layers a given outflow ate, and at what rate. Cost is copied here rather than
/// joined from the layer so that editing a price later cannot rewrite historical profit —
/// the single exception being a backfill, which may only ever fill a null.
/// </summary>
public class InventoryCostConsumption
{
    public long Id { get; set; }
    public long CostLayerId { get; set; }
    public long InventoryTransactionId { get; set; }
    public long? OrderItemId { get; set; }

    public int Quantity { get; set; }
    public int ReversedQty { get; set; }

    public decimal? UnitCost { get; set; }
    public decimal? TotalCost { get; set; }

    public DateTimeOffset ConsumedAt { get; set; }

    public InventoryCostLayer? CostLayer { get; set; }
}
```

- [ ] **Step 6: Run the tests and watch them pass**

Run: `dotnet test tests/SlingKing.UnitTests --filter OptionalCostTests`
Expected: PASS, 3 tests.

- [ ] **Step 7: Commit**

```bash
git add .
git commit -m "feat: platform, catalog and inventory domain entities"
```

---

## Task 5: Domain entities — Purchasing, Sales, Payments, Operations

The remaining fifteen tables. Nothing in Release 1 writes to the Sales, Payments or Returns tables — they ship now so that Release 2 needs no migration against a database holding real stock.

**Files:**
- Create: `backend/src/SlingKing.Domain/Purchasing/Supplier.cs`, `Purchase.cs`, `PurchaseItem.cs`
- Create: `backend/src/SlingKing.Domain/Sales/SalesChannel.cs`, `Customer.cs`, `Order.cs`, `OrderItem.cs`, `OrderStatusHistory.cs`
- Create: `backend/src/SlingKing.Domain/Payments/Payment.cs`, `Return.cs`, `ReturnItem.cs`
- Create: `backend/src/SlingKing.Domain/Operations/ExpenseCategory.cs`, `Expense.cs`, `ImportBatch.cs`, `ImportRow.cs`
- Test: `backend/tests/SlingKing.UnitTests/Domain/EntityCountTests.cs`

**Interfaces:**
- Consumes: `SlingKing.Domain.Common` enums from Task 3.
- Produces: namespaces `SlingKing.Domain.Purchasing`, `.Sales`, `.Payments`, `.Operations`. Task 6 maps every one of these into `AppDbContext`.

- [ ] **Step 1: Write the failing test**

The spec commits to exactly 27 tables. This test makes an accidental 28th — or a forgotten one — fail the build rather than surface as a schema surprise.

Create `backend/tests/SlingKing.UnitTests/Domain/EntityCountTests.cs`:

```csharp
using Shouldly;
using Xunit;

namespace SlingKing.UnitTests.Domain;

public class EntityCountTests
{
    private static readonly string[] EntityNamespaces =
    {
        "SlingKing.Domain.Platform",
        "SlingKing.Domain.Catalog",
        "SlingKing.Domain.Inventory",
        "SlingKing.Domain.Purchasing",
        "SlingKing.Domain.Sales",
        "SlingKing.Domain.Payments",
        "SlingKing.Domain.Operations"
    };

    [Fact]
    public void The_domain_declares_exactly_twenty_seven_entities()
    {
        var entities = typeof(SlingKing.Domain.DomainAssembly).Assembly
            .GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && EntityNamespaces.Contains(t.Namespace))
            .Select(t => t.Name)
            .OrderBy(n => n)
            .ToArray();

        entities.Length.ShouldBe(27,
            "expected 27 entities, found " + entities.Length + ": " + string.Join(", ", entities));
    }

    [Fact]
    public void Every_entity_namespace_is_populated()
    {
        var assembly = typeof(SlingKing.Domain.DomainAssembly).Assembly;

        foreach (var ns in EntityNamespaces)
        {
            assembly.GetTypes()
                .Count(t => t.IsClass && !t.IsAbstract && t.Namespace == ns)
                .ShouldBeGreaterThan(0, $"{ns} has no entities");
        }
    }
}
```

- [ ] **Step 2: Run the test and watch it fail**

Run: `dotnet test tests/SlingKing.UnitTests --filter EntityCountTests`
Expected: FAIL — 12 entities found, 27 expected.

- [ ] **Step 3: Write the Purchasing entities**

Create `backend/src/SlingKing.Domain/Purchasing/Supplier.cs`:

```csharp
namespace SlingKing.Domain.Purchasing;

public class Supplier
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string? ContactPerson { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
```

Create `backend/src/SlingKing.Domain/Purchasing/Purchase.cs`:

```csharp
using SlingKing.Domain.Common;

namespace SlingKing.Domain.Purchasing;

public class Purchase
{
    public long Id { get; set; }

    /// <summary>Auto-generated, e.g. PUR-000014.</summary>
    public string Number { get; set; } = null!;

    public int SupplierId { get; set; }
    public DateTimeOffset PurchaseDate { get; set; }
    public PurchaseStatus Status { get; set; } = PurchaseStatus.Draft;

    /// <summary>Sum of the lines that have a cost. Null when no line is costed.</summary>
    public decimal? TotalAmount { get; set; }

    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Supplier? Supplier { get; set; }
    public ICollection<PurchaseItem> Items { get; set; } = new List<PurchaseItem>();
}
```

Create `backend/src/SlingKing.Domain/Purchasing/PurchaseItem.cs`:

```csharp
namespace SlingKing.Domain.Purchasing;

public class PurchaseItem
{
    public long Id { get; set; }
    public long PurchaseId { get; set; }
    public long VariantId { get; set; }
    public int Quantity { get; set; }

    /// <summary>Optional. A blank cost opens an uncosted layer on completion.</summary>
    public decimal? UnitCost { get; set; }

    public decimal? LineTotal { get; set; }

    public Purchase? Purchase { get; set; }
}
```

- [ ] **Step 4: Write the Sales entities**

Create `backend/src/SlingKing.Domain/Sales/SalesChannel.cs`:

```csharp
namespace SlingKing.Domain.Sales;

/// <summary>
/// Channel behaviour is data, not code. Adding Meesho later is a row, not a table.
/// </summary>
public class SalesChannel
{
    public int Id { get; set; }
    public string Code { get; set; } = null!;      // instagram, whatsapp, offline
    public string Name { get; set; } = null!;

    /// <summary>Offline sales hand the bag over immediately and never reserve.</summary>
    public bool UsesReservation { get; set; }

    /// <summary>Drives which status transitions are legal for orders on this channel.</summary>
    public bool RequiresShipping { get; set; }

    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
}
```

Create `backend/src/SlingKing.Domain/Sales/Customer.cs`:

```csharp
namespace SlingKing.Domain.Sales;

public class Customer
{
    public long Id { get; set; }
    public string Name { get; set; } = null!;
    public string? Phone { get; set; }
    public string? InstagramHandle { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
```

Create `backend/src/SlingKing.Domain/Sales/Order.cs`:

```csharp
using SlingKing.Domain.Common;

namespace SlingKing.Domain.Sales;

public class Order
{
    public long Id { get; set; }

    /// <summary>Auto-generated, e.g. ORD-000001.</summary>
    public string Number { get; set; } = null!;

    public int ChannelId { get; set; }
    public long? CustomerId { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.Draft;

    public DateTimeOffset OrderDate { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; }
    public DateTimeOffset? ShippedAt { get; set; }

    /// <summary>Revenue and COGS are recognised here, not on OrderDate.</summary>
    public DateTimeOffset? CompletedAt { get; set; }

    public DateTimeOffset? CancelledAt { get; set; }

    public decimal SubtotalAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal ShippingAmount { get; set; }
    public decimal TotalAmount { get; set; }

    /// <summary>Derived from the payment rows. Never written by a client.</summary>
    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Pending;

    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public SalesChannel? Channel { get; set; }
    public Customer? Customer { get; set; }
    public ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();
}
```

Create `backend/src/SlingKing.Domain/Sales/OrderItem.cs`:

```csharp
namespace SlingKing.Domain.Sales;

public class OrderItem
{
    public long Id { get; set; }
    public long OrderId { get; set; }
    public long VariantId { get; set; }
    public int Quantity { get; set; }

    /// <summary>Frozen at confirmation. Changing the variant's price never moves this.</summary>
    public decimal UnitPrice { get; set; }

    public decimal DiscountAmount { get; set; }
    public decimal LineTotal { get; set; }

    /// <summary>Set on delivery from the FIFO consumption. Null until then, or if uncosted.</summary>
    public decimal? CogsAmount { get; set; }

    public string ProductNameSnapshot { get; set; } = null!;
    public string SkuSnapshot { get; set; } = null!;

    public Order? Order { get; set; }
}
```

Create `backend/src/SlingKing.Domain/Sales/OrderStatusHistory.cs`:

```csharp
using SlingKing.Domain.Common;

namespace SlingKing.Domain.Sales;

public class OrderStatusHistory
{
    public long Id { get; set; }
    public long OrderId { get; set; }
    public OrderStatus? FromStatus { get; set; }
    public OrderStatus ToStatus { get; set; }
    public DateTimeOffset ChangedAt { get; set; }
    public long? ChangedBy { get; set; }
    public string? Notes { get; set; }
}
```

- [ ] **Step 5: Write the Payments and Returns entities**

Create `backend/src/SlingKing.Domain/Payments/Payment.cs`:

```csharp
using SlingKing.Domain.Common;

namespace SlingKing.Domain.Payments;

/// <summary>
/// Refunds are rows with Direction = Refund rather than negative amounts, so that
/// "how much came in" and "how much went back" are separately answerable.
/// </summary>
public class Payment
{
    public long Id { get; set; }
    public long OrderId { get; set; }
    public PaymentDirection Direction { get; set; } = PaymentDirection.In;
    public decimal Amount { get; set; }
    public PaymentMethod Method { get; set; }
    public string? Reference { get; set; }
    public DateTimeOffset PaidAt { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public long? CreatedBy { get; set; }
}
```

Create `backend/src/SlingKing.Domain/Payments/Return.cs`:

```csharp
using SlingKing.Domain.Common;

namespace SlingKing.Domain.Payments;

public class Return
{
    public long Id { get; set; }

    /// <summary>Auto-generated, e.g. RET-000003.</summary>
    public string Number { get; set; } = null!;

    public long OrderId { get; set; }
    public ReturnStatus Status { get; set; } = ReturnStatus.Draft;
    public DateTimeOffset ReturnDate { get; set; }
    public decimal RefundAmount { get; set; }

    /// <summary>COGS credited back by the FIFO reversal. Null if the original was uncosted.</summary>
    public decimal? RestockedValue { get; set; }

    public string? Reason { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    public ICollection<ReturnItem> Items { get; set; } = new List<ReturnItem>();
}
```

Create `backend/src/SlingKing.Domain/Payments/ReturnItem.cs`:

```csharp
using SlingKing.Domain.Common;

namespace SlingKing.Domain.Payments;

public class ReturnItem
{
    public long Id { get; set; }
    public long ReturnId { get; set; }
    public long OrderItemId { get; set; }
    public int Quantity { get; set; }
    public ReturnItemCondition Condition { get; set; } = ReturnItemCondition.Sellable;
    public decimal? CogsReversed { get; set; }

    public Return? Return { get; set; }
}
```

- [ ] **Step 6: Write the Operations entities**

Create `backend/src/SlingKing.Domain/Operations/ExpenseCategory.cs`:

```csharp
namespace SlingKing.Domain.Operations;

public class ExpenseCategory
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public bool IsActive { get; set; } = true;
}
```

Create `backend/src/SlingKing.Domain/Operations/Expense.cs`:

```csharp
namespace SlingKing.Domain.Operations;

public class Expense
{
    public long Id { get; set; }
    public int CategoryId { get; set; }

    /// <summary>Optional: attributing an expense to a channel makes per-channel net profit real.</summary>
    public int? ChannelId { get; set; }

    public decimal Amount { get; set; }
    public DateTimeOffset ExpenseDate { get; set; }
    public string? Description { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public long? CreatedBy { get; set; }

    public ExpenseCategory? Category { get; set; }
}
```

Create `backend/src/SlingKing.Domain/Operations/ImportBatch.cs`:

```csharp
using SlingKing.Domain.Common;

namespace SlingKing.Domain.Operations;

public class ImportBatch
{
    public long Id { get; set; }
    public string FileName { get; set; } = null!;
    public ImportBatchStatus Status { get; set; } = ImportBatchStatus.Uploaded;
    public int TotalRows { get; set; }
    public int ValidRows { get; set; }
    public int ErrorRows { get; set; }
    public DateTimeOffset UploadedAt { get; set; }
    public DateTimeOffset? CommittedAt { get; set; }
    public long? UploadedBy { get; set; }

    public ICollection<ImportRow> Rows { get; set; } = new List<ImportRow>();
}
```

Create `backend/src/SlingKing.Domain/Operations/ImportRow.cs`:

```csharp
using SlingKing.Domain.Common;

namespace SlingKing.Domain.Operations;

public class ImportRow
{
    public long Id { get; set; }
    public long BatchId { get; set; }
    public int RowNumber { get; set; }

    /// <summary>The original row as JSON, so a rejected upload can be explained months later.</summary>
    public string RawData { get; set; } = null!;

    public ImportRowStatus Status { get; set; }
    public string? ErrorMessage { get; set; }
    public long? ResolvedVariantId { get; set; }

    public ImportBatch? Batch { get; set; }
}
```

- [ ] **Step 7: Run the tests and watch them pass**

Run: `dotnet test tests/SlingKing.UnitTests --filter EntityCountTests`
Expected: PASS, 2 tests. The count test reports exactly 27.

- [ ] **Step 8: Commit**

```bash
git add .
git commit -m "feat: purchasing, sales, payments and operations domain entities"
```

---

## Task 6: AppDbContext, the abstraction seam, and EF configurations

The database constraints in the spec are the real guarantees — negative stock is impossible because a `CHECK` says so, not because a service remembered to look. This task encodes them in fluent configuration so they land in the migration.

**Files:**
- Create: `backend/src/SlingKing.Application/Common/Abstractions/IAppDbContext.cs`, `IClock.cs`
- Create: `backend/src/SlingKing.Infrastructure/SystemClock.cs`
- Create: `backend/src/SlingKing.Infrastructure/Persistence/AppDbContext.cs`
- Create: `backend/src/SlingKing.Infrastructure/Persistence/Configurations/PlatformConfiguration.cs`, `CatalogConfiguration.cs`, `InventoryConfiguration.cs`, `PurchasingConfiguration.cs`, `SalesConfiguration.cs`, `PaymentsConfiguration.cs`, `OperationsConfiguration.cs`
- Test: `backend/tests/SlingKing.UnitTests/Persistence/ModelTests.cs`

**Interfaces:**
- Consumes: all 27 entities from Tasks 4–5.
- Produces: `IAppDbContext` exposing `DbSet<T>` for every entity plus `SaveChangesAsync(CancellationToken)` and `Database`; `AppDbContext : DbContext, IAppDbContext`; `IClock.UtcNow`. Every later task takes `IAppDbContext`, never `AppDbContext`.

- [ ] **Step 1: Add the EF packages**

```bash
cd backend
dotnet add src/SlingKing.Infrastructure package Microsoft.EntityFrameworkCore.Relational --version 8.0.30
dotnet add src/SlingKing.Infrastructure package Microsoft.EntityFrameworkCore.Design --version 8.0.30
dotnet add src/SlingKing.Infrastructure package Npgsql.EntityFrameworkCore.PostgreSQL --version 8.0.11
dotnet add src/SlingKing.Infrastructure package EFCore.NamingConventions --version 8.0.3
dotnet tool install --global dotnet-ef --version 8.0.30
```

- [ ] **Step 2: Write the failing model tests**

Create `backend/tests/SlingKing.UnitTests/Persistence/ModelTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Shouldly;
using SlingKing.Infrastructure.Persistence;
using Xunit;

namespace SlingKing.UnitTests.Persistence;

/// <summary>
/// Model-shape assertions. These need no database — EF builds the model in memory,
/// so they run in milliseconds and catch a broken mapping before a migration is generated.
/// </summary>
public class ModelTests
{
    private static AppDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=unused")
            .UseSnakeCaseNamingConvention()
            .Options;

        return new AppDbContext(options);
    }

    [Fact]
    public void The_model_maps_exactly_twenty_seven_tables()
    {
        using var db = BuildContext();

        db.Model.GetEntityTypes().Count().ShouldBe(27);
    }

    [Fact]
    public void Table_and_column_names_are_snake_case()
    {
        using var db = BuildContext();

        var variant = db.Model.FindEntityType(typeof(SlingKing.Domain.Catalog.ProductVariant))!;

        variant.GetTableName().ShouldBe("product_variants");
        variant.FindProperty(nameof(SlingKing.Domain.Catalog.ProductVariant.LowStockThreshold))!
            .GetColumnName().ShouldBe("low_stock_threshold");
    }

    [Fact]
    public void Available_quantity_is_a_stored_generated_column()
    {
        using var db = BuildContext();

        var property = db.Model
            .FindEntityType(typeof(SlingKing.Domain.Inventory.StockLevel))!
            .FindProperty(nameof(SlingKing.Domain.Inventory.StockLevel.AvailableQty))!;

        property.GetComputedColumnSql().ShouldBe("physical_qty - reserved_qty");
        property.GetIsStored().ShouldBe(true);
    }

    [Fact]
    public void Stock_levels_carry_the_three_negative_stock_guards()
    {
        using var db = BuildContext();

        var names = db.Model
            .FindEntityType(typeof(SlingKing.Domain.Inventory.StockLevel))!
            .GetCheckConstraints()
            .Select(c => c.Name)
            .ToArray();

        names.ShouldContain("ck_physical_non_negative");
        names.ShouldContain("ck_reserved_non_negative");
        names.ShouldContain("ck_reserved_within_physical");
    }

    [Fact]
    public void Cost_layer_unit_cost_is_nullable_and_four_decimal_places()
    {
        using var db = BuildContext();

        var property = db.Model
            .FindEntityType(typeof(SlingKing.Domain.Inventory.InventoryCostLayer))!
            .FindProperty(nameof(SlingKing.Domain.Inventory.InventoryCostLayer.UnitCost))!;

        property.IsNullable.ShouldBeTrue();
        property.GetColumnType().ShouldBe("numeric(18,4)");
    }

    [Fact]
    public void The_fifo_index_only_covers_open_layers()
    {
        using var db = BuildContext();

        var index = db.Model
            .FindEntityType(typeof(SlingKing.Domain.Inventory.InventoryCostLayer))!
            .GetIndexes()
            .Single(i => i.GetDatabaseName() == "ix_layers_fifo");

        index.GetFilter().ShouldBe("remaining_qty > 0");
    }

    [Fact]
    public void Money_columns_use_two_decimal_places()
    {
        using var db = BuildContext();

        db.Model.FindEntityType(typeof(SlingKing.Domain.Sales.Order))!
            .FindProperty(nameof(SlingKing.Domain.Sales.Order.TotalAmount))!
            .GetColumnType().ShouldBe("numeric(18,2)");
    }
}
```

- [ ] **Step 3: Run the tests and watch them fail**

Run: `dotnet test tests/SlingKing.UnitTests --filter ModelTests`
Expected: FAIL — `AppDbContext` does not exist.

- [ ] **Step 4: Write the abstraction seam**

Create `backend/src/SlingKing.Application/Common/Abstractions/IClock.cs`:

```csharp
namespace SlingKing.Application.Common.Abstractions;

/// <summary>Injected everywhere instead of DateTimeOffset.UtcNow, so time is testable.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
```

Create `backend/src/SlingKing.Infrastructure/SystemClock.cs`:

```csharp
using SlingKing.Application.Common.Abstractions;

namespace SlingKing.Infrastructure;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
```

Add EF Core to Application so it can declare `DbSet<T>` (this is the Relational package only — Application still knows nothing about Npgsql):

```bash
dotnet add src/SlingKing.Application package Microsoft.EntityFrameworkCore.Relational --version 8.0.30
```

Create `backend/src/SlingKing.Application/Common/Abstractions/IAppDbContext.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using SlingKing.Domain.Catalog;
using SlingKing.Domain.Inventory;
using SlingKing.Domain.Operations;
using SlingKing.Domain.Payments;
using SlingKing.Domain.Platform;
using SlingKing.Domain.Purchasing;
using SlingKing.Domain.Sales;

namespace SlingKing.Application.Common.Abstractions;

/// <summary>
/// The only database surface Application code sees. Deliberately not a repository:
/// wrapping EF Core in a near-identical abstraction buys nothing and actively fights
/// the row-level locking the FIFO consumer needs.
/// </summary>
public interface IAppDbContext
{
    DbSet<User> Users { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<NumberSequence> NumberSequences { get; }
    DbSet<AppSetting> AppSettings { get; }

    DbSet<Category> Categories { get; }
    DbSet<Product> Products { get; }
    DbSet<ProductVariant> ProductVariants { get; }

    DbSet<Location> Locations { get; }
    DbSet<StockLevel> StockLevels { get; }
    DbSet<InventoryTransaction> InventoryTransactions { get; }
    DbSet<InventoryCostLayer> InventoryCostLayers { get; }
    DbSet<InventoryCostConsumption> InventoryCostConsumptions { get; }

    DbSet<Supplier> Suppliers { get; }
    DbSet<Purchase> Purchases { get; }
    DbSet<PurchaseItem> PurchaseItems { get; }

    DbSet<SalesChannel> SalesChannels { get; }
    DbSet<Customer> Customers { get; }
    DbSet<Order> Orders { get; }
    DbSet<OrderItem> OrderItems { get; }
    DbSet<OrderStatusHistory> OrderStatusHistories { get; }

    DbSet<Payment> Payments { get; }
    DbSet<Return> Returns { get; }
    DbSet<ReturnItem> ReturnItems { get; }

    DbSet<ExpenseCategory> ExpenseCategories { get; }
    DbSet<Expense> Expenses { get; }
    DbSet<ImportBatch> ImportBatches { get; }
    DbSet<ImportRow> ImportRows { get; }

    /// <summary>Exposed so services can open explicit transactions and run the FIFO raw SQL.</summary>
    DatabaseFacade Database { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
```

- [ ] **Step 5: Write AppDbContext**

Create `backend/src/SlingKing.Infrastructure/Persistence/AppDbContext.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SlingKing.Application.Common.Abstractions;
using SlingKing.Domain.Catalog;
using SlingKing.Domain.Inventory;
using SlingKing.Domain.Operations;
using SlingKing.Domain.Payments;
using SlingKing.Domain.Platform;
using SlingKing.Domain.Purchasing;
using SlingKing.Domain.Sales;

namespace SlingKing.Infrastructure.Persistence;

public class AppDbContext : DbContext, IAppDbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<NumberSequence> NumberSequences => Set<NumberSequence>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();

    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductVariant> ProductVariants => Set<ProductVariant>();

    public DbSet<Location> Locations => Set<Location>();
    public DbSet<StockLevel> StockLevels => Set<StockLevel>();
    public DbSet<InventoryTransaction> InventoryTransactions => Set<InventoryTransaction>();
    public DbSet<InventoryCostLayer> InventoryCostLayers => Set<InventoryCostLayer>();
    public DbSet<InventoryCostConsumption> InventoryCostConsumptions => Set<InventoryCostConsumption>();

    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<Purchase> Purchases => Set<Purchase>();
    public DbSet<PurchaseItem> PurchaseItems => Set<PurchaseItem>();

    public DbSet<SalesChannel> SalesChannels => Set<SalesChannel>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<OrderStatusHistory> OrderStatusHistories => Set<OrderStatusHistory>();

    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Return> Returns => Set<Return>();
    public DbSet<ReturnItem> ReturnItems => Set<ReturnItem>();

    public DbSet<ExpenseCategory> ExpenseCategories => Set<ExpenseCategory>();
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<ImportBatch> ImportBatches => Set<ImportBatch>();
    public DbSet<ImportRow> ImportRows => Set<ImportRow>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        base.OnModelCreating(builder);
    }
}
```

- [ ] **Step 6: Write the Platform and Catalog configurations**

Create `backend/src/SlingKing.Infrastructure/Persistence/Configurations/PlatformConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SlingKing.Domain.Platform;

namespace SlingKing.Infrastructure.Persistence.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.ToTable("users");
        b.HasKey(x => x.Id);
        b.Property(x => x.Email).HasMaxLength(256).IsRequired();
        b.Property(x => x.PasswordHash).HasMaxLength(256).IsRequired();
        b.Property(x => x.DisplayName).HasMaxLength(128).IsRequired();
        b.HasIndex(x => x.Email).IsUnique().HasDatabaseName("ux_users_email");
    }
}

public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> b)
    {
        b.ToTable("refresh_tokens");
        b.HasKey(x => x.Id);
        b.Property(x => x.TokenHash).HasMaxLength(128).IsRequired();
        b.Property(x => x.ReplacedByTokenHash).HasMaxLength(128);
        b.HasIndex(x => x.TokenHash).IsUnique().HasDatabaseName("ux_refresh_tokens_hash");
        b.HasOne(x => x.User).WithMany(u => u.RefreshTokens)
            .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class NumberSequenceConfiguration : IEntityTypeConfiguration<NumberSequence>
{
    public void Configure(EntityTypeBuilder<NumberSequence> b)
    {
        b.ToTable("number_sequences");
        b.HasKey(x => x.Key);
        b.Property(x => x.Key).HasMaxLength(32);
        b.Property(x => x.Prefix).HasMaxLength(8).IsRequired();
        b.Property(x => x.NextValue).IsRequired();
        b.Property(x => x.Padding).IsRequired();
    }
}

public class AppSettingConfiguration : IEntityTypeConfiguration<AppSetting>
{
    public void Configure(EntityTypeBuilder<AppSetting> b)
    {
        b.ToTable("app_settings");
        b.HasKey(x => x.Key);
        b.Property(x => x.Key).HasMaxLength(64);
        b.Property(x => x.Value).IsRequired();
    }
}
```

Create `backend/src/SlingKing.Infrastructure/Persistence/Configurations/CatalogConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SlingKing.Domain.Catalog;

namespace SlingKing.Infrastructure.Persistence.Configurations;

public class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> b)
    {
        b.ToTable("categories");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(128).IsRequired();
        b.HasOne(x => x.Parent).WithMany()
            .HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> b)
    {
        b.ToTable("products");
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(32).IsRequired();
        b.Property(x => x.Name).HasMaxLength(256).IsRequired();
        b.HasIndex(x => x.Code).IsUnique().HasDatabaseName("ux_products_code");
        b.HasOne(x => x.Category).WithMany(c => c.Products)
            .HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class ProductVariantConfiguration : IEntityTypeConfiguration<ProductVariant>
{
    public void Configure(EntityTypeBuilder<ProductVariant> b)
    {
        b.ToTable("product_variants");
        b.HasKey(x => x.Id);
        b.Property(x => x.Sku).HasMaxLength(48).IsRequired();
        b.Property(x => x.Colour).HasMaxLength(64);
        b.Property(x => x.Size).HasMaxLength(64);
        b.Property(x => x.SellingPrice).HasColumnType("numeric(18,2)");
        b.HasIndex(x => x.Sku).IsUnique().HasDatabaseName("ux_product_variants_sku");
        b.HasOne(x => x.Product).WithMany(p => p.Variants)
            .HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        b.ToTable(t => t.HasCheckConstraint(
            "ck_variant_price_non_negative", "selling_price >= 0"));
    }
}
```

- [ ] **Step 7: Write the Inventory configuration — the constraint-heavy one**

Create `backend/src/SlingKing.Infrastructure/Persistence/Configurations/InventoryConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SlingKing.Domain.Inventory;

namespace SlingKing.Infrastructure.Persistence.Configurations;

public class LocationConfiguration : IEntityTypeConfiguration<Location>
{
    public void Configure(EntityTypeBuilder<Location> b)
    {
        b.ToTable("locations");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(128).IsRequired();
    }
}

public class StockLevelConfiguration : IEntityTypeConfiguration<StockLevel>
{
    public void Configure(EntityTypeBuilder<StockLevel> b)
    {
        b.ToTable("stock_levels");
        b.HasKey(x => new { x.VariantId, x.LocationId });

        b.Property(x => x.PhysicalQty).HasDefaultValue(0);
        b.Property(x => x.ReservedQty).HasDefaultValue(0);

        // Generated column: there is no code path that can compute this differently.
        b.Property(x => x.AvailableQty)
            .HasComputedColumnSql("physical_qty - reserved_qty", stored: true);

        // The negative-stock guarantee. Any bug that tries to break these aborts
        // its whole transaction at the database rather than writing a bad number.
        b.ToTable(t =>
        {
            t.HasCheckConstraint("ck_physical_non_negative", "physical_qty >= 0");
            t.HasCheckConstraint("ck_reserved_non_negative", "reserved_qty >= 0");
            t.HasCheckConstraint("ck_reserved_within_physical", "reserved_qty <= physical_qty");
        });
    }
}

public class InventoryTransactionConfiguration : IEntityTypeConfiguration<InventoryTransaction>
{
    public void Configure(EntityTypeBuilder<InventoryTransaction> b)
    {
        b.ToTable("inventory_transactions");
        b.HasKey(x => x.Id);
        b.Property(x => x.Type).HasConversion<short>();
        b.Property(x => x.ReferenceType).HasConversion<short?>();
        b.Property(x => x.PhysicalDelta).HasDefaultValue(0);
        b.Property(x => x.ReservedDelta).HasDefaultValue(0);
        b.Property(x => x.UnitCost).HasColumnType("numeric(18,4)");
        b.Property(x => x.TotalCost).HasColumnType("numeric(18,2)");
        b.Property(x => x.Reason).HasMaxLength(256);

        b.HasIndex(x => new { x.VariantId, x.LocationId, x.OccurredAt })
            .HasDatabaseName("ix_inventory_transactions_variant_time");
    }
}

public class InventoryCostLayerConfiguration : IEntityTypeConfiguration<InventoryCostLayer>
{
    public void Configure(EntityTypeBuilder<InventoryCostLayer> b)
    {
        b.ToTable("inventory_cost_layers");
        b.HasKey(x => x.Id);
        b.Property(x => x.SourceType).HasConversion<short>();

        // Optional by design. Null means unknown, never zero.
        b.Property(x => x.UnitCost).HasColumnType("numeric(18,4)").IsRequired(false);

        // Partial index: the FIFO consumer only ever scans open layers.
        b.HasIndex(x => new { x.VariantId, x.LocationId, x.ReceivedAt, x.Id })
            .HasDatabaseName("ix_layers_fifo")
            .HasFilter("remaining_qty > 0");

        b.ToTable(t =>
        {
            t.HasCheckConstraint("ck_layer_original_positive", "original_qty > 0");
            t.HasCheckConstraint("ck_layer_remaining_non_negative", "remaining_qty >= 0");
            t.HasCheckConstraint("ck_remaining_within_original", "remaining_qty <= original_qty");
            t.HasCheckConstraint("ck_layer_cost_non_negative", "unit_cost >= 0");
        });
    }
}

public class InventoryCostConsumptionConfiguration : IEntityTypeConfiguration<InventoryCostConsumption>
{
    public void Configure(EntityTypeBuilder<InventoryCostConsumption> b)
    {
        b.ToTable("inventory_cost_consumptions");
        b.HasKey(x => x.Id);
        b.Property(x => x.ReversedQty).HasDefaultValue(0);
        b.Property(x => x.UnitCost).HasColumnType("numeric(18,4)").IsRequired(false);
        b.Property(x => x.TotalCost).HasColumnType("numeric(18,2)").IsRequired(false);

        b.HasOne(x => x.CostLayer).WithMany()
            .HasForeignKey(x => x.CostLayerId).OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => x.OrderItemId).HasDatabaseName("ix_consumptions_order_item");

        b.ToTable(t =>
        {
            t.HasCheckConstraint("ck_consumption_quantity_positive", "quantity > 0");
            t.HasCheckConstraint("ck_consumption_reversed_non_negative", "reversed_qty >= 0");
            t.HasCheckConstraint("ck_reversed_within_quantity", "reversed_qty <= quantity");
        });
    }
}
```

> **Note on `ck_layer_cost_non_negative`.** In PostgreSQL a `CHECK` that evaluates to `NULL` passes, so `unit_cost >= 0` permits a null cost while still rejecting a negative one. That is exactly the behaviour the optional-cost decision needs, and it is why the constraint needs no `OR unit_cost IS NULL` clause.

- [ ] **Step 8: Write the remaining configurations**

Create `backend/src/SlingKing.Infrastructure/Persistence/Configurations/PurchasingConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SlingKing.Domain.Purchasing;

namespace SlingKing.Infrastructure.Persistence.Configurations;

public class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    public void Configure(EntityTypeBuilder<Supplier> b)
    {
        b.ToTable("suppliers");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(256).IsRequired();
        b.Property(x => x.ContactPerson).HasMaxLength(128);
        b.Property(x => x.Phone).HasMaxLength(32);
        b.Property(x => x.Email).HasMaxLength(256);
    }
}

public class PurchaseConfiguration : IEntityTypeConfiguration<Purchase>
{
    public void Configure(EntityTypeBuilder<Purchase> b)
    {
        b.ToTable("purchases");
        b.HasKey(x => x.Id);
        b.Property(x => x.Number).HasMaxLength(32).IsRequired();
        b.Property(x => x.Status).HasConversion<short>();
        b.Property(x => x.TotalAmount).HasColumnType("numeric(18,2)");
        b.HasIndex(x => x.Number).IsUnique().HasDatabaseName("ux_purchases_number");
        b.HasOne(x => x.Supplier).WithMany()
            .HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class PurchaseItemConfiguration : IEntityTypeConfiguration<PurchaseItem>
{
    public void Configure(EntityTypeBuilder<PurchaseItem> b)
    {
        b.ToTable("purchase_items");
        b.HasKey(x => x.Id);
        b.Property(x => x.UnitCost).HasColumnType("numeric(18,4)").IsRequired(false);
        b.Property(x => x.LineTotal).HasColumnType("numeric(18,2)").IsRequired(false);
        b.HasOne(x => x.Purchase).WithMany(p => p.Items)
            .HasForeignKey(x => x.PurchaseId).OnDelete(DeleteBehavior.Cascade);
        b.ToTable(t => t.HasCheckConstraint(
            "ck_purchase_item_quantity_positive", "quantity > 0"));
    }
}
```

Create `backend/src/SlingKing.Infrastructure/Persistence/Configurations/SalesConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SlingKing.Domain.Sales;

namespace SlingKing.Infrastructure.Persistence.Configurations;

public class SalesChannelConfiguration : IEntityTypeConfiguration<SalesChannel>
{
    public void Configure(EntityTypeBuilder<SalesChannel> b)
    {
        b.ToTable("sales_channels");
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(32).IsRequired();
        b.Property(x => x.Name).HasMaxLength(64).IsRequired();
        b.HasIndex(x => x.Code).IsUnique().HasDatabaseName("ux_sales_channels_code");
    }
}

public class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> b)
    {
        b.ToTable("customers");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(256).IsRequired();
        b.Property(x => x.Phone).HasMaxLength(32);
        b.Property(x => x.InstagramHandle).HasMaxLength(64);
        b.Property(x => x.Email).HasMaxLength(256);
        b.HasIndex(x => x.Phone).HasDatabaseName("ix_customers_phone");
        b.HasIndex(x => x.InstagramHandle).HasDatabaseName("ix_customers_instagram");
    }
}

public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> b)
    {
        b.ToTable("orders");
        b.HasKey(x => x.Id);
        b.Property(x => x.Number).HasMaxLength(32).IsRequired();
        b.Property(x => x.Status).HasConversion<short>();
        b.Property(x => x.PaymentStatus).HasConversion<short>();
        b.Property(x => x.SubtotalAmount).HasColumnType("numeric(18,2)");
        b.Property(x => x.DiscountAmount).HasColumnType("numeric(18,2)");
        b.Property(x => x.ShippingAmount).HasColumnType("numeric(18,2)");
        b.Property(x => x.TotalAmount).HasColumnType("numeric(18,2)");
        b.HasIndex(x => x.Number).IsUnique().HasDatabaseName("ux_orders_number");
        b.HasIndex(x => x.CompletedAt).HasDatabaseName("ix_orders_completed_at");
        b.HasOne(x => x.Channel).WithMany()
            .HasForeignKey(x => x.ChannelId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Customer).WithMany()
            .HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> b)
    {
        b.ToTable("order_items");
        b.HasKey(x => x.Id);
        b.Property(x => x.UnitPrice).HasColumnType("numeric(18,2)");
        b.Property(x => x.DiscountAmount).HasColumnType("numeric(18,2)");
        b.Property(x => x.LineTotal).HasColumnType("numeric(18,2)");
        b.Property(x => x.CogsAmount).HasColumnType("numeric(18,2)").IsRequired(false);
        b.Property(x => x.ProductNameSnapshot).HasMaxLength(256).IsRequired();
        b.Property(x => x.SkuSnapshot).HasMaxLength(48).IsRequired();
        b.HasOne(x => x.Order).WithMany(o => o.Items)
            .HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);
        b.ToTable(t => t.HasCheckConstraint(
            "ck_order_item_quantity_positive", "quantity > 0"));
    }
}

public class OrderStatusHistoryConfiguration : IEntityTypeConfiguration<OrderStatusHistory>
{
    public void Configure(EntityTypeBuilder<OrderStatusHistory> b)
    {
        b.ToTable("order_status_history");
        b.HasKey(x => x.Id);
        b.Property(x => x.FromStatus).HasConversion<short?>();
        b.Property(x => x.ToStatus).HasConversion<short>();
        b.HasIndex(x => x.OrderId).HasDatabaseName("ix_order_status_history_order");
    }
}
```

Create `backend/src/SlingKing.Infrastructure/Persistence/Configurations/PaymentsConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SlingKing.Domain.Payments;

namespace SlingKing.Infrastructure.Persistence.Configurations;

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> b)
    {
        b.ToTable("payments");
        b.HasKey(x => x.Id);
        b.Property(x => x.Direction).HasConversion<short>();
        b.Property(x => x.Method).HasConversion<short>();
        b.Property(x => x.Amount).HasColumnType("numeric(18,2)");
        b.Property(x => x.Reference).HasMaxLength(128);
        b.HasIndex(x => x.OrderId).HasDatabaseName("ix_payments_order");
        b.ToTable(t => t.HasCheckConstraint(
            "ck_payment_amount_positive", "amount > 0"));
    }
}

public class ReturnConfiguration : IEntityTypeConfiguration<Return>
{
    public void Configure(EntityTypeBuilder<Return> b)
    {
        b.ToTable("returns");
        b.HasKey(x => x.Id);
        b.Property(x => x.Number).HasMaxLength(32).IsRequired();
        b.Property(x => x.Status).HasConversion<short>();
        b.Property(x => x.RefundAmount).HasColumnType("numeric(18,2)");
        b.Property(x => x.RestockedValue).HasColumnType("numeric(18,2)").IsRequired(false);
        b.HasIndex(x => x.Number).IsUnique().HasDatabaseName("ux_returns_number");
        b.HasIndex(x => x.OrderId).HasDatabaseName("ix_returns_order");
    }
}

public class ReturnItemConfiguration : IEntityTypeConfiguration<ReturnItem>
{
    public void Configure(EntityTypeBuilder<ReturnItem> b)
    {
        b.ToTable("return_items");
        b.HasKey(x => x.Id);
        b.Property(x => x.Condition).HasConversion<short>();
        b.Property(x => x.CogsReversed).HasColumnType("numeric(18,2)").IsRequired(false);
        b.HasOne(x => x.Return).WithMany(r => r.Items)
            .HasForeignKey(x => x.ReturnId).OnDelete(DeleteBehavior.Cascade);
        b.ToTable(t => t.HasCheckConstraint(
            "ck_return_item_quantity_positive", "quantity > 0"));
    }
}
```

Create `backend/src/SlingKing.Infrastructure/Persistence/Configurations/OperationsConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SlingKing.Domain.Operations;

namespace SlingKing.Infrastructure.Persistence.Configurations;

public class ExpenseCategoryConfiguration : IEntityTypeConfiguration<ExpenseCategory>
{
    public void Configure(EntityTypeBuilder<ExpenseCategory> b)
    {
        b.ToTable("expense_categories");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(128).IsRequired();
        b.HasIndex(x => x.Name).IsUnique().HasDatabaseName("ux_expense_categories_name");
    }
}

public class ExpenseConfiguration : IEntityTypeConfiguration<Expense>
{
    public void Configure(EntityTypeBuilder<Expense> b)
    {
        b.ToTable("expenses");
        b.HasKey(x => x.Id);
        b.Property(x => x.Amount).HasColumnType("numeric(18,2)");
        b.Property(x => x.Description).HasMaxLength(256);
        b.HasIndex(x => x.ExpenseDate).HasDatabaseName("ix_expenses_date");
        b.HasOne(x => x.Category).WithMany()
            .HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
        b.ToTable(t => t.HasCheckConstraint(
            "ck_expense_amount_positive", "amount > 0"));
    }
}

public class ImportBatchConfiguration : IEntityTypeConfiguration<ImportBatch>
{
    public void Configure(EntityTypeBuilder<ImportBatch> b)
    {
        b.ToTable("import_batches");
        b.HasKey(x => x.Id);
        b.Property(x => x.FileName).HasMaxLength(256).IsRequired();
        b.Property(x => x.Status).HasConversion<short>();
    }
}

public class ImportRowConfiguration : IEntityTypeConfiguration<ImportRow>
{
    public void Configure(EntityTypeBuilder<ImportRow> b)
    {
        b.ToTable("import_rows");
        b.HasKey(x => x.Id);
        b.Property(x => x.RawData).HasColumnType("jsonb").IsRequired();
        b.Property(x => x.Status).HasConversion<short>();
        b.Property(x => x.ErrorMessage).HasMaxLength(512);
        b.HasOne(x => x.Batch).WithMany(bb => bb.Rows)
            .HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => x.BatchId).HasDatabaseName("ix_import_rows_batch");
    }
}
```

- [ ] **Step 9: Run the tests and watch them pass**

Run: `dotnet test tests/SlingKing.UnitTests --filter ModelTests`
Expected: PASS, 7 tests. If the snake-case test fails, `UseSnakeCaseNamingConvention()` is missing from the options builder in the test helper.

- [ ] **Step 10: Commit**

```bash
git add .
git commit -m "feat: AppDbContext, IAppDbContext seam and EF configurations for all 27 tables"
```

---
## Task 7: The initial migration

Generated offline against the model — no database contact. The output is inspected as SQL before anything is applied anywhere.

**Files:**
- Create: `backend/src/SlingKing.Infrastructure/Persistence/DesignTimeDbContextFactory.cs`
- Create: `backend/src/SlingKing.Infrastructure/Migrations/*_InitialSchema.cs` (generated)
- Test: `backend/tests/SlingKing.UnitTests/Persistence/MigrationTests.cs`

**Interfaces:**
- Consumes: `AppDbContext` from Task 6.
- Produces: migration `InitialSchema`, applied by Task 9 and by every deploy thereafter.

- [ ] **Step 1: Write the design-time factory**

`dotnet ef` needs to construct the context without running `Program.cs`. It reads the migration connection string, which never needs to be reachable to *generate* a migration — only to apply one.

Create `backend/src/SlingKing.Infrastructure/Persistence/DesignTimeDbContextFactory.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using SlingKing.Infrastructure.Configuration;

namespace SlingKing.Infrastructure.Persistence;

/// <summary>
/// Used only by the dotnet-ef tooling. Always resolves the DIRECT connection —
/// migrations need prepared statements and advisory locks, which the transaction
/// pooler does not support.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        EnvFile.Load(Path.Combine(Directory.GetCurrentDirectory(), "..", "..", ".env"));
        EnvFile.Load(Path.Combine(Directory.GetCurrentDirectory(), ".env"));

        var connection = Environment.GetEnvironmentVariable("SLINGKING_DB_MIGRATION")
                         ?? "Host=localhost;Database=slingking_designtime;Username=postgres";

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connection, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history"))
            .UseSnakeCaseNamingConvention()
            .Options;

        return new AppDbContext(options);
    }
}
```

- [ ] **Step 2: Generate the migration**

```bash
cd backend
dotnet ef migrations add InitialSchema \
  --project src/SlingKing.Infrastructure \
  --startup-project src/SlingKing.Infrastructure \
  --output-dir Migrations
```

Expected: `Done.` and three new files under `src/SlingKing.Infrastructure/Migrations/`.

- [ ] **Step 3: Render the migration as SQL and read it**

```bash
dotnet ef migrations script \
  --project src/SlingKing.Infrastructure \
  --startup-project src/SlingKing.Infrastructure \
  --output ../scratch-initial-schema.sql
```

Open `scratch-initial-schema.sql` and confirm by eye:
- 27 `CREATE TABLE` statements
- `available_qty integer GENERATED ALWAYS AS (physical_qty - reserved_qty) STORED`
- `CONSTRAINT ck_reserved_within_physical CHECK (reserved_qty <= physical_qty)`
- `unit_cost numeric(18,4)` on `inventory_cost_layers` with **no** `NOT NULL`
- `CREATE INDEX ix_layers_fifo … WHERE (remaining_qty > 0)`
- `raw_data jsonb NOT NULL` on `import_rows`

This file is scratch — do not commit it.

- [ ] **Step 4: Write the test that keeps the migration honest**

Create `backend/tests/SlingKing.UnitTests/Persistence/MigrationTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Shouldly;
using SlingKing.Infrastructure.Persistence;
using Xunit;

namespace SlingKing.UnitTests.Persistence;

public class MigrationTests
{
    private static AppDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=unused")
            .UseSnakeCaseNamingConvention()
            .Options;

        return new AppDbContext(options);
    }

    [Fact]
    public void There_is_exactly_one_migration_and_it_is_the_initial_schema()
    {
        using var db = BuildContext();

        var migrations = db.Database.GetMigrations().ToArray();

        migrations.Length.ShouldBe(1);
        migrations[0].ShouldEndWith("InitialSchema");
    }

    [Fact]
    public void The_model_has_no_changes_left_unmigrated()
    {
        using var db = BuildContext();

        var differ = db.GetService<IMigrationsModelDiffer>();
        var snapshot = db.GetService<IMigrationsAssembly>().ModelSnapshot!;
        var source = db.GetService<IModelRuntimeInitializer>()
            .Initialize(snapshot.Model, designTime: true);

        var differences = differ.GetDifferences(
            source.GetRelationalModel(),
            db.GetService<IDesignTimeModel>().Model.GetRelationalModel());

        differences.ShouldBeEmpty(
            "the model has drifted from the migration — run 'dotnet ef migrations add'");
    }
}
```

- [ ] **Step 5: Run the tests and watch them pass**

Run: `dotnet test tests/SlingKing.UnitTests --filter MigrationTests`
Expected: PASS, 2 tests.

If test two fails, the model and the migration disagree — regenerate rather than hand-editing. During M1 there is no applied migration to preserve, so it is safe to delete the `Migrations` folder and repeat Step 2.

- [ ] **Step 6: Commit**

```bash
rm ../scratch-initial-schema.sql
git add .
git commit -m "feat: initial migration covering all 27 tables"
```

---

## Task 8: The test-schema guard

This task exists because a single database now holds both real stock and a destructive test suite. The guard makes the accident structurally impossible rather than merely unlikely, and it lands **before** any migration is applied.

**Files:**
- Create: `backend/tests/SlingKing.IntegrationTests/Fixtures/TestSchemaFixture.cs`
- Test: `backend/tests/SlingKing.UnitTests/Persistence/SchemaGuardTests.cs`

**Interfaces:**
- Consumes: `AppDbContext` from Task 6.
- Produces: `TestSchemaFixture` — an `IAsyncLifetime` xUnit fixture exposing `AppDbContext Db`, plus the static `TestSchemaFixture.AssertTargetsTestSchema(string connectionString)` used by the unit tests above.

- [ ] **Step 1: Write the failing guard tests**

Create `backend/tests/SlingKing.UnitTests/Persistence/SchemaGuardTests.cs`:

```csharp
using Shouldly;
using SlingKing.IntegrationTests.Fixtures;
using Xunit;

namespace SlingKing.UnitTests.Persistence;

public class SchemaGuardTests
{
    [Theory]
    [InlineData("Host=db.supabase.co;Database=postgres;Search Path=test")]
    [InlineData("Host=db.supabase.co;Database=postgres;SearchPath=test")]
    [InlineData("Host=db.supabase.co;Database=postgres;search path=TEST")]
    public void Accepts_a_connection_string_pinned_to_the_test_schema(string connection)
    {
        Should.NotThrow(() => TestSchemaFixture.AssertTargetsTestSchema(connection));
    }

    [Theory]
    [InlineData("Host=db.supabase.co;Database=postgres")]
    [InlineData("Host=db.supabase.co;Database=postgres;Search Path=public")]
    [InlineData("Host=db.supabase.co;Database=postgres;Search Path=public,test")]
    [InlineData("")]
    public void Refuses_anything_that_could_reach_real_data(string connection)
    {
        Should.Throw<InvalidOperationException>(
            () => TestSchemaFixture.AssertTargetsTestSchema(connection));
    }
}
```

- [ ] **Step 2: Run the tests and watch them fail**

Run: `dotnet test tests/SlingKing.UnitTests --filter SchemaGuardTests`
Expected: FAIL — `TestSchemaFixture` does not exist.

- [ ] **Step 3: Reference the integration project and add its packages**

```bash
cd backend
dotnet add tests/SlingKing.UnitTests reference tests/SlingKing.IntegrationTests
dotnet add tests/SlingKing.IntegrationTests package Shouldly --version 4.3.0
dotnet add tests/SlingKing.IntegrationTests package Npgsql.EntityFrameworkCore.PostgreSQL --version 8.0.11
dotnet add tests/SlingKing.IntegrationTests package EFCore.NamingConventions --version 8.0.3
dotnet add tests/SlingKing.IntegrationTests package Microsoft.AspNetCore.Mvc.Testing --version 8.0.30
```

- [ ] **Step 4: Write the fixture**

Create `backend/tests/SlingKing.IntegrationTests/Fixtures/TestSchemaFixture.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SlingKing.Infrastructure.Configuration;
using SlingKing.Infrastructure.Persistence;
using Xunit;

namespace SlingKing.IntegrationTests.Fixtures;

/// <summary>
/// Creates a throwaway "test" schema, migrates into it, and drops it afterwards.
/// The guard below is the only thing standing between a destructive test run and
/// the schema holding real stock, so it runs before a single connection is opened.
/// </summary>
public class TestSchemaFixture : IAsyncLifetime
{
    public const string SchemaName = "test";

    public AppDbContext Db { get; private set; } = null!;

    private string _connectionString = null!;

    /// <summary>
    /// Throws unless the connection string pins the search path to exactly the test
    /// schema. A list such as "public,test" is rejected: the first entry is where
    /// unqualified writes would land.
    /// </summary>
    public static void AssertTargetsTestSchema(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "SLINGKING_DB_TEST is not set. Integration tests will not run without it.");
        }

        var searchPath = new NpgsqlConnectionStringBuilder(connectionString).SearchPath;

        if (!string.Equals(searchPath?.Trim(), SchemaName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Integration tests refuse to run outside the '{SchemaName}' schema. " +
                $"SLINGKING_DB_TEST resolves to search path '{searchPath ?? "(none)"}'. " +
                "Point it at the test schema — never at public.");
        }
    }

    public async Task InitializeAsync()
    {
        EnvFile.Load(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".env"));

        _connectionString = Environment.GetEnvironmentVariable("SLINGKING_DB_TEST") ?? "";
        AssertTargetsTestSchema(_connectionString);

        await using (var admin = new NpgsqlConnection(_connectionString))
        {
            await admin.OpenAsync();
            await using var cmd = admin.CreateCommand();
            cmd.CommandText =
                $"drop schema if exists {SchemaName} cascade; create schema {SchemaName};";
            await cmd.ExecuteNonQueryAsync();
        }

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_connectionString, npgsql =>
            {
                npgsql.MigrationsHistoryTable("__ef_migrations_history", SchemaName);
            })
            .UseSnakeCaseNamingConvention()
            .Options;

        Db = new AppDbContext(options);
        await Db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await Db.DisposeAsync();

        await using var admin = new NpgsqlConnection(_connectionString);
        await admin.OpenAsync();
        await using var cmd = admin.CreateCommand();
        cmd.CommandText = $"drop schema if exists {SchemaName} cascade;";
        await cmd.ExecuteNonQueryAsync();
    }
}

[CollectionDefinition("database")]
public class DatabaseCollection : ICollectionFixture<TestSchemaFixture>
{
}
```

- [ ] **Step 5: Run the tests and watch them pass**

Run: `dotnet test tests/SlingKing.UnitTests --filter SchemaGuardTests`
Expected: PASS, 7 cases. These need no database — they test the guard, not the connection.

- [ ] **Step 6: Commit**

```bash
git add .
git commit -m "feat: test-schema fixture that refuses to run against public"
```

---

## Task 9: Apply the schema to Supabase and prove the constraints bite

Configuration written on a whiteboard is a hope. This task applies the migration for real and then attempts each forbidden write, asserting the database rejects it.

**Files:**
- Test: `backend/tests/SlingKing.IntegrationTests/Persistence/SchemaConstraintTests.cs`

**Interfaces:**
- Consumes: `TestSchemaFixture` from Task 8, migration from Task 7.
- Produces: a migrated `public` schema on Supabase; the `database` xUnit collection every later integration test joins.

- [ ] **Step 1: Fill in the real connection strings**

Open `backend/.env` (created in Task 2) and paste the three connection strings from Supabase → *Project Settings → Database*. `SLINGKING_DB_TEST` must be the direct connection with `;Search Path=test` appended.

Sanity-check that git still cannot see the file:

```bash
git status --porcelain backend/.env
```
Expected: no output.

- [ ] **Step 2: Write the constraint tests**

Create `backend/tests/SlingKing.IntegrationTests/Persistence/SchemaConstraintTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Shouldly;
using SlingKing.Domain.Catalog;
using SlingKing.Domain.Common;
using SlingKing.Domain.Inventory;
using SlingKing.IntegrationTests.Fixtures;
using Xunit;

namespace SlingKing.IntegrationTests.Persistence;

[Collection("database")]
public class SchemaConstraintTests
{
    private readonly TestSchemaFixture _fixture;

    public SchemaConstraintTests(TestSchemaFixture fixture) => _fixture = fixture;

    private async Task<(long VariantId, int LocationId)> SeedVariantAsync()
    {
        var db = _fixture.Db;
        var now = DateTimeOffset.UtcNow;

        var product = new Product
        {
            Code = $"BAG-{Random.Shared.Next(100000, 999999)}",
            Name = "Test tote",
            CreatedAt = now,
            UpdatedAt = now
        };
        db.Products.Add(product);
        await db.SaveChangesAsync();

        var variant = new ProductVariant
        {
            ProductId = product.Id,
            Sku = product.Code + "-V01",
            SellingPrice = 1200m,
            LowStockThreshold = 2,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.ProductVariants.Add(variant);

        var location = new Location { Name = "Home", IsDefault = true };
        db.Locations.Add(location);
        await db.SaveChangesAsync();

        return (variant.Id, location.Id);
    }

    [Fact]
    public async Task Available_quantity_is_computed_by_the_database()
    {
        var db = _fixture.Db;
        var (variantId, locationId) = await SeedVariantAsync();

        db.StockLevels.Add(new StockLevel
        {
            VariantId = variantId,
            LocationId = locationId,
            PhysicalQty = 20,
            ReservedQty = 5,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var stored = await db.StockLevels.AsNoTracking()
            .SingleAsync(s => s.VariantId == variantId && s.LocationId == locationId);

        stored.AvailableQty.ShouldBe(15);
    }

    [Fact]
    public async Task Physical_stock_cannot_go_negative()
    {
        var db = _fixture.Db;
        var (variantId, locationId) = await SeedVariantAsync();

        db.StockLevels.Add(new StockLevel
        {
            VariantId = variantId,
            LocationId = locationId,
            PhysicalQty = -1,
            UpdatedAt = DateTimeOffset.UtcNow
        });

        var error = await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());
        error.InnerException!.Message.ShouldContain("ck_physical_non_negative");

        db.ChangeTracker.Clear();
    }

    [Fact]
    public async Task Reserved_stock_cannot_exceed_physical_stock()
    {
        var db = _fixture.Db;
        var (variantId, locationId) = await SeedVariantAsync();

        db.StockLevels.Add(new StockLevel
        {
            VariantId = variantId,
            LocationId = locationId,
            PhysicalQty = 3,
            ReservedQty = 4,
            UpdatedAt = DateTimeOffset.UtcNow
        });

        var error = await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());
        error.InnerException!.Message.ShouldContain("ck_reserved_within_physical");

        db.ChangeTracker.Clear();
    }

    [Fact]
    public async Task A_cost_layer_may_be_saved_with_no_cost_at_all()
    {
        var db = _fixture.Db;
        var (variantId, locationId) = await SeedVariantAsync();

        db.InventoryCostLayers.Add(new InventoryCostLayer
        {
            VariantId = variantId,
            LocationId = locationId,
            SourceType = CostLayerSourceType.OpeningStock,
            SourceId = 1,
            ReceivedAt = DateTimeOffset.UtcNow,
            OriginalQty = 10,
            RemainingQty = 10,
            UnitCost = null,
            CreatedAt = DateTimeOffset.UtcNow
        });

        await Should.NotThrowAsync(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task A_cost_layer_may_not_be_saved_with_a_negative_cost()
    {
        var db = _fixture.Db;
        var (variantId, locationId) = await SeedVariantAsync();

        db.InventoryCostLayers.Add(new InventoryCostLayer
        {
            VariantId = variantId,
            LocationId = locationId,
            SourceType = CostLayerSourceType.Purchase,
            SourceId = 1,
            ReceivedAt = DateTimeOffset.UtcNow,
            OriginalQty = 10,
            RemainingQty = 10,
            UnitCost = -1m,
            CreatedAt = DateTimeOffset.UtcNow
        });

        var error = await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());
        error.InnerException!.Message.ShouldContain("ck_layer_cost_non_negative");

        db.ChangeTracker.Clear();
    }

    [Fact]
    public async Task Remaining_quantity_can_never_exceed_the_original()
    {
        var db = _fixture.Db;
        var (variantId, locationId) = await SeedVariantAsync();

        db.InventoryCostLayers.Add(new InventoryCostLayer
        {
            VariantId = variantId,
            LocationId = locationId,
            SourceType = CostLayerSourceType.Purchase,
            SourceId = 1,
            ReceivedAt = DateTimeOffset.UtcNow,
            OriginalQty = 5,
            RemainingQty = 6,
            CreatedAt = DateTimeOffset.UtcNow
        });

        var error = await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());
        error.InnerException!.Message.ShouldContain("ck_remaining_within_original");

        db.ChangeTracker.Clear();
    }

    [Fact]
    public async Task Every_expected_table_exists()
    {
        var count = await _fixture.Db.Database
            .SqlQuery<int>($@"
                select count(*)::int
                  from information_schema.tables
                 where table_schema = 'test' and table_type = 'BASE TABLE'")
            .SingleAsync();

        // 27 domain tables plus EF's own __ef_migrations_history
        count.ShouldBe(28);
    }
}
```

- [ ] **Step 3: Run the integration tests**

Run: `dotnet test tests/SlingKing.IntegrationTests`
Expected: PASS, 7 tests. First run takes noticeably longer — it creates the schema and applies the migration over the network.

If it fails with `Integration tests refuse to run outside the 'test' schema`, the guard is working correctly and `SLINGKING_DB_TEST` is wrong. Fix the connection string, not the guard.

- [ ] **Step 4: Apply the migration to the real schema**

Only now, with the constraints proven, touch `public`:

```bash
cd backend
dotnet ef database update \
  --project src/SlingKing.Infrastructure \
  --startup-project src/SlingKing.Infrastructure
```

Expected: `Applying migration '..._InitialSchema'` then `Done.`

- [ ] **Step 5: Verify in the Supabase SQL editor**

Open Supabase → *SQL Editor* and run:

```sql
select table_name
  from information_schema.tables
 where table_schema = 'public' and table_type = 'BASE TABLE'
 order by table_name;
```

Expected: 28 rows — the 27 tables plus `__ef_migrations_history`.

- [ ] **Step 6: Commit**

```bash
git add .
git commit -m "test: prove schema constraints reject every forbidden write"
```

---

## Task 10: Gap-free number sequences

`ORD-000001`. Getting this wrong under concurrency produces duplicate order numbers, which is the kind of bug that surfaces as a customer dispute. `MAX(id)+1` is not an option; a single `UPDATE … RETURNING` is.

**Files:**
- Create: `backend/src/SlingKing.Application/Common/Abstractions/INumberSequenceAllocator.cs`
- Create: `backend/src/SlingKing.Infrastructure/Persistence/NumberSequenceAllocator.cs`
- Test: `backend/tests/SlingKing.IntegrationTests/Persistence/NumberSequenceTests.cs`

**Interfaces:**
- Consumes: `IAppDbContext` from Task 6, `TestSchemaFixture` from Task 8.
- Produces: `INumberSequenceAllocator.NextAsync(string key, CancellationToken)` returning `string` — the formatted document number. Milestones M2, M3, R2·M7 and R2·M8 all call this.

- [ ] **Step 1: Write the failing tests**

Create `backend/tests/SlingKing.IntegrationTests/Persistence/NumberSequenceTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Shouldly;
using SlingKing.Domain.Platform;
using SlingKing.Infrastructure.Persistence;
using SlingKing.IntegrationTests.Fixtures;
using Xunit;

namespace SlingKing.IntegrationTests.Persistence;

[Collection("database")]
public class NumberSequenceTests
{
    private readonly TestSchemaFixture _fixture;

    public NumberSequenceTests(TestSchemaFixture fixture) => _fixture = fixture;

    private async Task<string> SeedSequenceAsync(string prefix, long start = 1, int padding = 6)
    {
        var key = $"seq-{Guid.NewGuid():N}"[..20];

        _fixture.Db.NumberSequences.Add(new NumberSequence
        {
            Key = key, Prefix = prefix, NextValue = start, Padding = padding
        });
        await _fixture.Db.SaveChangesAsync();

        return key;
    }

    [Fact]
    public async Task Formats_the_number_with_prefix_and_padding()
    {
        var key = await SeedSequenceAsync("ORD");
        var allocator = new NumberSequenceAllocator(_fixture.Db);

        (await allocator.NextAsync(key)).ShouldBe("ORD-000001");
        (await allocator.NextAsync(key)).ShouldBe("ORD-000002");
    }

    [Fact]
    public async Task Respects_a_starting_value_and_a_custom_padding()
    {
        var key = await SeedSequenceAsync("PUR", start: 47, padding: 4);
        var allocator = new NumberSequenceAllocator(_fixture.Db);

        (await allocator.NextAsync(key)).ShouldBe("PUR-0047");
    }

    [Fact]
    public async Task Numbers_do_not_overflow_the_padding()
    {
        var key = await SeedSequenceAsync("RET", start: 1_000_000);
        var allocator = new NumberSequenceAllocator(_fixture.Db);

        (await allocator.NextAsync(key)).ShouldBe("RET-1000000");
    }

    [Fact]
    public async Task An_unknown_key_fails_loudly_rather_than_inventing_a_number()
    {
        var allocator = new NumberSequenceAllocator(_fixture.Db);

        await Should.ThrowAsync<InvalidOperationException>(
            () => allocator.NextAsync("no-such-sequence"));
    }

    [Fact]
    public async Task Twenty_concurrent_callers_receive_twenty_distinct_numbers()
    {
        var key = await SeedSequenceAsync("ORD");
        var connection = _fixture.Db.Database.GetConnectionString()!;

        var tasks = Enumerable.Range(0, 20).Select(async _ =>
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(connection, o =>
                    o.MigrationsHistoryTable("__ef_migrations_history", TestSchemaFixture.SchemaName))
                .UseSnakeCaseNamingConvention()
                .Options;

            await using var db = new AppDbContext(options);
            return await new NumberSequenceAllocator(db).NextAsync(key);
        });

        var numbers = await Task.WhenAll(tasks);

        numbers.Distinct().Count().ShouldBe(20, "duplicate document numbers were allocated");
    }
}
```

- [ ] **Step 2: Run the tests and watch them fail**

Run: `dotnet test tests/SlingKing.IntegrationTests --filter NumberSequenceTests`
Expected: FAIL — `NumberSequenceAllocator` does not exist.

- [ ] **Step 3: Write the interface**

Create `backend/src/SlingKing.Application/Common/Abstractions/INumberSequenceAllocator.cs`:

```csharp
namespace SlingKing.Application.Common.Abstractions;

/// <summary>
/// Allocates the next document number for a sequence key ("order", "purchase",
/// "return", "product"). Gap-free and safe under concurrency.
/// </summary>
public interface INumberSequenceAllocator
{
    Task<string> NextAsync(string key, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 4: Write the implementation**

Create `backend/src/SlingKing.Infrastructure/Persistence/NumberSequenceAllocator.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SlingKing.Application.Common.Abstractions;

namespace SlingKing.Infrastructure.Persistence;

public sealed class NumberSequenceAllocator : INumberSequenceAllocator
{
    private readonly IAppDbContext _db;

    public NumberSequenceAllocator(IAppDbContext db) => _db = db;

    private sealed record Allocation(string Prefix, long Value, int Padding);

    public async Task<string> NextAsync(string key, CancellationToken cancellationToken = default)
    {
        // A single statement: the read, the increment and the claim are one atomic
        // operation, so two concurrent callers can never observe the same value.
        var rows = await _db.Database
            .SqlQuery<Allocation>($@"
                update number_sequences
                   set next_value = next_value + 1
                 where key = {key}
                returning prefix as ""Prefix"",
                          next_value - 1 as ""Value"",
                          padding as ""Padding""")
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            throw new InvalidOperationException(
                $"Number sequence '{key}' does not exist. Seed it before allocating from it.");
        }

        var allocation = rows[0];

        return $"{allocation.Prefix}-{allocation.Value.ToString(new string('0', allocation.Padding))}";
    }
}
```

- [ ] **Step 5: Run the tests and watch them pass**

Run: `dotnet test tests/SlingKing.IntegrationTests --filter NumberSequenceTests`
Expected: PASS, 5 tests. The concurrency test is the one that matters — if it reports fewer than 20 distinct numbers, the allocation is not atomic.

- [ ] **Step 6: Commit**

```bash
git add .
git commit -m "feat: gap-free number sequence allocation via UPDATE ... RETURNING"
```

---

## Task 11: Password hashing, JWT issuance and refresh-token rotation

**Files:**
- Create: `backend/src/SlingKing.Application/Common/Abstractions/IPasswordHasher.cs`, `ITokenService.cs`
- Create: `backend/src/SlingKing.Infrastructure/Auth/BcryptPasswordHasher.cs`, `JwtTokenService.cs`, `JwtOptions.cs`
- Test: `backend/tests/SlingKing.UnitTests/Auth/PasswordHasherTests.cs`, `JwtTokenServiceTests.cs`

**Interfaces:**
- Consumes: `IClock` from Task 6.
- Produces:
  - `IPasswordHasher.Hash(string password) : string` and `Verify(string password, string hash) : bool`
  - `ITokenService.CreateAccessToken(long userId, string email) : string`
  - `ITokenService.CreateRefreshToken() : (string Token, string Hash)` — the raw token goes to the client, only the hash is stored
  - `ITokenService.HashRefreshToken(string token) : string`
  - `JwtOptions` with `Secret`, `Issuer`, `Audience`, `AccessTokenMinutes`, `RefreshTokenDays`

- [ ] **Step 1: Add the packages**

```bash
cd backend
dotnet add src/SlingKing.Infrastructure package BCrypt.Net-Next --version 4.2.1
dotnet add src/SlingKing.Infrastructure package Microsoft.AspNetCore.Authentication.JwtBearer --version 8.0.30
```

- [ ] **Step 2: Write the failing tests**

Create `backend/tests/SlingKing.UnitTests/Auth/PasswordHasherTests.cs`:

```csharp
using Shouldly;
using SlingKing.Infrastructure.Auth;
using Xunit;

namespace SlingKing.UnitTests.Auth;

public class PasswordHasherTests
{
    private readonly BcryptPasswordHasher _hasher = new();

    [Fact]
    public void A_hash_never_contains_the_password()
    {
        var hash = _hasher.Hash("correct horse battery staple");

        hash.ShouldNotContain("correct horse");
        hash.ShouldStartWith("$2");
    }

    [Fact]
    public void The_same_password_hashes_differently_every_time()
    {
        _hasher.Hash("same").ShouldNotBe(_hasher.Hash("same"));
    }

    [Fact]
    public void Verify_accepts_the_right_password()
    {
        var hash = _hasher.Hash("s3cret-value");

        _hasher.Verify("s3cret-value", hash).ShouldBeTrue();
    }

    [Fact]
    public void Verify_rejects_the_wrong_password()
    {
        var hash = _hasher.Hash("s3cret-value");

        _hasher.Verify("s3cret-valuf", hash).ShouldBeFalse();
    }

    [Fact]
    public void Verify_returns_false_rather_than_throwing_on_a_malformed_hash()
    {
        _hasher.Verify("anything", "not-a-bcrypt-hash").ShouldBeFalse();
    }
}
```

Create `backend/tests/SlingKing.UnitTests/Auth/JwtTokenServiceTests.cs`:

```csharp
using System.IdentityModel.Tokens.Jwt;
using Microsoft.Extensions.Options;
using Shouldly;
using SlingKing.Application.Common.Abstractions;
using SlingKing.Infrastructure;
using SlingKing.Infrastructure.Auth;
using Xunit;

namespace SlingKing.UnitTests.Auth;

public class JwtTokenServiceTests
{
    private static JwtTokenService BuildService(IClock? clock = null)
    {
        var options = Options.Create(new JwtOptions
        {
            Secret = "a-test-signing-key-of-at-least-32-bytes-length",
            Issuer = "slingking-api",
            Audience = "slingking-web",
            AccessTokenMinutes = 30,
            RefreshTokenDays = 14
        });

        return new JwtTokenService(options, clock ?? new SystemClock());
    }

    [Fact]
    public void An_access_token_carries_the_user_id_and_email()
    {
        var token = BuildService().CreateAccessToken(42, "owner@slingking.test");

        var parsed = new JwtSecurityTokenHandler().ReadJwtToken(token);

        parsed.Subject.ShouldBe("42");
        parsed.Claims.Single(c => c.Type == JwtRegisteredClaimNames.Email)
            .Value.ShouldBe("owner@slingking.test");
        parsed.Issuer.ShouldBe("slingking-api");
        parsed.Audiences.ShouldContain("slingking-web");
    }

    [Fact]
    public void An_access_token_expires_after_the_configured_window()
    {
        var token = BuildService().CreateAccessToken(1, "a@b.test");

        var parsed = new JwtSecurityTokenHandler().ReadJwtToken(token);

        (parsed.ValidTo - parsed.ValidFrom).TotalMinutes.ShouldBe(30, tolerance: 1);
    }

    [Fact]
    public void Every_access_token_has_a_unique_jti()
    {
        var service = BuildService();
        var handler = new JwtSecurityTokenHandler();

        var first = handler.ReadJwtToken(service.CreateAccessToken(1, "a@b.test"));
        var second = handler.ReadJwtToken(service.CreateAccessToken(1, "a@b.test"));

        first.Id.ShouldNotBe(second.Id);
    }

    [Fact]
    public void A_refresh_token_is_returned_raw_but_only_its_hash_is_meant_for_storage()
    {
        var (token, hash) = BuildService().CreateRefreshToken();

        token.Length.ShouldBeGreaterThan(40);
        hash.ShouldNotBe(token);
        hash.Length.ShouldBe(64);   // SHA-256, lowercase hex
    }

    [Fact]
    public void Hashing_a_refresh_token_is_deterministic()
    {
        var service = BuildService();
        var (token, hash) = service.CreateRefreshToken();

        service.HashRefreshToken(token).ShouldBe(hash);
    }

    [Fact]
    public void Two_refresh_tokens_are_never_the_same()
    {
        var service = BuildService();

        service.CreateRefreshToken().Token.ShouldNotBe(service.CreateRefreshToken().Token);
    }

    [Fact]
    public void A_short_secret_is_refused_at_construction_rather_than_at_first_login()
    {
        var options = Options.Create(new JwtOptions
        {
            Secret = "too-short",
            Issuer = "i",
            Audience = "a",
            AccessTokenMinutes = 30,
            RefreshTokenDays = 14
        });

        Should.Throw<InvalidOperationException>(
            () => new JwtTokenService(options, new SystemClock()));
    }
}
```

- [ ] **Step 3: Run the tests and watch them fail**

Run: `dotnet test tests/SlingKing.UnitTests --filter "PasswordHasherTests|JwtTokenServiceTests"`
Expected: FAIL — none of these types exist.

- [ ] **Step 4: Write the abstractions**

Create `backend/src/SlingKing.Application/Common/Abstractions/IPasswordHasher.cs`:

```csharp
namespace SlingKing.Application.Common.Abstractions;

public interface IPasswordHasher
{
    string Hash(string password);

    /// <summary>Returns false — never throws — for a malformed or unrecognised hash.</summary>
    bool Verify(string password, string hash);
}
```

Create `backend/src/SlingKing.Application/Common/Abstractions/ITokenService.cs`:

```csharp
namespace SlingKing.Application.Common.Abstractions;

public interface ITokenService
{
    string CreateAccessToken(long userId, string email);

    /// <summary>
    /// The raw token is handed to the client exactly once. Only the hash is ever persisted,
    /// so a leaked database does not hand an attacker usable sessions.
    /// </summary>
    (string Token, string Hash) CreateRefreshToken();

    string HashRefreshToken(string token);
}
```

- [ ] **Step 5: Write the implementations**

Create `backend/src/SlingKing.Infrastructure/Auth/JwtOptions.cs`:

```csharp
namespace SlingKing.Infrastructure.Auth;

public class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Secret { get; set; } = "";
    public string Issuer { get; set; } = "";
    public string Audience { get; set; } = "";
    public int AccessTokenMinutes { get; set; } = 30;
    public int RefreshTokenDays { get; set; } = 14;
}
```

Create `backend/src/SlingKing.Infrastructure/Auth/BcryptPasswordHasher.cs`:

```csharp
using SlingKing.Application.Common.Abstractions;

namespace SlingKing.Infrastructure.Auth;

public sealed class BcryptPasswordHasher : IPasswordHasher
{
    private const int WorkFactor = 12;

    public string Hash(string password) => BCrypt.Net.BCrypt.HashPassword(password, WorkFactor);

    public bool Verify(string password, string hash)
    {
        try
        {
            return BCrypt.Net.BCrypt.Verify(password, hash);
        }
        catch (BCrypt.Net.SaltParseException)
        {
            // A malformed stored hash is an authentication failure, not a server error.
            return false;
        }
    }
}
```

Create `backend/src/SlingKing.Infrastructure/Auth/JwtTokenService.cs`:

```csharp
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SlingKing.Application.Common.Abstractions;

namespace SlingKing.Infrastructure.Auth;

public sealed class JwtTokenService : ITokenService
{
    private const int MinimumSecretBytes = 32;

    private readonly JwtOptions _options;
    private readonly IClock _clock;
    private readonly SigningCredentials _credentials;

    public JwtTokenService(IOptions<JwtOptions> options, IClock clock)
    {
        _options = options.Value;
        _clock = clock;

        var keyBytes = Encoding.UTF8.GetBytes(_options.Secret ?? "");

        if (keyBytes.Length < MinimumSecretBytes)
        {
            throw new InvalidOperationException(
                $"SLINGKING_JWT_SECRET must be at least {MinimumSecretBytes} bytes. " +
                "Generate one with: openssl rand -base64 48");
        }

        _credentials = new SigningCredentials(
            new SymmetricSecurityKey(keyBytes), SecurityAlgorithms.HmacSha256);
    }

    public string CreateAccessToken(long userId, string email)
    {
        var issuedAt = _clock.UtcNow.UtcDateTime;

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, email),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
        };

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: issuedAt,
            expires: issuedAt.AddMinutes(_options.AccessTokenMinutes),
            signingCredentials: _credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public (string Token, string Hash) CreateRefreshToken()
    {
        var raw = RandomNumberGenerator.GetBytes(48);
        var token = Convert.ToBase64String(raw)
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

        return (token, HashRefreshToken(token));
    }

    public string HashRefreshToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
}
```

- [ ] **Step 6: Run the tests and watch them pass**

Run: `dotnet test tests/SlingKing.UnitTests --filter "PasswordHasherTests|JwtTokenServiceTests"`
Expected: PASS, 12 tests.

- [ ] **Step 7: Commit**

```bash
git add .
git commit -m "feat: bcrypt password hashing and JWT issuance with hashed refresh tokens"
```

---
## Task 12: Idempotent database seeding

Reference data the system cannot function without: one location, three sales channels, four number sequences, the expense categories, and the single user. Running it twice must change nothing — it executes on every startup.

**Files:**
- Create: `backend/src/SlingKing.Infrastructure/Persistence/Seed/DatabaseSeeder.cs`
- Test: `backend/tests/SlingKing.IntegrationTests/Persistence/SeedingTests.cs`

**Interfaces:**
- Consumes: `IAppDbContext`, `IClock`, `IPasswordHasher`.
- Produces: `DatabaseSeeder.SeedAsync(CancellationToken)`. Number sequence keys seeded: `product`, `purchase`, `order`, `return`. Sales channel codes seeded: `instagram`, `whatsapp`, `offline`.

- [ ] **Step 1: Write the failing tests**

Create `backend/tests/SlingKing.IntegrationTests/Persistence/SeedingTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Shouldly;
using SlingKing.Infrastructure;
using SlingKing.Infrastructure.Auth;
using SlingKing.Infrastructure.Persistence.Seed;
using SlingKing.IntegrationTests.Fixtures;
using Xunit;

namespace SlingKing.IntegrationTests.Persistence;

[Collection("database")]
public class SeedingTests
{
    private readonly TestSchemaFixture _fixture;

    public SeedingTests(TestSchemaFixture fixture) => _fixture = fixture;

    private DatabaseSeeder BuildSeeder() => new(
        _fixture.Db,
        new SystemClock(),
        new BcryptPasswordHasher(),
        adminEmail: "owner@slingking.test",
        adminPassword: "initial-password-01");

    [Fact]
    public async Task Seeding_creates_the_reference_data_the_system_needs()
    {
        await BuildSeeder().SeedAsync();

        (await _fixture.Db.Locations.CountAsync(l => l.IsDefault)).ShouldBe(1);
        (await _fixture.Db.SalesChannels.CountAsync()).ShouldBeGreaterThanOrEqualTo(3);
        (await _fixture.Db.NumberSequences.CountAsync()).ShouldBe(4);
        (await _fixture.Db.ExpenseCategories.CountAsync()).ShouldBeGreaterThanOrEqualTo(4);
        (await _fixture.Db.Users.CountAsync(u => u.Email == "owner@slingking.test")).ShouldBe(1);
    }

    [Fact]
    public async Task Offline_is_the_only_channel_that_does_not_reserve()
    {
        await BuildSeeder().SeedAsync();

        var offline = await _fixture.Db.SalesChannels.SingleAsync(c => c.Code == "offline");
        var instagram = await _fixture.Db.SalesChannels.SingleAsync(c => c.Code == "instagram");

        offline.UsesReservation.ShouldBeFalse();
        offline.RequiresShipping.ShouldBeFalse();
        instagram.UsesReservation.ShouldBeTrue();
        instagram.RequiresShipping.ShouldBeTrue();
    }

    [Fact]
    public async Task The_seeded_password_is_stored_hashed_and_verifies()
    {
        await BuildSeeder().SeedAsync();

        var user = await _fixture.Db.Users.SingleAsync(u => u.Email == "owner@slingking.test");

        user.PasswordHash.ShouldNotContain("initial-password-01");
        new BcryptPasswordHasher().Verify("initial-password-01", user.PasswordHash).ShouldBeTrue();
    }

    [Fact]
    public async Task Seeding_twice_changes_nothing()
    {
        await BuildSeeder().SeedAsync();
        await BuildSeeder().SeedAsync();
        await BuildSeeder().SeedAsync();

        (await _fixture.Db.Locations.CountAsync()).ShouldBe(1);
        (await _fixture.Db.SalesChannels.CountAsync()).ShouldBe(3);
        (await _fixture.Db.NumberSequences.CountAsync()).ShouldBe(4);
        (await _fixture.Db.Users.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Re_seeding_never_resets_an_already_allocated_sequence()
    {
        await BuildSeeder().SeedAsync();

        var sequence = await _fixture.Db.NumberSequences.SingleAsync(s => s.Key == "order");
        sequence.NextValue = 57;
        await _fixture.Db.SaveChangesAsync();

        await BuildSeeder().SeedAsync();

        (await _fixture.Db.NumberSequences.AsNoTracking()
            .SingleAsync(s => s.Key == "order")).NextValue.ShouldBe(57);
    }
}
```

- [ ] **Step 2: Run the tests and watch them fail**

Run: `dotnet test tests/SlingKing.IntegrationTests --filter SeedingTests`
Expected: FAIL — `DatabaseSeeder` does not exist.

- [ ] **Step 3: Write the seeder**

Create `backend/src/SlingKing.Infrastructure/Persistence/Seed/DatabaseSeeder.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SlingKing.Application.Common.Abstractions;
using SlingKing.Domain.Inventory;
using SlingKing.Domain.Operations;
using SlingKing.Domain.Platform;
using SlingKing.Domain.Sales;

namespace SlingKing.Infrastructure.Persistence.Seed;

/// <summary>
/// Runs on every startup. Every insert is guarded by an existence check, so this is
/// safe to run against a database that already holds a year of real trading.
/// </summary>
public sealed class DatabaseSeeder
{
    private readonly IAppDbContext _db;
    private readonly IClock _clock;
    private readonly IPasswordHasher _hasher;
    private readonly string _adminEmail;
    private readonly string _adminPassword;

    public DatabaseSeeder(
        IAppDbContext db,
        IClock clock,
        IPasswordHasher hasher,
        string adminEmail,
        string adminPassword)
    {
        _db = db;
        _clock = clock;
        _hasher = hasher;
        _adminEmail = adminEmail;
        _adminPassword = adminPassword;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;

        if (!await _db.Locations.AnyAsync(cancellationToken))
        {
            _db.Locations.Add(new Location { Name = "Main", IsDefault = true, IsActive = true });
        }

        await SeedChannelsAsync(cancellationToken);
        await SeedSequencesAsync(cancellationToken);
        await SeedExpenseCategoriesAsync(cancellationToken);
        await SeedAdminUserAsync(now, cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task SeedChannelsAsync(CancellationToken cancellationToken)
    {
        // Channel behaviour is data. Offline hands the bag over immediately, so it
        // never reserves and never ships.
        var wanted = new[]
        {
            new SalesChannel { Code = "instagram", Name = "Instagram", UsesReservation = true,  RequiresShipping = true,  SortOrder = 1 },
            new SalesChannel { Code = "whatsapp",  Name = "WhatsApp",  UsesReservation = true,  RequiresShipping = true,  SortOrder = 2 },
            new SalesChannel { Code = "offline",   Name = "Offline",   UsesReservation = false, RequiresShipping = false, SortOrder = 3 }
        };

        var existing = await _db.SalesChannels
            .Select(c => c.Code)
            .ToListAsync(cancellationToken);

        foreach (var channel in wanted.Where(c => !existing.Contains(c.Code)))
        {
            _db.SalesChannels.Add(channel);
        }
    }

    private async Task SeedSequencesAsync(CancellationToken cancellationToken)
    {
        var wanted = new[]
        {
            new NumberSequence { Key = "product",  Prefix = "BAG", NextValue = 1, Padding = 6 },
            new NumberSequence { Key = "purchase", Prefix = "PUR", NextValue = 1, Padding = 6 },
            new NumberSequence { Key = "order",    Prefix = "ORD", NextValue = 1, Padding = 6 },
            new NumberSequence { Key = "return",   Prefix = "RET", NextValue = 1, Padding = 6 }
        };

        var existing = await _db.NumberSequences
            .Select(s => s.Key)
            .ToListAsync(cancellationToken);

        // Never touch NextValue on an existing row — that would re-issue document numbers.
        foreach (var sequence in wanted.Where(s => !existing.Contains(s.Key)))
        {
            _db.NumberSequences.Add(sequence);
        }
    }

    private async Task SeedExpenseCategoriesAsync(CancellationToken cancellationToken)
    {
        var wanted = new[] { "Shipping", "Packaging", "Marketing", "Platform fees", "Other" };

        var existing = await _db.ExpenseCategories
            .Select(c => c.Name)
            .ToListAsync(cancellationToken);

        foreach (var name in wanted.Where(n => !existing.Contains(n)))
        {
            _db.ExpenseCategories.Add(new ExpenseCategory { Name = name, IsActive = true });
        }
    }

    private async Task SeedAdminUserAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_adminEmail) || string.IsNullOrWhiteSpace(_adminPassword))
        {
            return;
        }

        if (await _db.Users.AnyAsync(u => u.Email == _adminEmail, cancellationToken))
        {
            return;
        }

        _db.Users.Add(new User
        {
            Email = _adminEmail,
            PasswordHash = _hasher.Hash(_adminPassword),
            DisplayName = "Owner",
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        });
    }
}
```

- [ ] **Step 4: Run the tests and watch them pass**

Run: `dotnet test tests/SlingKing.IntegrationTests --filter SeedingTests`
Expected: PASS, 5 tests.

- [ ] **Step 5: Commit**

```bash
git add .
git commit -m "feat: idempotent seeding of reference data and the single user"
```

---

## Task 13: The API host — logging, errors, auth endpoints

**Files:**
- Modify: `backend/src/SlingKing.Api/Program.cs` (replace wholesale)
- Delete: `backend/src/SlingKing.Api/Controllers/WeatherForecastController.cs`, `WeatherForecast.cs`
- Create: `backend/src/SlingKing.Api/Middleware/ProblemDetailsExceptionHandler.cs`
- Create: `backend/src/SlingKing.Api/Contracts/Auth/AuthContracts.cs`
- Create: `backend/src/SlingKing.Api/Controllers/HealthController.cs`, `AuthController.cs`
- Create: `backend/src/SlingKing.Infrastructure/DependencyInjection.cs`
- Test: `backend/tests/SlingKing.IntegrationTests/Api/AuthEndpointTests.cs`

**Interfaces:**
- Consumes: everything from Tasks 6, 10, 11, 12.
- Produces: `POST /api/v1/auth/login`, `POST /api/v1/auth/refresh`, `POST /api/v1/auth/logout`, `GET /api/v1/auth/me`, `GET /api/v1/health`. `LoginResponse { AccessToken, RefreshToken, ExpiresAtUtc, User { Id, Email, DisplayName } }` — the frontend in Task 15 depends on these exact names.

- [ ] **Step 1: Add the packages**

```bash
cd backend
dotnet add src/SlingKing.Api package Serilog.AspNetCore --version 8.0.3
dotnet add src/SlingKing.Api package Serilog.Sinks.Console --version 6.1.1
dotnet add src/SlingKing.Api package Serilog.Sinks.File --version 7.0.0
dotnet add src/SlingKing.Api package Swashbuckle.AspNetCore --version 6.9.0
dotnet add src/SlingKing.Api package FluentValidation.DependencyInjectionExtensions --version 11.12.0
rm src/SlingKing.Api/Controllers/WeatherForecastController.cs src/SlingKing.Api/WeatherForecast.cs
```

- [ ] **Step 2: Write the failing endpoint tests**

Create `backend/tests/SlingKing.IntegrationTests/Api/AuthEndpointTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Shouldly;
using SlingKing.IntegrationTests.Fixtures;
using Xunit;

namespace SlingKing.IntegrationTests.Api;

[Collection("database")]
public class AuthEndpointTests : IClassFixture<SlingKingApiFactory>
{
    private readonly HttpClient _client;

    public AuthEndpointTests(SlingKingApiFactory factory) => _client = factory.CreateClient();

    private record LoginRequest(string Email, string Password);
    private record UserDto(long Id, string Email, string DisplayName);
    private record LoginResponse(
        string AccessToken, string RefreshToken, DateTimeOffset ExpiresAtUtc, UserDto User);
    private record RefreshRequest(string RefreshToken);

    private Task<HttpResponseMessage> LoginAsync(string email, string password) =>
        _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, password));

    [Fact]
    public async Task Health_is_green_without_a_token()
    {
        var response = await _client.GetAsync("/api/v1/health");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Correct_credentials_return_a_token_pair()
    {
        var response = await LoginAsync(SlingKingApiFactory.SeedEmail, SlingKingApiFactory.SeedPassword);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        body!.AccessToken.ShouldNotBeNullOrWhiteSpace();
        body.RefreshToken.ShouldNotBeNullOrWhiteSpace();
        body.User.Email.ShouldBe(SlingKingApiFactory.SeedEmail);
        body.ExpiresAtUtc.ShouldBeGreaterThan(DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task A_wrong_password_is_401_and_says_nothing_useful()
    {
        var response = await LoginAsync(SlingKingApiFactory.SeedEmail, "not-the-password");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var body = await response.Content.ReadAsStringAsync();
        body.ShouldNotContain("password");
        body.ShouldNotContain("Bcrypt");
    }

    [Fact]
    public async Task An_unknown_email_is_also_401_and_indistinguishable()
    {
        var unknown = await LoginAsync("nobody@slingking.test", "whatever");
        var wrongPassword = await LoginAsync(SlingKingApiFactory.SeedEmail, "whatever");

        unknown.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await unknown.Content.ReadAsStringAsync())
            .ShouldBe(await wrongPassword.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Me_requires_a_token()
    {
        (await _client.GetAsync("/api/v1/auth/me")).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Me_returns_the_current_user_when_authenticated()
    {
        var login = await (await LoginAsync(
            SlingKingApiFactory.SeedEmail, SlingKingApiFactory.SeedPassword))
            .Content.ReadFromJsonAsync<LoginResponse>();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", login!.AccessToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<UserDto>())!
            .Email.ShouldBe(SlingKingApiFactory.SeedEmail);
    }

    [Fact]
    public async Task A_refresh_token_can_be_exchanged_exactly_once()
    {
        var login = await (await LoginAsync(
            SlingKingApiFactory.SeedEmail, SlingKingApiFactory.SeedPassword))
            .Content.ReadFromJsonAsync<LoginResponse>();

        var first = await _client.PostAsJsonAsync(
            "/api/v1/auth/refresh", new RefreshRequest(login!.RefreshToken));
        first.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Replaying the same token must fail — rotation is the point.
        var replay = await _client.PostAsJsonAsync(
            "/api/v1/auth/refresh", new RefreshRequest(login.RefreshToken));
        replay.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logging_out_revokes_the_refresh_token()
    {
        var login = await (await LoginAsync(
            SlingKingApiFactory.SeedEmail, SlingKingApiFactory.SeedPassword))
            .Content.ReadFromJsonAsync<LoginResponse>();

        var logout = await _client.PostAsJsonAsync(
            "/api/v1/auth/logout", new RefreshRequest(login!.RefreshToken));
        logout.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var afterLogout = await _client.PostAsJsonAsync(
            "/api/v1/auth/refresh", new RefreshRequest(login.RefreshToken));
        afterLogout.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_validation_failure_is_problem_details_not_a_stack_trace()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/v1/auth/login", new LoginRequest("", ""));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");

        var body = await response.Content.ReadAsStringAsync();
        body.ShouldNotContain("Exception");
        body.ShouldNotContain("   at ");
    }
}
```

Create `backend/tests/SlingKing.IntegrationTests/Fixtures/SlingKingApiFactory.cs`:

```csharp
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using SlingKing.Infrastructure.Configuration;
using SlingKing.Infrastructure.Persistence;

namespace SlingKing.IntegrationTests.Fixtures;

/// <summary>
/// Boots the real API against the test schema. Uses the same guard as TestSchemaFixture,
/// so an in-process API can no more reach real data than a direct test can.
/// </summary>
public class SlingKingApiFactory : WebApplicationFactory<Program>
{
    public const string SeedEmail = "api-test-owner@slingking.test";
    public const string SeedPassword = "api-test-password-01";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        EnvFile.Load(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".env"));

        var connection = Environment.GetEnvironmentVariable("SLINGKING_DB_TEST") ?? "";
        TestSchemaFixture.AssertTargetsTestSchema(connection);

        Environment.SetEnvironmentVariable("SLINGKING_DB_RUNTIME", connection);
        Environment.SetEnvironmentVariable("SLINGKING_SEED_ADMIN_EMAIL", SeedEmail);
        Environment.SetEnvironmentVariable("SLINGKING_SEED_ADMIN_PASSWORD", SeedPassword);

        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            using var scope = services.BuildServiceProvider().CreateScope();
            scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();
        });
    }
}
```

- [ ] **Step 3: Run the tests and watch them fail**

Run: `dotnet test tests/SlingKing.IntegrationTests --filter AuthEndpointTests`
Expected: FAIL — `Program` is not accessible and the endpoints do not exist.

- [ ] **Step 4: Write the error handler**

Create `backend/src/SlingKing.Api/Middleware/ProblemDetailsExceptionHandler.cs`:

```csharp
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace SlingKing.Api.Middleware;

/// <summary>
/// The last line of defence: whatever went wrong inside, the client sees RFC 7807 and
/// never an exception type, a SQL fragment or a stack trace. Detail goes to the log.
/// </summary>
public sealed class ProblemDetailsExceptionHandler : IExceptionHandler
{
    private readonly ILogger<ProblemDetailsExceptionHandler> _logger;

    public ProblemDetailsExceptionHandler(ILogger<ProblemDetailsExceptionHandler> logger) =>
        _logger = logger;

    public async ValueTask<bool> TryHandleAsync(
        HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        _logger.LogError(exception,
            "Unhandled exception on {Method} {Path}", context.Request.Method, context.Request.Path);

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "An unexpected error occurred.",
            Detail = "The request could not be completed. The failure has been logged.",
            Instance = context.Request.Path
        };

        problem.Extensions["traceId"] = context.TraceIdentifier;

        context.Response.StatusCode = problem.Status.Value;
        await context.Response.WriteAsJsonAsync(
            problem, cancellationToken: cancellationToken);

        return true;
    }
}
```

- [ ] **Step 5: Write the Infrastructure registration**

Create `backend/src/SlingKing.Infrastructure/DependencyInjection.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SlingKing.Application.Common.Abstractions;
using SlingKing.Infrastructure.Auth;
using SlingKing.Infrastructure.Persistence;
using SlingKing.Infrastructure.Persistence.Seed;

namespace SlingKing.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, string runtimeConnectionString)
    {
        services.AddDbContext<AppDbContext>(options => options
            .UseNpgsql(runtimeConnectionString,
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history"))
            .UseSnakeCaseNamingConvention());

        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IPasswordHasher, BcryptPasswordHasher>();
        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddScoped<INumberSequenceAllocator, NumberSequenceAllocator>();

        services.AddScoped(sp => new DatabaseSeeder(
            sp.GetRequiredService<IAppDbContext>(),
            sp.GetRequiredService<IClock>(),
            sp.GetRequiredService<IPasswordHasher>(),
            Environment.GetEnvironmentVariable("SLINGKING_SEED_ADMIN_EMAIL") ?? "",
            Environment.GetEnvironmentVariable("SLINGKING_SEED_ADMIN_PASSWORD") ?? ""));

        return services;
    }
}
```

- [ ] **Step 6: Write the auth contracts**

Create `backend/src/SlingKing.Api/Contracts/Auth/AuthContracts.cs`:

```csharp
using System.ComponentModel.DataAnnotations;

namespace SlingKing.Api.Contracts.Auth;

public record LoginRequest(
    [Required, EmailAddress] string Email,
    [Required, MinLength(1)] string Password);

public record RefreshRequest([Required, MinLength(1)] string RefreshToken);

public record UserDto(long Id, string Email, string DisplayName);

public record LoginResponse(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset ExpiresAtUtc,
    UserDto User);
```

- [ ] **Step 7: Write the controllers**

Create `backend/src/SlingKing.Api/Controllers/HealthController.cs`:

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SlingKing.Application.Common.Abstractions;

namespace SlingKing.Api.Controllers;

[ApiController]
[Route("api/v1/health")]
public class HealthController : ControllerBase
{
    private readonly IAppDbContext _db;

    public HealthController(IAppDbContext db) => _db = db;

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var databaseReachable = await _db.Database.CanConnectAsync(cancellationToken);

        if (!databaseReachable)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { status = "degraded", database = "unreachable" });
        }

        return Ok(new { status = "ok", database = "ok" });
    }
}
```

Create `backend/src/SlingKing.Api/Controllers/AuthController.cs`:

```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SlingKing.Api.Contracts.Auth;
using SlingKing.Application.Common.Abstractions;
using SlingKing.Domain.Platform;
using SlingKing.Infrastructure.Auth;
using Microsoft.Extensions.Options;

namespace SlingKing.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
public class AuthController : ControllerBase
{
    private readonly IAppDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly ITokenService _tokens;
    private readonly IClock _clock;
    private readonly JwtOptions _jwt;

    public AuthController(
        IAppDbContext db,
        IPasswordHasher hasher,
        ITokenService tokens,
        IClock clock,
        IOptions<JwtOptions> jwt)
    {
        _db = db;
        _hasher = hasher;
        _tokens = tokens;
        _clock = clock;
        _jwt = jwt.Value;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var user = await _db.Users
            .SingleOrDefaultAsync(u => u.Email == request.Email && u.IsActive, cancellationToken);

        // Identical response for "no such user" and "wrong password" — an attacker
        // must not be able to enumerate valid emails.
        if (user is null || !_hasher.Verify(request.Password, user.PasswordHash))
        {
            return Problem(
                title: "Invalid credentials.",
                detail: "The email or password is incorrect.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        return Ok(await IssueTokensAsync(user, cancellationToken));
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<IActionResult> Refresh(RefreshRequest request, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var hash = _tokens.HashRefreshToken(request.RefreshToken);

        var stored = await _db.RefreshTokens
            .Include(t => t.User)
            .SingleOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);

        if (stored is null || stored.RevokedAt is not null || stored.ExpiresAt <= now ||
            stored.User is null || !stored.User.IsActive)
        {
            return Problem(
                title: "Invalid refresh token.",
                detail: "The token is unknown, expired or already used.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        // Rotation: the presented token dies here, whatever happens next.
        stored.RevokedAt = now;

        var response = await IssueTokensAsync(stored.User, cancellationToken);
        stored.ReplacedByTokenHash = _tokens.HashRefreshToken(response.RefreshToken);
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(response);
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout(RefreshRequest request, CancellationToken cancellationToken)
    {
        var hash = _tokens.HashRefreshToken(request.RefreshToken);

        var stored = await _db.RefreshTokens
            .SingleOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);

        if (stored is not null && stored.RevokedAt is null)
        {
            stored.RevokedAt = _clock.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
        }

        // Always 204: logging out an already-dead token is not an error worth reporting.
        return NoContent();
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> Me(CancellationToken cancellationToken)
    {
        var subject = User.FindFirstValue(ClaimTypes.NameIdentifier)
                      ?? User.FindFirstValue("sub");

        if (!long.TryParse(subject, out var userId))
        {
            return Unauthorized();
        }

        var user = await _db.Users
            .SingleOrDefaultAsync(u => u.Id == userId && u.IsActive, cancellationToken);

        return user is null
            ? Unauthorized()
            : Ok(new UserDto(user.Id, user.Email, user.DisplayName));
    }

    private async Task<LoginResponse> IssueTokensAsync(User user, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var (refreshToken, refreshHash) = _tokens.CreateRefreshToken();

        _db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = refreshHash,
            ExpiresAt = now.AddDays(_jwt.RefreshTokenDays),
            CreatedAt = now
        });

        await _db.SaveChangesAsync(cancellationToken);

        return new LoginResponse(
            _tokens.CreateAccessToken(user.Id, user.Email),
            refreshToken,
            now.AddMinutes(_jwt.AccessTokenMinutes),
            new UserDto(user.Id, user.Email, user.DisplayName));
    }
}
```

- [ ] **Step 8: Write Program.cs**

Replace `backend/src/SlingKing.Api/Program.cs` entirely:

```csharp
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;
using SlingKing.Api.Middleware;
using SlingKing.Infrastructure;
using SlingKing.Infrastructure.Auth;
using SlingKing.Infrastructure.Configuration;
using SlingKing.Infrastructure.Persistence;
using SlingKing.Infrastructure.Persistence.Seed;

EnvFile.Load(Path.Combine(Directory.GetCurrentDirectory(), "..", "..", ".env"));

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, configuration) => configuration
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("logs/slingking-.log", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14));

string Required(string key) =>
    Environment.GetEnvironmentVariable(key)
    ?? throw new InvalidOperationException($"{key} is not set. See backend/.env.example.");

builder.Services.Configure<JwtOptions>(options =>
{
    options.Secret = Required("SLINGKING_JWT_SECRET");
    options.Issuer = Required("SLINGKING_JWT_ISSUER");
    options.Audience = Required("SLINGKING_JWT_AUDIENCE");
    options.AccessTokenMinutes = 30;
    options.RefreshTokenDays = 14;
});

builder.Services.AddInfrastructure(Required("SLINGKING_DB_RUNTIME"));

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = Required("SLINGKING_JWT_ISSUER"),
            ValidAudience = Required("SLINGKING_JWT_AUDIENCE"),
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(Required("SLINGKING_JWT_SECRET"))),
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });

builder.Services.AddAuthorization();

var corsOrigins = (Environment.GetEnvironmentVariable("SLINGKING_CORS_ORIGINS") ?? "")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(corsOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod()));

builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ProblemDetailsExceptionHandler>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "SlingKing API", Version = "v1" });
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme, Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

app.UseExceptionHandler();
app.UseSerilogRequestLogging();

if (!app.Environment.IsProduction())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    await services.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    await services.GetRequiredService<DatabaseSeeder>().SeedAsync();
}

app.Run();

/// <summary>Exposed so WebApplicationFactory can boot the real host in tests.</summary>
public partial class Program
{
}
```

- [ ] **Step 9: Run the tests and watch them pass**

Run: `dotnet test tests/SlingKing.IntegrationTests --filter AuthEndpointTests`
Expected: PASS, 9 tests.

- [ ] **Step 10: Run the API by hand and read the Swagger page**

```bash
cd backend/src/SlingKing.Api
dotnet run
```

Open `http://localhost:5xxx/swagger` (the port is printed on startup). Confirm all five endpoints appear and the **Authorize** button is present.

- [ ] **Step 11: Commit**

```bash
git add .
git commit -m "feat: API host with Serilog, ProblemDetails, Swagger, CORS and auth endpoints"
```

---

## Task 14: The backup script

One database, no automated backups on the free tier, and real stock arriving at M4. This script is what stands between a bad migration and a rebuild from memory.

**Files:**
- Create: `backup.ps1`
- Modify: `.gitignore` (already excludes `backups/` from Task 1 — verify)

**Interfaces:**
- Consumes: `SLINGKING_DB_MIGRATION` from `backend/.env`.
- Produces: `backups/slingking-YYYY-MM-DD-HHmmss.sql` and a printed file size.

- [ ] **Step 1: Install the PostgreSQL client tools**

`pg_dump` is not on this machine. Install **only the command-line tools** — no server, no service, no port:

1. Download the PostgreSQL 17 Windows installer from `https://www.postgresql.org/download/windows/`
2. Run it and **deselect everything except "Command Line Tools"**
3. Add the install's `bin` folder to `PATH` (typically `C:\Program Files\PostgreSQL\17\bin`)

Version 17 client tools read a 15, 16 or 17 server. An older `pg_dump` refuses a newer server, so do not install 15 or 16.

- [ ] **Step 2: Verify pg_dump is reachable**

Run: `pg_dump --version`
Expected: `pg_dump (PostgreSQL) 17.x`. If the command is not found, the `PATH` entry has not been picked up — open a new terminal.

- [ ] **Step 3: Write the script**

Create `backup.ps1` at the repository root:

```powershell
<#
.SYNOPSIS
  Dumps the SlingKing database to a timestamped .sql file.
.DESCRIPTION
  Run this before any migration that touches live data, and before the first real
  opening-stock import. The Supabase free tier keeps no automated backups, so this
  file is the only copy that exists outside the hosted database.
#>
param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot "backups")
)

$ErrorActionPreference = "Stop"

$envFile = Join-Path $PSScriptRoot "backend/.env"
if (-not (Test-Path $envFile)) {
    throw "backend/.env not found. Copy backend/.env.example and fill it in."
}

# Read SLINGKING_DB_MIGRATION without disturbing the rest of the environment.
$connection = $null
foreach ($line in Get-Content $envFile) {
    $trimmed = $line.Trim()
    if ($trimmed.StartsWith("SLINGKING_DB_MIGRATION=")) {
        $connection = $trimmed.Substring("SLINGKING_DB_MIGRATION=".Length).Trim('"')
        break
    }
}

if ([string]::IsNullOrWhiteSpace($connection)) {
    throw "SLINGKING_DB_MIGRATION is not set in backend/.env"
}

# Npgsql keyword form -> libpq parameters that pg_dump understands.
$parts = @{}
foreach ($pair in $connection.Split(';')) {
    $index = $pair.IndexOf('=')
    if ($index -gt 0) {
        $parts[$pair.Substring(0, $index).Trim().ToLower()] = $pair.Substring($index + 1).Trim()
    }
}

$pgHost = $parts["host"]
$port   = if ($parts.ContainsKey("port")) { $parts["port"] } else { "5432" }
$db     = $parts["database"]
$user   = $parts["username"]

if (-not (Test-Path $OutputDirectory)) {
    New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
}

$stamp  = Get-Date -Format "yyyy-MM-dd-HHmmss"
$target = Join-Path $OutputDirectory "slingking-$stamp.sql"

Write-Host "Dumping $db on $pgHost to $target ..."

$env:PGPASSWORD = $parts["password"]
try {
    & pg_dump --host=$pgHost --port=$port --username=$user --dbname=$db `
              --schema=public --no-owner --no-privileges --file=$target
    if ($LASTEXITCODE -ne 0) { throw "pg_dump exited with code $LASTEXITCODE" }
}
finally {
    # Never leave the password sitting in the session environment.
    Remove-Item Env:PGPASSWORD -ErrorAction SilentlyContinue
}

$sizeKb = [math]::Round((Get-Item $target).Length / 1KB, 1)
Write-Host "Backup complete: $target ($sizeKb KB)" -ForegroundColor Green
Write-Host "Keep a copy somewhere that is not this laptop." -ForegroundColor Yellow
```

- [ ] **Step 4: Run it and verify the dump is real**

```powershell
.\backup.ps1
```

Expected: a green `Backup complete` line. Then confirm the file actually contains the schema:

```bash
grep -c "CREATE TABLE" backups/slingking-*.sql
```
Expected: `27`.

- [ ] **Step 5: Confirm backups cannot be committed**

Run: `git status --porcelain backups/`
Expected: no output.

- [ ] **Step 6: Commit**

```bash
git add backup.ps1 .gitignore
git commit -m "feat: pg_dump backup script for the Supabase database"
```

---
## Visual direction — read before Tasks 15 and 16

Every UI decision in this milestone derives from this section. It is not decoration; later
milestones inherit it, so getting it wrong here costs more later.

**The subject.** One person, at a laptop, in the evening, entering the day's bags. The app's
single job is: *know exactly what stock exists and what it cost.* This is a ledger and a
workshop tool, not a storefront and not a SaaS marketing page.

**Palette.** Cool canvas — the colour of unbleached cotton duck under workshop light — rather
than the warm cream that every admin template reaches for.

| Token | Value | Use |
|---|---|---|
| `--color-canvas` | `#EDEFEA` | Page ground |
| `--color-panel` | `#FFFFFF` | Cards, table surfaces |
| `--color-thread` | `#141A17` | Ink. Near-black with green in it, like waxed thread |
| `--color-saddle` | `#A85D2E` | The one accent. Actions, focus rings, the stitch line |
| `--color-ledger` | `#0C6B57` | Positive signal: in stock, costed, paid |
| `--color-flag` | `#9C2B2B` | Negative signal: out of stock, uncosted, overdue |
| `--color-muted` | `#6B7671` | Secondary text |
| `--color-rule` | `#D5D9D2` | Hairlines |

**Typography.** Three faces, three jobs, no overlap.

- **Space Grotesk** — headings, navigation, buttons. Engineered, slightly odd letterforms;
  it reads like hardware and tooling rather than like a magazine.
- **Inter** — form labels, body, help text. Deliberately unremarkable: maximum legibility
  in a dense form is the whole requirement.
- **JetBrains Mono** — **every number in the application, without exception.** SKUs,
  quantities, rupee amounts, dates. This is a functional rule before it is an aesthetic one:
  columns of figures align, SKUs are scannable, quantities compare at a glance. It is also
  the cheapest strong signature the app has.

**The structural device: the topstitch.** A 1px dashed saddle-tan rule, inset from an edge —
exactly the topstitching that runs parallel to the edge of a bag panel. It marks edges and
nothing else, so it never becomes decoration. It appears under the top rail and around the
login card.

**The signature: the login card is a hangtag.** Every variant in this system is, physically,
a hangtag — SKU, colour, size, price, tied to a bag with string. The login card is that
artifact: a panel with a punched eyelet at the top, a topstitch inset from its border, and
small stamped corners (3px radius). This is the *only* place the metaphor is made literal.
Everywhere else the app is quiet and dense.

**The layout risk: no sidebar.** Navigation is a horizontal top rail; content gets the full
viewport width. Six destinations do not justify surrendering 224px of horizontal space on
every screen, and dense stock tables — physical, reserved, available, cost, value, per
variant — need every pixel of width they can get. This is the opposite of the default admin
shell, and it is the right call for a ledger.

**Motion.** One moment, on load: the login card's topstitch draws itself, as if being run
through a machine. About 600ms. Everything else is standard hover and focus. `prefers-reduced-motion`
turns it off entirely.

**Copy rules.** Active voice; a control says what happens when it is used. No system vocabulary
in user-facing text — never "milestone M5", never "endpoint", never "seeded". Errors state what
happened and do not apologise. An empty screen says what is true and what comes next.

---

## Task 15: Frontend scaffold and the API client

**Files:**
- Create: `frontend/` via Vite, then `frontend/.env.example`, `frontend/src/lib/money.ts`, `frontend/src/lib/dates.ts`, `frontend/src/api/client.ts`, `frontend/src/api/auth.ts`
- Modify: `frontend/src/index.css`, `frontend/vite.config.ts`
- Test: `frontend/src/api/client.test.ts`, `frontend/src/lib/money.test.ts`

**Interfaces:**
- Consumes: the `LoginResponse` shape from Task 13.
- Produces: `api` (configured axios instance), `tokenStore` with `get()/set()/clear()`, `login()`, `logout()`, `getMe()`; `formatInr(paise: number | null)`, `formatIstDate(iso: string)`.

- [ ] **Step 1: Scaffold and pin**

```bash
cd "C:/Hamza Projects/SlingKing"
npm create vite@latest frontend -- --template react-ts
cd frontend
npm install
npm install react@19.2.8 react-dom@19.2.8 axios@1.19.0 \
  @tanstack/react-query@5.102.2 react-router-dom@7.18.2 \
  react-hook-form@7.86.0 zod@4.4.3
npm install -D typescript@5.9.3 vite@8.2.2 @vitejs/plugin-react@6.1.0 \
  tailwindcss@4.3.3 @tailwindcss/vite@4.3.3 vitest@3.2.4 jsdom@27.0.0
```

TypeScript is pinned to the 5.9 line deliberately. TypeScript 7 is a full native rewrite and the surrounding tooling is still catching up — this is a business system, not a place to be early.

- [ ] **Step 2: Configure Vite, Tailwind and the test runner**

Replace `frontend/vite.config.ts`:

```typescript
import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import tailwindcss from "@tailwindcss/vite";

export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    port: 5173,
  },
  test: {
    environment: "jsdom",
    globals: true,
  },
});
```

Replace `frontend/src/index.css` with the Tailwind 4 entry point and the design tokens.
Every value here comes from the Visual direction section above — do not invent others.

```css
@import url("https://fonts.googleapis.com/css2?family=Space+Grotesk:wght@500;600;700&family=Inter:wght@400;500;600&family=JetBrains+Mono:wght@400;500;600&display=swap");
@import "tailwindcss";

@theme {
  /* Cool canvas, not warm cream. */
  --color-canvas: #edefea;
  --color-panel: #ffffff;
  --color-thread: #141a17;
  --color-saddle: #a85d2e;
  --color-ledger: #0c6b57;
  --color-flag: #9c2b2b;
  --color-muted: #6b7671;
  --color-rule: #d5d9d2;

  --font-display: "Space Grotesk", ui-sans-serif, system-ui, sans-serif;
  --font-sans: "Inter", ui-sans-serif, system-ui, sans-serif;
  --font-mono: "JetBrains Mono", ui-monospace, "Cascadia Mono", monospace;

  --radius-stamp: 3px;
}

html,
body,
#root {
  height: 100%;
}

body {
  background: var(--color-canvas);
  color: var(--color-thread);
  font-family: var(--font-sans);
  -webkit-font-smoothing: antialiased;
}

/* Every number in this application is monospace and tabular. Figures must align
   in a column and SKUs must be scannable — this is a functional rule. */
.num {
  font-family: var(--font-mono);
  font-variant-numeric: tabular-nums;
  font-feature-settings: "tnum" 1;
}

/* The topstitch: a stitch line inset from an edge, the way it runs on a bag panel.
   It marks edges and nothing else. */
.topstitch {
  border-top: 1px dashed var(--color-saddle);
  opacity: 0.55;
}

/* Visible keyboard focus everywhere, in the accent. */
:where(a, button, input, select, textarea):focus-visible {
  outline: 2px solid var(--color-saddle);
  outline-offset: 2px;
}

@media (prefers-reduced-motion: reduce) {
  *,
  *::before,
  *::after {
    animation-duration: 0.01ms !important;
    animation-iteration-count: 1 !important;
    transition-duration: 0.01ms !important;
  }
}
```

Create `frontend/.env.example`:

```dotenv
# Vite only exposes variables prefixed VITE_ to the browser. Never put a secret here —
# anything in this file ships to every visitor in the JavaScript bundle.
VITE_API_BASE_URL=http://localhost:5210
```

Copy it to `frontend/.env` and set the port the API actually printed in Task 13 Step 10.

- [ ] **Step 3: Write the failing tests**

Create `frontend/src/lib/money.test.ts`:

```typescript
import { describe, expect, it } from "vitest";
import { formatInr } from "./money";

describe("formatInr", () => {
  it("formats whole rupees with the Indian digit grouping", () => {
    expect(formatInr(184500)).toBe("₹1,84,500.00");
  });

  it("keeps two decimal places", () => {
    expect(formatInr(6100.5)).toBe("₹6,100.50");
  });

  it("renders zero as a real number, not a dash", () => {
    expect(formatInr(0)).toBe("₹0.00");
  });

  it("renders an unknown cost as an em dash, never as zero", () => {
    expect(formatInr(null)).toBe("—");
  });
});
```

Create `frontend/src/api/client.test.ts`:

```typescript
import { beforeEach, describe, expect, it } from "vitest";
import { tokenStore } from "./client";

describe("tokenStore", () => {
  beforeEach(() => {
    localStorage.clear();
    tokenStore.clear();
  });

  it("returns null when nothing is stored", () => {
    expect(tokenStore.get()).toBeNull();
  });

  it("round-trips a token pair", () => {
    tokenStore.set({ accessToken: "a", refreshToken: "r" });

    expect(tokenStore.get()).toEqual({ accessToken: "a", refreshToken: "r" });
  });

  it("clears both tokens", () => {
    tokenStore.set({ accessToken: "a", refreshToken: "r" });
    tokenStore.clear();

    expect(tokenStore.get()).toBeNull();
  });

  it("survives corrupt storage rather than throwing", () => {
    localStorage.setItem("slingking.tokens", "{not json");

    expect(tokenStore.get()).toBeNull();
  });
});
```

- [ ] **Step 4: Run the tests and watch them fail**

Run: `npx vitest run`
Expected: FAIL — neither module exists.

- [ ] **Step 5: Write the formatters**

Create `frontend/src/lib/money.ts`:

```typescript
const inr = new Intl.NumberFormat("en-IN", {
  style: "currency",
  currency: "INR",
  minimumFractionDigits: 2,
  maximumFractionDigits: 2,
});

/**
 * Null means the cost is unknown — it is never zero. Rendering it as ₹0.00 would
 * turn absent data into a claim about profit, which is the whole failure mode the
 * optional-cost design exists to avoid.
 */
export function formatInr(amount: number | null | undefined): string {
  if (amount === null || amount === undefined) return "—";
  return inr.format(amount);
}
```

Create `frontend/src/lib/dates.ts`:

```typescript
const ist = new Intl.DateTimeFormat("en-IN", {
  timeZone: "Asia/Kolkata",
  day: "2-digit",
  month: "short",
  year: "numeric",
});

const istWithTime = new Intl.DateTimeFormat("en-IN", {
  timeZone: "Asia/Kolkata",
  day: "2-digit",
  month: "short",
  year: "numeric",
  hour: "2-digit",
  minute: "2-digit",
});

/** Timestamps are stored UTC and always rendered in IST. */
export function formatIstDate(iso: string): string {
  return ist.format(new Date(iso));
}

export function formatIstDateTime(iso: string): string {
  return istWithTime.format(new Date(iso));
}
```

- [ ] **Step 6: Write the API client**

Create `frontend/src/api/client.ts`:

```typescript
import axios, {
  type AxiosError,
  type InternalAxiosRequestConfig,
} from "axios";

const STORAGE_KEY = "slingking.tokens";

export interface TokenPair {
  accessToken: string;
  refreshToken: string;
}

export const tokenStore = {
  get(): TokenPair | null {
    try {
      const raw = localStorage.getItem(STORAGE_KEY);
      if (!raw) return null;
      const parsed = JSON.parse(raw) as TokenPair;
      return parsed.accessToken && parsed.refreshToken ? parsed : null;
    } catch {
      // Corrupt or unavailable storage is a logged-out user, not a crash.
      return null;
    }
  },
  set(tokens: TokenPair) {
    try {
      localStorage.setItem(STORAGE_KEY, JSON.stringify(tokens));
    } catch {
      /* private mode — the session simply will not persist */
    }
  },
  clear() {
    try {
      localStorage.removeItem(STORAGE_KEY);
    } catch {
      /* nothing to do */
    }
  },
};

export const api = axios.create({
  baseURL: import.meta.env.VITE_API_BASE_URL ?? "http://localhost:5210",
  headers: { "Content-Type": "application/json" },
});

api.interceptors.request.use((config) => {
  const tokens = tokenStore.get();
  if (tokens) {
    config.headers.Authorization = `Bearer ${tokens.accessToken}`;
  }
  return config;
});

// One refresh at a time. Without this, five parallel 401s trigger five rotations
// and four of them lose the race — logging the user out mid-session.
let refreshInFlight: Promise<TokenPair | null> | null = null;

async function refreshTokens(): Promise<TokenPair | null> {
  const current = tokenStore.get();
  if (!current) return null;

  try {
    const { data } = await axios.post<{
      accessToken: string;
      refreshToken: string;
    }>(`${api.defaults.baseURL}/api/v1/auth/refresh`, {
      refreshToken: current.refreshToken,
    });

    const next = {
      accessToken: data.accessToken,
      refreshToken: data.refreshToken,
    };
    tokenStore.set(next);
    return next;
  } catch {
    tokenStore.clear();
    return null;
  }
}

api.interceptors.response.use(
  (response) => response,
  async (error: AxiosError) => {
    const request = error.config as InternalAxiosRequestConfig & {
      _retried?: boolean;
    };

    const isAuthCall = request?.url?.includes("/auth/");

    if (error.response?.status !== 401 || request?._retried || isAuthCall) {
      return Promise.reject(error);
    }

    request._retried = true;

    refreshInFlight ??= refreshTokens().finally(() => {
      refreshInFlight = null;
    });

    const tokens = await refreshInFlight;

    if (!tokens) {
      window.location.assign("/login");
      return Promise.reject(error);
    }

    request.headers.Authorization = `Bearer ${tokens.accessToken}`;
    return api.request(request);
  },
);
```

Create `frontend/src/api/auth.ts`:

```typescript
import { api, tokenStore } from "./client";

export interface User {
  id: number;
  email: string;
  displayName: string;
}

interface LoginResponse {
  accessToken: string;
  refreshToken: string;
  expiresAtUtc: string;
  user: User;
}

export async function login(email: string, password: string): Promise<User> {
  const { data } = await api.post<LoginResponse>("/api/v1/auth/login", {
    email,
    password,
  });

  tokenStore.set({
    accessToken: data.accessToken,
    refreshToken: data.refreshToken,
  });

  return data.user;
}

export async function logout(): Promise<void> {
  const tokens = tokenStore.get();
  if (tokens) {
    try {
      await api.post("/api/v1/auth/logout", {
        refreshToken: tokens.refreshToken,
      });
    } catch {
      // A failed logout call still logs the user out locally.
    }
  }
  tokenStore.clear();
}

export async function getMe(): Promise<User> {
  const { data } = await api.get<User>("/api/v1/auth/me");
  return data;
}
```

- [ ] **Step 7: Run the tests and watch them pass**

Run: `npx vitest run`
Expected: PASS, 8 tests.

- [ ] **Step 8: Commit**

```bash
cd "C:/Hamza Projects/SlingKing"
git add .
git commit -m "feat: frontend scaffold, INR/IST formatters and axios client with token refresh"
```

---

## Task 16: Login and the authenticated shell

**Files:**
- Create: `frontend/src/app/AuthContext.tsx`, `ProtectedRoute.tsx`, `AppShell.tsx`, `router.tsx`
- Create: `frontend/src/features/auth/LoginPage.tsx`
- Create: `frontend/src/features/dashboard/DashboardPage.tsx`
- Modify: `frontend/src/main.tsx`, delete `frontend/src/App.tsx` and `App.css`

**Interfaces:**
- Consumes: `login`, `logout`, `getMe`, `User` from Task 15.
- Produces: routes `/login` and `/` (protected). `useAuth()` returning `{ user, signIn, signOut, isLoading }`.

- [ ] **Step 1: Write the auth context**

Create `frontend/src/app/AuthContext.tsx`:

```tsx
import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useState,
  type ReactNode,
} from "react";
import { getMe, login as apiLogin, logout as apiLogout, type User } from "../api/auth";
import { tokenStore } from "../api/client";

interface AuthValue {
  user: User | null;
  isLoading: boolean;
  signIn: (email: string, password: string) => Promise<void>;
  signOut: () => Promise<void>;
}

const AuthContext = createContext<AuthValue | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<User | null>(null);
  const [isLoading, setIsLoading] = useState(true);

  useEffect(() => {
    // A stored token is a claim, not proof. Ask the API who we are before trusting it.
    if (!tokenStore.get()) {
      setIsLoading(false);
      return;
    }

    getMe()
      .then(setUser)
      .catch(() => tokenStore.clear())
      .finally(() => setIsLoading(false));
  }, []);

  const signIn = useCallback(async (email: string, password: string) => {
    setUser(await apiLogin(email, password));
  }, []);

  const signOut = useCallback(async () => {
    await apiLogout();
    setUser(null);
  }, []);

  const value = useMemo(
    () => ({ user, isLoading, signIn, signOut }),
    [user, isLoading, signIn, signOut],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthValue {
  const value = useContext(AuthContext);
  if (!value) throw new Error("useAuth must be used inside AuthProvider");
  return value;
}
```

- [ ] **Step 2: Write the protected route and shell**

Create `frontend/src/app/ProtectedRoute.tsx`:

```tsx
import type { ReactNode } from "react";
import { Navigate, useLocation } from "react-router-dom";
import { useAuth } from "./AuthContext";

export function ProtectedRoute({ children }: { children: ReactNode }) {
  const { user, isLoading } = useAuth();
  const location = useLocation();

  if (isLoading) {
    return (
      <div className="flex h-full items-center justify-center bg-canvas text-[13px] text-muted">
        Loading
      </div>
    );
  }

  if (!user) {
    return <Navigate to="/login" replace state={{ from: location.pathname }} />;
  }

  return <>{children}</>;
}
```

Create `frontend/src/app/AppShell.tsx`. Note the deliberate absence of a sidebar — see the
Visual direction section. Navigation is a top rail; the content area gets the full width,
because stock tables need it.

```tsx
import { NavLink, Outlet } from "react-router-dom";
import { useAuth } from "./AuthContext";

const navigation = [
  { to: "/", label: "Dashboard", end: true },
  { to: "/products", label: "Products" },
  { to: "/inventory", label: "Inventory" },
  { to: "/purchases", label: "Purchases" },
];

export function AppShell() {
  const { user, signOut } = useAuth();

  return (
    <div className="flex h-full flex-col bg-canvas">
      <header className="bg-thread text-white">
        <div className="flex items-center gap-8 px-6">
          <NavLink
            to="/"
            className="font-display text-[15px] font-bold tracking-[0.14em] uppercase"
          >
            SlingKing
          </NavLink>

          <nav className="flex flex-1 items-stretch">
            {navigation.map((item) => (
              <NavLink
                key={item.to}
                to={item.to}
                end={item.end}
                className={({ isActive }) =>
                  [
                    "font-display px-4 py-3.5 text-[13px] font-medium tracking-wide transition-colors",
                    isActive
                      ? "text-white shadow-[inset_0_-2px_0_0_var(--color-saddle)]"
                      : "text-white/55 hover:text-white/90",
                  ].join(" ")
                }
              >
                {item.label}
              </NavLink>
            ))}
          </nav>

          <div className="flex items-center gap-4">
            <span className="text-[12px] text-white/50">{user?.email}</span>
            <button
              type="button"
              onClick={() => void signOut()}
              className="font-display text-[13px] font-medium text-white/75 transition-colors hover:text-white"
            >
              Sign out
            </button>
          </div>
        </div>
      </header>

      {/* The stitch line where the strap meets the panel. */}
      <div className="topstitch" />

      <main className="flex-1 overflow-auto">
        <Outlet />
      </main>
    </div>
  );
}
```

- [ ] **Step 3: Write the login page**

Create `frontend/src/features/auth/LoginPage.tsx`. This is the signature screen: the card is
a hangtag — punched eyelet, topstitch inset from the border, stamped corners. It is the only
place in the application where the metaphor is literal.

```tsx
import { useState, type FormEvent } from "react";
import { useNavigate } from "react-router-dom";
import { useAuth } from "../../app/AuthContext";

export function LoginPage() {
  const { signIn } = useAuth();
  const navigate = useNavigate();

  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    setIsSubmitting(true);

    try {
      await signIn(email, password);
      navigate("/", { replace: true });
    } catch {
      // One message for every failure. The API does not distinguish "no such user"
      // from "wrong password", and neither should the screen.
      setError("Email or password is incorrect.");
    } finally {
      setIsSubmitting(false);
    }
  }

  const field =
    "mt-1.5 w-full rounded-stamp border border-rule bg-panel px-3 py-2.5 text-[14px] " +
    "transition-colors placeholder:text-muted/60 focus:border-saddle";

  return (
    <div className="flex h-full items-center justify-center bg-canvas px-4 py-10">
      <form
        onSubmit={handleSubmit}
        className="relative w-full max-w-[380px] rounded-stamp border border-rule bg-panel px-9 pb-9 pt-11 shadow-[0_1px_2px_rgba(20,26,23,0.06)]"
      >
        {/* The punched eyelet. */}
        <span
          aria-hidden
          className="absolute left-1/2 top-4 h-3 w-3 -translate-x-1/2 rounded-full border border-rule bg-canvas shadow-[inset_0_1px_1px_rgba(20,26,23,0.12)]"
        />

        {/* The topstitch, inset from the card edge, drawing itself on load. */}
        <svg
          aria-hidden
          className="pointer-events-none absolute inset-0 h-full w-full"
          preserveAspectRatio="none"
        >
          <rect
            x="10"
            y="10"
            width="calc(100% - 20px)"
            height="calc(100% - 20px)"
            rx="2"
            fill="none"
            stroke="var(--color-saddle)"
            strokeWidth="1"
            strokeDasharray="5 4"
            opacity="0.5"
            className="stitch"
          />
        </svg>

        <div className="relative">
          <h1 className="font-display text-[22px] font-bold tracking-[0.12em] uppercase">
            SlingKing
          </h1>
          <p className="mt-1 text-[13px] text-muted">Sign in to the stock ledger.</p>

          <div className="mt-7">
            <label className="text-[12px] font-medium tracking-wide" htmlFor="email">
              Email
            </label>
            <input
              id="email"
              type="email"
              required
              autoFocus
              autoComplete="username"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              className={field}
            />
          </div>

          <div className="mt-4">
            <label className="text-[12px] font-medium tracking-wide" htmlFor="password">
              Password
            </label>
            <input
              id="password"
              type="password"
              required
              autoComplete="current-password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              className={field}
            />
          </div>

          {error && (
            <p role="alert" className="mt-4 text-[13px] text-flag">
              {error}
            </p>
          )}

          <button
            type="submit"
            disabled={isSubmitting}
            className="font-display mt-7 w-full rounded-stamp bg-saddle px-4 py-2.5 text-[14px] font-semibold tracking-wide text-white transition-opacity hover:opacity-90 disabled:opacity-55"
          >
            {isSubmitting ? "Signing in" : "Sign in"}
          </button>
        </div>
      </form>
    </div>
  );
}
```

Add the stitch animation to `frontend/src/index.css`, below the tokens:

```css
/* The stitch runs itself through the machine, once, on load. */
@keyframes run-stitch {
  from {
    stroke-dashoffset: 900;
  }
  to {
    stroke-dashoffset: 0;
  }
}

.stitch {
  animation: run-stitch 600ms ease-out both;
}
```

- [ ] **Step 4: Write the placeholder dashboard and the router**

Create `frontend/src/features/dashboard/DashboardPage.tsx`. There is genuinely no data yet,
so this is an empty state — which per the copy rules states what is true and what comes next,
in the user's vocabulary. No milestone numbers, no apology.

```tsx
import { useAuth } from "../../app/AuthContext";

export function DashboardPage() {
  const { user } = useAuth();

  return (
    <div className="mx-auto max-w-6xl px-6 py-10">
      <p className="text-[12px] font-medium tracking-[0.16em] uppercase text-muted">
        Signed in as {user?.displayName}
      </p>
      <h1 className="font-display mt-1 text-[28px] font-bold tracking-tight">
        Nothing in the ledger yet
      </h1>
      <p className="mt-2 max-w-prose text-[14px] leading-relaxed text-muted">
        Your database is set up and every table is ready. Products, stock and purchases
        come next — once they land, this page shows what you hold and what it cost.
      </p>

      <div className="topstitch mt-8" />

      <dl className="mt-8 grid gap-px overflow-hidden rounded-stamp border border-rule bg-rule sm:grid-cols-3">
        {[
          { label: "Products", value: "0" },
          { label: "Units in stock", value: "0" },
          { label: "Stock value", value: "—" },
        ].map((tile) => (
          <div key={tile.label} className="bg-panel px-5 py-4">
            <dt className="text-[12px] tracking-wide text-muted">{tile.label}</dt>
            <dd className="num mt-1 text-[24px] font-medium">{tile.value}</dd>
          </div>
        ))}
      </dl>

      <p className="mt-3 text-[12px] text-muted">
        Stock value reads &ldquo;&mdash;&rdquo; when no cost is known. It never reads
        &#8377;0 &mdash; an unknown cost is not a free bag.
      </p>
    </div>
  );
}
```

Create `frontend/src/app/router.tsx`:

```tsx
import { createBrowserRouter } from "react-router-dom";
import { AppShell } from "./AppShell";
import { ProtectedRoute } from "./ProtectedRoute";
import { LoginPage } from "../features/auth/LoginPage";
import { DashboardPage } from "../features/dashboard/DashboardPage";

export const router = createBrowserRouter([
  { path: "/login", element: <LoginPage /> },
  {
    path: "/",
    element: (
      <ProtectedRoute>
        <AppShell />
      </ProtectedRoute>
    ),
    children: [{ index: true, element: <DashboardPage /> }],
  },
]);
```

Replace `frontend/src/main.tsx`:

```tsx
import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { RouterProvider } from "react-router-dom";
import { AuthProvider } from "./app/AuthContext";
import { router } from "./app/router";
import "./index.css";

const queryClient = new QueryClient({
  defaultOptions: {
    queries: { retry: 1, refetchOnWindowFocus: false, staleTime: 30_000 },
  },
});

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <AuthProvider>
        <RouterProvider router={router} />
      </AuthProvider>
    </QueryClientProvider>
  </StrictMode>,
);
```

```bash
rm frontend/src/App.tsx frontend/src/App.css
```

- [ ] **Step 5: Type-check and build**

Run: `cd frontend && npx tsc --noEmit && npm run build`
Expected: no errors, `dist/` produced.

- [ ] **Step 6: Verify the flow by hand**

Start the API in one terminal and the frontend in another:

```bash
cd backend/src/SlingKing.Api && dotnet run
cd frontend && npm run dev
```

Open `http://localhost:5173` and confirm, in order:
1. You are redirected to `/login` — the shell is not reachable while signed out.
2. A wrong password shows "Email or password is incorrect."
3. The seeded credentials sign you in and land on the dashboard.
4. Reloading the page keeps you signed in.
5. **Sign out** returns you to `/login`, and the browser back button does not restore the shell.

- [ ] **Step 7: Commit**

```bash
cd "C:/Hamza Projects/SlingKing"
git add .
git commit -m "feat: login page, auth context and protected application shell"
```

---

## Task 17: The M1 gate

The spec's gate for this milestone, verified as one pass rather than assumed from the tasks above.

**Files:** none created — this task runs and records.

- [ ] **Step 1: Full test run**

```bash
cd backend
dotnet build
dotnet test
```
Expected: build clean with warnings-as-errors on; **all** unit and integration tests pass. Record the totals.

- [ ] **Step 2: Migrations apply to an empty database**

Confirm the migration is reproducible from nothing, using the test schema as the stand-in for an empty project:

```bash
dotnet ef database drop --force \
  --project src/SlingKing.Infrastructure --startup-project src/SlingKing.Infrastructure --dry-run
```

Then in the Supabase SQL editor:

```sql
drop schema if exists gate_check cascade;
create schema gate_check;
```

```bash
SLINGKING_DB_MIGRATION="<direct connection>;Search Path=gate_check" \
  dotnet ef database update \
  --project src/SlingKing.Infrastructure --startup-project src/SlingKing.Infrastructure
```

Then verify and clean up in the SQL editor:

```sql
select count(*) from information_schema.tables where table_schema = 'gate_check';
-- expect 28

drop schema gate_check cascade;
```

- [ ] **Step 3: Health is green**

```bash
curl -s http://localhost:5210/api/v1/health
```
Expected: `{"status":"ok","database":"ok"}`

- [ ] **Step 4: Login returns a token**

```bash
curl -s -X POST http://localhost:5210/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"<seeded email>","password":"<seeded password>"}'
```
Expected: JSON containing `accessToken`, `refreshToken` and `user`.

- [ ] **Step 5: The shell renders behind auth**

Already confirmed in Task 16 Step 6. Re-confirm point 1 only: signed out, `http://localhost:5173/` must redirect to `/login`.

- [ ] **Step 6: The integration suite refuses to run against public**

The single most important check in this milestone. Temporarily point the test connection at the wrong schema and confirm the suite refuses:

```bash
SLINGKING_DB_TEST="<direct connection>;Search Path=public" dotnet test tests/SlingKing.IntegrationTests
```
Expected: **every test fails** with `Integration tests refuse to run outside the 'test' schema`. If even one test runs, stop and fix the guard before going any further — this is the property that lets a single database be safe.

Restore the correct value afterwards and re-run `dotnet test` to confirm green.

- [ ] **Step 7: A backup exists**

```bash
.\backup.ps1
grep -c "CREATE TABLE" backups/slingking-*.sql
```
Expected: `27`.

- [ ] **Step 8: Tag the milestone**

```bash
git add .
git commit -m "chore: R1 M1 foundation complete" --allow-empty
git tag -a r1-m1 -m "R1 M1: foundation, schema, auth, shell"
```

---

## Self-Review

**Spec coverage.** Every M1 line in spec §11 maps to a task: solution and four projects → 1; `git init` → 1; EF Core with the full 27-table migration applied to Supabase → 4, 5, 6, 7, 9; environment-variable configuration → 2, 13; JWT auth with one seeded user → 11, 12, 13; Swagger → 13; Serilog → 13; ProblemDetails handler → 13; CORS → 13; React shell with routing, axios client and login → 15, 16; backup script → 14; test-schema guard → 8. The gate's five clauses are Task 17 steps 2, 4, 3, 5 and 6.

**One deliberate deferral.** `ISkuGenerator` and `IFileParser` appear in the spec's Application-interfaces list but have no consumer until M2 and M4 respectively. Writing them now would mean writing an interface with no implementation and no caller. They are M2 and M4 work; `INumberSequenceAllocator`, which M2 needs immediately and which is genuinely hard to get right, is built here instead.

**Placeholder scan.** No `TBD`, no "add error handling", no "similar to Task N". Every code step carries the actual file content. Task 17 Step 2 requires the reader to substitute their own connection string — that is a value only they possess, and the surrounding shape is complete.

**Type consistency.** `IAppDbContext` property names in Task 6 match `AppDbContext` in the same task and the seeder's usage in Task 12. `LoginResponse { AccessToken, RefreshToken, ExpiresAtUtc, User }` is declared in Task 13 and consumed under the same names, camel-cased by System.Text.Json's default policy, in Task 15's `auth.ts`. `TestSchemaFixture.AssertTargetsTestSchema` is defined in Task 8 and called from Tasks 8 and 13. `formatInr` returns `—` for null in Task 15 and nothing else calls it in M1.

**One risk worth naming.** Task 9 onward runs integration tests over the network against Supabase. Expect the suite to take minutes, not seconds, and expect occasional flakiness from connection limits on the free tier. If it becomes painful, the fix is to reduce integration-test count rather than to weaken the schema guard.
