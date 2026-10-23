import { computed, onBeforeUnmount, shallowRef, watchEffect, type ComputedRef } from 'vue'
import { useRoute } from 'vue-router'

export const applicationName = 'FieldService Admin'

const titleOverride = shallowRef<string | null>(null)

export function formatDocumentTitle(title: string | null | undefined): string {
  return title === null || title === undefined || title.length === 0
    ? applicationName
    : `${title} · ${applicationName}`
}

export function useCurrentPageTitle(): ComputedRef<string | null> {
  const route = useRoute()
  return computed(() => titleOverride.value ?? route.meta.title ?? null)
}

export function usePageTitle(
  title: () => string | null,
  documentTitle: () => string | null = () => null,
): void {
  const route = useRoute()
  watchEffect(() => {
    const value = title()
    titleOverride.value = value
    document.title = formatDocumentTitle(documentTitle() ?? value ?? route.meta.title)
  })
  onBeforeUnmount(() => {
    titleOverride.value = null
  })
}

export function resetPageTitle(): void {
  titleOverride.value = null
}
