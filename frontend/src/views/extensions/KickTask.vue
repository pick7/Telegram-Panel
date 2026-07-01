<template>
  <div>
    <el-card shadow="never" class="page-card">
      <template #header><span>踢人/封禁</span></template>
      <div class="stack">
        <el-alert
          title="提交后会在后台执行，可到任务中心查看进度与结果。频道数量较多时可能需要较长时间。"
          type="info"
          :closable="false"
          show-icon
        />

        <el-form label-position="top">
          <el-form-item label="选择机器人">
            <el-select v-model="form.botId" class="full" :disabled="loading || submitting" @change="onBotChanged">
              <el-option label="全部机器人（谨慎）" :value="0" />
              <el-option v-for="bot in bots" :key="bot.id" :label="botLabel(bot)" :value="bot.id" />
            </el-select>
          </el-form-item>

          <el-form-item label="用户 ID（每行一个，可多行）">
            <el-input
              v-model="form.userIdsText"
              type="textarea"
              :rows="3"
              placeholder="例如：123456789&#10;987654321"
              :disabled="submitting"
            />
            <div class="cell-sub">支持逗号、空格、换行分隔；已解析 {{ parsedUserIds.length }} 个用户 ID</div>
          </el-form-item>

          <el-form-item>
            <el-switch v-model="form.permanentBan" active-text="永久封禁（否则仅踢出，可重新加入）" :disabled="submitting" />
          </el-form-item>

          <el-form-item>
            <el-switch
              v-model="form.useAllChats"
              active-text="使用全部频道/群组（关闭后可按分类筛选或输入 chat_id）"
              :disabled="submitting || form.botId === 0"
              @change="onUseAllChatsChanged"
            />
          </el-form-item>

          <template v-if="form.botId !== 0 && !form.useAllChats">
            <div class="toolbar mb-3">
              <span class="muted">选择分类：{{ form.categoryIds.length }} / {{ categories.length }}</span>
              <div class="toolbar-spacer" />
              <el-button :disabled="loading || categories.length === 0" @click="selectAllCategories">全选</el-button>
              <el-button :disabled="loading || form.categoryIds.length === 0" @click="clearCategories">清空</el-button>
            </div>

            <el-table
              ref="categoryTableRef"
              v-loading="loading"
              :data="categories"
              row-key="id"
              stripe
              class="mb-3"
              @selection-change="onCategorySelectionChange"
            >
              <el-table-column type="selection" width="48" />
              <el-table-column prop="name" label="分类" min-width="180" />
              <el-table-column prop="description" label="说明" min-width="240">
                <template #default="{ row }">{{ row.description || '-' }}</template>
              </el-table-column>
            </el-table>

            <el-form-item>
              <el-checkbox v-model="form.includeUncategorized" :disabled="submitting">包含未分类</el-checkbox>
            </el-form-item>

            <el-form-item label="chat_id 列表（可选）">
              <el-input
                v-model="form.chatIdsText"
                type="textarea"
                :rows="3"
                placeholder="支持逗号/空格/换行分隔，例如：123,456,789"
                :disabled="submitting"
              />
              <div class="cell-sub">已解析 {{ parsedChatIds.length }} 个 chat_id</div>
            </el-form-item>
          </template>
        </el-form>

        <div class="toolbar">
          <el-button type="danger" :icon="RemoveFilled" :loading="submitting" :disabled="!canSubmit" @click="submit">
            提交任务
          </el-button>
          <el-button :icon="Tickets" @click="router.push('/tasks')">打开任务中心</el-button>
        </div>
      </div>
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { useRouter } from 'vue-router'
import { RemoveFilled, Tickets } from '@element-plus/icons-vue'
import { ElMessage } from 'element-plus'
import type { TableInstance } from 'element-plus'
import { panelApi } from '@/api/panel'
import type { BotOption, SimpleCategory } from '@/api/types'

const router = useRouter()
const loading = ref(false)
const submitting = ref(false)
const bots = ref<BotOption[]>([])
const categories = ref<SimpleCategory[]>([])
const categoryTableRef = ref<TableInstance>()

const form = reactive({
  botId: 0,
  userIdsText: '',
  permanentBan: false,
  useAllChats: true,
  categoryIds: [] as number[],
  includeUncategorized: false,
  chatIdsText: '',
})

const parsedUserIds = computed(() => parseLongIds(form.userIdsText).filter((x) => x > 0))
const parsedChatIds = computed(() => parseLongIds(form.chatIdsText).filter((x) => x !== 0))
const hasTargetScope = computed(() => form.botId === 0 || form.useAllChats || form.includeUncategorized || form.categoryIds.length > 0 || parsedChatIds.value.length > 0)
const canSubmit = computed(() => !loading.value && !submitting.value && parsedUserIds.value.length > 0 && hasTargetScope.value)

async function load() {
  loading.value = true
  try {
    const [botRows, categoryRows] = await Promise.all([panelApi.externalApiBots(), panelApi.botChannelCategories()])
    bots.value = botRows.filter((x) => x.isActive)
    categories.value = categoryRows
    if (bots.value.length > 0) {
      form.botId = bots.value[0].id
      form.useAllChats = false
    }
  } finally {
    loading.value = false
  }
}

function botLabel(bot: BotOption) {
  return bot.username ? `${bot.name}（@${bot.username}）` : bot.name
}

function onBotChanged() {
  if (form.botId === 0) form.useAllChats = true
  else form.useAllChats = false
  clearTargetScope()
}

function onUseAllChatsChanged() {
  if (form.useAllChats) clearTargetScope()
}

function onCategorySelectionChange(selection: SimpleCategory[]) {
  form.categoryIds = selection.map((x) => x.id)
}

function selectAllCategories() {
  categories.value.forEach((row) => categoryTableRef.value?.toggleRowSelection(row, true))
}

function clearCategories() {
  categoryTableRef.value?.clearSelection()
}

function clearTargetScope() {
  form.categoryIds = []
  form.includeUncategorized = false
  form.chatIdsText = ''
  categoryTableRef.value?.clearSelection()
}

async function submit() {
  if (parsedUserIds.value.length === 0) {
    ElMessage.warning('请输入至少一个用户 ID')
    return
  }
  if (!hasTargetScope.value) {
    ElMessage.warning('请至少选择一个分类、包含未分类或填写 chat_id')
    return
  }

  submitting.value = true
  try {
    const result = await panelApi.createKickTasks({
      botId: form.botId,
      useAllChats: form.botId === 0 || form.useAllChats,
      categoryIds: form.categoryIds,
      includeUncategorized: form.includeUncategorized,
      chatIds: parsedChatIds.value,
      userIds: parsedUserIds.value,
      permanentBan: form.permanentBan,
    })
    ElMessage.success(`已提交 ${result.tasks.length} 个任务。请到任务中心查看进度。`)
    router.push('/tasks')
  } finally {
    submitting.value = false
  }
}

function parseLongIds(raw: string) {
  const seen = new Set<number>()
  const ids: number[] = []
  raw
    .replace(/\r/g, '\n')
    .split(/[\s,;]+/)
    .map((x) => x.trim())
    .filter(Boolean)
    .forEach((part) => {
      const value = Number(part)
      if (!Number.isSafeInteger(value)) return
      if (seen.has(value)) return
      seen.add(value)
      ids.push(value)
    })
  return ids.sort((a, b) => a - b)
}

onMounted(load)
</script>
