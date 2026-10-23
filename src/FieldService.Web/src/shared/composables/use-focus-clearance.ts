import { onBeforeUnmount, onMounted, type ShallowRef } from 'vue'

export const focusClearanceMargin = 32

export type MainEndPadding = '--spacing-fs-56' | '--spacing-fs-48'

let clearanceSheet: CSSStyleSheet | null = null

function canAdoptStyleSheets(): boolean {
  return typeof CSSStyleSheet !== 'undefined' && 'adoptedStyleSheets' in document
}

export function clearanceRules(pixels: number, mainEndPadding: MainEndPadding): string {
  const clearance = Math.ceil(pixels)
  return [
    `:root { scroll-padding-bottom: ${clearance}px; }`,
    `#main { padding-bottom: max(var(${mainEndPadding}), ${clearance}px); }`,
  ].join('\n')
}

export function setToastClearance(
  pixels: number | null,
  mainEndPadding: MainEndPadding = '--spacing-fs-56',
): void {
  if (!canAdoptStyleSheets()) {
    return
  }
  if (pixels === null || !Number.isFinite(pixels)) {
    const sheet = clearanceSheet
    if (sheet !== null) {
      document.adoptedStyleSheets = document.adoptedStyleSheets.filter((item) => item !== sheet)
      clearanceSheet = null
    }
    return
  }
  if (clearanceSheet === null) {
    clearanceSheet = new CSSStyleSheet()
    document.adoptedStyleSheets = [...document.adoptedStyleSheets, clearanceSheet]
  }
  clearanceSheet.replaceSync(clearanceRules(pixels, mainEndPadding))
}

export function useFocusClearance(
  target: Readonly<ShallowRef<HTMLElement | null>>,
  mainEndPadding: MainEndPadding,
): void {
  let observer: ResizeObserver | null = null

  function update(): void {
    const height = target.value?.getBoundingClientRect().height ?? 0
    setToastClearance(height > 0 ? height + focusClearanceMargin : null, mainEndPadding)
  }

  onMounted(() => {
    const element = target.value
    if (element === null || typeof ResizeObserver === 'undefined') {
      return
    }
    observer = new ResizeObserver(update)
    observer.observe(element)
  })

  onBeforeUnmount(() => {
    observer?.disconnect()
    observer = null
    setToastClearance(null)
  })
}
