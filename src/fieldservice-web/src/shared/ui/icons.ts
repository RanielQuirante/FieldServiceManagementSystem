export type IconShape =
  | { readonly kind: 'path'; readonly d: string }
  | {
      readonly kind: 'circle'
      readonly cx: number
      readonly cy: number
      readonly r: number
      readonly dasharray?: string
    }
  | {
      readonly kind: 'rect'
      readonly x: number
      readonly y: number
      readonly width: number
      readonly height: number
      readonly rx?: number
    }

export const icons = {
  check: [{ kind: 'path', d: 'M20 6 9 17l-5-5' }],
  x: [{ kind: 'path', d: 'M18 6 6 18M6 6l12 12' }],
  minus: [{ kind: 'path', d: 'M5 12h14' }],
  plus: [{ kind: 'path', d: 'M12 5v14M5 12h14' }],
  'chevron-right': [{ kind: 'path', d: 'm9 18 6-6-6-6' }],
  'chevron-left': [{ kind: 'path', d: 'm15 18-6-6 6-6' }],
  'chevron-down': [{ kind: 'path', d: 'm6 9 6 6 6-6' }],
  'chevron-up': [{ kind: 'path', d: 'm6 15 6-6 6 6' }],
  'arrow-left': [{ kind: 'path', d: 'M19 12H5M12 19l-7-7 7-7' }],
  search: [
    { kind: 'circle', cx: 11, cy: 11, r: 7 },
    { kind: 'path', d: 'm20 20-3.5-3.5' },
  ],
  filter: [{ kind: 'path', d: 'M3 4h18l-7 8.5V19l-4 2v-8.5z' }],
  clock: [
    { kind: 'circle', cx: 12, cy: 12, r: 9 },
    { kind: 'path', d: 'M12 7v5l3 2' },
  ],
  calendar: [
    { kind: 'rect', x: 3, y: 5, width: 18, height: 16, rx: 2 },
    { kind: 'path', d: 'M16 3v4M8 3v4M3 10h18' },
  ],
  'map-pin': [
    { kind: 'path', d: 'M12 21s-7-6.2-7-11a7 7 0 0 1 14 0c0 4.8-7 11-7 11z' },
    { kind: 'circle', cx: 12, cy: 10, r: 2.5 },
  ],
  building: [
    { kind: 'rect', x: 4, y: 2, width: 16, height: 20, rx: 2 },
    {
      kind: 'path',
      d: 'M9 22v-4h6v4M8 6h.01M12 6h.01M16 6h.01M8 10h.01M12 10h.01M16 10h.01M8 14h.01M12 14h.01M16 14h.01',
    },
  ],
  user: [
    { kind: 'circle', cx: 12, cy: 8, r: 4 },
    { kind: 'path', d: 'M4 21a8 8 0 0 1 16 0' },
  ],
  users: [
    { kind: 'circle', cx: 9, cy: 8, r: 3.5 },
    { kind: 'path', d: 'M2 20a7 7 0 0 1 14 0' },
    { kind: 'path', d: 'M16 4.5a3.5 3.5 0 0 1 0 7M22 20a7 7 0 0 0-4-6.3' },
  ],
  'user-check': [
    { kind: 'circle', cx: 9, cy: 8, r: 4 },
    { kind: 'path', d: 'M2 21a7 7 0 0 1 14 0' },
    { kind: 'path', d: 'm16 11 2 2 4-4' },
  ],
  phone: [
    {
      kind: 'path',
      d: 'M5 4h4l2 5-2.5 1.5a11 11 0 0 0 5 5L15 13l5 2v4a2 2 0 0 1-2 2A16 16 0 0 1 3 6a2 2 0 0 1 2-2',
    },
  ],
  wrench: [
    {
      kind: 'path',
      d: 'M14.7 6.3a1 1 0 0 0 0 1.4l1.6 1.6a1 1 0 0 0 1.4 0l3.77-3.77a6 6 0 0 1-7.94 7.94l-6.91 6.91a2.12 2.12 0 0 1-3-3l6.91-6.91a6 6 0 0 1 7.94-7.94l-3.76 3.76z',
    },
  ],
  'clipboard-check': [
    { kind: 'rect', x: 8, y: 2, width: 8, height: 4, rx: 1 },
    { kind: 'path', d: 'M16 4h2a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2h2' },
    { kind: 'path', d: 'm9 14 2 2 4-4' },
  ],
  'circle-dashed': [{ kind: 'circle', cx: 12, cy: 12, r: 9, dasharray: '3 3' }],
  'check-circle': [
    { kind: 'circle', cx: 12, cy: 12, r: 9 },
    { kind: 'path', d: 'm8 12 3 3 5-6' },
  ],
  'x-circle': [
    { kind: 'circle', cx: 12, cy: 12, r: 9 },
    { kind: 'path', d: 'm15 9-6 6M9 9l6 6' },
  ],
  'pause-circle': [
    { kind: 'circle', cx: 12, cy: 12, r: 9 },
    { kind: 'path', d: 'M10 9v6M14 9v6' },
  ],
  'play-circle': [
    { kind: 'circle', cx: 12, cy: 12, r: 9 },
    { kind: 'path', d: 'm10 8 6 4-6 4z' },
  ],
  'alert-triangle': [
    {
      kind: 'path',
      d: 'M10.3 3.9 1.8 18a2 2 0 0 0 1.7 3h17a2 2 0 0 0 1.7-3L13.7 3.9a2 2 0 0 0-3.4 0z',
    },
    { kind: 'path', d: 'M12 9v4M12 17h.01' },
  ],
  refresh: [
    { kind: 'path', d: 'M21 12a9 9 0 0 1-15.5 6.2L3 16M3 12a9 9 0 0 1 15.5-6.2L21 8' },
    { kind: 'path', d: 'M21 3v5h-5M3 21v-5h5' },
  ],
  'wifi-off': [
    { kind: 'path', d: 'M2 2l20 20' },
    {
      kind: 'path',
      d: 'M8.5 16.5a5 5 0 0 1 7 0M5 13a10 10 0 0 1 5.2-2.8M19 13a10 10 0 0 0-2.2-1.6M2 8.8a15 15 0 0 1 4.2-2.6M22 8.8A15 15 0 0 0 11.5 5',
    },
    { kind: 'path', d: 'M12 20h.01' },
  ],
  bell: [
    { kind: 'path', d: 'M6 8a6 6 0 0 1 12 0c0 7 3 9 3 9H3s3-2 3-9' },
    { kind: 'path', d: 'M10.3 21a1.9 1.9 0 0 0 3.4 0' },
  ],
  grid: [
    { kind: 'rect', x: 3, y: 3, width: 7, height: 9, rx: 1 },
    { kind: 'rect', x: 14, y: 3, width: 7, height: 5, rx: 1 },
    { kind: 'rect', x: 14, y: 12, width: 7, height: 9, rx: 1 },
    { kind: 'rect', x: 3, y: 16, width: 7, height: 5, rx: 1 },
  ],
  list: [{ kind: 'path', d: 'M8 6h13M8 12h13M8 18h13M3 6h.01M3 12h.01M3 18h.01' }],
  'log-out': [
    { kind: 'path', d: 'M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4' },
    { kind: 'path', d: 'm16 17 5-5-5-5M21 12H9' },
  ],
  'more-vertical': [
    { kind: 'circle', cx: 12, cy: 5, r: 1 },
    { kind: 'circle', cx: 12, cy: 12, r: 1 },
    { kind: 'circle', cx: 12, cy: 19, r: 1 },
  ],
  navigation: [{ kind: 'path', d: 'm3 11 19-9-9 19-2-8z' }],
  lock: [
    { kind: 'rect', x: 4, y: 11, width: 16, height: 10, rx: 2 },
    { kind: 'path', d: 'M8 11V7a4 4 0 0 1 8 0v4' },
  ],
  eye: [
    { kind: 'path', d: 'M2 12s3.5-7 10-7 10 7 10 7-3.5 7-10 7S2 12 2 12z' },
    { kind: 'circle', cx: 12, cy: 12, r: 3 },
  ],
  message: [{ kind: 'path', d: 'M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z' }],
  send: [
    { kind: 'path', d: 'm22 2-7 20-4-9-9-4z' },
    { kind: 'path', d: 'M22 2 11 13' },
  ],
  activity: [{ kind: 'path', d: 'M22 12h-4l-3 9L9 3l-3 9H2' }],
  thermometer: [{ kind: 'path', d: 'M14 4v10.5a4 4 0 1 1-4 0V4a2 2 0 0 1 4 0z' }],
  mail: [
    { kind: 'rect', x: 3, y: 5, width: 18, height: 14, rx: 2 },
    { kind: 'path', d: 'm3 7 9 6 9-6' },
  ],
} as const satisfies Record<string, readonly IconShape[]>

export type IconName = keyof typeof icons

export type IconSize = 14 | 16 | 18 | 20 | 22 | 24 | 26 | 28
