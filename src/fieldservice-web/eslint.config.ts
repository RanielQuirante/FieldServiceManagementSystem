import js from '@eslint/js'
import pluginVitest from '@vitest/eslint-plugin'
import { defineConfig, globalIgnores } from 'eslint/config'
import skipFormatting from 'eslint-config-prettier/flat'
import pluginPlaywright from 'eslint-plugin-playwright'
import pluginVue from 'eslint-plugin-vue'
import tseslint from 'typescript-eslint'
import vueParser from 'vue-eslint-parser'

const specFiles = ['src/**/__tests__/*.spec.ts']
const endToEndFiles = ['e2e/**/*.ts']

const impliedEvalGlobals = {
  global: 'readonly',
  self: 'readonly',
  setInterval: 'readonly',
  setTimeout: 'readonly',
  window: 'readonly',
} as const

export default defineConfig(
  globalIgnores([
    'dist/**',
    'coverage/**',
    'playwright-report/**',
    'test-results/**',
    'src/shared/api/schema.gen.ts',
  ]),
  {
    name: 'app/files-to-lint',
    files: ['**/*.{ts,mts,cts,vue}'],
    languageOptions: {
      globals: impliedEvalGlobals,
    },
  },
  js.configs.recommended,
  tseslint.configs.recommended,
  pluginVue.configs['flat/essential'],
  {
    name: 'app/vue-script-typescript',
    files: ['**/*.vue'],
    languageOptions: {
      parser: vueParser,
      parserOptions: {
        parser: tseslint.parser,
        sourceType: 'module',
      },
    },
  },
  {
    ...tseslint.configs.eslintRecommended,
    name: 'app/vue-script-core-rules',
    files: ['**/*.vue'],
  },
  {
    name: 'app/rules',
    rules: {
      'no-console': 'error',
      'no-eval': 'error',
      'no-implied-eval': 'error',
      'vue/no-v-html': 'error',
      'vue/no-static-inline-styles': 'error',
      'vue/multi-word-component-names': ['error', { ignores: ['Avatar', 'Pagination', 'Toast'] }],
    },
  },
  {
    name: 'app/error-handling-console',
    files: ['src/app/error-handling.ts'],
    rules: {
      'no-console': 'off',
    },
  },
  {
    ...pluginVitest.configs.recommended,
    name: 'app/vitest',
    files: specFiles,
  },
  {
    name: 'app/vitest-suite-guards',
    files: specFiles,
    rules: {
      'vitest/no-focused-tests': 'error',
      'vitest/no-disabled-tests': 'error',
    },
  },
  {
    ...pluginPlaywright.configs['flat/recommended'],
    name: 'app/playwright',
    files: endToEndFiles,
  },
  {
    name: 'app/playwright-suite-guards',
    files: endToEndFiles,
    rules: {
      'playwright/no-focused-test': 'error',
      'playwright/no-skipped-test': 'error',
      'playwright/no-wait-for-timeout': 'error',
      'playwright/no-networkidle': 'error',
      'playwright/no-force-option': 'error',
      'playwright/prefer-web-first-assertions': 'error',
      'playwright/no-conditional-in-test': 'error',
    },
  },
  skipFormatting,
)
