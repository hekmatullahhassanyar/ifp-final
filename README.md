# Delivery Quote and Inventory Processing System

Final project for Introduction to Functional Programming (academic year 2026-2027).
A C# console application for logistics coordinators and dispatchers. It validates delivery requests against an inventory, prices them with configurable shipping policies, reserves stock through safe state transitions, and produces LINQ reports. The discount calculation is done by a native C library called through P/Invoke.

## Project information

| Item | Value |
|---|---|
| Project title | Delivery Quote and Inventory Processing System |
| Student | Hekmatullah Hassanyar |
| Group | IT-2512 |
| Team type | Individual project |
| Repository URL | https://github.com/hekmatullahhassanyar/ifp-final |


## Latest test result

**90 tests, 90 passed, 0 failed** (Visual Studio Test Explorer, 15 November 2026).

## How to build, run and test

Run all commands from the repository root. Working directory for the program is the output folder of the App project.

### 1. Build the native library (once, and after any change to `native/deliveryfee.c`)

Windows: open **x64 Native Tools Command Prompt for VS 2022** and run:

```
cd native
build.bat
dir bin
```

`bin\deliverynative.dll` must exist. Linux/macOS: `sh native/build.sh`.
The binary must match the process architecture (x64). Build the native library before building the .NET solution, so it is copied next to the executable.

### 2. Build, test, run

Command line:

```
dotnet build DeliveryQuote.sln
dotnet test DeliveryQuote.sln
dotnet run --project src/DeliveryQuote.App
```

Visual Studio: **Build > Rebuild Solution**, **Test > Run All Tests**, then press Start on `DeliveryQuote.App`.

Optional arguments: `dotnet run --project src/DeliveryQuote.App -- <dataDir> <outputDir>`.
The program writes `output/products.updated.json` (inventory after confirmations) and `output/report.txt` (report rows) in the current working directory (when started from Visual Studio this is `src\DeliveryQuote.App\bin\Debug\net8.0\output`).

### Failure demonstrations

- Missing native library: rename `deliverynative.dll` in the output folder and run; every quote fails with `NATIVE_LIBRARY_NOT_FOUND` and the program does not crash.
- Missing data file: the demo at the end of the run loads `missing.json` and reports `FILE_NOT_FOUND`.
- Invalid requests: R007 to R011 in `requests.csv` demonstrate rejected input.

## Intended users and scope

Users: logistics coordinators and dispatchers who need delivery prices, stock checks, and summaries.

Domain models: `Product`, `Catalog` (inventory), `ValidatedRequest`, `PricedQuote`, `DeliveryState` (Pending / Confirmed).

Implemented workflows (all use the same products and requests data):

1. **Quote a delivery:** raw request -> validation (fields, product lookup, distance, stock) -> pricing with a shipping policy and a native-calculated quantity discount -> pending quote.
2. **Confirm and reserve stock:** a pending quote is confirmed, stock is reserved in a new immutable catalog, a repeated confirmation is rejected, and later orders fail if stock ran out.
3. **Reporting and export:** category revenue summary and high-value quote list, then saving the updated inventory and exporting the report.

## Domain rules

- Quantity must be a whole number greater than 0.
- Distance must be a whole number from 0 to 500 km (0 and 500 are valid, 501 is rejected).
- Product must exist, and quantity must not exceed stock. Stock of 0 is a legitimate value.
- Product price must be positive; stock must not be negative.
- Quantity discount: quantity 5 to 9 gives 5 %, quantity 10 or more gives 10 %.
- Shipping: Standard = 500 + 100 per km; Express = 800 + 150 per km (amounts in cents).
- All money is stored as integer cents. The native function rounds half up.

## Sample data and expected results

Files: `src/DeliveryQuote.App/Data/products.json` (6 products) and `requests.csv` (11 requests).

| Request | Standard result | Express total |
|---|---|---|
| R001 chair x2, 2 km | items 170.00, ship 7.00, **total 177.00** | 181.00 |
| R002 laptop stand x5, 30 km | 160.00 - 5 % = 152.00, ship 35.00, **187.00** | 205.00 |
| R003 USB-C cable x10, 0 km | 90.00 - 10 % = 81.00, ship 5.00, **86.00** | 89.00 |
| R004 monitor x3, 120 km | 567.00, ship 125.00, **692.00** | 755.00 |
| R005 monitor x2, 50 km | 378.00, ship 55.00, **433.00** (confirmation later fails: only 1 monitor left) | 461.00 |
| R006 desk x1, 500 km | 249.00, ship 505.00, **754.00** | 1007.00 |
| R007 | rejected `OUT_OF_SERVICE_AREA` (501 km) | |
| R008 | rejected `INVALID_QUANTITY` (0) | |
| R009 | rejected `PRODUCT_NOT_FOUND` (P999) | |
| R010 | rejected `INSUFFICIENT_STOCK` (stock 0) | |
| R011 | rejected `INVALID_QUANTITY` ("abc") | |

Report 1 (revenue by category, Standard): Electronics 2 quotes 1125.00; Furniture 2 quotes 931.00; Accessories 2 quotes 273.00.
Report 2 (quotes of 500.00 or more): R006 754.00, then R004 692.00.
Confirmation: R001, R002, R003, R004, R006 confirmed; R005 fails with `INSUFFICIENT_STOCK`; a repeated confirmation is rejected with `ALREADY_CONFIRMED`.

## Requirement map

| Requirement | Where implemented |
|---|---|
| Pure functional core, no I/O in logic | `Domain.cs`, `Pricing.cs`, `Workflow.cs`, `Reports.cs`. Console and files only in `Program.cs` and `Persistence.cs` |
| Immutable models | `Product`, `ValidatedRequest`, `PricedQuote`, `DeliveryState` are records with private constructors where validation applies; `Catalog` wraps an `ImmutableDictionary` and every change returns a new catalog |
| Pattern matching for a domain decision | `Fulfilment.Confirm` (switch over `DeliveryState`), `Pricing.DiscountPercentFor` (relational patterns), `Catalog.Reserve` (switch over `Maybe`) |
| Pipeline with 3 named stages | `QuoteWorkflow.Stage1Validate` -> `Stage2Price` -> `Stage3OpenPending`, chained in `QuoteWorkflow.Quote` |
| Higher-order function changing a rule | `Pricing.Quote(request, ShippingPolicy, Func<long,int,Result<long>> applyDiscount)` calls both delegates |
| Currying and partial application | `Pricing.CreateShipping` (base fee -> per-km fee -> request -> cents); `Standard = CreateShipping(500)(100)`, `Express = CreateShipping(800)(150)` |
| Safe operation sequence | `ValidatedRequest.Create` is the only way to obtain a validated request; `Pricing.Quote` accepts only that type; `Fulfilment.Confirm` accepts a `DeliveryState` and rejects `Confirmed` |
| Result with 3+ domain failures | `Result.cs` plus the error codes listed below |
| Map and Bind in a real workflow | `QuoteWorkflow.Quote` (Bind, Bind, Map); `Catalog.Reserve`; `Fulfilment.Confirm`; `Program.Main` |
| Missing lookups | `Catalog.Find` returns `Maybe<Product>`, converted with `ToResult` in `ValidatedRequest.Create` |
| Boundary exceptions | `DataStore` (file, JSON and CSV exceptions) and `NativeDiscountAdapter` (library, entry point and architecture exceptions) |
| 2+ custom extension methods | `QueryExtensions.HighValue`, `QueryExtensions.Successes`, `QueryExtensions.ToMoney`, and `ResultExtensions.Map/Bind/Match/ToResult` |
| 2 LINQ reports | `Reports.CategorySummaries` (GroupBy, Count, Sum, OrderByDescending, ThenBy) and `Reports.HighValueQuotes` (Where through the extension, OrderByDescending, ThenBy, Select) |
| yield return and deferred execution | `QuoteWorkflow.QuoteAll`, `DataStore.StreamRequests`, `QueryExtensions.Successes`; materialised with `ToArray()` in `Program.Run` and `DataStore.LoadRequests` |
| JSON/CSV load, save/export, validation | `DataStore.LoadCatalog`, `LoadRequests`, `StreamRequests`, `SaveCatalog`, `ExportLines` |
| Native C integration | `native/deliveryfee.c`, `native/deliveryfee.h`, `NativeDiscount.cs` |
| Automated tests | `tests/DeliveryQuote.Tests` (90 test cases) |

## Explicit outcomes (Result and Maybe)

`Result<T>` is either `Success(value)` or `Failure(DomainError)`. Its constructor is private, so a success without a value or a failure with a value cannot be created. Failure skips every later stage; `Map` and `Bind` do not call their function when the input is a failure (proved by tests with counters).
`Maybe<T>` is `Some(value)` or `None`; absence is never encoded as null, 0 or an empty string.
`DeliveryState` (Pending / Confirmed) is the workflow state of a successfully produced quote. It is a different type from `Result`, because a validation failure and a workflow state mean different things.

| Group | Error codes |
|---|---|
| Invalid fields | `INVALID_QUANTITY`, `INVALID_DISTANCE`, `INVALID_REQUEST_ID`, `INVALID_ID`, `INVALID_NAME`, `INVALID_CATEGORY`, `INVALID_PRICE`, `INVALID_STOCK`, `MISSING_FIELD`, `MISSING_INPUT` |
| Missing item or rule | `PRODUCT_NOT_FOUND`, `INSUFFICIENT_STOCK`, `OUT_OF_SERVICE_AREA`, `DUPLICATE_PRODUCT` |
| State | `ALREADY_CONFIRMED` (repeat confirmation is rejected and stock is not reserved twice) |
| Files | `FILE_NOT_FOUND`, `MALFORMED_DATA`, `FILE_UNREADABLE`, `FILE_WRITE_FAILED` |
| Native | `NATIVE_LIBRARY_NOT_FOUND`, `NATIVE_ENTRY_POINT_NOT_FOUND`, `NATIVE_ARCHITECTURE_MISMATCH`, `NATIVE_INVALID_ARGUMENT`, `NATIVE_OVERFLOW`, `NATIVE_UNKNOWN_CODE`, `INVALID_AMOUNT`, `INVALID_DISCOUNT` |

## Temporal coupling: unsafe before, safe after

Unsafe example (documentation only, it is not in the application):

```csharp
var order = new Order();
order.Confirm();
order.Validate(raw);
```

`Confirm()` succeeds although `Validate()` and pricing never ran, so an unvalidated, unpriced order is confirmed and stock can go negative. The required order is hidden from the caller.

Safe design in this project: `Fulfilment.Confirm` needs a `DeliveryState.Pending`, which can only be produced by `Stage1Validate` followed by `Stage2Price`. There is no overload that accepts a raw request, so the wrong order does not compile. A repeated confirmation follows a documented rule: it is rejected with `ALREADY_CONFIRMED`. A failed confirmation leaves the original catalog unchanged.

## Native library contract

Function: `int64_t dq_apply_discount(int64_t amount_cents, int32_t percent)` in `native/deliveryfee.c`.

| Item | Value |
|---|---|
| Result | `amount * (100 - percent) / 100` in cents, rounded half up |
| Valid input | amount 0 to 90,000,000,000,000,000 cents; percent 0 to 100 |
| Invalid input | returns -1 (argument out of range) or -2 (amount too large) |
| Units | integer cents, so no floating point is used |
| Manual check 1 | amount 995, percent 10: 995 x 90 / 100 = 895.5, rounds to **896** |
| Manual check 2 | amount 12345, percent 5: 12345 x 95 / 100 = 11727.75, rounds to **11728** |

C# binding: `NativeMethods.ApplyDiscount` in `NativeDiscount.cs` uses `DllImport("deliverynative")` with the Cdecl convention, `long` for `int64_t` and `int` for `int32_t`. Only simple values cross the boundary, so no native memory is allocated and nothing needs to be released.
`NativeDiscountAdapter.Apply` validates the domain input before the call and converts `DllNotFoundException`, `EntryPointNotFoundException`, `BadImageFormatException` and negative native return codes into `Result` failures.

Build and loading: `native/build.bat` (MSVC `cl /LD /MT`, x64). The project file copies `native/bin/*.dll` next to the executable. Build environment used: Windows 11, x64, Visual Studio 2022 17.14 (MSVC).
Integration difficulty and solution: the application reported `NATIVE_LIBRARY_NOT_FOUND` because the DLL had not been compiled for Windows. The fix was building it from the x64 Native Tools prompt and rebuilding the solution so the DLL was copied to the output folder. A 32-bit DLL would fail with `BadImageFormatException`, which the adapter reports as `NATIVE_ARCHITECTURE_MISMATCH`.

## Resource ownership and retention notes

- `DataStore.LoadCatalog` owns a `FileStream` (`using`); it is released on success and on exception.
- `DataStore.SaveCatalog` owns the stream of a temporary file (`using` block), closes it, then moves the file over the target, so a failure never leaves a half-written data file.
- `DataStore.ExportLines` uses `File.WriteAllLines`, a convenience API that opens and closes the file itself.
- `DataStore.StreamRequests` yields lines while a `StreamReader` is open. The `using` inside the iterator disposes it when iteration finishes, fails, or stops early (tested).
- Materialisation points: `DataStore.LoadRequests` (file snapshot) and `Program.Run` (`ToArray()` once per policy). Reusing a snapshot does not read the file or run the pipeline again; enumerating a lazy sequence twice would.
- A lazy query captures its source and catalog. Both are immutable, so retaining them is safe.
- No static caches, event subscriptions or growing long-lived collections are used.
- Trade-off: streaming keeps memory low but holds the file open and repeats work if enumerated twice; a snapshot uses memory but is stable and reusable.

## Automated tests (xUnit, 90 cases)

| File | Covers |
|---|---|
| `CoreTests.cs` | Result `Map`/`Bind` success and failure propagation with non-execution proof, `Maybe` present/absent and legitimate zero/empty values, product and catalog validation, request validation and boundaries, shipping policies and discount tiers, extension methods |
| `WorkflowTests.cs` | pipeline success and failure skipping, deterministic output, empty input, confirmation and stock reservation, repeated confirmation, state preservation after failure, two complete workflows (one with the real native call), deferred execution and early stop |
| `ReportTests.cs` | grouping and totals, empty input, no matches, tie-breakers, inputs not modified |
| `BoundaryTests.cs` | missing and malformed JSON and CSV, invalid and missing fields, early-stop and failure resource release, save/load round trip, export, real native calls against hand-calculated values, adapter validation, load and binding failures, native error codes |

File tests use a temporary folder created per test and deleted afterwards. Test run result: **90 passed, 0 failed** (15 November 2026).

## Team and responsibilities

| Member | Group | Responsibilities |
|---|---|---|
| Hekmatullah Hassanyar | IT-2512 | Individual project: all modules (domain models and factories, pricing and pipeline, Result/Maybe, reports and extensions, persistence, native C library and adapter, tests, documentation) |

## Limitations

- Console demonstration only; there is no interactive menu or graphical interface.
- Distance is in whole kilometres and prices are in a single unit (cents), with no currency conversion.
- The native library is built for Windows x64 (and Linux/macOS with the provided script); other platforms need a rebuild.
- Customer, cancellation and restocking workflows are not implemented.

## Repository contents

```
DeliveryQuote.sln
README.md
native/        deliveryfee.c, deliveryfee.h, build.bat, build.sh
src/DeliveryQuote.App/   application source and Data/ sample files
tests/DeliveryQuote.Tests/   xUnit tests
```


