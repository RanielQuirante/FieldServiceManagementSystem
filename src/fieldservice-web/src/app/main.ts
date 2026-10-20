import { createPinia } from 'pinia'
import { createApp } from 'vue'

import App from './App.vue'
import { installErrorHandling } from './error-handling'
import { router } from './router'

const app = createApp(App)

app.use(createPinia())
app.use(router)
installErrorHandling(app, router)

app.mount('#app')
