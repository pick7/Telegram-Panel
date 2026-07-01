<template>
  <div>
    <el-alert
      title="当前系统内置变量：{time}，格式为 yyyyMMddHHmmss。文本字段通常支持 {time} 和 {字典名}；图片字段通常只能使用单个图片字典变量，例如 {avatar}。"
      type="info"
      :closable="false"
      show-icon
      class="mb-3"
    />

    <el-card shadow="never" class="page-card">
      <div class="toolbar">
        <el-input v-model="search" placeholder="搜索字典" clearable class="search" />
        <el-button type="primary" @click="openCreate('text')">新建文本字典</el-button>
        <el-button @click="openCreate('image')">新建图片字典</el-button>
        <el-button :icon="Refresh" :loading="loading" @click="load">刷新</el-button>
      </div>
    </el-card>

    <el-card shadow="never" class="page-card mt-4">
      <el-table v-loading="loading" :data="filteredRows" stripe>
        <el-table-column label="显示名称" min-width="180">
          <template #default="{ row }">
            <el-tooltip v-if="row.description" :content="row.description" placement="top">
              <span>{{ row.displayName }}</span>
            </el-tooltip>
            <span v-else>{{ row.displayName }}</span>
          </template>
        </el-table-column>
        <el-table-column label="变量名" min-width="140">
          <template #default="{ row }">
            <code>{{ variableName(row.name) }}</code>
          </template>
        </el-table-column>
        <el-table-column label="类型" width="90">
          <template #default="{ row }">
            <el-tag :type="row.type === 'image' ? 'warning' : 'primary'" size="small">{{ row.type === 'image' ? '图片' : '文本' }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column label="读取模式" width="110">
          <template #default="{ row }">{{ row.readMode === 'queue' ? '队列轮询' : '随机' }}</template>
        </el-table-column>
        <el-table-column label="状态" width="90">
          <template #default="{ row }">
            <el-tag :type="row.isEnabled ? 'success' : 'warning'" size="small">{{ row.isEnabled ? '启用' : '停用' }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column prop="enabledItemCount" label="内容数" width="90" />
        <el-table-column prop="nextIndex" label="轮询游标" width="100" />
        <el-table-column label="更新时间" width="180">
          <template #default="{ row }">{{ formatTime(row.updatedAt) }}</template>
        </el-table-column>
        <el-table-column label="操作" width="150" fixed="right">
          <template #default="{ row }">
            <div class="row-actions">
              <el-tooltip content="编辑" placement="top">
                <el-button link type="primary" :icon="Edit" @click="edit(row)" />
              </el-tooltip>
              <el-tooltip :content="row.isEnabled ? '停用' : '启用'" placement="top">
                <el-button link :type="row.isEnabled ? 'warning' : 'success'" :icon="row.isEnabled ? SwitchButton : VideoPlay" @click="toggle(row)" />
              </el-tooltip>
              <el-tooltip content="重置轮询游标" placement="top">
                <el-button link type="primary" :icon="RefreshLeft" @click="resetQueue(row.id)" />
              </el-tooltip>
              <el-tooltip content="删除" placement="top">
                <el-button link type="danger" :icon="Delete" @click="remove(row)" />
              </el-tooltip>
            </div>
          </template>
        </el-table-column>
      </el-table>
    </el-card>

    <el-dialog v-model="editor.visible" :title="editorTitle" width="860px" destroy-on-close>
      <el-form label-width="96px">
        <el-row :gutter="12">
          <el-col :span="12">
            <el-form-item label="字典名称">
              <el-input v-model="editor.form.name" :disabled="editor.saving || !!editor.form.id" placeholder="变量名，如 nickname" />
            </el-form-item>
          </el-col>
          <el-col :span="12">
            <el-form-item label="显示名称">
              <el-input v-model="editor.form.displayName" :disabled="editor.saving" />
            </el-form-item>
          </el-col>
        </el-row>
        <el-form-item label="描述">
          <el-input v-model="editor.form.description" :disabled="editor.saving" type="textarea" :rows="2" />
        </el-form-item>
        <el-row :gutter="12">
          <el-col :span="8">
            <el-form-item label="字典类型">
              <el-select v-model="editor.form.type" :disabled="editor.saving || !!editor.form.id" class="full">
                <el-option label="文本字典" value="text" />
                <el-option label="图片字典" value="image" />
              </el-select>
            </el-form-item>
          </el-col>
          <el-col :span="8">
            <el-form-item label="读取模式">
              <el-select v-model="editor.form.readMode" :disabled="editor.saving" class="full">
                <el-option label="随机" value="random" />
                <el-option label="队列轮询" value="queue" />
              </el-select>
            </el-form-item>
          </el-col>
          <el-col :span="8">
            <el-form-item label="启用字典">
              <el-switch v-model="editor.form.isEnabled" :disabled="editor.saving" />
            </el-form-item>
          </el-col>
        </el-row>

        <el-form-item v-if="editor.form.type === 'text'" label="文本内容">
          <el-input
            v-model="editor.form.textLines"
            :disabled="editor.saving"
            type="textarea"
            :rows="12"
            placeholder="一行一条内容，保存时会自动忽略空行"
          />
        </el-form-item>

        <template v-else>
          <el-alert
            title="图片字典支持一次或分批选择多张图；删除字典或删除字典项时会同步清理图片文件，点击保存字典后才正式生效。"
            type="info"
            :closable="false"
            show-icon
            class="mb-3"
          />
          <el-form-item label="选择图片">
            <el-upload
              v-model:file-list="editor.imageFiles"
              :auto-upload="false"
              multiple
              accept="image/*"
              list-type="picture-card"
              :disabled="editor.saving"
            >
              <el-icon><Plus /></el-icon>
            </el-upload>
          </el-form-item>

          <el-form-item v-if="editor.existingImages.length" label="现有图片">
            <div class="image-grid">
              <div v-for="image in editor.existingImages" :key="image.id" class="image-cell">
                <el-image :src="assetUrl(image.assetPath)" fit="cover" class="image-preview" :preview-src-list="[assetUrl(image.assetPath)]" />
                <div class="image-meta">
                  <span class="ellipsis">{{ image.fileName }}</span>
                  <el-button link type="danger" @click="removeExistingImage(image)">删除</el-button>
                </div>
              </div>
            </div>
          </el-form-item>
          <el-form-item v-if="pendingImages.length" label="待保存图片">
            <el-table :data="pendingImages" size="small" border>
              <el-table-column prop="name" label="文件名" min-width="220" />
              <el-table-column label="大小" width="120">
                <template #default="{ row }">{{ formatBytes(row.size || 0) }}</template>
              </el-table-column>
              <el-table-column label="操作" width="90">
                <template #default="{ $index }">
                  <el-button link type="danger" @click="removePendingImage($index)">移除</el-button>
                </template>
              </el-table-column>
            </el-table>
          </el-form-item>
        </template>
      </el-form>
      <template #footer>
        <el-button :disabled="editor.saving" @click="editor.visible = false">关闭</el-button>
        <el-button type="primary" :loading="editor.saving" @click="saveDictionary">保存字典</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import type { UploadUserFile } from 'element-plus'
import { Delete, Edit, Plus, Refresh, RefreshLeft, SwitchButton, VideoPlay } from '@element-plus/icons-vue'
import { panelApi } from '@/api/panel'
import type { DataDictionary, DataDictionaryItem } from '@/api/types'
import { formatTime } from '@/utils/format'

type DictionaryType = 'text' | 'image'

interface ExistingImage {
  id: number
  assetPath: string
  fileName: string
}

const loading = ref(false)
const rows = ref<DataDictionary[]>([])
const search = ref('')
const editor = reactive({
  visible: false,
  saving: false,
  form: {
    id: null as number | null,
    type: 'text' as DictionaryType,
    name: '',
    displayName: '',
    description: '',
    readMode: 'random',
    isEnabled: true,
    textLines: '',
  },
  existingImages: [] as ExistingImage[],
  imageFiles: [] as UploadUserFile[],
})

const editorTitle = computed(() => {
  if (editor.form.type === 'image') return editor.form.id ? '编辑图片字典' : '新建图片字典'
  return editor.form.id ? '编辑文本字典' : '新建文本字典'
})

const filteredRows = computed(() => {
  const text = search.value.trim().toLowerCase()
  const matched = !text
    ? rows.value
    : rows.value.filter((x) =>
        safeText(x.name).toLowerCase().includes(text)
        || safeText(x.displayName).toLowerCase().includes(text)
        || safeText(x.description).toLowerCase().includes(text),
      )

  return [...matched].sort((a, b) =>
    safeText(a.displayName).localeCompare(safeText(b.displayName), 'zh-Hans-CN')
    || safeText(a.name).localeCompare(safeText(b.name), 'zh-Hans-CN'),
  )
})

const pendingImages = computed(() => editor.imageFiles.filter((x) => x.raw))

async function load() {
  loading.value = true
  try {
    rows.value = await panelApi.dictionaries()
  } finally {
    loading.value = false
  }
}

async function toggle(row: DataDictionary) {
  const next = !row.isEnabled
  await panelApi.setDictionaryEnabled(row.id, next)
  row.isEnabled = next
  ElMessage.success(next ? '字典已启用' : '字典已停用')
}

function openCreate(type: DictionaryType) {
  resetEditor()
  editor.form.type = type
  editor.visible = true
}

function edit(row: DataDictionary) {
  resetEditor()
  const items = Array.isArray(row.items) ? row.items : []
  editor.form.id = row.id
  editor.form.type = row.type === 'image' ? 'image' : 'text'
  editor.form.name = row.name
  editor.form.displayName = row.displayName
  editor.form.description = row.description || ''
  editor.form.readMode = row.readMode || 'random'
  editor.form.isEnabled = row.isEnabled

  if (editor.form.type === 'text') {
    editor.form.textLines = items
      .filter((x) => x.isEnabled && x.textValue)
      .sort(sortItems)
      .map((x) => x.textValue)
      .join('\n')
  } else {
    editor.existingImages = items
      .filter((x) => x.isEnabled && x.assetPath)
      .sort(sortItems)
      .map((x) => ({
        id: x.id,
        assetPath: x.assetPath || '',
        fileName: x.fileName || `image-${x.id}.jpg`,
      }))
  }

  editor.visible = true
}

async function saveDictionary() {
  if (!editor.form.name.trim()) {
    ElMessage.warning('字典名称不能为空')
    return
  }
  if (!editor.form.displayName.trim()) {
    ElMessage.warning('显示名称不能为空')
    return
  }

  if (editor.form.type === 'text') {
    await saveTextDictionary()
  } else {
    await saveImageDictionary()
  }
}

async function saveTextDictionary() {
  const items = editor.form.textLines.split(/\r?\n/).map((x) => x.trim()).filter(Boolean)
  if (items.length === 0) {
    ElMessage.warning('文本字典至少需要一条内容')
    return
  }

  editor.saving = true
  try {
    await panelApi.saveTextDictionary({
      id: editor.form.id,
      name: editor.form.name,
      displayName: editor.form.displayName,
      description: editor.form.description,
      readMode: editor.form.readMode,
      isEnabled: editor.form.isEnabled,
      items,
    })
    ElMessage.success('字典已保存')
    editor.visible = false
    await load()
  } catch (error: any) {
    ElMessage.error(error?.response?.data?.message || error?.message || '保存字典失败')
  } finally {
    editor.saving = false
  }
}

async function saveImageDictionary() {
  const files: File[] = []
  for (const uploadFile of editor.imageFiles) {
    if (uploadFile.raw) files.push(uploadFile.raw as File)
  }
  if (editor.existingImages.length === 0 && files.length === 0) {
    ElMessage.warning('图片字典至少需要一张图片')
    return
  }

  const form = new FormData()
  if (editor.form.id) form.append('id', String(editor.form.id))
  form.append('name', editor.form.name)
  form.append('displayName', editor.form.displayName)
  form.append('description', editor.form.description)
  form.append('readMode', editor.form.readMode)
  form.append('isEnabled', String(editor.form.isEnabled))
  form.append('keepItemIds', editor.existingImages.map((x) => x.id).join(','))
  files.forEach((file) => form.append('images', file))

  editor.saving = true
  try {
    await panelApi.saveImageDictionary(form)
    ElMessage.success('图片字典已保存')
    editor.visible = false
    await load()
  } catch (error: any) {
    ElMessage.error(error?.response?.data?.message || error?.message || '保存图片字典失败')
  } finally {
    editor.saving = false
  }
}

async function removeExistingImage(image: ExistingImage) {
  await ElMessageBox.confirm(`确定从字典中移除图片 ${image.fileName} 吗？保存字典后会同步清理图片文件。`, '确认移除图片', {
    type: 'warning',
    confirmButtonText: '移除',
    cancelButtonText: '取消',
  })
  editor.existingImages = editor.existingImages.filter((x) => x.id !== image.id)
}

function removePendingImage(index: number) {
  const uploadFile = pendingImages.value[index]
  if (!uploadFile) return
  editor.imageFiles = editor.imageFiles.filter((x) => x.uid !== uploadFile.uid)
}

async function resetQueue(id: number) {
  await panelApi.resetDictionaryQueue(id)
  await load()
}

async function remove(row: DataDictionary) {
  await ElMessageBox.confirm(`确定删除字典 ${row.displayName} 吗？删除后将同步清理关联图片文件。`, '确认删除', {
    type: 'warning',
    confirmButtonText: '删除',
    cancelButtonText: '取消',
  })
  await panelApi.deleteDictionary(row.id)
  await load()
}

function resetEditor() {
  editor.form.id = null
  editor.form.type = 'text'
  editor.form.name = ''
  editor.form.displayName = ''
  editor.form.description = ''
  editor.form.readMode = 'random'
  editor.form.isEnabled = true
  editor.form.textLines = ''
  editor.existingImages = []
  editor.imageFiles = []
}

function variableName(name: string) {
  return `{${safeText(name)}}`
}

function assetUrl(assetPath: string) {
  return `/${safeText(assetPath).replace(/^\/+/, '')}`
}

function safeText(value: unknown) {
  return String(value ?? '')
}

function formatBytes(bytes: number) {
  if (!Number.isFinite(bytes) || bytes <= 0) return '0 B'
  const units = ['B', 'KB', 'MB', 'GB']
  let value = bytes
  let unit = 0
  while (value >= 1024 && unit < units.length - 1) {
    value /= 1024
    unit++
  }
  return `${value.toFixed(unit === 0 ? 0 : 1)} ${units[unit]}`
}

function sortItems(a: DataDictionaryItem, b: DataDictionaryItem) {
  return a.sortOrder - b.sortOrder || a.id - b.id
}

onMounted(load)
</script>

<style scoped>
.full {
  width: 100%;
}

.row-actions {
  display: flex;
  align-items: center;
  gap: 2px;
}

.image-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(132px, 1fr));
  gap: 12px;
  width: 100%;
}

.image-cell {
  border: 1px solid var(--tp-border);
  border-radius: 4px;
  overflow: hidden;
  background: var(--tp-code-bg);
}

.image-preview {
  width: 100%;
  aspect-ratio: 1;
  display: block;
}

.image-meta {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 6px 8px;
}

.ellipsis {
  min-width: 0;
  flex: 1;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
</style>
