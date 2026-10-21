<script setup lang="ts">
import StatusChip from '@/shared/ui/StatusChip.vue'
</script>

<template>
  <section class="sign-in-hero" data-surface="dark" aria-label="FieldService Admin">
    <div class="sign-in-hero__lockup">
      <span class="sign-in-hero__mark" aria-hidden="true">
        <svg
          width="26"
          height="26"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          stroke-width="2"
          stroke-linecap="round"
          stroke-linejoin="round"
          aria-hidden="true"
          focusable="false"
        >
          <path d="M12 21s-7-6.2-7-11a7 7 0 0 1 14 0c0 4.8-7 11-7 11z" />
          <circle class="sign-in-hero__lantern" cx="12" cy="10" r="2.75" />
        </svg>
      </span>
      <span class="sign-in-hero__wordmark">
        <span class="sign-in-hero__name">FieldService</span>
        <span class="sign-in-hero__product">Admin</span>
      </span>
    </div>
    <div class="sign-in-hero__message">
      <h2 class="sign-in-hero__headline">Dispatch and track field jobs in real time.</h2>
      <p class="sign-in-hero__lede">
        Assign work, follow every status change from the field and review completed inspections.
      </p>
    </div>
    <div class="sign-in-hero__glimpse" aria-hidden="true">
      <div class="sign-in-hero__row">
        <span class="sign-in-hero__dot sign-in-hero__dot--live"></span>
        <span class="sign-in-hero__time">10:42 AM</span>
        <span class="sign-in-hero__event"
          ><span class="sign-in-hero__actor">Priya Natarajan</span> started
          <span class="sign-in-hero__job">JOB-1049</span></span
        >
        <StatusChip status="InProgress" />
      </div>
      <div class="sign-in-hero__row">
        <span class="sign-in-hero__dot"></span>
        <span class="sign-in-hero__time">10:31 AM</span>
        <span class="sign-in-hero__event"
          ><span class="sign-in-hero__actor">Tom Becker</span> put
          <span class="sign-in-hero__job">JOB-1044</span> on hold</span
        >
        <StatusChip status="OnHold" />
      </div>
    </div>
  </section>
</template>

<style scoped>
.sign-in-hero {
  display: flex;
  flex: 1 1 560px;
  flex-direction: column;
  gap: var(--spacing-fs-48);
  padding: var(--spacing-fs-48) clamp(24px, 5vw, 72px);
  background: var(--fs-glow-signin), var(--fs-gradient-hero);
  color: var(--color-fs-on-dark);
}

.sign-in-hero__lockup {
  display: flex;
  align-items: center;
  gap: var(--spacing-fs-12);
}

.sign-in-hero__mark {
  display: inline-flex;
  flex: none;
  align-items: center;
  justify-content: center;
  width: 48px;
  height: 48px;
  border-radius: var(--radius-fs-logo-lg);
  background: var(--fs-gloss-logo-on-dark), var(--fs-glass-g4-control-bg);
  box-shadow: var(--shadow-fs-logo-on-dark);
  color: var(--color-fs-on-dark);
}

.sign-in-hero__lantern {
  fill: var(--color-fs-amber-300);
  stroke: none;
}

.sign-in-hero__wordmark {
  display: flex;
  flex-direction: column;
  gap: 3px;
  min-width: 0;
}

.sign-in-hero__name {
  font-size: 1.25rem;
  font-weight: 600;
  line-height: 1.1;
  letter-spacing: -0.02em;
  color: var(--color-fs-on-dark);
}

.sign-in-hero__product {
  font-size: 0.6875rem;
  font-weight: 600;
  line-height: 1;
  letter-spacing: 0.14em;
  text-transform: uppercase;
  color: var(--color-fs-on-dark-2);
}

.sign-in-hero__message {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-fs-16);
  max-width: 520px;
}

.sign-in-hero__headline {
  margin: 0;
  font-size: var(--text-fs-signin-headline);
  font-weight: var(--text-fs-signin-headline--font-weight);
  line-height: var(--text-fs-signin-headline--line-height);
  letter-spacing: var(--text-fs-signin-headline--letter-spacing);
  color: var(--color-fs-on-dark);
  text-wrap: balance;
}

.sign-in-hero__lede {
  margin: 0;
  font-size: 1.0625rem;
  line-height: 1.55;
  color: var(--color-fs-on-dark-2);
  text-wrap: pretty;
}

.sign-in-hero__glimpse {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-fs-12);
  max-width: 460px;
}

.sign-in-hero__row {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: var(--spacing-fs-12);
  padding: var(--spacing-fs-14) var(--spacing-fs-16);
  border-radius: var(--radius-fs-banner);
  background: var(--fs-glass-g4-bg);
  -webkit-backdrop-filter: var(--fs-glass-g4-filter);
  backdrop-filter: var(--fs-glass-g4-filter);
  box-shadow: var(--shadow-fs-g4);
}

.sign-in-hero__dot {
  flex: none;
  width: 8px;
  height: 8px;
  border-radius: var(--radius-fs-pill);
}

.sign-in-hero__dot--live {
  background: var(--fs-gradient-accent);
  box-shadow: var(--shadow-fs-live-dot);
}

.sign-in-hero__time {
  flex: none;
  font-family: var(--font-mono);
  font-size: var(--text-fs-mono);
  font-weight: var(--text-fs-mono--font-weight);
  line-height: var(--text-fs-mono--line-height);
  letter-spacing: var(--text-fs-mono--letter-spacing);
  font-variant-numeric: tabular-nums;
  color: var(--color-fs-on-dark-2);
}

.sign-in-hero__event {
  flex: 1 1 180px;
  min-width: 0;
  font-size: var(--text-fs-table);
  line-height: var(--text-fs-table--line-height);
  color: var(--color-fs-on-dark);
  text-wrap: pretty;
}

.sign-in-hero__actor {
  font-weight: 600;
}

.sign-in-hero__job {
  font-family: var(--font-mono);
  font-size: var(--text-fs-mono);
  font-weight: var(--text-fs-mono--font-weight);
  letter-spacing: var(--text-fs-mono--letter-spacing);
}

@media (width < 600px) {
  .sign-in-hero {
    gap: var(--spacing-fs-24);
    padding: var(--spacing-fs-32) var(--spacing-fs-24);
  }

  .sign-in-hero__glimpse {
    display: none;
  }
}

@media (forced-colors: active) {
  .sign-in-hero__row {
    border: 1px solid CanvasText;
  }
}
</style>
