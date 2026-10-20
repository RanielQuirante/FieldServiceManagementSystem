import { fileURLToPath } from 'node:url'

import { configDefaults, defineConfig, mergeConfig } from 'vitest/config'

import viteConfig from './vite.config.ts'

export default mergeConfig(
  viteConfig,
  defineConfig({
    test: {
      environment: 'jsdom',
      root: fileURLToPath(new URL('./', import.meta.url)),
      include: ['src/**/__tests__/*.spec.ts'],
      exclude: [...configDefaults.exclude, 'e2e/**'],
      setupFiles: ['src/shared/testing/setup.ts'],
      coverage: {
        provider: 'v8',
      },
    },
  }),
)
