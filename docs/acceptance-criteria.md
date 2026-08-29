# M0 Acceptance Criteria

The frozen behavior contract for the M0 legacy order-creation and ERP-export flow. Each row states
the exact expected behavior, how it's verified today, and an honest classification — this document
exists specifically so that gaps stay visible instead of getting quietly assumed away.

## Classification key

- **Deterministic** — an automated test asserts this every run, with no dependence on timing or
  chance.
- **Probabilistic** — an automated test exercises this under conditions that make it *likely* but
  not *guaranteed* to occur; a pass does not prove the underlying mechanism fired.
- **Structural** — verified by inspecting the deployed code/SQL itself (a contract test), not by
  observing runtime behavior. Proves the mechanism exists, not that it works as intended under load.
- **Undemonstrated** — no automated test exists; verified manually at some point, or not at all.

## AC1 — Happy path

Valid order, sufficient stock for every line → order + items created, stock decremented, response
`{success:true, data:{orderId, status, statusText, totalAmount, items}}`, HTTP 200.

**Verification:** `OrderApiTests.CreateOrder_HappyPath_PersistsOrderAndDecrementsStock` (asserts DB
state, not just the HTTP response). **Classification: Deterministic.**

## AC2 — Insufficient stock is all-or-nothing

Any line short on stock → the entire order fails, nothing is written, nothing is decremented —
including lines that individually had enough stock. `{success:false, message}`, HTTP 200.

**Verification:** `OrderApiTests.CreateOrder_InsufficientStock_IsAllOrNothing`. **Classification: Deterministic.**

## AC3 — Conditional UPDATE prevents oversell

The conditional `UPDATE ... WHERE StockQuantity >= @Qty` guarantees no negative stock / no
oversell, specifically. **It does not by itself guarantee freedom from deadlocks — see AC11.**

**Verification:** `ConcurrencyTests.CreateOrder_ConcurrentOrdersForScarceStock_NeverOversells`.
**Classification: Deterministic** (the invariant itself is asserted with certainty; see AC11 for
why the *concurrency* framing around it is weaker).

## AC4 — Duplicate rapid submission (known bug)

No idempotency key exists. Rapid duplicate submission of an identical order creates **two**
separate orders and double-decrements stock. This is a known bug, characterized as-is on purpose —
fixing it is an explicit, called-out behavior change planned for M2, not something to quietly patch
in M0.

**Verification: none.** **Classification: Undemonstrated** (accepted gap).

## AC5 — Missing order returns HTTP 200, not 404

`GET /api/orders/{id}` for a non-existent id → `{success:false, message:"Order not found."}` with
HTTP 200 — a known legacy quirk, characterized as-is rather than "fixed" to a proper 404.

**Verification:** `OrderApiTests.GetOrderById_MissingOrder_ReturnsHttp200NotFoundEnvelope`.
**Classification: Deterministic.**

## AC6 — ERP export success path

The export job eventually sets `IsExportedToErp = 1` and `ExportedDate` for an order once FakeErp
accepts it.

**Verification: manual only** — demonstrated live against a real container (`ErpExportJob` run,
DB state checked directly). No automated regression test exists in the characterization suite.
**Classification: Undemonstrated** (accepted gap).

## AC7 — Unbounded ERP export attempts (known bug)

Repeated FakeErp failure increments `ErpExportAttempts` with **no cap and no backoff** — a known
bug, to be fixed properly in M5 once resilience is actually the subject under test.

**Verification: manual only** — reproduced once in an earlier session (FakeErp stopped, job run
three times, `ErpExportAttempts` confirmed to reach 3 with no ceiling). No automated regression
test. **Classification: Undemonstrated** (accepted gap, but the bug itself has been observed to
genuinely reproduce, not just asserted on paper).

## AC8 — Duplicate product line

The same `productId` twice in one request → **not** merged or rejected: one `OrderItem` row per
original request line (duplicates preserved), but stock is decremented **once per distinct
product**, using the summed quantity across the duplicate lines.

**Verification:** `OrderApiTests.CreateOrder_DuplicateProductLine_KeepsSeparateRowsButDecrementsOnceByTheSum`.
**Classification: Deterministic.**

## AC9 — Empty order

An empty `items` array → rejected before any transaction opens: `{success:false, message:"Order
must contain at least one item."}`, nothing written.

**Verification:** `OrderApiTests.CreateOrder_EmptyItems_IsRejectedWithoutWritingAnything`.
**Classification: Deterministic.**

## AC10 — Invalid quantity

Zero or negative quantity on any line → rejected before any stock operation:
`{success:false, message:"Quantity must be greater than zero for product X."}`.

**Verification:** `OrderApiTests.CreateOrder_ZeroQuantity_IsRejectedWithoutTouchingStock`.
**Classification: Deterministic.**

## AC11 — Concurrency: stock/order integrity, and the limits of what's proven

Under concurrent load, stock never goes negative and no order is ever partially written. If SQL
error 1205 (deadlock victim) occurs, `OrderDataAccess` retries up to 3 total attempts before
letting the error propagate as a generic failure.

**What is proven, and how:**
- **Stock/order integrity under concurrent HTTP load — Probabilistic.**
  `ConcurrencyTests` launches many concurrent requests (including deliberately crossed product
  submission order) and asserts stock/order counts end up exactly right. This does **not** prove
  that any two requests' database transactions genuinely overlapped in time — a local SQL Server
  call can complete in single-digit milliseconds, so the same passing result would also occur if
  every request ran in strict, non-overlapping sequence.
- **Canonical lock ordering exists in the deployed procedure — Structural.**
  `StoredProcedureContractTests` queries `OBJECT_DEFINITION` for the live `usp_CreateOrder` and
  confirms it groups and orders stock updates ascending by `ProductId`. This proves the mechanism
  is present in what's actually running, not that it prevents deadlocks at runtime.
- **Retry-loop mechanics — Deterministic, but narrowly scoped.**
  `RetryPolicyTests` proves the extracted `RetryPolicy.Execute` retries exactly the configured
  number of times, invokes backoff the correct number of times, and rethrows the original
  exception when the budget is exhausted — against a **fake** exception, not a real
  `SqlException` or a real deadlock. It does not prove that `SqlException.Number == 1205` is the
  correct thing to retry on; that one-line mapping in `OrderDataAccess` is verified by code
  inspection only.

**What is explicitly *not* claimed, and why it can't be, meaningfully:**
Two concurrent callers of `usp_CreateOrder` that both correctly use canonical ascending-`ProductId`
ordering **cannot deadlock against each other** — this is a structural property of the algorithm
(it breaks the circular-wait condition required for a deadlock), not a probability that a runtime
race test could meaningfully raise or lower confidence in. A test built to "catch" this deadlock
would either never observe it (because it's impossible by construction, so the test proves
nothing) or, if it somehow did observe a 1205 between two such callers, that would indicate a bug
elsewhere in the reasoning — not evidence the test was doing its job. **No test in this suite
claims to have observed a real deadlock, and none claims that canonical ordering has been proven
to prevent one at runtime.**

There is also no automated test verifying that once the retry budget is exhausted, the client
receives the generic `{success:false,"An unexpected error occurred."}` message rather than raw SQL
exception text — documented as an open, accepted risk (see `docs/roadmap.md`).
