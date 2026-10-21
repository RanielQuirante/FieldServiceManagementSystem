import type { IncomingMessage, ServerResponse } from 'node:http'
import type { Logger } from '../../logging.ts'
import { HttpError, MethodNotAllowedError, NotFoundError, ServerError } from './http-errors.ts'
import { correlationIdOf, sendProblem } from './responses.ts'

export type RouteHandler = (
  request: IncomingMessage,
  response: ServerResponse,
) => void | Promise<void>

export interface Router {
  readonly get: (path: string, handler: RouteHandler) => void
  readonly post: (path: string, handler: RouteHandler) => void
  readonly handle: (request: IncomingMessage, response: ServerResponse) => void
}

function pathOf(target: string | undefined): string {
  const url = target ?? '/'
  const queryStart = url.indexOf('?')
  return queryStart === -1 ? url : url.slice(0, queryStart)
}

export function createRouter(logger: Logger): Router {
  const routes = new Map<string, Map<string, RouteHandler>>()

  function add(method: string, path: string, handler: RouteHandler): void {
    const methods = routes.get(path) ?? new Map<string, RouteHandler>()
    methods.set(method, handler)
    routes.set(path, methods)
  }

  async function dispatch(request: IncomingMessage, response: ServerResponse): Promise<void> {
    const methods = routes.get(pathOf(request.url))
    if (methods === undefined) {
      throw new NotFoundError()
    }
    const handler = methods.get(request.method ?? '')
    if (handler === undefined) {
      throw new MethodNotAllowedError([...methods.keys()])
    }
    await handler(request, response)
  }

  function fail(request: IncomingMessage, response: ServerResponse, error: unknown): void {
    if (!(error instanceof HttpError)) {
      logger.error({ err: error }, 'request.failed')
    }
    if (response.headersSent) {
      response.destroy()
      return
    }
    const problem = error instanceof HttpError ? error : new ServerError()
    sendProblem(response, problem, correlationIdOf(request))
  }

  return {
    get: (path, handler) => {
      add('GET', path, handler)
    },
    post: (path, handler) => {
      add('POST', path, handler)
    },
    handle: (request, response) => {
      void dispatch(request, response).catch((error: unknown) => {
        fail(request, response, error)
      })
    },
  }
}
