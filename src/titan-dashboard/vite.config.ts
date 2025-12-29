import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// https://vite.dev/config/
export default defineConfig({
  // Base path for gateway routing - all assets prefixed with /dashboard
  base: '/dashboard',
  plugins: [react()],
  server: {
    // Allow Aspire's JavaScript app hosting proxy
    allowedHosts: ['host.docker.internal'],
  },
})
