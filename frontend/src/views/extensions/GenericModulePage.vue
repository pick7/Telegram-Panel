<template>
  <div>
    <el-card shadow="never" class="page-card">
      <template #header>
        <div class="card-header">
          <span>{{ title }}</span>
          <div class="header-actions">
            <el-button size="small" @click="openStandalone">新窗口打开</el-button>
          </div>
        </div>
      </template>

      <el-alert
        type="info"
        :closable="false"
        show-icon
        title="该扩展页面已在当前后台内加载。"
      />

      <div class="generic-module-frame-wrap mt-3">
        <iframe :key="frameKey" class="generic-module-frame" :src="nativePageUrl" />
      </div>
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'

const route = useRoute()
const router = useRouter()

const moduleId = computed(() => String(route.params.moduleId || ''))
const pageKey = computed(() => String(route.params.pageKey || ''))
const title = computed(() => (route.meta.title as string) || '扩展模块')
const nativePageUrl = computed(() => `/ext/${encodeURIComponent(moduleId.value)}/${encodeURIComponent(pageKey.value)}?legacy=1&embed=1`)
const standalonePageUrl = computed(() => `/ext/${encodeURIComponent(moduleId.value)}/${encodeURIComponent(pageKey.value)}?legacy=1`)
const frameKey = ref(0)

function openStandalone() {
  window.open(standalonePageUrl.value, '_blank', 'noopener,noreferrer')
}

watch([moduleId, pageKey], () => {
  frameKey.value += 1
})
</script>

<style scoped>
.card-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
}

.header-actions {
  display: inline-flex;
  align-items: center;
  gap: 8px;
}

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
