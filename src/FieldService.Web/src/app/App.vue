<script setup lang="ts">
import { computed, ref } from 'vue'
import { RouterView } from 'vue-router'

import { useSessionStore } from '@/features/auth/session-store'
import { roleLabel } from '@/shared/format/labels'
import AppShell from '@/shared/ui/AppShell.vue'
import ToastHost from '@/shared/ui/ToastHost.vue'

import { usesAppShell } from './router'

const session = useSessionStore()

const signingOut = ref(false)

const userName = computed(() => session.user?.displayName ?? '')

const userRole = computed(() => (session.user ? roleLabel(session.user.role) : ''))

async function signOut(): Promise<void> {
  if (signingOut.value) {
    return
  }
  signingOut.value = true
  try {
    await session.signOut()
  } finally {
    signingOut.value = false
  }
}
</script>

<template>
  <RouterView v-slot="{ Component, route }">
    <AppShell
      v-if="usesAppShell(route)"
      :user-name="userName"
      :user-role="userRole"
      :signing-out="signingOut"
      @sign-out="signOut"
    >
      <component :is="Component" v-if="Component" :key="route.path" />
    </AppShell>
    <template v-else>
      <component :is="Component" v-if="Component" />
      <ToastHost placement="viewport" />
    </template>
  </RouterView>
</template>
