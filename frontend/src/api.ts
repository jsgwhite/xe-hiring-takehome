import type { AlertDto, CreateAlertRequest, RateDto } from './types'

/// Extracts a message from either the alerts endpoints' `{ error }` shape or the RFC 9110
/// ProblemDetails `{ title }` shape ASP.NET Core's own model validation returns (e.g. a null body).
async function readErrorMessage(response: Response): Promise<string> {
  const body = await response.json().catch(() => null)
  return body?.error ?? body?.title ?? `Request failed with status ${response.status}`
}

async function handle<T>(response: Response): Promise<T> {
  if (!response.ok) {
    throw new Error(await readErrorMessage(response))
  }
  return response.json()
}

export function getRates(): Promise<RateDto[]> {
  return fetch('/api/rates').then((response) => handle<RateDto[]>(response))
}

export function getAlerts(): Promise<AlertDto[]> {
  return fetch('/api/alerts').then((response) => handle<AlertDto[]>(response))
}

export function createAlert(request: CreateAlertRequest): Promise<AlertDto> {
  return fetch('/api/alerts', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(request),
  }).then((response) => handle<AlertDto>(response))
}

export async function deleteAlert(id: string): Promise<void> {
  const response = await fetch(`/api/alerts/${id}`, { method: 'DELETE' })
  if (!response.ok) {
    throw new Error(await readErrorMessage(response))
  }
}
