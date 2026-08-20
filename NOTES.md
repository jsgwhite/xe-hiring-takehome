# NOTES

Submission notes for the Rate Alerts take-home. **Track: backend depth.**

> This is a living document — I am updating it as I go rather than reconstructing it at the end, so
> the reasoning is recorded while it is still fresh. The detailed audit that backs it up is in
> [`docs/CODE_ANALYSIS.md`](docs/CODE_ANALYSIS.md).

---

## Time spent

Target is 2-3 hours. Running tally:

| Phase | Time |
| --- | --- |
| Reading the brief, auditing the codebase, verifying the baseline runs | 30m |
| CI + test scaffolding (this commit) | — |
| Rate service refactor | — |
| Alert domain, store, evaluator | — |
| Alert endpoints | — |
| Tests | — |
| Frontend wiring | — |

Final total recorded at submission.

---

## Approach

I audited and verified before writing anything. That produced four findings from probing the live
Xe API that changed the design, and they are worth stating up front because everything else follows
from them:

1. **The supplied API key is a mock.** Every currency pair returns exactly `1.2345` — verified
   across USD/CAD, USD/JPY, USD/KRW, EUR/HUF, GBP/CAD and AUD/JPY. Real markets differ by orders of
   magnitude. The `timestamp` does advance, so the endpoint is live; it just serves a canned value.
   Alert evaluation against it is therefore degenerate: thresholds above 1.2345 never trigger, below
   always trigger, for every pair alike. So the rate source sits behind an `IRateProvider` interface
   with a config-toggled fake that produces varied, drifting per-pair rates for local demo. That
   seam is good design regardless; the mock made it mandatory.

2. **An unknown currency returns HTTP 200 with an empty `to: []` array.** Two consequences:
   `RatesController.cs:32`'s `GetProperty("to")[0]` crashes on it, and Xe will not validate currency
   codes on my behalf — so supporting arbitrary pairs means validating against `/v1/currencies.json`
   myself (170 currencies, each with an `is_obsolete` flag).

3. **Batched responses come back reordered.** Requesting `to=CAD,GBP,EUR` returns `CAD, EUR, GBP` —
   alphabetical, not request order. Results must be matched on `quotecurrency`, never by index.
   Batching itself works and is worth using.

4. **`GET /api/rates` measured 2.75s**; one batched call takes ~1.1s. The async/batching refactor is
   worth real latency, not just tidiness.

Findings 2 and 3 are the highest-value tests in the suite, because they encode things I discovered
rather than things I assumed. Note that `EnsureSuccessStatusCode()` catches neither — both failures
arrive as `200`.

I also got one thing wrong on my first read: I predicted a `null` POST body would throw
`NullReferenceException`. It does not — `[ApiController]` returns a clean `400` first. Reading code
is not the same as running it.

---

## Working order

I am building CI and the test harness **first**, before the feature, so the alert domain can be
developed test-first against a pipeline that is already reporting. That is why the early commits are
scaffolding rather than features.

---

## Design decisions

### Triggered state is computed, not stored

The decision I most expect to be asked about. An alert is a *rule*; whether it currently fires is a
function of (rule, current rate) evaluated at read time. Storing a boolean means owning its
staleness — every read has to ask whether the value was written recently enough to trust.

The cost is real: no trigger *history*, so "when did this first fire?" is unanswerable, and nothing
can notify anyone, because nothing runs when no one is looking. The moment this feature needs to
send an email, computed-only stops being sufficient. See *Next steps*.

### Alerts store base and quote currency separately, not a `"GBP/CAD"` string

It maps one-to-one onto the Xe API's `from`/`to` parameters, makes grouping requests by base
currency natural (which is what makes batching possible), and turns "support any pair" into a
validation change rather than a schema change. It still serialises out as `"GBP/CAD"`, so the wire
contract documented in the README is unchanged.

### In-memory persistence

Deliberate. There is no user concept and no auth in this app, so durable per-user alert storage
would be inventing requirements. The `IAlertStore` interface means swapping in EF Core and Postgres
is a one-class change. The costs I am accepting: alerts are lost on restart, and the design assumes
a single instance — no distributed state, and no leader election for a future background evaluator.

### Direction is an enum, threshold is `decimal`

The stub compared the string `"above"` in two separate places. `JsonStringEnumConverter` keeps the
JSON identical while making the invalid case unrepresentable. `decimal` because these are money
values and `double` would be wrong.

---

## What I fixed, and why

*(Updated as work lands.)*

- **CI and test harness first** — so the feature could be built test-first.

## What I deliberately left, and why

Recording these because an unexplained omission looks like an oversight, while an explained one is a
decision.

- **The planted TODO at `main.ts:5` (move state into Pinia).** Pinia is installed and registered but
  no store is ever defined. I am leaving it: a 7-line `reactive` object is adequate for one
  component plus one panel, and adding a store to satisfy a TODO would be ceremony. On a
  backend-depth track, the depth belongs in the alert domain.
- **The planted TODO at `vite.config.ts:15` (test coverage config).** Same reasoning — my testing
  effort is on the backend for this track.
- **`App.test.ts`'s structure.** One monolithic test asserting ~10 unrelated things, mixing
  `@testing-library/vue` with `@vue/test-utils`, using `setTimeout(0)` instead of `flushPromises`,
  and mutating state at L28-30 without ever asserting the result. It passes, and it is not my core
  logic.
- **API credentials committed in `appsettings.json`.** Supplied that way deliberately. In real life:
  user-secrets locally, environment injection in deployment. Rotating a key the reviewers need would
  be actively unhelpful.
- **Unscoped global CSS in `App.vue`.** The brief says CSS is not being judged.
- **No auth, rate limiting, or multi-user support.** Out of scope for the time budget, and each
  would need a user concept the app does not have.

---

## Next steps with more time

Roughly in the order I would actually do them:

1. **Persistence** — EF Core against Postgres behind the existing `IAlertStore`. One class, plus
   migrations.
2. **Background evaluation** — an `IHostedService` on a timer writing `LastTriggeredAt` and a
   state-transition log. This is what makes stored triggered-state correct rather than stale: it
   gives the value a clear owner and a clear write path. It also unlocks trigger history.
3. **Notification delivery** — email or webhook on the *transition* into triggered, with
   deduplication so a rate oscillating around a threshold does not spam. Needs (2) first.
4. **Hysteresis / debounce** on evaluation, for the same oscillation reason.
5. **Auth and multi-user** — alerts scoped to a user, which changes the store's key and the API
   surface.
6. **Resilience on the Xe client** — Polly retry with jitter, a circuit breaker, and explicit
   handling of 429. Currently one upstream hiccup degrades a request; it should degrade gracefully
   and recover.
7. **OpenAPI** — the `.csproj` has no Swagger package; the contract is documented only in prose.
8. **Frontend polish** — the "Last updated" label implies liveness the app does not have (there is
   no polling, only a manual button). Either add polling or stop implying it.
9. **Structured logging and metrics** — no `ILogger` is injected anywhere today, despite logging
   config existing in `appsettings.json`.

---

## Stretch items

The brief says pick at most one. I did two, and would rather say so than pretend otherwise:

- **Any currency pair supported by the API** — this fell out almost free once `CurrencyPair` existed
  as a type, since the batching design already needed base/quote separated. The only real work was
  validating codes myself, because of finding 2 above.
- **GitHub Actions CI** — mostly configuration, and it paid for itself immediately by letting me
  work test-first.

Neither displaced core work.

---

## AI tools used

**Claude Opus (Claude Code in VS Code)** for the whole session.

How I used it, honestly:

- **Codebase audit.** Fast and accurate at cataloguing the rough edges with line references. I did
  not take it at face value — see the `NullReferenceException` prediction above, which was wrong and
  which I only caught by actually running the thing.
- **Verifying assumptions before building on them.** The most valuable use by a distance. Probing
  the live API surfaced the mock rates, the empty-array-on-200 behaviour, and the response
  reordering. All three were invisible from reading code, and all three changed the design.
- **Scaffolding.** Test project setup, CI workflow, boilerplate — the parts where typing is the only
  bottleneck.

What I rejected or overrode:

- Its initial instinct to fix *everything* it found. Most of those smells are deliberate bait in a
  2-3 hour exercise; the interesting decision is what to leave, so I scoped the refactor to what the
  alert feature actually touches.
- The claim that a null POST body would 500. Wrong, and worth recording as a reminder that static
  reading is not verification.

*(This section gets a final pass at submission.)*
