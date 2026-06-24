import { createApp } from 'vue'
import { createPinia } from 'pinia'
import App from './App.vue'

// TODO: move app state into a proper store at some point
createApp(App).use(createPinia()).mount('#app')
