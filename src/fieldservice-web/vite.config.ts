import { fileURLToPath, URL } from 'node:url'

import vue from '@vitejs/plugin-vue'
import { defineConfig, type ProxyOptions } from 'vite'

const host = '127.0.0.1'
const port = 5173

const apiProxyTarget = process.env.API_PROXY_TARGET ?? 'http://127.0.0.1:5080'
const realtimeProxyTarget = process.env.REALTIME_PROXY_TARGET ?? 'http://127.0.0.1:5090'

const securityHeaders: Record<string, string> = {
  'Content-Security-Policy':
    "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; font-src 'self'; connect-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'",
  'X-Content-Type-Options': 'nosniff',
  'X-Frame-Options': 'DENY',
  'Referrer-Policy': 'no-referrer',
  'Permissions-Policy':
    'camera=(), microphone=(), geolocation=(), accelerometer=(), gyroscope=(), magnetometer=(), payment=(), usb=()',
  'Cross-Origin-Opener-Policy': 'same-origin',
  'Cross-Origin-Resource-Policy': 'same-origin',
}

function createProxy(): Record<string, ProxyOptions> {
  return {
    '/api': {
      target: apiProxyTarget,
      changeOrigin: true,
      xfwd: true,
    },
    '/socket.io': {
      target: realtimeProxyTarget,
      ws: true,
      changeOrigin: true,
    },
  }
}

export default defineConfig({
  plugins: [vue()],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  server: {
    host,
    port,
    strictPort: true,
    proxy: createProxy(),
  },
  preview: {
    host,
    port,
    strictPort: true,
    proxy: createProxy(),
    headers: securityHeaders,
  },
})
