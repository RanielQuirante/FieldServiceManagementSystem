export const applicationName = 'FieldService Admin'

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
  checklistCard: 'Checklist',
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

export const notFoundCopy = {
  heading: 'Page not found',
  title: 'Nothing is at this address',
  goToJobs: 'Go to jobs',
} as const

export const toastCopy = {
  errorTitle: 'Something went wrong',
  dismiss: 'Dismiss notification',
} as const
