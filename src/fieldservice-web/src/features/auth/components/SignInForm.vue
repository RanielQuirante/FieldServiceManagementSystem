<script setup lang="ts">
import { computed, ref, useTemplateRef } from 'vue'

import {
  firstFieldError,
  isApiError,
  unmappedFieldErrors,
  type ApiError,
} from '@/shared/api/api-error'
import AppButton from '@/shared/ui/AppButton.vue'
import Banner from '@/shared/ui/Banner.vue'
import type { IconName } from '@/shared/ui/icons'
import SectionCard from '@/shared/ui/SectionCard.vue'
import TextField from '@/shared/ui/TextField.vue'

import { useSessionStore } from '../session-store'

interface Failure {
  readonly title: string
  readonly message: string | null
  readonly reference: string | null
}

interface Notice {
  readonly title: string
  readonly message: string | null
  readonly icon: IconName
}

const emit = defineEmits<{
  'signed-in': []
}>()

const session = useSessionStore()

const email = ref('')
const password = ref('')
const emailError = ref<string | null>(null)
const passwordError = ref<string | null>(null)
const failure = ref<Failure | null>(null)
const attempt = ref(0)
const busy = ref(false)

const emailField = useTemplateRef<InstanceType<typeof TextField>>('emailField')
const passwordField = useTemplateRef<InstanceType<typeof TextField>>('passwordField')

const notice = computed<Notice | null>(() => {
  switch (session.endReason) {
    case 'expired':
      return { title: 'Your session ended.', message: 'Sign in again to continue.', icon: 'clock' }
    case 'signed-out-elsewhere':
      return { title: 'You signed out in another tab.', message: null, icon: 'log-out' }
    default:
      return null
  }
})

function emailMessage(validity: ValidityState | null): string | null {
  if (validity?.valueMissing) {
    return 'Enter your email address.'
  }
  if (validity?.typeMismatch) {
    return 'Enter a complete email address, such as name@fieldservice.example.'
  }
  if (validity?.tooLong) {
    return 'Use an email address of 256 characters or fewer.'
  }
  return null
}

function passwordMessage(validity: ValidityState | null): string | null {
  if (validity?.valueMissing) {
    return 'Enter your password.'
  }
  if (validity?.tooLong) {
    return 'Use a password of 256 characters or fewer.'
  }
  return null
}

function validate(): boolean {
  emailError.value = emailMessage(emailField.value?.validity() ?? null)
  passwordError.value = passwordMessage(passwordField.value?.validity() ?? null)
  if (emailError.value !== null) {
    emailField.value?.focus()
    return false
  }
  if (passwordError.value !== null) {
    passwordField.value?.focus()
    return false
  }
  return true
}

function retryWait(error: ApiError): string {
  const seconds = error.retryAfterSeconds ?? 60
  return seconds === 1 ? 'Try again in 1 second.' : `Try again in ${seconds} seconds.`
}

function describeFailure(error: ApiError): Failure | null {
  if (error.isNetworkError) {
    return {
      title: 'FieldService could not be reached.',
      message: 'Check your connection, then try again.',
      reference: null,
    }
  }
  if (error.status === 401) {
    return { title: 'The email or password is incorrect.', message: null, reference: null }
  }
  if (error.code === 'auth.wrong-application') {
    return {
      title: 'This is a technician account.',
      message: 'Technicians sign in from the FieldService Tech app.',
      reference: null,
    }
  }
  if (error.status === 429) {
    return { title: 'Too many attempts.', message: retryWait(error), reference: null }
  }
  if (error.code === 'request.validation-failed') {
    emailError.value = firstFieldError(error.fieldErrors, 'email')
    passwordError.value = firstFieldError(error.fieldErrors, 'password')
    const others = unmappedFieldErrors(error.fieldErrors, ['email', 'password'])
    if (others.length === 0 && (emailError.value !== null || passwordError.value !== null)) {
      return null
    }
    return {
      title: 'Check the sign-in details.',
      message: others.length > 0 ? others.join(' ') : null,
      reference: null,
    }
  }
  return {
    title: 'Sign-in could not be completed.',
    message: 'Try again in a moment.',
    reference: error.correlationId,
  }
}

async function submit(): Promise<void> {
  if (busy.value) {
    return
  }
  failure.value = null
  if (!validate()) {
    return
  }
  busy.value = true
  attempt.value += 1
  try {
    await session.signIn(email.value.trim(), password.value)
    password.value = ''
    emit('signed-in')
  } catch (error) {
    if (!isApiError(error)) {
      failure.value = {
        title: 'Sign-in could not be completed.',
        message: 'Try again in a moment.',
        reference: null,
      }
      throw error
    }
    failure.value = describeFailure(error)
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <SectionCard variant="plain" class="sign-in-form">
    <form
      class="sign-in-form__form"
      novalidate
      aria-labelledby="sign-in-title"
      @submit.prevent="submit"
    >
      <h1 id="sign-in-title" class="sign-in-form__title">Sign in</h1>
      <Banner
        v-if="failure"
        :key="attempt"
        variant="error"
        role="alert"
        :title="failure.title"
        :message="failure.message"
        :reference="failure.reference"
      />
      <Banner
        v-else-if="notice"
        variant="info"
        :icon="notice.icon"
        :title="notice.title"
        :message="notice.message"
      />
      <TextField
        id="sign-in-email"
        ref="emailField"
        v-model="email"
        label="Email"
        type="email"
        icon="mail"
        name="email"
        autocomplete="username"
        required
        :maxlength="256"
        :error="emailError"
      />
      <TextField
        id="sign-in-password"
        ref="passwordField"
        v-model="password"
        label="Password"
        type="password"
        icon="lock"
        name="password"
        autocomplete="current-password"
        required
        :maxlength="256"
        :error="passwordError"
      />
      <AppButton type="submit" size="large" block :busy="busy" busy-label="Signing in…">
        Sign in
      </AppButton>
      <p class="sign-in-form__hint">Technicians sign in from the FieldService Tech app.</p>
    </form>
  </SectionCard>
</template>

<style scoped>
.sign-in-form {
  width: 100%;
  max-width: 420px;
}

.sign-in-form__form {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-fs-24);
}

.sign-in-form__title {
  margin: 0;
  font-size: 1.75rem;
  font-weight: 600;
  line-height: 1.15;
  letter-spacing: -0.025em;
  color: var(--color-fs-ink);
  text-wrap: balance;
}

.sign-in-form__hint {
  margin: 0;
  font-size: var(--text-fs-table);
  line-height: var(--text-fs-table--line-height);
  color: var(--color-fs-muted);
  text-wrap: pretty;
}
</style>
