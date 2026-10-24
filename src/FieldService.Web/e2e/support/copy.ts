export const applicationName = 'FieldService Admin'

const noBreakSpace = String.fromCharCode(0xa0)

export function documentTitle(page: string): string {
  return `${page} · ${applicationName}`
}

export const signInCopy = {
  heading: 'Sign in',
  email: 'Email',
  password: 'Password',
  showPassword: 'Show password',
  submit: 'Sign in',
  hint: 'Technicians sign in from the FieldService Tech app.',
  invalidCredentials: 'The email or password is incorrect.',
  technicianAccount: 'This is a technician account.',
  technicianApp: 'Technicians sign in from the FieldService Tech app.',
  signedOutElsewhere: 'You signed out in another tab.',
  emailRequired: 'Enter your email address.',
  passwordRequired: 'Enter your password.',
} as const

export const shellCopy = {
  skipLink: 'Skip to main content',
  mainNavigation: 'Main',
  breadcrumb: 'Breadcrumb',
  jobsLink: 'Jobs',
  newJob: 'New job',
  signOut: 'Sign out',
  notifications: 'Notifications',
} as const

export const jobsCopy = {
  heading: 'Jobs',
  search: 'Search jobs, customers or sites',
  priority: 'Priority',
  allPriorities: 'All priorities',
  statusFilter: 'Filter by status',
  allStatuses: 'All',
  table: 'Jobs',
  pagination: 'Pagination',
  previous: 'Previous',
  next: 'Next',
  noMatchesTitle: 'No jobs match these filters',
  noMatchesBody: 'Change the search or filters, or clear them to see every job.',
  noJobsTitle: 'No jobs yet',
  noJobsBody: 'Jobs you create appear here, newest activity first.',
  clearFilters: 'Clear filters',
  loadFailed: 'Jobs could not be loaded.',
  tryAgain: 'Try again',
  unassigned: 'Unassigned',
  technicianFilter: 'Technician',
  allTechnicians: 'All technicians',
  selectedTechnician: 'Selected technician',
} as const

export const tradeLabels = {
  Hvac: 'HVAC',
  Electrical: 'Electrical',
  Plumbing: 'Plumbing',
  FireSafety: 'Fire safety',
} as const

export function openJobsLabel(count: number): string {
  return count === 1 ? '1 open job' : `${count} open jobs`
}

export const assignCopy = {
  heading: 'Assign technician',
  reassignHeading: 'Reassign technician',
  group: 'Assign technician',
  search: 'Search technicians',
  button: 'Assign technician',
  busy: 'Assigning…',
  helper: 'Choose an available technician.',
  chooseFromList: 'Choose a technician from the list.',
  differentTrade: 'Different trade',
  onShift: 'On shift',
  offShift: 'Off shift',
  tradeFirst: (trade: string): string => `${trade} technicians first`,
  assignTo: (name: string): string => `Assign to ${name}`,
  reassignTo: (name: string): string => `Reassign to ${name}`,
  willSee: (name: string): string =>
    `${name} will see this job in the technician app straight away.`,
  assigned: (name: string): string => `Assigned to ${name}.`,
  assignedNow: (name: string): string =>
    `${name} is assigned now. Choose another technician to reassign the job.`,
  historyAssigned: (name: string): string => `Assigned to ${name}`,
  changedTitle: (jobNumber: string): string => `${jobNumber} changed after you opened it.`,
  changedMessage: 'The latest version is shown. Check it, then try again.',
} as const

export const statusLabels = {
  Unassigned: 'Unassigned',
  Assigned: 'Assigned',
  InProgress: 'In progress',
  OnHold: 'On hold',
  Completed: 'Completed',
  Cancelled: 'Cancelled',
} as const

export const createJobCopy = {
  heading: 'New job',
  jobCard: 'Job',
  customerCard: 'Customer and site',
  scheduleCard: 'Schedule and priority',
  checklistCard: 'Checklist and assignment',
  technician: 'Assign technician',
  leaveUnassigned: 'Leave unassigned',
  technicianHelp: 'The technician sees the job in the app as soon as you create it.',
  templatesFailed: 'Checklist templates could not be loaded.',
  noTemplates: 'No checklist templates are available',
  techniciansFailed: 'Technicians could not be loaded.',
  techniciansFailedHelp: 'You can still create the job unassigned.',
  tryAgain: 'Try again',
  title: 'Title',
  description: 'Description',
  equipment: 'Equipment',
  equipmentPlaceholder: 'Unit, model or tag',
  equipmentHelp: 'Name the unit or system to work on. The technician sees it on the job details.',
  customer: 'Customer',
  siteAddress: 'Site address',
  siteAccessNotes: 'Site access notes',
  contactName: 'Contact name',
  contactPhone: 'Contact phone',
  priority: 'Priority',
  date: 'Date',
  start: 'Start',
  end: 'End',
  checklistTemplate: 'Checklist template',
  create: 'Create job',
  cancel: 'Cancel',
  siteAddressRequired: 'Enter the site address so the technician can find the job.',
  titleRequired: 'Enter a title for the job.',
  summaryTitle: 'Some details need attention.',
  serverSiteAddressRequired: 'Enter the site address.',
  serverEndAfterStart: 'The scheduled end must be after the scheduled start.',
  summaryForSiteAndEnd: 'Check Site address and End.',
} as const

export const jobDetailCopy = {
  detailsCard: 'Job details',
  description: 'Description',
  history: 'Status history',
  notFoundHeading: 'Job not found',
  notFoundTitle: 'No job matches this link',
  backToJobs: 'Back to jobs',
} as const

export const historyCopy = {
  created: (name: string): string => `Created by ${name}`,
  assigned: (name: string): string => `Assigned to ${name}`,
  started: (name: string): string => `Started by ${name}`,
  putOnHold: (name: string): string => `Put on hold by ${name}`,
  resumed: (name: string): string => `Resumed by ${name}`,
  completed: (name: string): string => `Completed by ${name}`,
  note: (text: string): string => `“${text}”`,
} as const

export const reportCopy = {
  documentTitle: (jobNumber: string): string => documentTitle(`${jobNumber} inspection report`),
  eyebrowSuffix: 'Inspection report',
  reportLink: 'Inspection report',
  jobDetailsLink: 'Job details',
  results: 'Inspection results',
  heading: (template: string): string => `Inspection · ${template}`,
  technicianNotes: 'Technician notes',
  statusHistory: 'Status history',
  notStartedTitle: 'Not started yet.',
  notStartedMessage: 'The checklist has no answers.',
  notSubmittedTitle: 'Not submitted yet.',
  notSubmittedMessage: 'This report shows the answers saved so far.',
  notesPending: 'Notes are added when the job is submitted.',
  notAnswered: 'Not answered',
  lessThanAMinute: 'Less than a minute',
  loading: 'Loading inspection report',
  loadFailed: 'The inspection report could not be loaded.',
  answered: (answered: number, total: number): string => `${answered} of ${total} answered`,
  reading: (label: string, value: string, unit: string): string =>
    `${label} ${value}${noBreakSpace}${unit}`,
} as const

export const notFoundCopy = {
  heading: 'Page not found',
  title: 'Nothing is at this address',
  goToDashboard: 'Go to dashboard',
} as const

export const toastCopy = {
  errorTitle: 'Something went wrong',
  errorBody: 'Your last action may not have finished. Check the page, then try again.',
  dismiss: 'Dismiss notification',
  reference: 'Reference',
} as const

function escapeForPattern(text: string): string {
  return text.replace(/[.*+?^${}()|[\]\\/]/g, '\\$&')
}

export const clockPattern = '\\d{1,2}:\\d{2}\\s[AP]M'

export const dashboardTiles = [
  { status: 'Unassigned', label: 'Unassigned' },
  { status: 'Assigned', label: 'Assigned' },
  { status: 'InProgress', label: 'In progress' },
  { status: 'OnHold', label: 'On hold' },
  { status: 'Completed', label: 'Completed today' },
  { status: 'Cancelled', label: 'Cancelled' },
] as const

export const dashboardCopy = {
  heading: 'Dashboard',
  greeting: (name: string): RegExp =>
    new RegExp(`^Good (?:morning|afternoon|evening), ${escapeForPattern(name)}$`),
  onShiftCaption: 'technicians on shift',
  assignedCaption: 'open jobs assigned',
  jobsByStatus: 'Jobs by status',
  needsTechnician: 'Needs a technician',
  liveActivity: 'Live activity',
  workload: 'Technician workload',
  loadingCounts: 'Loading job counts',
  loadingTechnicians: 'Loading technicians',
  countsFailed: 'Job counts could not be loaded.',
  techniciansFailed: 'Technicians could not be loaded.',
  failureMessage: 'Check your connection, then try again.',
  needsFailed: 'This list could not be loaded.',
  activityFailed: 'Activity could not be loaded.',
  needsEmpty: 'Every open job has a technician',
  activityEmpty: 'No activity yet today',
  workloadEmpty: 'No technicians yet',
  tryAgain: 'Try again',
  newTag: 'New',
  assign: (jobNumber: string): string => `Assign ${jobNumber}`,
  viewAll: (count: number): string =>
    count === 1 ? 'View all 1 unassigned job' : `View all ${count} unassigned jobs`,
  statusSubline: (open: number, completed: number, cancelled: number): string =>
    `${open} open, ${completed} completed and ${cancelled} cancelled today`,
  tileName: (label: string, value: number, subline: string | null): RegExp =>
    subline === null
      ? new RegExp(`^${escapeForPattern(label)}, ${value}$`)
      : new RegExp(`^${escapeForPattern(label)}, ${value}, ${subline}$`),
  backOnline: new RegExp(`^Back online\\. The dashboard was refreshed at ${clockPattern}\\.$`),
} as const

export const techniciansCopy = {
  heading: 'Technicians',
  shiftFilter: 'Filter by shift',
  search: 'Search technicians',
  table: 'Technicians',
  all: 'All',
  onShift: 'On shift',
  offShift: 'Off shift',
  inactive: 'Inactive',
  subline: (onShift: number, total: number): string => `${onShift} of ${total} on shift`,
  showing: (shown: number, total: number): string => `Showing ${shown} of ${total} technicians`,
  noMatchesTitle: 'No technicians match these filters',
  noTechniciansTitle: 'No technicians yet',
  clearFilters: 'Clear filters',
  loading: 'Loading technicians',
  loadFailed: 'Technicians could not be loaded.',
  tryAgain: 'Try again',
  call: (name: string, phone: string): string => `Call ${name}, ${phone}`,
} as const

export const cancelCopy = {
  trigger: 'Cancel job',
  dialogTitle: 'Cancel this job?',
  reason: 'Reason for cancelling',
  reasonHelp: "The reason is saved in the job's status history.",
  keep: 'Keep job',
  confirm: 'Cancel job',
  rule: 'Cancelled jobs are read-only. This cannot be undone.',
  consequence: (technician: string | null): string =>
    technician === null
      ? 'Cancelled jobs are read-only. This cannot be undone.'
      : `${technician} sees it as cancelled in the technician app. Cancelled jobs are read-only. This cannot be undone.`,
  blankReason: 'Enter a reason for cancelling the job.',
  controlCharacters: 'Remove control characters other than line breaks and tabs.',
  changed: (jobNumber: string): string => `${jobNumber} changed after you opened it.`,
  changedMessage: 'Its current status is shown above. Cancel it again if you still want to.',
  notCancellable: (jobNumber: string): string => `${jobNumber} can no longer be cancelled.`,
  notCancellableMessage: 'Its status changed after you opened it. The latest version is shown.',
  alreadyCancelled: (jobNumber: string): string => `${jobNumber} was already cancelled.`,
  alreadyCancelledMessage: 'The status history shows who cancelled it and why.',
  notice: (actor: string): RegExp =>
    new RegExp(
      `^Cancelled (?:at|on [A-Z][a-z]{2}\\s\\d{1,2}\\s[A-Z][a-z]{2} at) ${clockPattern} by ${escapeForPattern(actor)}\\.$`,
    ),
  announcement: (jobNumber: string): string => `${jobNumber} cancelled.`,
  history: (actor: string): string => `Cancelled by ${actor}`,
  liveLine: 'Status changes from the technician app appear here in real time.',
} as const

export const liveCopy = {
  pillAnyState: /^(?:Connecting…|Live|Reconnecting…|Live updates paused)/,
  pillLive: /^Live(?! updates paused)/,
  pillLiveName: /^Live, updated \d{1,2}:\d{2} [AP]M$/,
  pillConnecting: 'Connecting…',
  pillReconnecting: 'Reconnecting…',
  pillPaused: 'Live updates paused',
  reconnectingBanner: 'Reconnecting to live updates. Job statuses on this page may be out of date.',
  pausedLead: 'Live updates are paused.',
  pausedBanner: /^Live updates are paused\.(?: Showing data from \d{1,2}:\d{2}\s[AP]M\.)?$/,
  backOnlineBanner: /^Back online\. Jobs refreshed at \d{1,2}:\d{2}\s[AP]M\.$/,
  refreshNow: 'Refresh now',
  justNow: 'Just now',
  started: (actor: string): string => `${actor} started work`,
  putOnHold: (actor: string): string => `${actor} put a job on hold`,
  resumed: (actor: string): string => `${actor} resumed work`,
  completed: (actor: string): string => `${actor} completed a job`,
  body: (title: string, status: string): string => `${title} is now ${status}.`,
  announcement: (jobNumber: string, title: string, body: string): string =>
    `${jobNumber}. ${title}. ${body}`,
} as const
