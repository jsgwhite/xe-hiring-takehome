<script setup lang="ts">
import { onMounted } from 'vue'
import { getRates } from './api'
import AlertsPanel from './components/AlertsPanel.vue'
import { state } from './state'

// The rate board always shows these three cards, filling in "..." for any pair the backend didn't
// return (e.g. a partial upstream failure) - this list, not state.rates, drives what renders.
const DISPLAYED_PAIRS = [
  { pair: 'USD/CAD', label: 'USD / CAD', caption: '1 US dollar in Canadian dollars' },
  { pair: 'GBP/USD', label: 'GBP / USD', caption: '1 British pound in US dollars' },
  { pair: 'EUR/USD', label: 'EUR / USD', caption: '1 euro in US dollars' },
]

function loadRates() {
  getRates().then((data) => {
    state.rates = data
    state.lastUpdated = new Date().toLocaleTimeString()
  })
}

function formattedRate(pair: string) {
  const match = state.rates.find((rate) => rate.pair === pair)
  return match ? match.rate.toFixed(4) : '...'
}

onMounted(() => {
  loadRates()
})

defineExpose({ loadRates })
</script>

<template>
  <main class="page">
    <header class="header">
      <h1>Xe Rate Board</h1>
      <span class="updated" v-if="state.lastUpdated">Last updated {{ state.lastUpdated }}</span>
    </header>

    <section class="cards">
      <div class="card" v-for="item in DISPLAYED_PAIRS" :key="item.pair">
        <div class="pair">{{ item.label }}</div>
        <div class="rate">{{ formattedRate(item.pair) }}</div>
        <div class="caption">{{ item.caption }}</div>
      </div>
    </section>

    <button class="refresh" @click="loadRates()">Refresh rates</button>

    <AlertsPanel />
  </main>
</template>

<style>
* {
  box-sizing: border-box;
}

body {
  margin: 0;
  font-family: 'Segoe UI', system-ui, sans-serif;
  background: #f4f6f8;
  color: #1a2233;
}

.page {
  max-width: 860px;
  margin: 0 auto;
  padding: 32px 20px;
}

.header {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  margin-bottom: 24px;
}

h1 {
  font-size: 1.6rem;
  margin: 0;
}

.updated {
  font-size: 0.85rem;
  color: #66718a;
}

.cards {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
  gap: 16px;
}

.card {
  background: #ffffff;
  border: 1px solid #e1e6ee;
  border-radius: 10px;
  padding: 20px;
}

.pair {
  font-size: 0.9rem;
  font-weight: 600;
  color: #66718a;
  letter-spacing: 0.04em;
}

.rate {
  font-size: 2rem;
  font-weight: 700;
  margin: 8px 0 4px;
  font-variant-numeric: tabular-nums;
}

.caption {
  font-size: 0.8rem;
  color: #8a93a8;
}

.refresh {
  margin-top: 24px;
  padding: 10px 18px;
  border: none;
  border-radius: 8px;
  background: #16345c;
  color: #ffffff;
  font-size: 0.9rem;
  cursor: pointer;
}

.refresh:hover {
  background: #1d4377;
}
</style>
