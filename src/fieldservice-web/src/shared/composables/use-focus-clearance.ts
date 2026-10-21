import { onBeforeUnmount, onMounted, type ShallowRef } from 'vue'

export const focusClearanceMargin = 32

let clearanceSheet: CSSStyleSheet | null = null

function canAdoptStyleSheets(): boolean {
  return typeof CSSStyleSheet !== 'undefined' && 'adoptedStyleSheets' in document
}

export function setScrollPaddingTop(pixels: number | null): void {
  if (!canAdoptStyleSheets()) {
    return
  }
  if (pixels === null) {
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
  clearanceSheet.replaceSync(`:root { scroll-padding-top: ${Math.ceil(pixels)}px; }`)
}

export function useFocusClearance(target: Readonly<ShallowRef<HTMLElement | null>>): void {
  let observer: ResizeObserver | null = null

  function update(): void {
    const height = target.value?.getBoundingClientRect().height ?? 0
    setScrollPaddingTop(height > 0 ? height + focusClearanceMargin : null)
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
    setScrollPaddingTop(null)
  })
}
