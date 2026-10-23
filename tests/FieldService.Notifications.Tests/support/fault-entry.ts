const [fault, valueKind, marker = 'fault raised by the test entry', nestedValue = marker] =
  process.argv.slice(2)

await import('../../../src/FieldService.Notifications/src/main.ts')

function failureValue(): unknown {
  if (valueKind === 'error') {
    return new Error(marker)
  }
  if (valueKind === 'error-with-properties') {
    return Object.assign(new Error(marker), { handshake: { auth: { ticket: nestedValue } } })
  }
  if (valueKind === 'nested-object') {
    return { handshake: { auth: { ticket: marker } } }
  }
  throw new Error(`unknown failure value ${String(valueKind)}`)
}

const value = failureValue()

if (fault === 'uncaught-exception') {
  setImmediate(() => {
    throw value
  })
} else if (fault === 'unhandled-rejection') {
  void Promise.reject(value)
} else {
  throw new Error(`unknown fault ${String(fault)}`)
}
