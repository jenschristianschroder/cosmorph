import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'

// The Observatory is served by the API container, so development proxies same-origin API calls.
export default defineConfig({
  plugins: [react()],
  build: {
    outDir: 'dist',
    sourcemap: false,
  },
  server: {
    proxy: {
      '/api': 'http://127.0.0.1:5199',
    },
  },
  test: {
    environment: 'jsdom',
    include: ['src/**/*.test.ts', 'src/**/*.test.tsx'],
  },
})
