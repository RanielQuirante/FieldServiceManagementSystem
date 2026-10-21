const jobNumberLabel = /^JOB-\d+$/

export function formatJobNumber(number: number): string {
  return `JOB-${number}`
}

export function isJobNumberLabel(text: string): boolean {
  return jobNumberLabel.test(text)
}
