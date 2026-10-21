import './styles/fonts.css'
import './styles/tokens.css'
import './styles/base.css'

import { createPinia } from 'pinia'
import { createApp } from 'vue'

import App from './App.vue'
import { installErrorHandling } from './error-handling'
import { installNavigationGuards, router } from './router'

const app = createApp(App)

app.use(createPinia())
installNavigationGuards(router)
app.use(router)
installErrorHandling(app, router)

app.mount('#app')
