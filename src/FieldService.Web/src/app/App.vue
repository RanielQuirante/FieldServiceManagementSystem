<script setup lang="ts">
import { computed, ref } from 'vue'
import { RouterView } from 'vue-router'

import { useSessionStore } from '@/features/auth/session-store'
import { useRealtimeConnection } from '@/features/realtime/use-realtime-connection'
import { roleLabel } from '@/shared/format/labels'
import AppShell from '@/shared/ui/AppShell.vue'
import ConnectionBanner from '@/shared/ui/ConnectionBanner.vue'
import LivePill, { type LivePillState } from '@/shared/ui/LivePill.vue'
import ToastHost from '@/shared/ui/ToastHost.vue'

import { usesAppShell } from './router'

const session = useSessionStore()

const realtime = useRealtimeConnection()

const signingOut = ref(false)

const userName = computed(() => session.user?.displayName ?? '')

const userRole = computed(() => (session.user ? roleLabel(session.user.role) : ''))

const pillState = computed<LivePillState>(() =>
  realtime.state === 'idle' ? 'connecting' : realtime.state,
)

function refreshNow(): void {
  void realtime.refreshNow()
}

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
      <template #status>
        <LivePill :state="pillState" :updated-at="realtime.refreshedAt" />
      </template>
      <template #notice>
        <ConnectionBanner
          :notice="realtime.notice"
          :time="realtime.noticeTime"
          :failure="realtime.refreshFailure"
          :busy="realtime.refreshing"
          @refresh="refreshNow"
        />
      </template>
      <component :is="Component" v-if="Component" :key="route.path" />
    </AppShell>
    <template v-else>
      <component :is="Component" v-if="Component" />
      <ToastHost placement="sign-in" />
    </template>
  </RouterView>
</template>
