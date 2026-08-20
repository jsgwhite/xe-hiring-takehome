# NOTES — Diagrams

Companion to [`NOTES.md`](NOTES.md). These four diagrams cover the parts of the design that are
hard to see from any single file: how the rate providers compose, what a request actually costs
upstream, and where an alert's triggered state comes from.

---

## 1. Structure — services and models

The rate source is a decorator chain behind one interface. Nothing above `IRateProvider` knows
whether a rate came from Xe, from the cache, or from the development fake.

```mermaid
classDiagram
    class IRateProvider {
        <<interface>>
        +GetRatesAsync(pairs, ct) Dictionary~CurrencyPair, Rate~
    }

    class CachingRateProvider {
        -IRateProvider inner
        -IMemoryCache cache
        -TimeSpan ttl
        +GetRatesAsync(pairs, ct)
    }

    class XeRateProvider {
        -HttpClient httpClient
        +GetRatesAsync(pairs, ct)
        -FetchForBaseAsync(base, pairs, ct)
    }

    class FakeRateProvider {
        +GetRatesAsync(pairs, ct)
        -RateFor(pair, now)
    }

    class AlertService {
        -IAlertStore store
        -IRateProvider rateProvider
        +GetAllWithEvaluationsAsync(ct)
        +EvaluateAsync(alert, ct)
    }

    class AlertEvaluator {
        <<static>>
        +Evaluate(alert, rate) AlertEvaluation
    }

    class IAlertStore {
        <<interface>>
        +GetAll() IReadOnlyList~Alert~
        +Add(alert) Alert
        +Remove(id) bool
    }

    class InMemoryAlertStore {
        -ConcurrentDictionary~Guid, Alert~ alerts
    }

    class Alert {
        +Guid Id
        +CurrencyPair Pair
        +decimal Threshold
        +AlertDirection Direction
        +DateTimeOffset CreatedAt
    }

    class Rate {
        +CurrencyPair Pair
        +decimal Mid
        +DateTimeOffset AsOf
    }

    class CurrencyPair {
        +string Base
        +string Quote
        +TryParse(value, out pair) bool
    }

    class AlertEvaluation {
        +bool Triggered
        +decimal? CurrentRate
        +DateTimeOffset? AsOf
        +EvaluationStatus Status
    }

    IRateProvider <|.. CachingRateProvider
    IRateProvider <|.. XeRateProvider
    IRateProvider <|.. FakeRateProvider
    CachingRateProvider o-- IRateProvider : decorates
    IAlertStore <|.. InMemoryAlertStore

    AlertService --> IAlertStore
    AlertService --> IRateProvider
    AlertService ..> AlertEvaluator : calls
    AlertEvaluator ..> AlertEvaluation : returns

    Alert --> CurrencyPair
    Rate --> CurrencyPair
    InMemoryAlertStore o-- Alert
```

**Why the chain rather than one class:** the Xe sandbox key returns a constant `1.2345` for every
pair, which makes alert evaluation undemonstrable. `FakeRateProvider` substitutes for the *whole*
upstream in Development only. `CachingRateProvider` sits above whichever one is active, so the rate
board and every alert evaluation share one short-lived cache instead of each paying its own
round-trip.

---

## 2. Listing alerts — one batched call, not one per alert

`GET /api/alerts` is the request that would degrade worst under a naive implementation: N alerts
would mean N upstream calls. Distinct pairs are collected first, the cache absorbs repeats, and only
genuine misses reach Xe — grouped so that pairs sharing a base currency travel together.

```mermaid
sequenceDiagram
    autonumber
    participant UI as AlertsPanel
    participant C as AlertsController
    participant S as AlertService
    participant Cache as CachingRateProvider
    participant Xe as XeRateProvider
    participant API as Xe API

    UI->>C: GET /api/alerts
    C->>S: GetAllWithEvaluationsAsync()
    S->>S: store.GetAll()
    S->>S: distinct pairs across all alerts

    S->>Cache: GetRatesAsync(distinct pairs)
    Cache->>Cache: partition into cached / uncached

    alt some pairs uncached
        Cache->>Xe: GetRatesAsync(uncached only)
        Xe->>Xe: group by base currency
        par one call per base currency
            Xe->>API: convert_from.json?from=USD&to=CAD,JPY
            API-->>Xe: 200, "to" list (may be reordered)
        and
            Xe->>API: convert_from.json?from=GBP&to=USD
            API-->>Xe: 200, "to" list
        end
        Xe->>Xe: match on quotecurrency, never by index
        Xe-->>Cache: rates for the pairs Xe knew
        Cache->>Cache: store each with TTL
    end

    Cache-->>S: rates
    loop each alert
        S->>S: AlertEvaluator.Evaluate(alert, rate or null)
    end
    S-->>C: alerts + evaluations
    C-->>UI: 200, AlertDto[]
```

**Failure behaviour:** if the provider throws or Xe is unreachable, `AlertService` catches it and
substitutes an empty rate dictionary. Every alert then evaluates to `RateUnavailable` and the list
still returns `200` — an upstream outage degrades the page, it does not blank it.

---

## 3. Creating an alert — evaluate before persisting

The controller evaluates the alert *before* adding it to the store, so a rule that could never be
evaluated is never saved. This is also the step that rejects currency codes Xe does not recognise,
since Xe answers `200` with an empty list rather than an error for those.

```mermaid
sequenceDiagram
    autonumber
    participant UI as AlertsPanel
    participant C as AlertsController
    participant S as AlertService
    participant P as IRateProvider
    participant St as InMemoryAlertStore

    UI->>C: POST /api/alerts {pair, threshold, direction}

    alt pair does not parse
        C-->>UI: 400 "not a valid currency pair"
    else threshold <= 0
        C-->>UI: 400 "threshold must be greater than zero"
    else direction not above/below
        C-->>UI: 400 "direction must be above or below"
    else input valid
        C->>C: build Alert, not yet stored
        C->>S: EvaluateAsync(alert)
        S->>P: GetRatesAsync([alert.Pair])
        P-->>S: rate, or nothing for an unknown code
        S-->>C: AlertEvaluation

        alt status = RateUnavailable
            Note over C,St: nothing is persisted
            C-->>UI: 400 "no rate is available for this pair"
        else status = Ok
            C->>St: Add(alert)
            C-->>UI: 201 + Location /api/alerts/{id}
        end
    end
```

**A trap this diagram originally hid:** the `RateUnavailable` branch depends entirely on the
provider *withholding* a rate. `FakeRateProvider` — active in Development, so it is what a local run
exercises — used to synthesise a rate for any well-formed pair, which meant that branch never fired
locally and `USD/ZZZ` was accepted with an invented rate. The controller was correct and its test
passed the whole time; the double was the problem. It now omits unknown pairs exactly as
`XeRateProvider` does, so the path above behaves the same locally as against real Xe. Written up in
NOTES.md, because the general form — a happy-path-only double disabling failure handling without
failing any test — is easy to repeat.

---

## 4. Where triggered state comes from

An alert stores a *rule*. Whether it currently fires is computed at read time from the rule plus the
rate, never written down — so there is no stored boolean that can go stale. `AlertEvaluator` is a
pure function with no I/O, which is why it is the most heavily tested piece in the project.

```mermaid
flowchart TD
    A["Evaluate(alert, rate)"] --> B{"rate is for a<br/>different pair?"}
    B -->|yes| C["throw ArgumentException<br/>fail loudly, never guess"]
    B -->|no| D{"rate is null?"}

    D -->|yes| E["Triggered = false<br/>Status = RateUnavailable"]
    D -->|no| F{"direction?"}

    F -->|Above| G{"rate > threshold?"}
    F -->|Below| H{"rate &lt; threshold?"}

    G -->|yes| I["Triggered = true<br/>Status = Ok"]
    G -->|no| J["Triggered = false<br/>Status = Ok"]
    H -->|yes| I
    H -->|no| J

    E --> K["UI: orange 'rate unavailable'"]
    I --> L["UI: red 'TRIGGERED'"]
    J --> M["UI: green 'OK'"]

    style C fill:#ffdddd,stroke:#cc0000
    style E fill:#ffeecc,stroke:#ff9900
    style I fill:#ffdddd,stroke:#cc0000
    style J fill:#ddffdd,stroke:#00aa00
```

Two decisions this diagram makes explicit:

- **`RateUnavailable` is a third outcome, not a flavour of "not triggered".** Collapsing them would
  make an upstream outage read to the user as "everything is fine".
- **A rate sitting exactly on the threshold triggers neither direction.** It has not gone above it
  and has not dropped below it. Tested at the boundary in both directions.
