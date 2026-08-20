import { reactive } from 'vue'
import type { RateDto } from './types'

// quick and dirty shared state, works fine for now
export const state = reactive({
  rates: [] as RateDto[],
  lastUpdated: '',
})
