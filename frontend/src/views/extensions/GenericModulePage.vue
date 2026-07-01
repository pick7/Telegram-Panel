<template>
  <div>
    <el-card shadow="never" class="page-card">
      <template #header>
        <div class="card-header">
          <span>{{ title }}</span>
          <el-button size="small" @click="openStandalone">新窗口打开</el-button>
        </div>
      </template>

      <el-alert
        type="info"
        :closable="false"
        show-icon
        title="该扩展提供的是模块原生页面，已在当前界面内加载。"
      />

      <div class="generic-module-frame-wrap mt-3">
        <iframe class="generic-module-frame" :src="nativePageUrl" />
      </div>
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'

const route = useRoute()
const router = useRouter()

const moduleId = computed(() => String(route.params.moduleId || ''))
const pageKey = computed(() => String(route.params.pageKey || ''))
const title = computed(() => (route.meta.title as string) || '扩展模块')
const nativePageUrl = computed(() => `/ext/${encodeURIComponent(moduleId.value)}/${encodeURIComponent(pageKey.value)}?legacy=1&embed=1`)

function openStandalone() {
  window.open(nativePageUrl.value, '_blank', 'noopener,noreferrer')
}
</script>

<style scoped>
.generic-module-frame-wrap {
  border: 1px solid var(--tp-border);
  border-radius: 4px;
  overflow: hidden;
  background: var(--tp-bg);
}

.generic-module-frame {
  display: block;
  width: 100%;
  min-height: calc(100vh - 230px);
  border: 0;
  background: var(--tp-bg);
}
</style>
