import { render } from '@testing-library/vue'
import { mount } from '@vue/test-utils'
import { vi, test, expect } from 'vitest'

const fakeState = vi.hoisted(() => ({
  rates: [
    { pair: 'USD/CAD', rate: 1.3 },
    { pair: 'GBP/USD', rate: 1.25 },
    { pair: 'EUR/USD', rate: 1.1 },
  ] as any[],
  lastUpdated: '',
}))
vi.mock('./state', () => ({ state: fakeState }))

import App from './App.vue'

test('shows the cards and pokes state', async () => {
  const fetchSpy = vi.fn(() =>
    Promise.resolve({ ok: true, json: () => Promise.resolve([{ pair: 'USD/CAD', rate: 9.9 }]) }),
  )
  ;(globalThis as any).fetch = fetchSpy

  const { getByText, container } = render(App)

  expect(getByText('USD / CAD')).toBeTruthy()
  expect(getByText('1.3000')).toBeTruthy()

  fakeState.rates[1].rate = 1.2599
  await Promise.resolve()
  const rates = container.querySelectorAll('.rate')

  expect(getByText('GBP / USD')).toBeTruthy()
  expect(getByText('EUR / USD')).toBeTruthy()
  expect(getByText('1.1000')).toBeTruthy()
  expect(fetchSpy).toHaveBeenCalledWith('/api/rates')
  expect(rates.length).toBe(3)

  await new Promise((r) => setTimeout(r, 0))
  expect(fakeState.rates).toEqual([{ pair: 'USD/CAD', rate: 9.9 }])
  expect(fakeState.lastUpdated).not.toBe('')

  fakeState.lastUpdated = ''
  const fetchSpy2 = vi.fn(() =>
    Promise.resolve({ ok: true, json: () => Promise.resolve([{ pair: 'GBP/USD', rate: 7.7 }]) }),
  )
  ;(globalThis as any).fetch = fetchSpy2
  const wrapper = mount(App)
  ;(wrapper.vm as any).loadRates()
  await new Promise((r) => setTimeout(r, 0))
  expect(fakeState.rates).toEqual([{ pair: 'GBP/USD', rate: 7.7 }])
  expect(fakeState.lastUpdated).not.toBe('')
})
