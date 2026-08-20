import { mount, flushPromises } from '@vue/test-utils'
import { beforeEach, describe, expect, test, vi } from 'vitest'

// App.vue imports the shared state module directly, so it has to be mocked to control the board.
// Replacing this with a real store is a deliberate non-goal for this track - see NOTES.md.
const fakeState = vi.hoisted(() => ({
  rates: [] as Array<{ pair: string; rate: number }>,
  lastUpdated: '',
}))
vi.mock('./state', () => ({ state: fakeState }))

import App from './App.vue'

function stubFetch(payload: unknown) {
  const spy = vi.fn(() => Promise.resolve({ ok: true, json: () => Promise.resolve(payload) }))
  ;(globalThis as any).fetch = spy
  return spy
}

beforeEach(() => {
  fakeState.rates = []
  fakeState.lastUpdated = ''
  vi.restoreAllMocks()
})

describe('rate board rendering', () => {
  // These assertions pin the behaviour the board has today, so that swapping the three hardcoded
  // cards for a v-for can be verified as a refactor rather than a rewrite.

  test('renders a card for each of the three displayed pairs', () => {
    stubFetch([])
    fakeState.rates = [
      { pair: 'USD/CAD', rate: 1.3 },
      { pair: 'GBP/USD', rate: 1.25 },
      { pair: 'EUR/USD', rate: 1.1 },
    ]

    const wrapper = mount(App)

    expect(wrapper.findAll('.card')).toHaveLength(3)
    expect(wrapper.text()).toContain('USD / CAD')
    expect(wrapper.text()).toContain('GBP / USD')
    expect(wrapper.text()).toContain('EUR / USD')
  })

  test('formats rates to four decimal places', () => {
    stubFetch([])
    fakeState.rates = [{ pair: 'USD/CAD', rate: 1.3 }]

    const wrapper = mount(App)

    expect(wrapper.text()).toContain('1.3000')
  })

  test('shows a placeholder for a pair missing from state', () => {
    stubFetch([])
    fakeState.rates = [{ pair: 'USD/CAD', rate: 1.3 }]

    const wrapper = mount(App)
    const rates = wrapper.findAll('.rate').map((node) => node.text())

    // USD/CAD resolves; the other two have no data yet.
    expect(rates).toEqual(['1.3000', '...', '...'])
  })
})

describe('loading rates', () => {
  test('fetches the rates endpoint on mount and stores the response', async () => {
    const fetchSpy = stubFetch([{ pair: 'USD/CAD', rate: 1.3712 }])

    mount(App)
    await flushPromises()

    expect(fetchSpy).toHaveBeenCalledWith('/api/rates')
    expect(fakeState.rates).toEqual([{ pair: 'USD/CAD', rate: 1.3712 }])
  })

  test('records a last-updated timestamp after a successful load', async () => {
    stubFetch([{ pair: 'USD/CAD', rate: 1.3712 }])

    mount(App)
    await flushPromises()

    expect(fakeState.lastUpdated).not.toBe('')
  })

  test('re-fetches when the refresh button is clicked', async () => {
    const fetchSpy = stubFetch([{ pair: 'USD/CAD', rate: 1.3712 }])

    const wrapper = mount(App)
    await flushPromises()
    expect(fetchSpy).toHaveBeenCalledTimes(1)

    await wrapper.get('.refresh').trigger('click')
    await flushPromises()

    expect(fetchSpy).toHaveBeenCalledTimes(2)
  })

  test('does not show updated timestamp when lastUpdated is empty', () => {
    stubFetch([])
    fakeState.lastUpdated = ''

    const wrapper = mount(App)

    expect(wrapper.find('.updated').exists()).toBe(false)
  })

  test('shows updated timestamp when lastUpdated is present', () => {
    stubFetch([])
    fakeState.lastUpdated = '12:34:56 PM'

    const wrapper = mount(App)

    expect(wrapper.find('.updated').exists()).toBe(true)
    expect(wrapper.find('.updated').text()).toContain('Last updated 12:34:56 PM')
  })
})
