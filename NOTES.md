# NOTES

Submission notes for the Rate Alerts take-home. **Track: backend depth.**

**(This document is updated by AI as I did changes, I left text below unchanged even when incorrect, as it is interesting to see how accurate it can be. I added notes in brackets where it was wrong for interest. )**

**(Here is the github link so you can see commits in more detail: https://github.com/jsgwhite/xe-hiring-takehome)**

> This is a living document — I am updating it as I go rather than reconstructing it at the end, so
> the reasoning is recorded while it is still fresh. The detailed audit that backs it up is in
> [`docs/CODE_ANALYSIS.md`](docs/CODE_ANALYSIS.md).

---

## Time spent

Target is 2-3 hours. Actual:

| Phase | Time |
| --- | --- |
| Reading the brief, auditing the codebase, verifying the baseline runs | 30m |
| CI + test scaffolding | 20m |
| Rate service refactor (batching, caching, fake provider) | 40m |
| Alert domain, store, evaluator + tests | 45m |
| Alert endpoints (CRUD with validation) + backend tests | 35m |
| Frontend wiring (types, API client, AlertsPanel component) + tests | 60m |
| Code review, UI fixes, rate sync and display clarity | 25m |
| **Total** | **~3h 35m** |

Slightly over the 2–3h target. The extra time went to: (1) extensive frontend test coverage (~35 tests) where the budget was backend-focused, and (2) iterating on UX details (prefill on card click, auto-direction, rate display consistency, and code review findings). Both were worth the time.

** (Measuring time on tasks is typically wrong with AI, or at least hard to measure, since about half the time is spent waiting and in many cases today I did other tasks just to come back and find it stuck on a prompt for a long time) **

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
- **Rate fetching rebuilt behind `IRateProvider`.** `XeRateProvider` batches pairs sharing a base
  currency into one upstream call and matches results by `quotecurrency` rather than array position
  (finding 3), and treats an empty `to: []` as "unavailable" rather than crashing on it (finding 2).
  `CachingRateProvider` decorates it with a short TTL so listing several alerts doesn't cost a
  round-trip per alert. `FakeRateProvider` covers finding 1 — enabled only in Development via
  `appsettings.Development.json`, and Program.cs refuses to honour it outside Development regardless
  of config, so it can never mask real data anywhere else.
  Measured end to end: `/api/rates` went from 2.75s to 1.07s on the real API (matches the ~1.1s
  predicted from the raw batched call), and 2.6ms on a cache hit.
- **Alert domain built and tested before the endpoints that use it (TDD).** `Alert` is a rule, not a
  rule plus its last known answer — `AlertEvaluator.Evaluate` is a pure function computing triggered
  state at read time, tested for both directions and the exact-equality boundary before it had any
  caller. `InMemoryAlertStore` swaps the stub's manual locking (which was genuinely inconsistent —
  `IsTriggered` called inside the lock in `List`, outside it in `Create`) for a lock-free
  `ConcurrentDictionary`. `AlertService` batches rate lookups across every stored alert into one call
  per distinct pair, and falls back every alert to `RateUnavailable` if the rate provider throws,
  rather than 500ing the whole list.
- **`AlertsController` replaces the frontend-track stub**, fixing two bugs verified against the
  running stub before the fix: `POST` accepted a negative threshold outright, and the `Location`
  header on `201` came back as `/api/alerts` — missing the id — because `CreatedAtAction` was passed
  the alert as route values to a parameterless action. Direction is accepted case-insensitively and
  serialised back as lower-case `"above"`/`"below"` to match the README's documented contract exactly.

  It later grew the check that closes finding 2: `POST` evaluates the alert *before* storing it and
  rejects it with a `400` if no rate comes back, so a rule that could never be evaluated is never
  persisted. Since Xe answers an unknown currency code with `200` and an empty `to` array rather than
  an error, "no rate came back" is the only signal available that a code is bogus — which is why the
  check is an evaluation rather than a lookup against a list of codes.
- **`AlertEvaluator` didn't check that the rate it was handed was for the alert's own pair** — a code
  review question caught it: nothing stopped a caller from passing, say, a EUR/USD rate into a
  GBP/CAD alert's evaluation, and the function would have used its `Mid` anyway. Both real callers
  happen to look the rate up by `alert.Pair` already, so this couldn't fire today, but the function
  itself enforced nothing. Added a guard that throws on a pair mismatch, so a future caller that gets
  the lookup wrong fails loudly at the point of the mistake instead of producing a confident, silently
  wrong triggered/not-triggered answer.
- **Frontend: typed API client, rate board de-duplicated, alert management UI added.** `api.ts` gives
  every endpoint a real `res.ok` check and a thrown `Error` with the backend's actual message —
  `App.vue`'s original inline `fetch` had neither, so a `500` just left the board stuck at `...`
  forever. The three duplicated `getUsdCad`/`getGbpUsd`/`getEurUsd` linear-scan functions and their
  matching hardcoded cards became one `formattedRate()` lookup and a `v-for`; deliberately still over
  a fixed pair list rather than `state.rates` directly, since a partial upstream failure means fewer
  entries in `state.rates` and the board should keep all three cards with `...` for whichever is
  missing, not silently lose a column. `AlertsPanel.vue` adds create/list/delete with a status badge
  (triggered / rate unavailable / ok); its one real gap on arrival — a failed initial load fell back
  silently to the "no alerts yet" empty state, `console.error`'d and nothing else — was fixed before
  merging, for the same reason the api client exists: a failure must not look identical to "nothing to
  show".
- **`FakeRateProvider` invented rates for currencies that don't exist, which silently disabled the
  check above.** The most interesting bug in the exercise, found late by re-reading the code against
  this document rather than by any test, so it is worth writing up properly.

  The fake originally synthesised a plausible rate for *any* well-formed pair — reasonable-looking, on
  the view that a demo double just needs to supply numbers. But `UseFakeRates` is on in
  `appsettings.Development.json`, so that fake **is** what a reviewer running `dotnet run` gets. With
  it, no rate was ever unavailable, so `AlertsController`'s `RateUnavailable` branch never executed
  and `POST /api/alerts {"pair":"USD/ZZZ"}` returned `201` with a fabricated rate of `0.6336` for a
  currency that does not exist.

  What makes it worth recording is the shape of the failure rather than the bug: the guard is
  correct, it is covered by a controller test, that test passed throughout, and the code works
  properly against the real Xe API. Nothing was broken except the one configuration anybody
  demonstrating the project would actually run. A test double that reproduces only the upstream's
  happy path silently disables every code path that exists to handle the upstream's failures — and it
  does so without failing anything, because the doubles used in tests are a different object entirely.

  Fixed by giving the fake a set of real currency codes and having it omit unknown pairs from its
  result, exactly as `XeRateProvider` omits a quote currency the upstream left out of its `to` array.
  The fake now models both quirks the system depends on, not just the constant-rate one it was
  originally written for. `FakeRateProviderTests` pins that down, with the reasoning in the file, and
  `USD/ZZZ` now returns `400` locally while `EUR/SEK` still returns `201`.

## What I deliberately left, and why

Recording these because an unexplained omission looks like an oversight, while an explained one is a
decision.

- **The planted TODO at `main.ts:5` (move state into Pinia).** Pinia is installed and registered but
  no store is ever defined. I am leaving it: a 7-line `reactive` object is adequate for one
  component plus one panel, and adding a store to satisfy a TODO would be ceremony. On a
  backend-depth track, the depth belongs in the alert domain.
- **The planted TODO at `vite.config.ts:15` (test coverage config)** — actually done, not left. A
  coverage check showed `App.vue` fully covered on statements and 91.66% on branch (one untested
  `v-if` path); closing that plus adding the `@vitest/coverage-v8` provider was cheap, so it made
  the cut after all.
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
  as a type, since the batching design already needed base/quote separated. `POST /api/alerts` takes
  any pair Xe can quote: `AUD/JPY` and `EUR/SEK` work as readily as the three on the board.

  Rejecting codes that *aren't* real took the actual work, because of finding 2: Xe answers an
  unknown code with `200` and an empty `to` array, so the only available signal is that no rate came
  back. The controller therefore evaluates before persisting and returns `400` when the evaluation is
  `RateUnavailable`. Note what this does **not** do — it never consults Xe's published currency list
  (`/v1/currencies.json`), so it cannot distinguish "this code does not exist" from "Xe is
  unreachable right now", and the latter is currently reported to the user in the language of the
  former. Fetching that list at startup is the honest fix; see *Next steps*.
- **GitHub Actions CI** — mostly configuration, and it paid for itself immediately by letting me
  work test-first.

Neither displaced core work.

---

## AI tools used

**Claude Code (Claude Sonnet 5) in VS Code** for the whole session.

**(In actual fact I started with Opus for planning, then as work progressed I explicitly told Claude to use a lower agent if it makes sense, it actually ended up using a combo of Opus, Sonnet and Haiku, and even a little Codex models when I ran out of usage mins)**

How I used it, honestly:

- **Codebase audit.** Fast and accurate at cataloguing the rough edges with line references. I did
  not take it at face value — see the `NullReferenceException` prediction above, which was wrong and
  which I only caught by actually running the thing.
- **Verifying assumptions before building on them.** The most valuable use by a distance. Probing
  the live API surfaced the mock rates, the empty-array-on-200 behaviour, and the response
  reordering. All three were invisible from reading code, and all three changed the design.
- **Scaffolding.** Test project setup, CI workflow, boilerplate — the parts where typing is the only
  bottleneck.
- **Delegating self-contained, well-specified pieces to a cheaper model in the background** (Claude
  Haiku, via subagents) while continuing other work: the in-memory alert store, a coverage pass that
  closed two real branch-coverage gaps, and the first draft of the alert management UI. Each got a
  precise spec (exact interface, exact conventions to match, exact test cases) rather than an open
  brief — quality held up well given that structure. I reviewed each result before merging it; the
  alerts UI's one real gap (initial load failure falling back silently to the empty state) was caught
  in that review and fixed before landing, not shipped as-is.

What I rejected or overrode:

- Its initial instinct to fix *everything* it found. Most of those smells are deliberate bait in a
  2-3 hour exercise; the interesting decision is what to leave, so I scoped the refactor to what the
  alert feature actually touches.
- The claim that a null POST body would 500. Wrong, and worth recording as a reminder that static
  reading is not verification.

Iteration during code review:

- A code review found that the initial frontend fix (showing live board rates) conflated two different
  time points — the alert's evaluation time vs. the current board time — causing confusion when rates
  diverged. Fixed by reverting to show the evaluation rate with clearer labeling ("evaluated at:"
  instead of "current:"), which matches the actual time the triggered status is based on. This 
  caught a reasoning gap I missed, demonstrating the value of reviewing before submitting.
