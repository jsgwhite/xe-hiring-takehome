<script setup lang="ts">
import { onMounted } from 'vue'
import { state } from './state'

function loadRates() {
  fetch('/api/rates')
    .then((r) => r.json())
    .then((data) => {
      state.rates = data
      state.lastUpdated = new Date().toLocaleTimeString()
    })
}

function getUsdCad() {
  for (let i = 0; i < state.rates.length; i++) {
    if (state.rates[i].pair === 'USD/CAD') {
      return state.rates[i].rate.toFixed(4)
    }
  }
  return '...'
}

function getGbpUsd() {
  for (let i = 0; i < state.rates.length; i++) {
    if (state.rates[i].pair === 'GBP/USD') {
      return state.rates[i].rate.toFixed(4)
    }
  }
  return '...'
}

function getEurUsd() {
  for (let i = 0; i < state.rates.length; i++) {
    if (state.rates[i].pair === 'EUR/USD') {
      return state.rates[i].rate.toFixed(4)
    }
  }
  return '...'
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
      <div class="card">
        <div class="pair">USD / CAD</div>
        <div class="rate">{{ getUsdCad() }}</div>
        <div class="caption">1 US dollar in Canadian dollars</div>
      </div>

      <div class="card">
        <div class="pair">GBP / USD</div>
        <div class="rate">{{ getGbpUsd() }}</div>
        <div class="caption">1 British pound in US dollars</div>
      </div>

      <div class="card">
        <div class="pair">EUR / USD</div>
        <div class="rate">{{ getEurUsd() }}</div>
        <div class="caption">1 euro in US dollars</div>
      </div>
    </section>

    <button class="refresh" @click="loadRates()">Refresh rates</button>
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
