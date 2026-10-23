import assert from 'node:assert/strict'
import { describe, test } from 'node:test'
import { ConfigError, loadConfig } from '../../../src/FieldService.Notifications/src/config.ts'
import {
  createRequiredEnvironment,
  createSocketTicketKey,
  createWebhookSecret,
  readSignatureVector,
  readSocketTicketVector,
  type Environment,
} from '../support/fixtures.ts'

const configKeys = [
  'host',
  'logDir',
  'logLevel',
  'logPretty',
  'nodeEnv',
  'port',
  'socketAllowedOrigins',
  'socketSessionMaxSeconds',
  'socketTicketAudience',
  'socketTicketIssuer',
  'socketTicketKey',
  'webhookSecrets',
  'webhookToleranceSeconds',
]

const publishedTestKeyStarts = [0x00, 0x20, 0x40]

function bytes(length: number, valueAt: (index: number) => number): Buffer {
  return Buffer.from(Array.from({ length }, (_, index) => valueAt(index) % 256))
}

function base64Of(source: Buffer): string {
  return source.toString('base64')
}

function rejectionOf(environment: Environment): ConfigError {
  let caught: unknown
  try {
    loadConfig(environment)
  } catch (error) {
    caught = error
  }
  assert.ok(caught instanceof ConfigError, 'expected loadConfig to throw a ConfigError')
  return caught
}

function assertRejected(environment: Environment, variable: string, rule?: RegExp): ConfigError {
  const error = rejectionOf(environment)
  assert.deepEqual(
    error.issues.map((issue) => issue.variable),
    [variable],
  )
  assert.ok(error.message.includes(variable), `the message names ${variable}`)
  if (rule !== undefined) {
    assert.match(error.issues[0]?.rule ?? '', rule)
  }
  return error
}

function assertValueNotDisclosed(error: ConfigError, value: string): void {
  assert.ok(!error.message.includes(value), 'the message never contains the value')
  for (const issue of error.issues) {
    assert.ok(!issue.rule.includes(value), 'the rule never contains the value')
  }
}

describe('loadConfig defaults', () => {
  test('applies the documented defaults when only the required variables are set', () => {
    const config = loadConfig(createRequiredEnvironment())

    assert.equal(config.host, '127.0.0.1')
    assert.equal(config.port, 5090)
    assert.equal(config.nodeEnv, 'production')
    assert.equal(config.logLevel, 'info')
    assert.equal(config.logPretty, false)
    assert.equal(config.webhookToleranceSeconds, 300)
    assert.equal(config.socketTicketIssuer, 'fieldservice-api')
    assert.equal(config.socketTicketAudience, 'fieldservice-notifications')
    assert.equal(config.socketSessionMaxSeconds, 900)
  })

  test('treats empty and blank values as unset', () => {
    const config = loadConfig(
      createRequiredEnvironment({
        HOST: '',
        PORT: '   ',
        NODE_ENV: '',
        LOG_LEVEL: ' ',
        LOG_PRETTY: '',
        WEBHOOK_TOLERANCE_SECONDS: '',
        SOCKET_TICKET_ISSUER: '',
        SOCKET_TICKET_AUDIENCE: '  ',
        SOCKET_SESSION_MAX_SECONDS: '',
      }),
    )

    assert.equal(config.host, '127.0.0.1')
    assert.equal(config.port, 5090)
    assert.equal(config.nodeEnv, 'production')
    assert.equal(config.logLevel, 'info')
    assert.equal(config.logPretty, false)
    assert.equal(config.webhookToleranceSeconds, 300)
    assert.equal(config.socketTicketIssuer, 'fieldservice-api')
    assert.equal(config.socketTicketAudience, 'fieldservice-notifications')
    assert.equal(config.socketSessionMaxSeconds, 900)
  })

  test('reads every variable when each one is set', () => {
    const secret = randomSecretBytes()
    const key = randomSecretBytes()
    const config = loadConfig({
      HOST: '0.0.0.0',
      PORT: '5091',
      NODE_ENV: 'development',
      LOG_LEVEL: 'debug',
      LOG_DIR: '../../logs/notifications',
      LOG_PRETTY: 'true',
      WEBHOOK_SECRETS: `whsec_${base64Of(secret)}`,
      WEBHOOK_TOLERANCE_SECONDS: '30',
      SOCKET_TICKET_KEY: base64Of(key),
      SOCKET_TICKET_ISSUER: 'issuer-under-test',
      SOCKET_TICKET_AUDIENCE: 'audience-under-test',
      SOCKET_ALLOWED_ORIGINS: 'http://localhost:5173 https://fieldservice.example',
      SOCKET_SESSION_MAX_SECONDS: '60',
    })

    assert.equal(config.host, '0.0.0.0')
    assert.equal(config.port, 5091)
    assert.equal(config.nodeEnv, 'development')
    assert.equal(config.logLevel, 'debug')
    assert.equal(config.logDir, '../../logs/notifications')
    assert.equal(config.logPretty, true)
    assert.equal(config.webhookSecrets.length, 1)
    assert.deepEqual(Buffer.from(config.webhookSecrets[0] ?? []), secret)
    assert.equal(config.webhookToleranceSeconds, 30)
    assert.deepEqual(Buffer.from(config.socketTicketKey), key)
    assert.equal(config.socketTicketIssuer, 'issuer-under-test')
    assert.equal(config.socketTicketAudience, 'audience-under-test')
    assert.deepEqual(config.socketAllowedOrigins, [
      'http://localhost:5173',
      'https://fieldservice.example',
    ])
    assert.equal(config.socketSessionMaxSeconds, 60)
  })

  test('returns a frozen config with frozen lists', () => {
    const config = loadConfig(createRequiredEnvironment())

    assert.ok(Object.isFrozen(config))
    assert.ok(Object.isFrozen(config.webhookSecrets))
    assert.ok(Object.isFrozen(config.socketAllowedOrigins))
  })

  test('exposes the documented settings and no database setting', () => {
    const config = loadConfig(
      createRequiredEnvironment({ DATABASE_URL: 'postgres://ignored', PGPASSWORD: 'ignored' }),
    )

    assert.deepEqual(Object.keys(config).sort(), configKeys)
  })
})

describe('loadConfig required variables', () => {
  const requiredVariables = [
    'LOG_DIR',
    'WEBHOOK_SECRETS',
    'SOCKET_TICKET_KEY',
    'SOCKET_ALLOWED_ORIGINS',
  ]

  for (const variable of requiredVariables) {
    test(`rejects a missing ${variable}`, () => {
      assertRejected(createRequiredEnvironment({ [variable]: undefined }), variable, /is required/)
    })

    test(`rejects a blank ${variable}`, () => {
      assertRejected(createRequiredEnvironment({ [variable]: '   ' }), variable, /is required/)
    })
  }

  test('reports every invalid variable at once', () => {
    const error = rejectionOf(
      createRequiredEnvironment({
        PORT: 'not-a-port',
        WEBHOOK_SECRETS: undefined,
        SOCKET_SESSION_MAX_SECONDS: '0',
      }),
    )

    assert.deepEqual(error.issues.map((issue) => issue.variable).sort(), [
      'PORT',
      'SOCKET_SESSION_MAX_SECONDS',
      'WEBHOOK_SECRETS',
    ])
  })
})

describe('loadConfig numeric bounds', () => {
  const cases = [
    { variable: 'WEBHOOK_TOLERANCE_SECONDS', accepted: ['1', '300'], rejected: ['0', '301'] },
    { variable: 'SOCKET_SESSION_MAX_SECONDS', accepted: ['1', '3600'], rejected: ['0', '3601'] },
    { variable: 'PORT', accepted: ['0', '65535'], rejected: ['65536'] },
  ]

  for (const { variable, accepted, rejected } of cases) {
    for (const value of accepted) {
      test(`accepts ${variable}=${value}`, () => {
        assert.doesNotThrow(() => loadConfig(createRequiredEnvironment({ [variable]: value })))
      })
    }

    for (const value of [...rejected, '-1', '1.5', '12abc', 'ten']) {
      test(`rejects ${variable}=${value}`, () => {
        assertRejected(createRequiredEnvironment({ [variable]: value }), variable, /integer/)
      })
    }
  }

  test('keeps the tolerance at the value set', () => {
    const config = loadConfig(createRequiredEnvironment({ WEBHOOK_TOLERANCE_SECONDS: '1' }))

    assert.equal(config.webhookToleranceSeconds, 1)
  })
})

describe('loadConfig enumerations and hosts', () => {
  test('rejects an unknown NODE_ENV', () => {
    assertRejected(createRequiredEnvironment({ NODE_ENV: 'staging' }), 'NODE_ENV')
  })

  test('rejects an unknown LOG_LEVEL', () => {
    assertRejected(createRequiredEnvironment({ LOG_LEVEL: 'verbose' }), 'LOG_LEVEL')
  })

  test('accepts every pino level and silent for LOG_LEVEL', () => {
    for (const level of ['fatal', 'error', 'warn', 'info', 'debug', 'trace', 'silent']) {
      assert.equal(loadConfig(createRequiredEnvironment({ LOG_LEVEL: level })).logLevel, level)
    }
  })

  test('rejects a LOG_PRETTY value other than true or false', () => {
    assertRejected(createRequiredEnvironment({ LOG_PRETTY: 'yes' }), 'LOG_PRETTY')
  })

  test('honours LOG_PRETTY=true only in development', () => {
    const pretty = (nodeEnv: string): boolean =>
      loadConfig(createRequiredEnvironment({ NODE_ENV: nodeEnv, LOG_PRETTY: 'true' })).logPretty

    assert.equal(pretty('development'), true)
    assert.equal(pretty('test'), false)
    assert.equal(pretty('production'), false)
  })

  test('accepts IP literals and host names for HOST', () => {
    for (const host of ['127.0.0.1', '0.0.0.0', '::1', 'localhost', 'notifications.internal']) {
      assert.equal(loadConfig(createRequiredEnvironment({ HOST: host })).host, host)
    }
  })

  test('rejects a HOST that is neither an IP literal nor a host name', () => {
    const value = 'not a host!'
    const error = assertRejected(createRequiredEnvironment({ HOST: value }), 'HOST')

    assertValueNotDisclosed(error, value)
  })
})

describe('loadConfig webhook secrets', () => {
  test('accepts two secrets during a rotation and keeps their order', () => {
    const newest = randomSecretBytes()
    const oldest = randomSecretBytes()
    const config = loadConfig(
      createRequiredEnvironment({
        WEBHOOK_SECRETS: `whsec_${base64Of(newest)} whsec_${base64Of(oldest)}`,
      }),
    )

    assert.deepEqual(
      config.webhookSecrets.map((secret) => Buffer.from(secret)),
      [newest, oldest],
    )
  })

  test('rejects three secrets', () => {
    const value = [createWebhookSecret(), createWebhookSecret(), createWebhookSecret()].join(' ')
    const error = assertRejected(
      createRequiredEnvironment({ WEBHOOK_SECRETS: value }),
      'WEBHOOK_SECRETS',
      /1 or 2/,
    )

    assertValueNotDisclosed(error, value)
  })

  test('rejects a secret without the whsec_ prefix', () => {
    const value = createSocketTicketKey()
    const error = assertRejected(
      createRequiredEnvironment({ WEBHOOK_SECRETS: value }),
      'WEBHOOK_SECRETS',
      /whsec_/,
    )

    assertValueNotDisclosed(error, value)
  })

  test('rejects a secret shorter than 32 bytes', () => {
    const value = createWebhookSecret(31)
    const error = assertRejected(
      createRequiredEnvironment({ WEBHOOK_SECRETS: value }),
      'WEBHOOK_SECRETS',
      /32 bytes/,
    )

    assertValueNotDisclosed(error, value)
  })

  test('rejects a secret that is not base64', () => {
    const value = `whsec_${'!'.repeat(44)}`
    const error = assertRejected(
      createRequiredEnvironment({ WEBHOOK_SECRETS: value }),
      'WEBHOOK_SECRETS',
    )

    assertValueNotDisclosed(error, value)
  })

  test('rejects a valid secret followed by an invalid one', () => {
    const value = `${createWebhookSecret()} whsec_short`
    const error = assertRejected(
      createRequiredEnvironment({ WEBHOOK_SECRETS: value }),
      'WEBHOOK_SECRETS',
    )

    assertValueNotDisclosed(error, value)
  })
})

describe('loadConfig socket ticket key', () => {
  test('rejects a key shorter than 32 bytes', () => {
    const value = createSocketTicketKey(31)
    const error = assertRejected(
      createRequiredEnvironment({ SOCKET_TICKET_KEY: value }),
      'SOCKET_TICKET_KEY',
      /32 bytes/,
    )

    assertValueNotDisclosed(error, value)
  })

  test('rejects a key that is not base64', () => {
    const value = `${createSocketTicketKey()}!`
    const error = assertRejected(
      createRequiredEnvironment({ SOCKET_TICKET_KEY: value }),
      'SOCKET_TICKET_KEY',
    )

    assertValueNotDisclosed(error, value)
  })

  test('accepts a longer random key', () => {
    const key = Buffer.from(randomSecretBytes(48))
    const config = loadConfig(createRequiredEnvironment({ SOCKET_TICKET_KEY: base64Of(key) }))

    assert.deepEqual(Buffer.from(config.socketTicketKey), key)
  })
})

describe('loadConfig published and patterned keys', () => {
  for (const start of publishedTestKeyStarts) {
    const label = `0x${start.toString(16).padStart(2, '0')}`
    const published = base64Of(bytes(32, (index) => start + index))

    test(`rejects the published test key starting at ${label} as SOCKET_TICKET_KEY`, () => {
      const error = assertRejected(
        createRequiredEnvironment({ SOCKET_TICKET_KEY: published }),
        'SOCKET_TICKET_KEY',
        /published test key/,
      )

      assertValueNotDisclosed(error, published)
    })

    test(`rejects the published test key starting at ${label} as a webhook secret`, () => {
      const value = `whsec_${published}`
      const error = assertRejected(
        createRequiredEnvironment({ WEBHOOK_SECRETS: value }),
        'WEBHOOK_SECRETS',
        /published test key/,
      )

      assertValueNotDisclosed(error, value)
    })

    test(`rejects the published test key starting at ${label} as the second webhook secret`, () => {
      const value = `${createWebhookSecret()} whsec_${published}`
      const error = assertRejected(
        createRequiredEnvironment({ WEBHOOK_SECRETS: value }),
        'WEBHOOK_SECRETS',
        /published test key/,
      )

      assertValueNotDisclosed(error, value)
    })
  }

  const patterns = [
    { name: 'all-equal bytes', key: bytes(32, () => 0x41) },
    { name: 'zero bytes', key: bytes(32, () => 0) },
    { name: 'a constant step of 3', key: bytes(32, (index) => 7 + index * 3) },
    { name: 'a descending constant step', key: bytes(32, (index) => 512 + 200 - index) },
    { name: 'a constant step past the published runs', key: bytes(32, (index) => 0x60 + index) },
  ]

  for (const { name, key } of patterns) {
    test(`rejects a SOCKET_TICKET_KEY with ${name}`, () => {
      const value = base64Of(key)
      const error = assertRejected(
        createRequiredEnvironment({ SOCKET_TICKET_KEY: value }),
        'SOCKET_TICKET_KEY',
        /patterned key/,
      )

      assertValueNotDisclosed(error, value)
    })

    test(`rejects a webhook secret with ${name}`, () => {
      const value = `whsec_${base64Of(key)}`
      const error = assertRejected(
        createRequiredEnvironment({ WEBHOOK_SECRETS: value }),
        'WEBHOOK_SECRETS',
        /patterned key/,
      )

      assertValueNotDisclosed(error, value)
    })
  }
})

describe('loadConfig refuses the values published in the two test vector files', () => {
  const signatureSecret = readSignatureVector().secret
  const ticketKey = readSocketTicketVector().key
  const cases: readonly [string, string, string][] = [
    ['the signature vector secret', 'WEBHOOK_SECRETS', signatureSecret],
    [
      'the signature vector secret as the second entry',
      'WEBHOOK_SECRETS',
      `${createWebhookSecret()} ${signatureSecret}`,
    ],
    [
      'the signature vector secret bytes',
      'SOCKET_TICKET_KEY',
      signatureSecret.slice('whsec_'.length),
    ],
    ['the socket ticket vector key', 'SOCKET_TICKET_KEY', ticketKey],
    ['the socket ticket vector key in whsec_ form', 'WEBHOOK_SECRETS', `whsec_${ticketKey}`],
  ]

  for (const [name, variable, value] of cases) {
    test(`refuses ${name} as ${variable}`, () => {
      const error = assertRejected(
        createRequiredEnvironment({ [variable]: value }),
        variable,
        /published test key/,
      )

      assertValueNotDisclosed(error, value)
    })
  }
})

describe('loadConfig refuses a webhook secret that equals SOCKET_TICKET_KEY', () => {
  const sharedKeyRule = /^entries must differ from SOCKET_TICKET_KEY$/

  test('refuses a first WEBHOOK_SECRETS entry whose bytes equal SOCKET_TICKET_KEY', () => {
    const keyText = base64Of(randomSecretBytes())
    const error = assertRejected(
      createRequiredEnvironment({
        WEBHOOK_SECRETS: `whsec_${keyText}`,
        SOCKET_TICKET_KEY: keyText,
      }),
      'WEBHOOK_SECRETS',
      sharedKeyRule,
    )

    assert.ok(error.message.includes('SOCKET_TICKET_KEY'), 'the message names SOCKET_TICKET_KEY')
    assertValueNotDisclosed(error, keyText)
  })

  test('refuses a second, older WEBHOOK_SECRETS entry whose bytes equal SOCKET_TICKET_KEY', () => {
    const newest = createWebhookSecret()
    const keyText = base64Of(randomSecretBytes())
    const error = assertRejected(
      createRequiredEnvironment({
        WEBHOOK_SECRETS: `${newest} whsec_${keyText}`,
        SOCKET_TICKET_KEY: keyText,
      }),
      'WEBHOOK_SECRETS',
      sharedKeyRule,
    )

    assert.ok(error.message.includes('SOCKET_TICKET_KEY'), 'the message names SOCKET_TICKET_KEY')
    assertValueNotDisclosed(error, newest.slice('whsec_'.length))
    assertValueNotDisclosed(error, keyText)
  })

  test('compares decoded bytes, so an unpadded SOCKET_TICKET_KEY matches a padded entry', () => {
    const keyText = base64Of(randomSecretBytes())
    const unpaddedKeyText = keyText.replace(/=+$/, '')
    assert.notEqual(unpaddedKeyText, keyText)

    const error = assertRejected(
      createRequiredEnvironment({
        WEBHOOK_SECRETS: `whsec_${keyText}`,
        SOCKET_TICKET_KEY: unpaddedKeyText,
      }),
      'WEBHOOK_SECRETS',
      sharedKeyRule,
    )

    assertValueNotDisclosed(error, unpaddedKeyText)
  })

  test('accepts two entries and a key whose bytes all differ', () => {
    const newest = randomSecretBytes()
    const oldest = randomSecretBytes()
    const key = randomSecretBytes()
    const config = loadConfig(
      createRequiredEnvironment({
        WEBHOOK_SECRETS: `whsec_${base64Of(newest)} whsec_${base64Of(oldest)}`,
        SOCKET_TICKET_KEY: base64Of(key),
      }),
    )

    assert.deepEqual(
      config.webhookSecrets.map((secret) => Buffer.from(secret)),
      [newest, oldest],
    )
    assert.deepEqual(Buffer.from(config.socketTicketKey), key)
  })

  test('accepts an entry that only starts with the bytes of SOCKET_TICKET_KEY', () => {
    const key = randomSecretBytes()
    const longer = Buffer.concat([key, randomSecretBytes()])
    const config = loadConfig(
      createRequiredEnvironment({
        WEBHOOK_SECRETS: `whsec_${base64Of(longer)}`,
        SOCKET_TICKET_KEY: base64Of(key),
      }),
    )

    assert.deepEqual(Buffer.from(config.webhookSecrets[0] ?? []), longer)
  })

  test('reports the shared key together with every other invalid variable', () => {
    const keyText = base64Of(randomSecretBytes())
    const error = rejectionOf(
      createRequiredEnvironment({
        PORT: 'not-a-port',
        WEBHOOK_SECRETS: `whsec_${keyText}`,
        SOCKET_TICKET_KEY: keyText,
      }),
    )

    assert.deepEqual(error.issues.map((issue) => issue.variable).sort(), [
      'PORT',
      'WEBHOOK_SECRETS',
    ])
    assertValueNotDisclosed(error, keyText)
  })
})

describe('loadConfig allowed origins', () => {
  test('accepts ten origins', () => {
    const origins = Array.from({ length: 10 }, (_, index) => `http://localhost:${5170 + index}`)
    const config = loadConfig(
      createRequiredEnvironment({ SOCKET_ALLOWED_ORIGINS: origins.join(' ') }),
    )

    assert.deepEqual(config.socketAllowedOrigins, origins)
  })

  test('rejects eleven origins', () => {
    const origins = Array.from({ length: 11 }, (_, index) => `http://localhost:${5170 + index}`)

    assertRejected(
      createRequiredEnvironment({ SOCKET_ALLOWED_ORIGINS: origins.join(' ') }),
      'SOCKET_ALLOWED_ORIGINS',
      /1 to 10/,
    )
  })

  const invalidOrigins = [
    'http://localhost:5173/app',
    'http://localhost:5173/',
    '*',
    'http://*.example.com',
    'ftp://files.example.com',
    'localhost:5173',
    'http://localhost:5173?x=1',
    'http://user@localhost:5173',
  ]

  for (const origin of invalidOrigins) {
    test(`rejects the origin ${origin}`, () => {
      assertRejected(
        createRequiredEnvironment({ SOCKET_ALLOWED_ORIGINS: `http://127.0.0.1:5173 ${origin}` }),
        'SOCKET_ALLOWED_ORIGINS',
        /no path and no \*/,
      )
    })
  }
})

function randomSecretBytes(length = 32): Buffer {
  return Buffer.from(createSocketTicketKey(length), 'base64')
}
