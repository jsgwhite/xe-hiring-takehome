import { mount, flushPromises } from '@vue/test-utils'
import { beforeEach, describe, expect, test, vi } from 'vitest'
import AlertsPanel from './AlertsPanel.vue'

vi.mock('../api', () => ({
  getAlerts: vi.fn(),
  createAlert: vi.fn(),
  deleteAlert: vi.fn(),
}))

import { getAlerts, createAlert, deleteAlert } from '../api'

const mockGetAlerts = vi.mocked(getAlerts)
const mockCreateAlert = vi.mocked(createAlert)
const mockDeleteAlert = vi.mocked(deleteAlert)

beforeEach(() => {
  vi.clearAllMocks()
})

describe('AlertsPanel', () => {
  describe('loading and rendering alerts', () => {
    test('shows a loading state on mount', () => {
      mockGetAlerts.mockImplementation(() => new Promise(() => {})) // Never resolves

      const wrapper = mount(AlertsPanel)

      expect(wrapper.text()).toContain('Loading alerts...')
    })

    test('renders alerts returned by getAlerts on mount', async () => {
      mockGetAlerts.mockResolvedValue([
        {
          id: '1',
          pair: 'USD/CAD',
          threshold: 1.35,
          direction: 'above',
          triggered: false,
          currentRate: 1.36,
          asOf: '2024-01-01T10:00:00Z',
          status: 'ok',
        },
      ])

      const wrapper = mount(AlertsPanel)
      await flushPromises()

      expect(wrapper.text()).toContain('USD/CAD')
      expect(wrapper.text()).toContain('above 1.35')
    })

    test('shows empty state when getAlerts returns empty array', async () => {
      mockGetAlerts.mockResolvedValue([])

      const wrapper = mount(AlertsPanel)
      await flushPromises()

      expect(wrapper.text()).toContain('No alerts yet')
    })

    test('calls getAlerts on mount', async () => {
      mockGetAlerts.mockResolvedValue([])

      mount(AlertsPanel)
      await flushPromises()

      expect(mockGetAlerts).toHaveBeenCalled()
    })

    test('shows an error, not the empty state, when getAlerts fails', async () => {
      // A failed load must not look like "you have zero alerts" - those are different situations.
      mockGetAlerts.mockRejectedValue(new Error('upstream is down'))

      const wrapper = mount(AlertsPanel)
      await flushPromises()

      expect(wrapper.text()).toContain('upstream is down')
      expect(wrapper.text()).not.toContain('No alerts yet')
    })
  })

  describe('alert status display', () => {
    test('shows triggered badge for alert with triggered: true', async () => {
      mockGetAlerts.mockResolvedValue([
        {
          id: '1',
          pair: 'GBP/USD',
          threshold: 1.5,
          direction: 'above',
          triggered: true,
          currentRate: 1.51,
          asOf: '2024-01-01T10:00:00Z',
          status: 'ok',
        },
      ])

      const wrapper = mount(AlertsPanel)
      await flushPromises()

      expect(wrapper.text()).toContain('TRIGGERED')
    })

    test('does not show triggered badge for alert with triggered: false', async () => {
      mockGetAlerts.mockResolvedValue([
        {
          id: '1',
          pair: 'GBP/USD',
          threshold: 1.5,
          direction: 'above',
          triggered: false,
          currentRate: 1.49,
          asOf: '2024-01-01T10:00:00Z',
          status: 'ok',
        },
      ])

      const wrapper = mount(AlertsPanel)
      await flushPromises()

      expect(wrapper.find('.status-triggered').exists()).toBe(false)
      expect(wrapper.text()).toContain('OK')
    })

    test('shows rate unavailable indicator for status: rate_unavailable', async () => {
      mockGetAlerts.mockResolvedValue([
        {
          id: '1',
          pair: 'EUR/USD',
          threshold: 1.1,
          direction: 'above',
          triggered: false,
          currentRate: null,
          asOf: null,
          status: 'rate_unavailable',
        },
      ])

      const wrapper = mount(AlertsPanel)
      await flushPromises()

      expect(wrapper.text()).toContain('rate unavailable')
    })

    test('shows current rate when available', async () => {
      mockGetAlerts.mockResolvedValue([
        {
          id: '1',
          pair: 'GBP/CAD',
          threshold: 1.8,
          direction: 'above',
          triggered: false,
          currentRate: 1.85,
          asOf: '2024-01-01T10:00:00Z',
          status: 'ok',
        },
      ])

      const wrapper = mount(AlertsPanel)
      await flushPromises()

      expect(wrapper.text()).toContain('last checked: 1.85')
    })
  })

  describe('creating alerts', () => {
    test('submitting form with valid inputs calls createAlert with correctly-joined pair', async () => {
      mockGetAlerts.mockResolvedValue([])
      mockCreateAlert.mockResolvedValue({
        id: '2',
        pair: 'GBP/CAD',
        threshold: 1.84,
        direction: 'above',
        triggered: false,
        currentRate: 1.85,
        asOf: '2024-01-01T10:00:00Z',
        status: 'ok',
      })

      const wrapper = mount(AlertsPanel)
      await flushPromises()

      const inputs = wrapper.findAll('input[type="text"]')
      await inputs[0].setValue('gbp')
      await inputs[1].setValue('cad')
      await wrapper.vm.$nextTick()

      const numberInput = wrapper.find('input[type="number"]')
      await numberInput.setValue('1.84')
      await wrapper.vm.$nextTick()

      await wrapper.find('form').trigger('submit')
      await flushPromises()

      expect(mockCreateAlert).toHaveBeenCalledWith({
        pair: 'GBP/CAD',
        threshold: 1.84,
        direction: 'above',
      })
    })

    test('uppercases currency codes when creating alert', async () => {
      mockGetAlerts.mockResolvedValue([])
      mockCreateAlert.mockResolvedValue({
        id: '2',
        pair: 'EUR/USD',
        threshold: 1.1,
        direction: 'below',
        triggered: false,
        currentRate: 1.09,
        asOf: '2024-01-01T10:00:00Z',
        status: 'ok',
      })

      const wrapper = mount(AlertsPanel)
      await flushPromises()

      const inputs = wrapper.findAll('input[type="text"]')
      await inputs[0].setValue('eUr')
      await inputs[1].setValue('UsD')
      await wrapper.vm.$nextTick()

      const numberInput = wrapper.find('input[type="number"]')
      await numberInput.setValue('1.1')
      await wrapper.vm.$nextTick()

      const select = wrapper.find('select')
      ;(select.element as HTMLSelectElement).value = 'below'
      await select.trigger('change')
      await wrapper.vm.$nextTick()

      await wrapper.find('form').trigger('submit')
      await flushPromises()

      expect(mockCreateAlert).toHaveBeenCalledWith({
        pair: 'EUR/USD',
        threshold: 1.1,
        direction: 'below',
      })
    })

    test('does not call createAlert with invalid base currency (not 3 letters)', async () => {
      mockGetAlerts.mockResolvedValue([])

      const wrapper = mount(AlertsPanel)
      await flushPromises()

      const inputs = wrapper.findAll('input[type="text"]')
      await inputs[0].setValue('gb')
      await inputs[1].setValue('cad')
      await wrapper.vm.$nextTick()

      const numberInput = wrapper.find('input[type="number"]')
      await numberInput.setValue('1.84')
      await wrapper.vm.$nextTick()

      await wrapper.find('form').trigger('submit')
      await wrapper.vm.$nextTick()

      expect(mockCreateAlert).not.toHaveBeenCalled()
      expect(wrapper.text()).toContain('Base currency must be exactly 3 letters')
    })

    test('does not call createAlert with invalid quote currency (not 3 letters)', async () => {
      mockGetAlerts.mockResolvedValue([])

      const wrapper = mount(AlertsPanel)
      await flushPromises()

      const inputs = wrapper.findAll('input[type="text"]')
      await inputs[0].setValue('gbp')
      await inputs[1].setValue('ca')
      await wrapper.vm.$nextTick()

      const numberInput = wrapper.find('input[type="number"]')
      await numberInput.setValue('1.84')
      await wrapper.vm.$nextTick()

      await wrapper.find('form').trigger('submit')
      await wrapper.vm.$nextTick()

      expect(mockCreateAlert).not.toHaveBeenCalled()
      expect(wrapper.text()).toContain('Quote currency must be exactly 3 letters')
    })

    test('does not call createAlert with non-positive threshold', async () => {
      mockGetAlerts.mockResolvedValue([])

      const wrapper = mount(AlertsPanel)
      await flushPromises()

      const inputs = wrapper.findAll('input[type="text"]')
      await inputs[0].setValue('gbp')
      await inputs[1].setValue('cad')
      await wrapper.vm.$nextTick()

      const numberInput = wrapper.find('input[type="number"]')
      await numberInput.setValue('0')
      await wrapper.vm.$nextTick()

      await wrapper.find('form').trigger('submit')
      await wrapper.vm.$nextTick()

      expect(mockCreateAlert).not.toHaveBeenCalled()
      expect(wrapper.text()).toContain('Threshold must be a positive number')
    })

    test('does not call createAlert with empty threshold', async () => {
      mockGetAlerts.mockResolvedValue([])

      const wrapper = mount(AlertsPanel)
      await flushPromises()

      const inputs = wrapper.findAll('input[type="text"]')
      await inputs[0].setValue('gbp')
      await inputs[1].setValue('cad')
      await wrapper.vm.$nextTick()

      await wrapper.find('input[type="number"]').setValue('')

      await wrapper.find('form').trigger('submit')
      await wrapper.vm.$nextTick()

      expect(mockCreateAlert).not.toHaveBeenCalled()
      expect(wrapper.text()).toContain('Threshold must be a positive number')
    })

    test('adds created alert to list and restores the default form on success', async () => {
      mockGetAlerts.mockResolvedValue([])
      mockCreateAlert.mockResolvedValue({
        id: '2',
        pair: 'USD/CAD',
        threshold: 1.35,
        direction: 'above',
        triggered: false,
        currentRate: 1.36,
        asOf: '2024-01-01T10:00:00Z',
        status: 'ok',
      })

      const wrapper = mount(AlertsPanel)
      await flushPromises()

      const inputs = wrapper.findAll('input[type="text"]')
      await inputs[0].setValue('usd')
      await inputs[1].setValue('cad')
      await wrapper.vm.$nextTick()

      const numberInput = wrapper.find('input[type="number"]')
      await numberInput.setValue('1.35')
      await wrapper.vm.$nextTick()

      await wrapper.find('form').trigger('submit')
      await flushPromises()

      expect(wrapper.text()).toContain('USD/CAD')
      expect((inputs[0].element as HTMLInputElement).value).toBe('USD')
      expect((inputs[1].element as HTMLInputElement).value).toBe('CAD')
      expect((numberInput.element as HTMLInputElement).value).toBe('1.3')
    })

    test('shows error message on createAlert failure', async () => {
      mockGetAlerts.mockResolvedValue([])
      mockCreateAlert.mockRejectedValue(new Error('Threshold already exists for this pair'))

      const wrapper = mount(AlertsPanel)
      await flushPromises()

      const inputs = wrapper.findAll('input[type="text"]')
      await inputs[0].setValue('gbp')
      await inputs[1].setValue('usd')
      await wrapper.vm.$nextTick()

      const numberInput = wrapper.find('input[type="number"]')
      await numberInput.setValue('1.5')
      await wrapper.vm.$nextTick()

      await wrapper.find('form').trigger('submit')
      await flushPromises()

      expect(wrapper.text()).toContain('Threshold already exists for this pair')
    })

    test('does not clear form on createAlert failure', async () => {
      mockGetAlerts.mockResolvedValue([])
      mockCreateAlert.mockRejectedValue(new Error('Invalid pair'))

      const wrapper = mount(AlertsPanel)
      await flushPromises()

      const inputs = wrapper.findAll('input[type="text"]')
      await inputs[0].setValue('gbp')
      await inputs[1].setValue('usd')

      const numberInput = wrapper.find('input[type="number"]')
      await numberInput.setValue('1.5')

      await wrapper.find('button.submit-btn').trigger('click')
      await flushPromises()

      expect((inputs[0].element as HTMLInputElement).value).toBe('gbp')
      expect((inputs[1].element as HTMLInputElement).value).toBe('usd')
      expect((numberInput.element as HTMLInputElement).value).toBe('1.5')
    })

    test('does not add anything to list on createAlert failure', async () => {
      mockGetAlerts.mockResolvedValue([])
      mockCreateAlert.mockRejectedValue(new Error('Invalid pair'))

      const wrapper = mount(AlertsPanel)
      await flushPromises()

      const inputs = wrapper.findAll('input[type="text"]')
      await inputs[0].setValue('gbp')
      await inputs[1].setValue('usd')

      const numberInput = wrapper.find('input[type="number"]')
      await numberInput.setValue('1.5')

      await wrapper.find('button.submit-btn').trigger('click')
      await flushPromises()

      expect(wrapper.findAll('.alert-row')).toHaveLength(0)
    })
  })

  describe('deleting alerts', () => {
    test('clicking delete button calls deleteAlert with alert id', async () => {
      mockGetAlerts.mockResolvedValue([
        {
          id: '1',
          pair: 'USD/CAD',
          threshold: 1.35,
          direction: 'above',
          triggered: false,
          currentRate: 1.36,
          asOf: '2024-01-01T10:00:00Z',
          status: 'ok',
        },
      ])
      mockDeleteAlert.mockResolvedValue(undefined)

      const wrapper = mount(AlertsPanel)
      await flushPromises()

      await wrapper.find('.delete-btn').trigger('click')
      await flushPromises()

      expect(mockDeleteAlert).toHaveBeenCalledWith('1')
    })

    test('removes alert from list on deleteAlert success', async () => {
      mockGetAlerts.mockResolvedValue([
        {
          id: '1',
          pair: 'USD/CAD',
          threshold: 1.35,
          direction: 'above',
          triggered: false,
          currentRate: 1.36,
          asOf: '2024-01-01T10:00:00Z',
          status: 'ok',
        },
      ])
      mockDeleteAlert.mockResolvedValue(undefined)

      const wrapper = mount(AlertsPanel)
      await flushPromises()

      expect(wrapper.findAll('.alert-row')).toHaveLength(1)

      await wrapper.find('.delete-btn').trigger('click')
      await flushPromises()

      expect(wrapper.findAll('.alert-row')).toHaveLength(0)
      expect(wrapper.text()).toContain('No alerts yet')
    })

    test('shows error message on deleteAlert failure', async () => {
      mockGetAlerts.mockResolvedValue([
        {
          id: '1',
          pair: 'USD/CAD',
          threshold: 1.35,
          direction: 'above',
          triggered: false,
          currentRate: 1.36,
          asOf: '2024-01-01T10:00:00Z',
          status: 'ok',
        },
      ])
      mockDeleteAlert.mockRejectedValue(new Error('Failed to delete alert'))

      const wrapper = mount(AlertsPanel)
      await flushPromises()

      await wrapper.find('.delete-btn').trigger('click')
      await flushPromises()

      expect(wrapper.text()).toContain('Failed to delete alert')
    })

    test('keeps alert in list on deleteAlert failure', async () => {
      mockGetAlerts.mockResolvedValue([
        {
          id: '1',
          pair: 'USD/CAD',
          threshold: 1.35,
          direction: 'above',
          triggered: false,
          currentRate: 1.36,
          asOf: '2024-01-01T10:00:00Z',
          status: 'ok',
        },
      ])
      mockDeleteAlert.mockRejectedValue(new Error('Failed to delete'))

      const wrapper = mount(AlertsPanel)
      await flushPromises()

      await wrapper.find('.delete-btn').trigger('click')
      await flushPromises()

      expect(wrapper.findAll('.alert-row')).toHaveLength(1)
    })

    test('multiple alerts can be deleted independently', async () => {
      mockGetAlerts.mockResolvedValue([
        {
          id: '1',
          pair: 'USD/CAD',
          threshold: 1.35,
          direction: 'above',
          triggered: false,
          currentRate: 1.36,
          asOf: '2024-01-01T10:00:00Z',
          status: 'ok',
        },
        {
          id: '2',
          pair: 'GBP/USD',
          threshold: 1.5,
          direction: 'below',
          triggered: false,
          currentRate: 1.49,
          asOf: '2024-01-01T10:00:00Z',
          status: 'ok',
        },
      ])
      mockDeleteAlert.mockResolvedValue(undefined)

      const wrapper = mount(AlertsPanel)
      await flushPromises()

      expect(wrapper.findAll('.alert-row')).toHaveLength(2)

      const deleteButtons = wrapper.findAll('.delete-btn')
      await deleteButtons[0].trigger('click')
      await flushPromises()

      expect(wrapper.findAll('.alert-row')).toHaveLength(1)
      expect(wrapper.text()).toContain('GBP/USD')
      expect(wrapper.text()).not.toContain('USD/CAD')
    })
  })

  describe('form direction selection', () => {
    test('changes direction based on the threshold relative to the selected current rate', async () => {
      mockGetAlerts.mockResolvedValue([])

      const wrapper = mount(AlertsPanel)
      await flushPromises()

      wrapper.vm.prefillAlert('GBP/USD', 1.25)
      const numberInput = wrapper.find('input[type="number"]')
      const select = wrapper.find('select')

      await numberInput.setValue('1.3')
      expect((select.element as HTMLSelectElement).value).toBe('above')

      await numberInput.setValue('1.2')
      expect((select.element as HTMLSelectElement).value).toBe('below')
    })

    test('respects the direction select value when creating alert', async () => {
      mockGetAlerts.mockResolvedValue([])
      mockCreateAlert.mockResolvedValue({
        id: '2',
        pair: 'EUR/USD',
        threshold: 1.1,
        direction: 'below',
        triggered: false,
        currentRate: 1.09,
        asOf: '2024-01-01T10:00:00Z',
        status: 'ok',
      })

      const wrapper = mount(AlertsPanel)
      await flushPromises()

      const inputs = wrapper.findAll('input[type="text"]')
      await inputs[0].setValue('eur')
      await inputs[1].setValue('usd')
      await wrapper.vm.$nextTick()

      const numberInput = wrapper.find('input[type="number"]')
      await numberInput.setValue('1.1')
      await wrapper.vm.$nextTick()

      const select = wrapper.find('select')
      ;(select.element as HTMLSelectElement).value = 'below'
      await select.trigger('change')
      await wrapper.vm.$nextTick()

      await wrapper.find('form').trigger('submit')
      await flushPromises()

      expect(mockCreateAlert).toHaveBeenCalledWith({
        pair: 'EUR/USD',
        threshold: 1.1,
        direction: 'below',
      })
    })
  })
})
