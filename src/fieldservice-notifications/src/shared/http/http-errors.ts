export class HttpError extends Error {
  readonly status: number
  readonly code: string
  readonly title: string
  readonly reason: string
  readonly headers: Readonly<Record<string, string>>

  constructor(
    status: number,
    code: string,
    title: string,
    reason: string,
    headers: Readonly<Record<string, string>> = {},
  ) {
    super(code)
    this.name = new.target.name
    this.status = status
    this.code = code
    this.title = title
    this.reason = reason
    this.headers = headers
  }
}

export class InvalidPayloadError extends HttpError {
  constructor(reason: string) {
    super(400, 'webhook.invalid-payload', 'Bad Request', reason)
  }
}

export class UnauthenticatedError extends HttpError {
  constructor(reason: string) {
    super(401, 'webhook.unauthenticated', 'Unauthorized', reason)
  }
}

export class NotFoundError extends HttpError {
  constructor() {
    super(404, 'resource.not-found', 'Not Found', 'not-found')
  }
}

export class MethodNotAllowedError extends HttpError {
  constructor(allowedMethods: readonly string[]) {
    super(405, 'request.method-not-allowed', 'Method Not Allowed', 'method-not-allowed', {
      allow: allowedMethods.join(', '),
    })
  }
}

export class PayloadTooLargeError extends HttpError {
  constructor() {
    super(413, 'request.too-large', 'Content Too Large', 'too-large', { connection: 'close' })
  }
}

export class UnsupportedMediaTypeError extends HttpError {
  constructor() {
    super(415, 'request.unsupported-media-type', 'Unsupported Media Type', 'unsupported-media-type')
  }
}

export class ServerError extends HttpError {
  constructor() {
    super(500, 'server.error', 'Internal Server Error', 'server-error')
  }
}
