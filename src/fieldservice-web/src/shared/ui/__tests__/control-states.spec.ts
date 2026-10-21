import { mount, type VueWrapper } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { type Component } from 'vue'

import { installStyles, ruleDeclarations } from '@/app/styles/__tests__/stylesheet-source'

import AppButton from '../AppButton.vue'
import SelectField from '../SelectField.vue'
import TextArea from '../TextArea.vue'
import TextField from '../TextField.vue'

let removeStyles: (() => void) | null = null
let mounted: VueWrapper | null = null

afterEach(() => {
  mounted?.unmount()
  mounted = null
  removeStyles?.()
  removeStyles = null
})

function mountAttached(component: Component, props: Record<string, unknown>): VueWrapper {
  mounted = mount(component, { props, attachTo: document.body })
  return mounted
}

interface FieldCase {
  readonly name: string
  readonly file: string
  readonly component: Component
  readonly props: Record<string, unknown>
  readonly selector: string
}

const fieldCases: readonly FieldCase[] = [
  {
    name: 'TextField',
    file: 'shared/ui/TextField.vue',
    component: TextField,
    props: { label: 'Site address', id: 'site-address', error: 'Enter the site address.' },
    selector: 'input',
  },
  {
    name: 'TextArea',
    file: 'shared/ui/TextArea.vue',
    component: TextArea,
    props: { label: 'Description', id: 'job-description', error: 'Describe the work.' },
    selector: 'textarea',
  },
  {
    name: 'SelectField',
    file: 'shared/ui/SelectField.vue',
    component: SelectField,
    props: {
      label: 'Checklist template',
      id: 'checklist-template',
      options: [{ value: 'a', label: 'Boiler service' }],
      error: 'Choose a checklist template.',
    },
    selector: 'select',
  },
]

describe('fields in the error state', () => {
  it.each(fieldCases)(
    'show the focus values on a focused $name and the error values once it loses focus',
    ({ file, component, props, selector }) => {
      removeStyles = installStyles(file)
      const wrapper = mountAttached(component, props)
      const control = wrapper.find<HTMLElement>(selector).element

      expect(control.getAttribute('aria-invalid')).toBe('true')
      expect(getComputedStyle(control).boxShadow).toBe('var(--shadow-fs-input-error)')

      control.focus()
      expect(document.activeElement).toBe(control)
      expect(getComputedStyle(control).boxShadow).toBe('var(--shadow-fs-input-focus)')

      control.blur()
      expect(getComputedStyle(control).boxShadow).toBe('var(--shadow-fs-input-error)')
    },
  )

  it('keeps the whole date or time control focused while its picker button has focus', () => {
    expect(
      ruleDeclarations('shared/ui/TextField.vue', '.text-field__input:focus-within'),
    ).toMatchObject({
      'border-color': 'var(--color-fs-teal-700)',
      'box-shadow': 'var(--shadow-fs-input-focus)',
    })
    expect(
      ruleDeclarations(
        'shared/ui/TextField.vue',
        '.text-field__input--picker:focus-within:not(:focus) + .text-field__trailing',
      ),
    ).toEqual({
      'border-radius': '4px',
      outline: '2px solid var(--color-fs-focus)',
      'outline-offset': '2px',
      color: 'var(--color-fs-teal-700)',
    })
  })
})

describe('busy buttons', () => {
  beforeEach(() => {
    removeStyles = installStyles('shared/ui/AppButton.vue')
  })

  it.each([
    ['primary', 'var(--color-fs-on-dark)'],
    ['secondary', 'var(--color-fs-ink)'],
  ])('keep the %s colours and show progress while busy', (variant, color) => {
    const wrapper = mountAttached(AppButton, { variant, busy: true, busyLabel: 'Creating job…' })
    const style = getComputedStyle(wrapper.element)

    expect(wrapper.classes()).toContain('app-button--busy')
    expect(wrapper.attributes('aria-disabled')).toBe('true')
    expect(style.color).toBe(color)
    expect(style.cursor).toBe('progress')
  })

  it('use the disabled colours only when disabled', () => {
    const wrapper = mountAttached(AppButton, { disabled: true })
    const style = getComputedStyle(wrapper.element)

    expect(wrapper.classes()).not.toContain('app-button--busy')
    expect(style.color).toBe('var(--color-fs-disabled-text)')
    expect(style.cursor).toBe('not-allowed')
  })
})

describe('button icons', () => {
  beforeEach(() => {
    removeStyles = installStyles('shared/ui/AppButton.vue')
  })

  it('are teal in a secondary button, while the label keeps the ink colour', () => {
    const wrapper = mountAttached(AppButton, { variant: 'secondary', icon: 'list' })

    expect(getComputedStyle(wrapper.find('.app-icon').element).color).toBe(
      'var(--color-fs-teal-700)',
    )
    expect(getComputedStyle(wrapper.element).color).toBe('var(--color-fs-ink)')
  })

  it('follow the disabled colour in a disabled secondary button', () => {
    const wrapper = mountAttached(AppButton, { variant: 'secondary', icon: 'list', disabled: true })

    expect(getComputedStyle(wrapper.find('.app-icon').element).color).toBe(
      'var(--color-fs-disabled-text)',
    )
  })
})
