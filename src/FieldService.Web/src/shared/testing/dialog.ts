interface DialogStandIn {
  showModal(this: HTMLDialogElement): void
  close(this: HTMLDialogElement, returnValue?: string): void
}

const modalDialogs = new WeakSet<HTMLDialogElement>()

const standIn: DialogStandIn = {
  showModal() {
    if (this.open) {
      return
    }
    this.open = true
    modalDialogs.add(this)
  },
  close() {
    if (!this.open) {
      return
    }
    this.open = false
    modalDialogs.delete(this)
    this.dispatchEvent(new Event('close'))
  },
}

let installed = false

export function installDialogStandIn(): void {
  if (typeof HTMLDialogElement === 'undefined' || 'showModal' in HTMLDialogElement.prototype) {
    return
  }
  Object.defineProperty(HTMLDialogElement.prototype, 'showModal', {
    configurable: true,
    writable: true,
    value: standIn.showModal,
  })
  Object.defineProperty(HTMLDialogElement.prototype, 'close', {
    configurable: true,
    writable: true,
    value: standIn.close,
  })
  installed = true
}

export function removeDialogStandIn(): void {
  if (!installed) {
    return
  }
  Reflect.deleteProperty(HTMLDialogElement.prototype, 'showModal')
  Reflect.deleteProperty(HTMLDialogElement.prototype, 'close')
  installed = false
}

export function isModal(dialog: HTMLDialogElement): boolean {
  return dialog.open && modalDialogs.has(dialog)
}

export function pressEscape(dialog: HTMLDialogElement): void {
  const keydown = new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true })
  dialog.dispatchEvent(keydown)
  if (keydown.defaultPrevented) {
    return
  }
  const cancel = new Event('cancel', { cancelable: true })
  dialog.dispatchEvent(cancel)
  if (!cancel.defaultPrevented) {
    dialog.close()
  }
}

export function clickBackdrop(dialog: HTMLDialogElement): void {
  dialog.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }))
}
