export const keptSpace = '\u{00a0}'

export function kept(text: string): string {
  return text.split('~').join(keptSpace)
}
