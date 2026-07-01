<template>
  <div>
    <el-alert
      title="模块会在服务启动时加载；开启自动重启后，模块变更会请求服务重启并由容器/进程管理器拉起。"
      type="info"
      :closable="false"
      show-icon
      class="mb-3"
    />

    <el-card shadow="never" class="page-card">
      <div class="module-upload">
        <el-upload
          v-model:file-list="fileList"
          :auto-upload="false"
          :limit="1"
          accept=".tpm,.zip"
          :on-exceed="onExceed"
        >
          <el-button :icon="Upload">选择模块包（.tpm/.zip）</el-button>
        </el-upload>

        <div class="switch-row">
          <el-switch v-model="autoRestart" active-text="模块变更后自动重启服务（推荐）" />
          <el-switch v-model="activateAndEnable" active-text="上传后自动切换到新版本并启用" />
        </div>

        <div class="toolbar">
          <span class="muted">{{ selectedFile ? `已选择：${selectedFile.name}（${selectedFile.size} bytes）` : '未选择文件' }}</span>
          <div class="toolbar-spacer" />
          <el-button type="primary" :loading="installing" :disabled="!selectedFile" @click="install">上传</el-button>
        </div>
      </div>
    </el-card>

    <el-card shadow="never" class="page-card mt-4">
      <el-alert
        v-if="diagnostics.length"
        type="warning"
        :closable="false"
        show-icon
        class="mb-3"
      >
        <template #title>检测到模块贡献冲突（已自动忽略部分定义）</template>
        <div v-for="item in diagnostics" :key="item" class="diagnostic-line">{{ item }}</div>
      </el-alert>

      <div class="toolbar mb-3">
        <div class="toolbar-title">已安装模块 ({{ modules.length }})</div>
        <div class="toolbar-spacer" />
        <el-button :icon="Refresh" :loading="loading" @click="load">刷新</el-button>
      </div>

      <el-table v-loading="loading" :data="modules" stripe>
        <el-table-column label="模块" min-width="260">
          <template #default="{ row }">
            <div class="cell-main">{{ displayName(row) }}</div>
            <div class="cell-sub">{{ row.id }}</div>
            <div v-if="row.manifestError" class="error-text">{{ row.manifestError }}</div>
          </template>
        </el-table-column>
        <el-table-column label="版本" min-width="180">
          <template #default="{ row }">
            <div>{{ row.activeVersion || '-' }}</div>
            <div v-if="row.lastGoodVersion" class="cell-sub">last-good: {{ row.lastGoodVersion }}</div>
          </template>
        </el-table-column>
        <el-table-column label="状态" width="150">
          <template #default="{ row }">
            <el-tag :type="row.enabled ? 'success' : 'info'" size="small">{{ row.enabled ? '启用' : '停用' }}</el-tag>
            <el-tag v-if="row.builtIn" type="primary" size="small" class="ml-2">内置</el-tag>
          </template>
        </el-table-column>
        <el-table-column label="操作" width="300" fixed="right">
          <template #default="{ row }">
            <el-button v-if="row.enabled" link type="warning" @click="disable(row.id)">停用</el-button>
            <el-button v-else link type="primary" @click="enable(row.id)">启用</el-button>
            <el-button v-if="!row.builtIn && row.installedVersions.length > 1" link @click="prune(row)">清理旧版本</el-button>
            <el-button v-if="!row.builtIn" link type="danger" @click="remove(row)">删除</el-button>
            <el-button link @click="showDetails(row)">详情</el-button>
          </template>
        </el-table-column>
      </el-table>
    </el-card>

    <el-dialog v-model="details.visible" title="模块详情" width="720px">
      <el-descriptions v-if="details.module" :column="1" border>
        <el-descriptions-item label="模块 ID">{{ details.module.id }}</el-descriptions-item>
        <el-descriptions-item label="名称">{{ displayName(details.module) }}</el-descriptions-item>
        <el-descriptions-item label="状态">{{ details.module.enabled ? '启用' : '停用' }}</el-descriptions-item>
        <el-descriptions-item label="当前版本">{{ details.module.activeVersion || '-' }}</el-descriptions-item>
        <el-descriptions-item label="Last Good">{{ details.module.lastGoodVersion || '-' }}</el-descriptions-item>
        <el-descriptions-item label="已安装版本">{{ details.module.installedVersions.join(', ') || '-' }}</el-descriptions-item>
        <el-descriptions-item label="Host 兼容">{{ hostRange(details.module) }}</el-descriptions-item>
        <el-descriptions-item label="入口">{{ entryText(details.module) }}</el-descriptions-item>
        <el-descriptions-item label="依赖">
          <div v-if="details.module.manifest?.dependencies.length">
            <div v-for="dep in details.module.manifest.dependencies" :key="`${dep.id}-${dep.range}`">
              {{ dep.id }} {{ dep.range }}
            </div>
          </div>
          <span v-else>-</span>
        </el-descriptions-item>
        <el-descriptions-item v-if="details.module.manifestError" label="Manifest 错误">
          <span class="error-text">{{ details.module.manifestError }}</span>
        </el-descriptions-item>
      </el-descriptions>
      <template #footer>
        <el-button type="primary" @click="details.visible = false">关闭</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import type { UploadFile, UploadFiles, UploadUserFile } from 'element-plus'
import { Refresh, Upload } from '@element-plus/icons-vue'
import { panelApi } from '@/api/panel'
import type { ModuleOverview } from '@/api/types'

const loading = ref(false)
const installing = ref(false)
const autoRestart = ref(true)
const activateAndEnable = ref(true)
const modules = ref<ModuleOverview[]>([])
const diagnostics = ref<string[]>([])
const fileList = ref<UploadUserFile[]>([])

const selectedFile = computed(() => fileList.value[0]?.raw ?? null)

const details = reactive({
  visible: false,
  module: null as ModuleOverview | null,
})

async function load() {
  loading.value = true
  try {
    const center = await panelApi.modules()
    modules.value = center.modules
    diagnostics.value = center.diagnostics
  } finally {
    loading.value = false
  }
}

function displayName(module: ModuleOverview) {
  return module.manifest?.name || module.id
}

function hostRange(module: ModuleOverview) {
  const min = module.manifest?.hostMin || '-'
  const max = module.manifest?.hostMax || '-'
  return `${min} ~ ${max}`
}

function entryText(module: ModuleOverview) {
  const assembly = module.manifest?.entryAssembly || '-'
  const type = module.manifest?.entryType || '-'
  return `${assembly} / ${type}`
}

function onExceed(files: File[], uploadFiles: UploadFiles) {
  uploadFiles.splice(0, uploadFiles.length)
  if (files[0]) {
    fileList.value = [{
      name: files[0].name,
      raw: files[0],
    } as UploadFile]
  }
}

async function install() {
  if (!selectedFile.value || installing.value) return
  const form = new FormData()
  form.append('file', selectedFile.value)
  form.append('activateAndEnable', String(activateAndEnable.value))
  form.append('autoRestart', String(autoRestart.value))

  installing.value = true
  try {
    const result = await panelApi.installModule(form)
    ElMessage.success(result.message || '模块已上传')
    fileList.value = []
    await load()
  } finally {
    installing.value = false
  }
}

async function enable(id: string) {
  await panelApi.enableModule(id, autoRestart.value)
  ElMessage.success('已启用')
  await load()
}

async function disable(id: string) {
  await panelApi.disableModule(id, autoRestart.value)
  ElMessage.success('已停用')
  await load()
}

async function prune(module: ModuleOverview) {
  const active = module.activeVersion || ''
  const lastGood = module.lastGoodVersion || ''
  const keepSet = new Set([active, lastGood].filter(Boolean))
  const toRemove = module.installedVersions.filter((version) => !keepSet.has(version))
  if (toRemove.length === 0) {
    ElMessage.info('没有可清理的旧版本')
    return
  }
  const keep = Array.from(keepSet).join('、') || '当前有效版本'
  await ElMessageBox.confirm(
    `将删除模块「${module.id}」的旧版本：${toRemove.join(', ')}。\n保留：${keep}。\n删除会移动到回收站目录，是否继续？`,
    '确认清理旧版本',
    { type: 'warning' },
  )
  await panelApi.pruneModuleVersions(module.id, autoRestart.value)
  ElMessage.success('已清理旧版本')
  await load()
}

async function remove(module: ModuleOverview) {
  await ElMessageBox.confirm(
    `确定删除模块「${module.id}」吗？删除会移动到回收站目录，建议先停用并重启确认无依赖。`,
    '确认删除',
    { type: 'warning' },
  )
  await panelApi.deleteModule(module.id, autoRestart.value)
  ElMessage.success('已删除')
  await load()
}

function showDetails(module: ModuleOverview) {
  details.module = module
  details.visible = true
}

onMounted(load)
</script>

<style scoped>
.module-upload {
  display: grid;
  gap: 14px;
}

.switch-row {
  display: flex;
  flex-wrap: wrap;
  gap: 18px;
}

.toolbar-title {
  font-weight: 650;
}

.toolbar-spacer {
  flex: 1;
}

.diagnostic-line {
  margin-top: 4px;
  color: var(--tp-muted);
}

.error-text {
  color: #f56c6c;
  font-size: 12px;
}

.ml-2 {
  margin-left: 8px;
}
</style>
