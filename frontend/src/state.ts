import { reactive } from 'vue'

// quick and dirty shared state, works fine for now
export const state = reactive({
  rates: [] as any[],
  lastUpdated: '',
})
