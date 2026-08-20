# Code Analysis — Inherited Codebase

Written before any feature work, on branch `feat/rate-alerts`. This is the raw audit; `NOTES.md` at
submission time distils it into the short version.

Two purposes: establish that the baseline actually works before I change it, and record what I found
so the "what did you fix, what did you leave, and why" conversation has evidence behind it.

---

## 1. Baseline verification

I built and ran everything before planning, so I could tell inherited breakage apart from my own.

| Check | Result |
| --- | --- |
| `dotnet build` | Succeeded — 0 warnings, 0 errors (SDK 10.0.302) |
| `npm install` | 239 packages, 0 vulnerabilities (Node 22.20.0) |
| `npm run test` | 1 file, 1 test, passed |
| `npm run build` | `vue-tsc --noEmit` clean, Vite build in 247ms |
| Backend `:5180` | Starts clean, no errors logged |
| Frontend `:5173` | Vite ready in 124ms, `/api` proxy reaches backend |
| `GET /api/rates` | `200`, three pairs |
| Alerts stub CRUD | `POST` 201, `GET` 200, `DELETE` 204 / 404 — all correct |

**The README's claim holds: two commands and it runs.** Nothing below is repair work; it is all
deliberate improvement.

---

## 2. Findings from probing the live Xe API

These are the findings that actually changed my design. All were verified with `curl` against the
configured credentials, not assumed from reading code.

### 2.1 The supplied API key is a mock account

Every currency pair returns exactly `1.2345`:

| Pair | Xe returns | Real-world approx |
| --- | --- | --- |
| USD/CAD | 1.2345 | ~1.37 |
| USD/JPY | 1.2345 | ~150 |
| USD/KRW | 1.2345 | ~1300 |
| EUR/HUF | 1.2345 | ~390 |
| GBP/CAD | 1.2345 | ~1.71 |

The `timestamp` field *does* advance at minute granularity, so the endpoint is live — it just serves
a canned value.

**Consequence:** alert evaluation is degenerate against this API. A threshold above 1.2345 never
triggers; below always triggers; identically for every pair. Demoing "GBP/CAD above 1.84" is
impossible with real data.

**Response:** put an `IRateProvider` interface at the boundary, with the real `XeRateProvider` and a
config-toggled `FakeRateProvider` that returns varied, slowly-drifting per-pair rates. Tests inject
their own fake regardless. This is the right seam to have anyway — it just became mandatory rather
than tasteful.

### 2.2 An unknown currency returns HTTP 200, not an error

```
GET /v1/convert_from.json/?from=USD&to=ZZZ
200 OK
{"from":"USD","amount":1.0,"timestamp":"...","to":[]}
```

Two consequences, both material:

1. `RatesController.cs:32` does `doc.RootElement.GetProperty("to")[0]` — that throws
   `IndexOutOfRangeException` on an empty array, surfacing as an unhandled 500.
2. **Xe will not validate currency codes for me.** Supporting arbitrary pairs therefore requires
   validating against `/v1/currencies.json` myself (verified working: 170 currencies, each carrying
   an `is_obsolete` flag).

Note that `EnsureSuccessStatusCode()` would not catch this. The failure is a 200.

### 2.3 Batched responses come back reordered

Batching works and is worth using — one call can carry several quote currencies:

```
GET /v1/convert_from.json/?from=USD&to=CAD,GBP,EUR
-> to: [CAD, EUR, GBP]
```

Requested `CAD,GBP,EUR`; received `CAD,EUR,GBP` — alphabetical, not request order. Results must be
matched on the `quotecurrency` field, never by array index. The current code sidesteps this only
because it makes one call per pair.

### 2.4 Sequential fetching costs ~2.5x

`GET /api/rates` measured at **2.75s**. A single batched call takes **~1.1s**. Three sequential
blocking round-trips, where one grouped call (or `Task.WhenAll` across base currencies) would do.

### 2.5 Correction to my own first pass

My initial static read predicted that a `null` POST body would throw `NullReferenceException` at
`AlertsStubController.cs:53`. **It does not.** `[ApiController]` model validation returns a clean
RFC 9110 `400` before the action body runs. Dropped from the list — worth recording that reading
code is not the same as running it.

---

## 3. Backend issues

### `Controllers/RatesController.cs` — the deliberate showpiece

| Line(s) | Issue |
| --- | --- |
| 24-34, 36-46, 48-58 | The same 7-line block copy-pasted three times, differing only in the pair |
| 25, 37, 49 | `new HttpClient()` per call, never disposed — socket exhaustion antipattern |
| 29, 30, 41, 42, 53, 54 | Six `.Result` blocking calls; `GetRates()` is `IActionResult`, not `Task<IActionResult>` |
| 26-27, 38-39, 50-51 | Base64 credentials recomputed three times |
| 29-30 | No `EnsureSuccessStatusCode()`, no try/catch — upstream 401/429 becomes an unhandled 500 |
| 32 | `GetProperty("to")[0]` — crashes on the empty array from §2.2 |
| — | No `CancellationToken`; client disconnect does not cancel upstream work |
| 34, 46, 58 | Anonymous types as the public API contract — untyped, unshareable |
| 29, 41, 53 | Base URL hardcoded inline three times; `Math.Round(mid, 4)` precision hardcoded |
| 27 | Config read via magic strings rather than a bound options class |
| — | No `ILogger` injected, despite logging config existing in `appsettings.json` |
| — | No caching — every browser refresh costs three calls to a paid API |

README:61 ("if a rate shows as `...` and never resolves, check the backend terminal") documents the
symptom of the missing error handling rather than fixing it.

### `Program.cs`

Nine lines: `AddControllers()` and nothing else. No `IHttpClientFactory`, no CORS (works only
because Vite proxies `/api`), no `UseExceptionHandler` / `AddProblemDetails`, no options binding, no
OpenAPI, no health check.

### `Controllers/AlertsStubController.cs`

README:56 states plainly this is frontend-track scaffolding and is *"not a partial solution"* —
replace or delete it. Recording its flaws anyway, because several are traps worth not re-creating:

| Line(s) | Issue |
| --- | --- |
| 21-26 | Canned rates entirely disconnected from `/api/rates` — the `triggered` flag is fiction |
| 28-29 | Static mutable state as storage; no abstraction, no DI, untestable in isolation |
| 82-86 | `IsTriggered` is a `private static` inside a controller — cannot be unit-tested without hosting |
| 84 | Unguarded `CannedRates[alert.Pair]` indexer — `KeyNotFoundException` waiting to happen |
| 43 vs 69 | Inconsistent locking: `IsTriggered` called inside the lock in `List`, outside it in `Create` |
| 58, 85 | Stringly-typed direction, compared by string in two separate places |
| 69 | `CreatedAtAction` passes the alert as *route values* to a parameterless action — `Location` comes back as `/api/alerts`, missing `/{id}` (verified) |
| — | No `threshold > 0` validation — `{"threshold": -5}` is accepted with `201` (verified) |

### Project structure

No `.sln`, no `global.json`, no `.editorconfig`, **no backend test project**, and zero
`PackageReference` entries in the `.csproj`. Models are nested records inside a controller; there is
no `Models/` or `Services/` folder and no service layer at all — the HTTP call to Xe lives directly
in the controller action.

---

## 4. Frontend issues

| File:line | Issue |
| --- | --- |
| `App.vue:5-12` | `fetch` with no `.catch`, no `res.ok` check, no loading or error state — a backend 500 leaves the board at `...` forever |
| `App.vue:14-39` | Three duplicated linear-scan getters, differing only in the pair string |
| `App.vue:58,64,70` | Those getters are called *from the template*, so they re-run on every render instead of being `computed` |
| `App.vue:56-72` | Hardcoded card markup rather than `v-for` — the UI cannot display a pair the backend adds |
| `App.vue:45` | `defineExpose({ loadRates })` exists purely as a test backdoor |
| `App.vue:79-159` | Global unscoped `<style>` inside a component |
| `state.ts:5` | `rates: [] as any[]` — defeats the strict TS config; no `Rate` or `Alert` type exists anywhere |
| `main.ts:4` | Pinia installed and registered, but **no store is ever defined** — dead wiring |
| `vite.config.ts:9` | Backend URL hardcoded, no env var |
| `App.test.ts` | One monolithic test asserting ~10 unrelated things; mixes `@testing-library/vue` and `@vue/test-utils`; uses `setTimeout(0)` instead of `flushPromises`; mutates `rates[1].rate` at L28-30 and never asserts the result; mocks the component's own state module rather than the network boundary |
| — | No API client layer — `fetch('/api/rates')` is a string literal inline in the component |
| — | No polling or auto-refresh, yet the header advertises "Last updated", implying a liveness the app lacks |

### Repo hygiene

`frontend/package-lock.json` was deleted in commit `cf91055`. Installs are therefore
non-reproducible, and `npm ci` — which any CI workflow will want — cannot run at all. I regenerated
it during verification; it needs committing.

`.gitignore` covers the essentials but misses `.env`, `TestResults/`, and `coverage/`.

---

## 5. Two planted TODOs

Both look like deliberate invitations from the authors:

- `vite.config.ts:15` — *"TODO(for Senior+ candidates): improve tests setup with coverage and other improvements?"*
- `main.ts:5` — *"TODO: move app state into a proper store at some point"*

**I intend to decline both**, and to say so rather than silently ignore them. On the backend track,
depth belongs in the alert domain and its tests; a 7-line `reactive` object is adequate state
management for one component and one panel, and adding Pinia to satisfy a TODO would be ceremony.
Reasons go in `NOTES.md` — an unexplained omission looks like an oversight, an explained one is a
decision.

---

## 6. Disposition

### Fixing — because the alert feature depends on it

- Rate fetching rebuilt as an async, batched, cancellable service behind `IRateProvider`
- The two silent-200 failure modes from §2.2 and §2.3 — these are real bugs, and they become tests
- Error handling and ProblemDetails, so upstream failures degrade instead of 500-ing
- A real alert data model, an `IAlertStore`, and a pure `AlertEvaluator` that can actually be tested
- Response caching, because the upstream is metered
- Typed frontend models and a thin API client — needed to display `triggered` at all

### Leaving deliberately — with reasons

- **Both planted TODOs** — see §5
- **`App.test.ts`'s structure** — it passes and it is not my core logic; my testing effort belongs on
  the backend for this track
- **Committed API credentials in `appsettings.json`** — supplied that way on purpose. In real life:
  user-secrets locally, environment injection in deployment. Rotating a key the graders need would
  be actively unhelpful
- **Unscoped global CSS** — the brief says CSS is not being judged
- **No auth, no multi-user, no rate limiting** — out of scope for a 2-3 hour exercise with no user
  concept

### Cannot fix within the constraints

- **The mock API returning 1.2345 for everything** — worked around with a fake provider for local
  demo (§2.1), not solved

---

## 7. The design question I expect to be asked

**Should `triggered` be stored on the alert, or computed at read time?**

I am computing it. An alert is a *rule*; whether it currently fires is a function of (rule, current
rate) evaluated when asked. Storing a boolean means owning its staleness — every read has to ask
"was this written recently enough to trust?"

The cost is real and I would not hide it: there is no trigger *history*, so I cannot answer "when
did this first fire?" and I cannot notify anyone, because nothing runs when no one is looking. The
moment this feature needs to send an email, the computed-only model stops being sufficient.

The extension is a background `IHostedService` evaluating on a timer and writing `LastTriggeredAt`
plus a transition log — at which point the stored state has a clear owner and a clear write path,
which is exactly what it lacks today. That is the version I would build with more time, and it is in
`NOTES.md` as next steps.
