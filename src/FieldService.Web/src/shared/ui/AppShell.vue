<script setup lang="ts">
import { computed, useTemplateRef } from 'vue'
import { RouterLink, useRoute, useRouter, type RouteLocationRaw } from 'vue-router'

import { useCurrentPageTitle } from '@/shared/composables/use-page-title'
import { isJobNumberLabel } from '@/shared/format/job-number'

import AppButton from './AppButton.vue'
import AppIcon from './AppIcon.vue'
import Avatar from './Avatar.vue'
import IconButton from './IconButton.vue'
import type { IconName } from './icons'
import ToastHost from './ToastHost.vue'

interface NavigationItem {
  readonly section: 'dashboard' | 'jobs' | 'technicians'
  readonly routeName: string
  readonly label: string
  readonly icon: IconName
}

withDefaults(
  defineProps<{
    userName: string
    userRole: string
    signingOut?: boolean
  }>(),
  { signingOut: false },
)

const emit = defineEmits<{
  'sign-out': []
}>()

const route = useRoute()
const router = useRouter()

const navigationItems: readonly NavigationItem[] = [
  { section: 'dashboard', routeName: 'dashboard', label: 'Dashboard', icon: 'grid' },
  { section: 'jobs', routeName: 'jobs', label: 'Jobs', icon: 'list' },
  { section: 'technicians', routeName: 'technicians', label: 'Technicians', icon: 'users' },
]

const availableItems = computed(() =>
  navigationItems.filter((item) => router.hasRoute(item.routeName)),
)

const homeLocation: RouteLocationRaw = '/'

const currentTitle = useCurrentPageTitle()

const parentCrumbs = computed(() => route.meta.breadcrumbs ?? [])

const currentCrumbIsJobNumber = computed(
  () => currentTitle.value !== null && isJobNumberLabel(currentTitle.value),
)

const showsNewJob = computed(
  () => route.meta.newJobAction !== false && router.hasRoute('job-create'),
)

const main = useTemplateRef<HTMLElement>('main')

function skipToMain(): void {
  main.value?.focus()
}

function isActive(item: NavigationItem): boolean {
  return route.meta.section === item.section
}
</script>

<template>
  <div class="app-shell">
    <a class="app-shell__skip" href="#main" @click.prevent="skipToMain">Skip to main content</a>
    <nav class="app-shell__sidebar" aria-label="Main">
      <RouterLink class="app-shell__brand" :to="homeLocation">
        <span class="app-shell__mark" aria-hidden="true">
          <svg
            width="20"
            height="20"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            stroke-width="2"
            stroke-linecap="round"
            stroke-linejoin="round"
            aria-hidden="true"
            focusable="false"
          >
            <path d="M12 21s-7-6.2-7-11a7 7 0 0 1 14 0c0 4.8-7 11-7 11z" />
            <circle class="app-shell__lantern" cx="12" cy="10" r="2.75" />
          </svg>
        </span>
        <span class="app-shell__wordmark">
          <span class="app-shell__name">FieldService</span>
          <span class="visually-hidden">{{ ' ' }}</span>
          <span class="app-shell__product">Admin</span>
        </span>
      </RouterLink>
      <ul class="app-shell__nav">
        <li v-for="item in availableItems" :key="item.routeName">
          <RouterLink
            :to="{ name: item.routeName }"
            :class="['app-shell__link', { 'app-shell__link--active': isActive(item) }]"
            :aria-current="isActive(item) ? 'page' : undefined"
          >
            <AppIcon class="app-shell__link-icon" :name="item.icon" :size="20" />{{ item.label }}
          </RouterLink>
        </li>
      </ul>
      <div class="app-shell__account">
        <Avatar :name="userName" :size="36" self />
        <span class="app-shell__identity">
          <span class="app-shell__user">{{ userName }}</span>
          <span class="app-shell__role">{{ userRole }}</span>
        </span>
        <IconButton label="Sign out" icon="log-out" :busy="signingOut" @click="emit('sign-out')" />
      </div>
    </nav>
    <div class="app-shell__body">
      <header class="app-shell__topbar">
        <nav class="app-shell__breadcrumb" aria-label="Breadcrumb">
          <ol class="app-shell__crumbs">
            <li v-for="crumb in parentCrumbs" :key="crumb.label" class="app-shell__crumb">
              <RouterLink class="app-shell__crumb-link" :to="crumb.to">{{
                crumb.label
              }}</RouterLink>
              <AppIcon class="app-shell__crumb-separator" name="chevron-right" :size="16" />
            </li>
            <li v-if="currentTitle" class="app-shell__crumb">
              <span
                :class="[
                  'app-shell__crumb-current',
                  { 'app-shell__crumb-current--mono': currentCrumbIsJobNumber },
                ]"
                aria-current="page"
                >{{ currentTitle }}</span
              >
            </li>
          </ol>
        </nav>
        <div class="app-shell__actions">
          <div v-if="$slots.status" class="app-shell__status">
            <slot name="status" />
          </div>
          <AppButton
            v-if="showsNewJob"
            class="app-shell__new-job"
            :to="{ name: 'job-create' }"
            icon="plus"
            >New job</AppButton
          >
        </div>
      </header>
      <main id="main" ref="main" class="app-shell__main" tabindex="-1">
        <slot name="notice" />
        <div class="app-shell__content">
          <slot />
        </div>
      </main>
    </div>
    <ToastHost placement="shell" />
  </div>
</template>

<style scoped>
.app-shell {
  display: flex;
  flex-wrap: wrap;
  align-content: flex-start;
  min-height: 100vh;
  container-type: inline-size;
}

.app-shell__skip {
  position: absolute;
  top: var(--spacing-fs-12);
  left: var(--spacing-fs-12);
  z-index: 20;
  padding: var(--spacing-fs-10) var(--spacing-fs-16);
  border-radius: var(--radius-fs-control);
  background: var(--color-fs-surface);
  box-shadow: var(--shadow-fs-float);
  color: var(--color-fs-teal-700);
  font-size: var(--text-fs-button);
  font-weight: 600;
  text-decoration: none;
  transform: translateY(-200%);
}

.app-shell__skip:focus,
.app-shell__skip:focus-visible {
  transform: none;
}

.app-shell__sidebar {
  display: flex;
  flex: 1 1 248px;
  flex-direction: column;
  gap: var(--spacing-fs-28);
  padding: var(--spacing-fs-24) var(--spacing-fs-16);
  border-right: 1px solid var(--fs-glass-sidebar-border);
  background: var(--fs-glass-sidebar-bg);
  -webkit-backdrop-filter: var(--fs-glass-sidebar-filter);
  backdrop-filter: var(--fs-glass-sidebar-filter);
  box-shadow: var(--shadow-fs-sidebar);
}

.app-shell__brand {
  display: flex;
  align-items: center;
  gap: var(--spacing-fs-12);
  padding: 0 var(--spacing-fs-8);
  border-radius: var(--radius-fs-control);
  text-decoration: none;
}

.app-shell__mark {
  flex: none;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 36px;
  height: 36px;
  border-radius: var(--radius-fs-logo);
  background: var(--fs-gloss-logo), var(--fs-gradient-primary);
  box-shadow: var(--shadow-fs-logo);
  color: var(--color-fs-on-dark);
}

.app-shell__lantern {
  fill: var(--color-fs-amber-300);
  stroke: none;
}

.app-shell__wordmark {
  display: flex;
  flex-direction: column;
  gap: 3px;
  min-width: 0;
}

.app-shell__name {
  color: var(--color-fs-ink);
  font-size: 1.0625rem;
  font-weight: 600;
  line-height: 1.1;
  letter-spacing: -0.02em;
}

.app-shell__product {
  color: var(--color-fs-teal-700);
  font-size: 0.6875rem;
  font-weight: 600;
  line-height: 1;
  letter-spacing: 0.14em;
  text-transform: uppercase;
}

.app-shell__nav {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-fs-4);
  margin: 0;
  padding: 0;
  list-style: none;
}

.app-shell__link {
  display: flex;
  align-items: center;
  gap: var(--spacing-fs-12);
  min-height: 2.75rem;
  padding: 0 var(--spacing-fs-12);
  border: 1px solid transparent;
  border-radius: var(--radius-fs-control);
  color: var(--color-fs-ink-2);
  font-size: var(--text-fs-table);
  font-weight: 500;
  text-decoration: none;
  transition: background-color var(--fs-duration-hover);
}

.app-shell__link:hover {
  background: var(--color-fs-quiet-hover);
}

.app-shell__link-icon {
  color: var(--color-fs-muted);
}

.app-shell__link--active,
.app-shell__link--active:hover {
  background: var(--fs-gloss), var(--fs-gradient-primary);
  box-shadow: var(--shadow-fs-primary);
  color: var(--color-fs-on-dark);
  font-weight: 600;
}

.app-shell__link--active .app-shell__link-icon {
  color: currentColor;
}

.app-shell__account {
  display: flex;
  align-items: center;
  gap: var(--spacing-fs-12);
  margin-top: auto;
  padding: var(--spacing-fs-12);
  border-radius: var(--radius-fs-banner);
  background: var(--color-fs-account-bg);
  box-shadow: var(--shadow-fs-tile);
}

.app-shell__identity {
  display: flex;
  flex: 1;
  flex-direction: column;
  gap: 2px;
  min-width: 0;
}

.app-shell__user {
  color: var(--color-fs-ink);
  font-size: var(--text-fs-table);
  font-weight: 600;
  line-height: 1.2;
  overflow-wrap: anywhere;
}

.app-shell__role {
  color: var(--color-fs-muted);
  font-size: var(--text-fs-meta);
  line-height: 1.2;
}

.app-shell__body {
  display: flex;
  flex: 999 1 560px;
  flex-direction: column;
  min-width: 0;
}

.app-shell__topbar {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: var(--spacing-fs-12) var(--spacing-fs-16);
  min-height: 4rem;
  padding: var(--spacing-fs-12) clamp(16px, 3vw, 40px);
  border-bottom: 1px solid var(--fs-glass-topbar-border);
  background: var(--fs-glass-topbar-bg);
  -webkit-backdrop-filter: var(--fs-glass-topbar-filter);
  backdrop-filter: var(--fs-glass-topbar-filter);
  box-shadow: var(--shadow-fs-topbar);
}

.app-shell__breadcrumb {
  min-width: 0;
}

.app-shell__crumbs {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: var(--spacing-fs-8);
  margin: 0;
  padding: 0;
  font-size: var(--text-fs-table);
  line-height: 1.2;
  list-style: none;
}

.app-shell__crumb {
  display: flex;
  align-items: center;
  gap: var(--spacing-fs-8);
  min-width: 0;
}

.app-shell__crumb-link {
  color: var(--color-fs-teal-700);
  font-weight: 500;
  text-decoration: none;
}

.app-shell__crumb-link:hover {
  text-decoration: underline;
}

.app-shell__crumb-separator {
  color: var(--color-fs-line-input);
}

.app-shell__crumb-current {
  color: var(--color-fs-ink);
  font-weight: 600;
  overflow-wrap: anywhere;
}

.app-shell__crumb-current--mono {
  font-family: var(--font-mono);
  font-size: 0.8125rem;
  font-weight: 500;
  font-variant-numeric: tabular-nums;
}

.app-shell__actions {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: var(--spacing-fs-12);
}

.app-shell__status {
  display: flex;
  align-items: center;
}

@container (width < 600px) {
  .app-shell__topbar {
    display: grid;
    grid-template-columns: minmax(0, 1fr) auto;
    align-items: center;
  }

  .app-shell__actions {
    display: contents;
  }

  .app-shell__breadcrumb {
    grid-row: 1;
    grid-column: 1;
  }

  .app-shell__new-job {
    grid-row: 1;
    grid-column: 2;
  }

  .app-shell__status {
    grid-row: 2;
    grid-column: 1 / -1;
    justify-self: start;
  }
}

.app-shell__main {
  position: relative;
  display: flex;
  flex: 1;
  flex-direction: column;
  padding: var(--spacing-fs-28) clamp(16px, 3vw, 40px) var(--spacing-fs-56);
}

.app-shell__main:focus {
  outline: none;
}

.app-shell__content {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-fs-24);
  width: 100%;
  max-width: 80rem;
  margin: 0 auto;
}

@container (width >= 808px) {
  .app-shell__sidebar {
    position: sticky;
    top: 0;
    align-self: flex-start;
    height: 100dvh;
    overflow-y: auto;
  }
}

@supports not ((backdrop-filter: blur(1px)) or (-webkit-backdrop-filter: blur(1px))) {
  .app-shell__sidebar,
  .app-shell__topbar {
    background: var(--fs-glass-g3-bg);
  }
}

@media (prefers-reduced-transparency: reduce) {
  .app-shell__sidebar,
  .app-shell__topbar {
    background: var(--color-fs-surface);
    -webkit-backdrop-filter: none;
    backdrop-filter: none;
  }
}

@media (forced-colors: active) {
  .app-shell__sidebar {
    border-right-color: CanvasText;
  }

  .app-shell__topbar {
    border-bottom-color: CanvasText;
  }

  .app-shell__link--active {
    border-color: Highlight;
  }

  .app-shell__lantern {
    fill: CanvasText;
  }
}
</style>
