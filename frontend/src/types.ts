export interface RateDto {
  pair: string
  rate: number
  asOf: string
}

export type AlertDirection = 'above' | 'below'

export type EvaluationStatus = 'ok' | 'rate_unavailable'

export interface AlertDto {
  id: string
  pair: string
  threshold: number
  direction: AlertDirection
  triggered: boolean
  currentRate: number | null
  asOf: string | null
  status: EvaluationStatus
}

export interface CreateAlertRequest {
  pair: string
  threshold: number
  direction: AlertDirection
}
