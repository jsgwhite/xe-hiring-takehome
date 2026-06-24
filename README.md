# Rate Alerts

A small app that displays live exchange rates, backed by the Xe Currency Data API. Your take-home brief explains what to build on top of it.

## Prerequisites

- .NET 10 SDK
- Node.js 20 or newer

## Run it

Backend (from the repo root):

```
cd backend
dotnet run
```

The API starts on `http://localhost:5180`. API credentials are already configured in `backend/appsettings.json`; you do not need to sign up for anything.

Frontend (in a second terminal):

```
cd frontend
npm install
npm run dev
```

The app runs on `http://localhost:5173` and proxies `/api` calls to the backend, so start the backend first.

That's it. If both commands run, you are ready to work.

## Git

This folder is already a git repository with one initial commit. Work directly in it and **commit as you go with clear, honest messages**; your commit history is part of your submission. When you are done, zip the whole project folder **including the hidden `.git` directory** (but excluding `node_modules`, `bin`, and `obj`) and send it back as described in the brief.

## What's here

- `backend/` - a .NET Web API. `GET /api/rates` fetches live rates for three currency pairs from the Xe Currency Data API.
- `frontend/` - a Vue 3 + TypeScript app (Vite) that displays those rates.

Xe Currency Data API documentation, if you need it: https://xecdapi.xe.com/docs/v1/

## Alert stub API (frontend track)

If you are on the frontend track, build the alert UI against these endpoints, already running in the backend. They keep alerts in memory and evaluate the `triggered` flag against fixed stub rates (USD/CAD 1.3650, GBP/USD 1.2710, EUR/USD 1.0830), so an alert created on the wrong side of those values will show as triggered.

| Method | Path | Body | Returns |
| --- | --- | --- | --- |
| GET | `/api/alerts` | - | `[{ id, pair, threshold, direction, triggered }]` |
| POST | `/api/alerts` | `{ "pair": "USD/CAD", "threshold": 1.30, "direction": "above" }` | the created alert, `201` |
| DELETE | `/api/alerts/{id}` | - | `204`, or `404` if unknown |

`pair` must be one of the three stub pairs; `direction` is `above` or `below`.

Backend-track candidates: the stub lives in `backend/Controllers/AlertsStubController.cs` and is not a partial solution. Replace it or delete it; the alert feature is yours to design.

## Notes

- Alerts in the stub do not survive a restart. That is intentional.
- If a rate shows as `...` and never resolves, check the backend terminal for errors and that it is running on port 5180.
