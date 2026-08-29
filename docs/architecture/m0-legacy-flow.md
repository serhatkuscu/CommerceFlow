# M0 Legacy Flow

This document describes the **current M0 baseline only** — the deliberately simple, legacy-style
3-tier flow that exists today. It is not a description of the target architecture. M1 introduces a
separate `Domain`/`Application`/`Infrastructure`/`Api` skeleton alongside these legacy projects;
that flow will look meaningfully different and is documented separately once it exists. Do not
read this page as a preview of M1 — it isn't one.

## Order creation

```mermaid
sequenceDiagram
    participant Client
    participant OrdersController
    participant OrderManager as OrderManager (BLL)
    participant OrderDataAccess as OrderDataAccess (DAL)
    participant Sproc as usp_CreateOrder
    participant DB as Orders / OrderItems / Products

    Client->>OrdersController: POST /api/orders
    OrdersController->>OrderManager: CreateOrder(customerId, items)
    OrderManager->>OrderDataAccess: CreateOrder(customerId, items)
    OrderDataAccess->>Sproc: EXEC usp_CreateOrder (TVP @Items)

    Note over Sproc: Pure validation first -- no transaction open yet<br/>(empty order, quantity, customer, product existence)

    Sproc->>DB: BEGIN TRANSACTION
    Sproc->>DB: UPDATE Products (grouped, ascending ProductId)
    Sproc->>DB: INSERT Orders, INSERT OrderItems
    Sproc->>DB: COMMIT TRANSACTION

    alt success
        Sproc-->>OrderDataAccess: OrderId (OUTPUT)
        OrderDataAccess-->>OrderManager: OrderId
        OrderManager-->>OrdersController: OrderId
        OrdersController-->>Client: 200 {success:true, data:{...}}
    else business rule violated (51000-51004)
        Sproc-->>OrderDataAccess: THROW (specific error code + message)
        OrderDataAccess-->>OrderManager: BusinessRuleException
        OrderManager-->>OrdersController: BusinessRuleException
        OrdersController-->>Client: 200 {success:false, message:"..."}
    end
```

**Where the transaction starts:** inside `usp_CreateOrder`, not in C#. Validation that never
writes anything happens before `BEGIN TRANSACTION` — there's nothing to roll back, so it just
throws. The transaction wraps only the actual mutation (stock update + inserts), kept as narrow as
possible.

**Why the layers are separate even though M0 is "just legacy":** `OrdersController` knows HTTP,
not business rules. `OrderManager` orchestrates, and knows neither HTTP nor SQL. `OrderDataAccess`
knows SQL, not HTTP. Nothing today *requires* this separation to work — but M2 will split these
concerns into real modules, and the seams already existing here (even around code that's
deliberately unsophisticated) make that split cheaper than starting from a single fused layer.

## ERP export

```mermaid
sequenceDiagram
    participant Job as ErpExportJob
    participant DB as Orders (IsExportedToErp = 0)
    participant Erp as FakeErp

    Job->>DB: SELECT ... WHERE IsExportedToErp = 0
    DB-->>Job: pending orders

    loop for each pending order
        Job->>Erp: POST /api/erp/export
        alt success
            Erp-->>Job: 200 OK
            Job->>DB: UPDATE IsExportedToErp = 1, ExportedDate = now
        else failure or timeout
            Erp-->>Job: error
            Job->>DB: UPDATE ErpExportAttempts += 1 (no cap, no backoff)
        end
    end
```

`ErpExportJob` is a one-shot, Task-Scheduler-style process: it polls once, processes whatever it
finds, and exits — it is not a daemon. It also does not share `CommerceFlow.Legacy.DAL` with the
rest of the app; see [`../legacy-technical-debt.md`](../legacy-technical-debt.md) for why that's
deliberate.

## Connection string handling

`OrderDataAccess` reads its connection string from the `COMMERCEFLOW_DB_CONNECTION` environment
variable at construction time — never from a literal in source. Embedding it in code would mean a
different build per environment, a real risk of a password landing in git history (which never
fully goes away once it's there), and no guard against a developer's local run accidentally
pointing at a shared or production database.
