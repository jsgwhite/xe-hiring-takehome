<script setup lang="ts">
import { ref, onMounted } from 'vue'
import type { AlertDto, CreateAlertRequest, AlertDirection } from '../types'
import { getAlerts, createAlert, deleteAlert } from '../api'

interface FormState {
  base: string
  quote: string
  threshold: number | string
  direction: AlertDirection
}

const alerts = ref<AlertDto[]>([])
const loading = ref(true)
const form = ref<FormState>({
  base: '',
  quote: '',
  threshold: '',
  direction: 'above',
})
const formError = ref('')
const validationError = ref('')
const loadError = ref('')
const deleteErrors = ref<Record<string, string>>({})

function isValidCurrencyCode(code: string): boolean {
  return /^[A-Za-z]{3}$/.test(code)
}

function isValidThreshold(value: string): boolean {
  const num = parseFloat(value)
  return !isNaN(num) && num > 0
}

function validateForm(): boolean {
  validationError.value = ''
  const base = form.value.base.trim()
  const quote = form.value.quote.trim()
  const threshold = String(form.value.threshold).trim()

  if (!isValidCurrencyCode(base)) {
    validationError.value = 'Base currency must be exactly 3 letters'
    return false
  }

  if (!isValidCurrencyCode(quote)) {
    validationError.value = 'Quote currency must be exactly 3 letters'
    return false
  }

  if (!isValidThreshold(threshold)) {
    validationError.value = 'Threshold must be a positive number'
    return false
  }

  return true
}

async function handleCreateAlert() {
  formError.value = ''

  if (!validateForm()) {
    return
  }

  const request: CreateAlertRequest = {
    pair: `${form.value.base.toUpperCase()}/${form.value.quote.toUpperCase()}`,
    threshold: parseFloat(String(form.value.threshold)),
    direction: form.value.direction,
  }

  try {
    const newAlert = await createAlert(request)
    alerts.value.push(newAlert)
    form.value = { base: '', quote: '', threshold: '', direction: 'above' }
    validationError.value = ''
  } catch (error) {
    formError.value = error instanceof Error ? error.message : 'Failed to create alert'
  }
}

async function handleDeleteAlert(id: string) {
  deleteErrors.value[id] = ''

  try {
    await deleteAlert(id)
    alerts.value = alerts.value.filter((a) => a.id !== id)
  } catch (error) {
    deleteErrors.value[id] = error instanceof Error ? error.message : 'Failed to delete alert'
  }
}

onMounted(async () => {
  try {
    alerts.value = await getAlerts()
  } catch (error) {
    // Distinct from the empty state below - "the list failed to load" and "there are genuinely
    // zero alerts" must not look the same, or a backend outage reads as "you have no alerts".
    loadError.value = error instanceof Error ? error.message : 'Failed to load alerts'
  } finally {
    loading.value = false
  }
})
</script>

<template>
  <section class="alerts">
    <h2>Rate Alerts</h2>

    <div class="content">
      <!-- Create Form Section -->
      <div class="create-form">
        <h3>Create New Alert</h3>

        <form @submit.prevent="handleCreateAlert">
          <div class="form-row">
            <label>
              <span class="label-text">Base currency</span>
              <input
                v-model="form.base"
                type="text"
                placeholder="GBP"
                maxlength="3"
              />
            </label>

            <label>
              <span class="label-text">Quote currency</span>
              <input
                v-model="form.quote"
                type="text"
                placeholder="CAD"
                maxlength="3"
              />
            </label>
          </div>

          <div class="form-row">
            <label>
              <span class="label-text">Threshold</span>
              <input
                v-model="form.threshold"
                type="number"
                placeholder="1.84"
                step="0.0001"
                min="0"
                style="flex: 1"
              />
            </label>

            <label>
              <span class="label-text">Direction</span>
              <select v-model="form.direction">
                <option value="above">above</option>
                <option value="below">below</option>
              </select>
            </label>
          </div>

          <button type="submit" class="submit-btn">Create alert</button>

          <div v-if="validationError" class="error-message">{{ validationError }}</div>
          <div v-if="formError" class="error-message">{{ formError }}</div>
        </form>
      </div>

      <!-- Alerts List Section -->
      <div class="alerts-list">
        <h3>Your Alerts</h3>

        <div v-if="loading" class="loading">Loading alerts...</div>

        <div v-else-if="loadError" class="error-message">{{ loadError }}</div>

        <div v-else-if="alerts.length === 0" class="empty-state">
          No alerts yet. Create one above to get started.
        </div>

        <div v-else class="alert-rows">
          <div v-for="alert in alerts" :key="alert.id" class="alert-row">
            <div class="alert-info">
              <div class="alert-pair">{{ alert.pair }}</div>
              <div class="alert-details">
                <span class="threshold">{{ alert.direction }} {{ alert.threshold }}</span>
                <span v-if="alert.currentRate !== null" class="current-rate">
                  current: {{ alert.currentRate }}
                </span>
              </div>
              <div class="alert-status">
                <span v-if="alert.status === 'rate_unavailable'" class="status-unavailable">
                  rate unavailable
                </span>
                <span v-else-if="alert.triggered" class="status-triggered">TRIGGERED</span>
                <span v-else class="status-ok">OK</span>
              </div>
            </div>

            <button class="delete-btn" @click="handleDeleteAlert(alert.id)">Delete</button>

            <div v-if="deleteErrors[alert.id]" class="error-message delete-error">
              {{ deleteErrors[alert.id] }}
            </div>
          </div>
        </div>
      </div>
    </div>
  </section>
</template>

<style scoped>
.alerts {
  margin-top: 40px;
  padding: 0;
}

h2 {
  font-size: 1.4rem;
  margin: 0 0 24px 0;
  font-weight: 600;
}

h3 {
  font-size: 1rem;
  margin: 0 0 16px 0;
  font-weight: 600;
  color: #1a2233;
}

.content {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 32px;
}

@media (max-width: 768px) {
  .content {
    grid-template-columns: 1fr;
  }
}

/* Create Form Section */
.create-form {
  background: #ffffff;
  border: 1px solid #e1e6ee;
  border-radius: 10px;
  padding: 20px;
}

.form-row {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 12px;
  margin-bottom: 16px;
}

label {
  display: flex;
  flex-direction: column;
}

.label-text {
  font-size: 0.85rem;
  font-weight: 500;
  color: #66718a;
  margin-bottom: 6px;
}

input,
select {
  padding: 8px 12px;
  border: 1px solid #e1e6ee;
  border-radius: 6px;
  font-size: 0.9rem;
  font-family: inherit;
  background: #ffffff;
  color: #1a2233;
}

input:focus,
select:focus {
  outline: none;
  border-color: #16345c;
  box-shadow: 0 0 0 2px rgba(22, 52, 92, 0.1);
}

.submit-btn {
  width: 100%;
  padding: 10px 16px;
  margin-top: 8px;
  border: none;
  border-radius: 6px;
  background: #16345c;
  color: #ffffff;
  font-size: 0.9rem;
  font-weight: 500;
  cursor: pointer;
}

.submit-btn:hover {
  background: #1d4377;
}

.error-message {
  margin-top: 12px;
  padding: 10px 12px;
  border-radius: 6px;
  background: #fee;
  color: #c00;
  font-size: 0.85rem;
}

.delete-error {
  margin-top: 0;
}

/* Alerts List Section */
.alerts-list {
  background: #ffffff;
  border: 1px solid #e1e6ee;
  border-radius: 10px;
  padding: 20px;
}

.loading {
  text-align: center;
  padding: 40px 20px;
  color: #66718a;
  font-size: 0.9rem;
}

.empty-state {
  text-align: center;
  padding: 40px 20px;
  color: #66718a;
  font-size: 0.9rem;
}

.alert-rows {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.alert-row {
  padding: 12px;
  border: 1px solid #e1e6ee;
  border-radius: 6px;
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  gap: 12px;
}

.alert-info {
  flex: 1;
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.alert-pair {
  font-weight: 600;
  color: #1a2233;
  font-size: 0.95rem;
}

.alert-details {
  display: flex;
  gap: 12px;
  font-size: 0.85rem;
  color: #66718a;
}

.threshold {
  font-weight: 500;
}

.alert-status {
  display: flex;
  gap: 6px;
  margin-top: 4px;
}

.status-triggered {
  padding: 2px 8px;
  background: #c00;
  color: #ffffff;
  border-radius: 4px;
  font-size: 0.75rem;
  font-weight: 600;
}

.status-unavailable {
  padding: 2px 8px;
  background: #f90;
  color: #ffffff;
  border-radius: 4px;
  font-size: 0.75rem;
  font-weight: 600;
}

.status-ok {
  padding: 2px 8px;
  background: #0b0;
  color: #ffffff;
  border-radius: 4px;
  font-size: 0.75rem;
  font-weight: 600;
}

.delete-btn {
  padding: 6px 12px;
  border: 1px solid #e1e6ee;
  border-radius: 4px;
  background: #ffffff;
  color: #c00;
  font-size: 0.8rem;
  cursor: pointer;
  white-space: nowrap;
}

.delete-btn:hover {
  background: #fee;
  border-color: #c00;
}
</style>
