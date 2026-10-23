<script setup lang="ts">
import { useRoute, useRouter } from 'vue-router'

import SignInForm from '../components/SignInForm.vue'
import SignInHero from '../components/SignInHero.vue'
import { redirectFromQuery } from '../redirect'

const route = useRoute()
const router = useRouter()

async function openApplication(): Promise<void> {
  await router.replace(redirectFromQuery(route.query.redirect))
}
</script>

<template>
  <div class="sign-in-page">
    <SignInHero />
    <main id="main" class="sign-in-page__main" tabindex="-1">
      <SignInForm @signed-in="openApplication" />
    </main>
  </div>
</template>

<style scoped>
.sign-in-page {
  display: flex;
  flex-wrap: wrap;
  min-height: 100vh;
}

.sign-in-page__main {
  display: flex;
  flex: 1 1 560px;
  align-items: center;
  justify-content: center;
  padding: var(--spacing-fs-48) clamp(16px, 4vw, 64px);
}

.sign-in-page__main:focus {
  outline: none;
}

@media (width < 600px) {
  .sign-in-page__main {
    padding: var(--spacing-fs-24) var(--spacing-fs-16) var(--spacing-fs-48);
  }
}
</style>
