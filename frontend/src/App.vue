<template>
  <router-view />

  <div v-if="errorMessage" class="fatal-overlay">
    <div class="fatal-card">
      <div class="fatal-title">页面加载失败</div>
      <div class="fatal-message">{{ errorMessage }}</div>
      <div class="fatal-actions">
        <el-button type="primary" @click="reload">刷新页面</el-button>
        <el-button @click="clear">关闭提示</el-button>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { onMounted, onUnmounted, ref } from 'vue'

const errorMessage = ref('')

function toMessage(error: unknown) {
  if (error instanceof Error && error.message) return error.message
  if (typeof error === 'string' && error.trim()) return error
  return '前端运行时发生异常，请刷新页面重试。'
}

function isHttpClientError(error: unknown) {
  if (!error || typeof error !== 'object') return false
  const value = error as Record<string, unknown>
  return value.isAxiosError === true || ('response' in value && 'config' in value)
}

function isFatalPromiseError(error: unknown) {
  if (isHttpClientError(error)) return false

  const message = toMessage(error)
  return /Failed to fetch dynamically imported module|Importing a module script failed|Loading chunk|dynamically imported module|Script error|Cannot read properties|is not a function/i.test(message)
}

function onError(event: Event) {
  const custom = event as CustomEvent<unknown>
  if (isHttpClientError(custom.detail)) return
  errorMessage.value = toMessage(custom.detail)
}

function onUnhandledRejection(event: PromiseRejectionEvent) {
  if (!isFatalPromiseError(event.reason)) return
  errorMessage.value = toMessage(event.reason)
}

function reload() {
  window.location.reload()
}

function clear() {
  errorMessage.value = ''
}

onMounted(() => {
  window.addEventListener('telegram-panel:error', onError)
  window.addEventListener('unhandledrejection', onUnhandledRejection)
})

onUnmounted(() => {
  window.removeEventListener('telegram-panel:error', onError)
  window.removeEventListener('unhandledrejection', onUnhandledRejection)
})
</script>
