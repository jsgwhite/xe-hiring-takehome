/// <reference types="vitest/config" />
import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

export default defineConfig({
  plugins: [vue()],
  server: {
    proxy: {
      '/api': 'http://localhost:5180',
    },
  },
  test: {
    environment: 'jsdom',
    globals: true,
    // TODO(for Senior+ candidates): improve tests setup with coverage and other improvements?
  },
})
