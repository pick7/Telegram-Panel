<template>
  <div>
    <el-alert
      title="外部 API 配置会写入本地 appsettings.local.json；接口通过 X-API-Key 区分不同配置项。"
      type="info"
      :closable="false"
      show-icon
      class="mb-3"
    />

    <el-card shadow="never" class="page-card">
      <div class="toolbar">
        <div class="toolbar-title">外部 API ({{ apis.length }})</div>
        <div class="toolbar-spacer" />
        <el-button :icon="Refresh" :loading="loading" @click="load">刷新</el-button>
        <el-button type="primary" :icon="Plus" :disabled="types.length === 0" @click="openCreate">新建 API</el-button>
      </div>
    </el-card>

    <el-card shadow="never" class="page-card mt-4">
      <el-table v-loading="loading" :data="apis" stripe>
        <el-table-column label="API" min-width="240">
          <template #default="{ row }">
            <div class="cell-main">{{ row.name }}</div>
            <div class="cell-sub">{{ row.id }}</div>
          </template>
        </el-table-column>
        <el-table-column label="类型" min-width="180">
          <template #default="{ row }">
            <div>{{ row.typeName }}</div>
            <div class="cell-sub">{{ row.type }}</div>
          </template>
        </el-table-column>
        <el-table-column label="接口" min-width="180">
          <template #default="{ row }">{{ row.route || '-' }}</template>
        </el-table-column>
        <el-table-column label="状态" width="140">
          <template #default="{ row }">
            <el-tag :type="row.enabled ? 'success' : 'info'" size="small">{{ row.enabled ? '启用' : '停用' }}</el-tag>
            <el-tag v-if="!row.typeAvailable" type="danger" size="small" class="ml-2">类型停用</el-tag>
          </template>
        </el-table-column>
        <el-table-column label="操作" width="230" fixed="right">
          <template #default="{ row }">
            <el-button link type="primary" @click="openEdit(row)">编辑</el-button>
            <el-button link @click="showCurl(row)">curl 示例</el-button>
            <el-button link type="danger" @click="remove(row)">删除</el-button>
          </template>
        </el-table-column>
      </el-table>
    </el-card>

    <el-dialog v-model="createDialog.visible" title="新建 API" width="560px" destroy-on-close>
      <el-alert
        title="创建后可继续编辑 X-API-Key、机器人和频道/群组等详细配置。"
        type="info"
        :closable="false"
        class="mb-3"
      />
      <el-form label-width="96px">
        <el-form-item label="API 名称">
          <el-input v-model="createDialog.form.name" placeholder="用于面板内区分不同 API 配置" />
        </el-form-item>
        <el-form-item label="API 类型">
          <el-select v-model="createDialog.form.type" class="full">
            <el-option v-for="item in types" :key="item.type" :label="`${item.displayName}（${item.route}）`" :value="item.type" />
          </el-select>
        </el-form-item>
        <el-form-item label="立即启用">
          <el-switch v-model="createDialog.form.enabled" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button :disabled="createDialog.saving" @click="createDialog.visible = false">取消</el-button>
        <el-button type="primary" :loading="createDialog.saving" @click="createApi">创建</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="editDialog.visible" :title="editTitle" width="920px" destroy-on-close>
      <el-form label-width="112px">
        <el-alert
          v-if="editDialog.form.route"
          :title="`外部接口：${editDialog.form.route}（Type: ${editDialog.form.type}）`"
          type="info"
          :closable="false"
          class="mb-3"
        />
        <el-alert
          v-if="!editDialog.form.typeAvailable"
          title="该 API 类型对应模块已停用，当前配置不会生效；重启后仍不会提供接口。"
          type="warning"
          :closable="false"
          show-icon
          class="mb-3"
        />
        <el-row :gutter="12">
          <el-col :span="12">
            <el-form-item label="API 名称">
              <el-input v-model="editDialog.form.name" />
            </el-form-item>
          </el-col>
          <el-col :span="12">
            <el-form-item label="启用">
              <el-switch v-model="editDialog.form.enabled" />
            </el-form-item>
          </el-col>
        </el-row>
        <el-form-item label="X-API-Key">
          <div class="key-row">
            <el-input v-model="editDialog.form.apiKey" />
            <el-button @click="regenerateKey">重新生成 Key</el-button>
          </div>
        </el-form-item>

        <template v-if="isKickEditor">
          <el-divider content-position="left">踢人/封禁配置</el-divider>
          <el-row :gutter="12">
            <el-col :span="12">
              <el-form-item label="默认永久封禁">
                <el-switch v-model="editDialog.form.kick.permanentBanDefault" />
              </el-form-item>
            </el-col>
            <el-col :span="12">
              <el-form-item label="工作机器人">
                <el-select v-model="editDialog.form.kick.botId" class="full" @change="onBotChanged">
                  <el-option label="所有启用机器人" :value="0" />
                  <el-option
                    v-for="bot in bots"
                    :key="bot.id"
                    :label="botLabel(bot)"
                    :value="bot.id"
                    :disabled="!bot.isActive"
                  />
                </el-select>
              </el-form-item>
            </el-col>
          </el-row>
          <el-form-item v-if="editDialog.form.kick.botId > 0" label="频道范围">
            <el-radio-group v-model="chatScope" @change="onChatScopeChanged">
              <el-radio-button label="all">该机器人全部频道/群组</el-radio-button>
              <el-radio-button label="selected">指定频道/群组</el-radio-button>
            </el-radio-group>
          </el-form-item>
          <template v-if="editDialog.form.kick.botId > 0 && chatScope === 'selected'">
            <el-form-item label="搜索">
              <div class="key-row">
                <el-input v-model="chatSearch" placeholder="搜索标题 / 用户名 / ChatId" clearable />
                <el-button :loading="loadingChats" @click="loadBotChats">刷新频道/群组</el-button>
                <el-button @click="selectFilteredChats">全选当前筛选</el-button>
                <el-button @click="clearChats">清空</el-button>
              </div>
            </el-form-item>
            <el-form-item label="频道/群组">
              <el-table
                v-loading="loadingChats"
                :data="filteredChats"
                height="300"
                row-key="telegramId"
                @selection-change="onChatSelectionChanged"
                ref="chatTableRef"
              >
                <el-table-column type="selection" width="48" />
                <el-table-column label="标题" min-width="220">
                  <template #default="{ row }">
                    <div class="cell-main">{{ row.title }}</div>
                    <div class="cell-sub">{{ row.username ? `@${row.username}` : row.telegramId }}</div>
                  </template>
                </el-table-column>
                <el-table-column label="类型" width="90">
                  <template #default="{ row }">{{ row.isBroadcast ? '频道' : '群组' }}</template>
                </el-table-column>
                <el-table-column prop="memberCount" label="成员" width="100" />
              </el-table>
            </el-form-item>
          </template>
        </template>

        <template v-else>
          <el-form-item label="Config JSON">
            <el-input
              v-model="editDialog.configJson"
              type="textarea"
              :rows="10"
              placeholder="该 JSON 会写入 ExternalApi:Apis[].Config，由对应模块自行解析。"
            />
          </el-form-item>
        </template>
      </el-form>
      <template #footer>
        <el-button :disabled="editDialog.saving" @click="editDialog.visible = false">取消</el-button>
        <el-button type="primary" :loading="editDialog.saving" @click="saveEdit">保存</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="curlDialog.visible" title="调用示例（curl）" width="760px">
      <pre class="detail-pre">{{ curlDialog.content }}</pre>
      <template #footer>
        <el-button @click="copyCurl">复制</el-button>
        <el-button type="primary" @click="curlDialog.visible = false">关闭</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { computed, nextTick, onMounted, reactive, ref } from 'vue'
import { ElMessage, ElMessageBox, type TableInstance } from 'element-plus'
import { Plus, Refresh } from '@element-plus/icons-vue'
import { panelApi } from '@/api/panel'
import type {
  BotChatOption,
  BotOption,
  ExternalApiDefinition,
  ExternalApiType,
  KickApiConfig,
  SaveExternalApiRequest,
} from '@/api/types'

const loading = ref(false)
const apis = ref<ExternalApiDefinition[]>([])
const types = ref<ExternalApiType[]>([])
const bots = ref<BotOption[]>([])
const chats = ref<BotChatOption[]>([])
const loadingChats = ref(false)
const chatSearch = ref('')
const chatScope = ref<'all' | 'selected'>('all')
const chatTableRef = ref<TableInstance>()

const createDialog = reactive({
  visible: false,
  saving: false,
  form: {
    name: '',
    type: '',
    enabled: false,
  },
})

const editDialog = reactive({
  visible: false,
  saving: false,
  configJson: '{}',
  form: {
    id: '',
    name: '',
    type: '',
    typeName: '',
    route: '',
    typeAvailable: true,
    enabled: false,
    apiKey: '',
    config: {} as Record<string, unknown>,
    kick: {
      botId: 0,
      useAllChats: true,
      chatIds: [] as number[],
      permanentBanDefault: false,
    } as KickApiConfig,
  },
})

const curlDialog = reactive({
  visible: false,
  content: '',
})

const isKickEditor = computed(() => editDialog.form.type.toLowerCase() === 'kick')
const editTitle = computed(() => (isKickEditor.value ? '编辑 API：踢人/封禁' : '编辑 API'))

const filteredChats = computed(() => {
  const text = chatSearch.value.trim().toLowerCase()
  if (!text) return chats.value
  return chats.value.filter((chat) =>
    chat.title.toLowerCase().includes(text)
    || (chat.username || '').toLowerCase().includes(text)
    || String(chat.telegramId).includes(text),
  )
})

async function load() {
  loading.value = true
  try {
    const center = await panelApi.externalApis()
    apis.value = center.apis
    types.value = center.types
    if (!createDialog.form.type) createDialog.form.type = types.value[0]?.type || ''
  } finally {
    loading.value = false
  }
}

function openCreate() {
  createDialog.form.name = ''
  createDialog.form.type = types.value[0]?.type || ''
  createDialog.form.enabled = false
  createDialog.visible = true
}

async function createApi() {
  const name = createDialog.form.name.trim()
  if (!name) {
    ElMessage.warning('API 名称不能为空')
    return
  }
  if (!types.value.some((x) => x.type === createDialog.form.type)) {
    ElMessage.warning('请选择有效的 API 类型')
    return
  }

  const type = createDialog.form.type
  createDialog.saving = true
  try {
    const api = await panelApi.saveExternalApi({
      name,
      type,
      enabled: createDialog.form.enabled,
      apiKey: generateKey(),
      config: {},
      kick: type === 'kick' ? defaultKickConfig() : null,
    })
    createDialog.visible = false
    ElMessage.success('API 已创建')
    await load()
    openEdit(api)
  } finally {
    createDialog.saving = false
  }
}

async function openEdit(api: ExternalApiDefinition) {
  editDialog.form.id = api.id
  editDialog.form.name = api.name
  editDialog.form.type = api.type
  editDialog.form.typeName = api.typeName
  editDialog.form.route = api.route || ''
  editDialog.form.typeAvailable = api.typeAvailable
  editDialog.form.enabled = api.enabled
  editDialog.form.apiKey = api.apiKey || ''
  editDialog.form.config = api.config || {}
  editDialog.form.kick = cloneKick(api.kick)
  editDialog.configJson = JSON.stringify(api.config || {}, null, 2)
  chats.value = []
  chatSearch.value = ''
  chatScope.value = editDialog.form.kick.useAllChats ? 'all' : 'selected'
  editDialog.visible = true

  if (api.type.toLowerCase() === 'kick') {
    await loadBots()
    await loadBotChatsIfNeeded()
  }
}

async function loadBots() {
  try {
    bots.value = await panelApi.externalApiBots()
  } catch {
    bots.value = []
  }
}

async function loadBotChatsIfNeeded() {
  if (editDialog.form.kick.botId <= 0 || chatScope.value === 'all') {
    chats.value = []
    return
  }
  await loadBotChats()
}

async function loadBotChats() {
  const botId = editDialog.form.kick.botId
  if (botId <= 0) return
  loadingChats.value = true
  try {
    chats.value = await panelApi.externalApiBotChats(botId)
    await nextTick()
    restoreChatSelection()
  } finally {
    loadingChats.value = false
  }
}

async function onBotChanged() {
  if (editDialog.form.kick.botId <= 0) {
    chatScope.value = 'all'
    editDialog.form.kick.useAllChats = true
    editDialog.form.kick.chatIds = []
    chats.value = []
    return
  }
  editDialog.form.kick.chatIds = []
  await loadBotChatsIfNeeded()
}

function onChatScopeChanged() {
  editDialog.form.kick.useAllChats = chatScope.value === 'all'
  if (chatScope.value === 'all') {
    editDialog.form.kick.chatIds = []
    chats.value = []
  } else {
    loadBotChats()
  }
}

function onChatSelectionChanged(selection: BotChatOption[]) {
  editDialog.form.kick.chatIds = selection.map((x) => x.telegramId)
}

function restoreChatSelection() {
  const table = chatTableRef.value
  if (!table) return
  table.clearSelection()
  const selected = new Set(editDialog.form.kick.chatIds)
  chats.value.forEach((chat) => {
    if (selected.has(chat.telegramId)) table.toggleRowSelection(chat, true)
  })
}

async function selectFilteredChats() {
  await nextTick()
  const table = chatTableRef.value
  if (!table) return
  filteredChats.value.forEach((chat) => table.toggleRowSelection(chat, true))
}

function clearChats() {
  chatTableRef.value?.clearSelection()
  editDialog.form.kick.chatIds = []
}

function regenerateKey() {
  editDialog.form.apiKey = generateKey()
  ElMessage.info('已重新生成 Key，保存后生效')
}

async function saveEdit() {
  const payload = buildSavePayload()
  if (!payload) return

  editDialog.saving = true
  try {
    await panelApi.saveExternalApi(payload)
    editDialog.visible = false
    ElMessage.success('API 已保存')
    await load()
  } finally {
    editDialog.saving = false
  }
}

function buildSavePayload(): SaveExternalApiRequest | null {
  const name = editDialog.form.name.trim()
  const apiKey = editDialog.form.apiKey.trim()
  if (!name) {
    ElMessage.warning('API 名称不能为空')
    return null
  }
  if (!apiKey) {
    ElMessage.warning('请先设置 X-API-Key')
    return null
  }

  if (isKickEditor.value) {
    const kick = editDialog.form.kick
    kick.useAllChats = kick.botId === 0 || chatScope.value === 'all'
    if (kick.botId > 0 && !kick.useAllChats && kick.chatIds.length === 0) {
      ElMessage.warning('请至少选择一个频道/群组')
      return null
    }
    if (kick.useAllChats) kick.chatIds = []
    return {
      id: editDialog.form.id,
      name,
      type: editDialog.form.type,
      enabled: editDialog.form.enabled,
      apiKey,
      config: {},
      kick: { ...kick, chatIds: [...new Set(kick.chatIds)] },
    }
  }

  try {
    const parsed = JSON.parse(editDialog.configJson || '{}')
    if (!parsed || Array.isArray(parsed) || typeof parsed !== 'object') {
      ElMessage.warning('Config 必须是 JSON 对象（例如 {}）')
      return null
    }
    return {
      id: editDialog.form.id,
      name,
      type: editDialog.form.type,
      enabled: editDialog.form.enabled,
      apiKey,
      config: parsed,
      kick: null,
    }
  } catch (error) {
    ElMessage.warning(`Config JSON 无效：${error instanceof Error ? error.message : String(error)}`)
    return null
  }
}

async function remove(api: ExternalApiDefinition) {
  await ElMessageBox.confirm(`确定删除 API「${api.name}」吗？此操作不可恢复。`, '确认删除', { type: 'warning' })
  await panelApi.deleteExternalApi(api.id)
  ElMessage.success('已删除')
  await load()
}

function showCurl(api: ExternalApiDefinition) {
  if (!api.route) {
    ElMessage.warning('未知 API 类型')
    return
  }
  if (!api.apiKey) {
    ElMessage.warning('该 API 未配置 X-API-Key')
    return
  }
  const url = new URL(api.route, window.location.origin).toString()
  const body = api.type.toLowerCase() === 'kick' ? '{"user_id":123456789}' : '{}'
  curlDialog.content = [
    `curl -X POST "${url}" \\`,
    '  -H "Content-Type: application/json" \\',
    `  -H "X-API-Key: ${api.apiKey}" \\`,
    `  -d '${body}'`,
  ].join('\n')
  curlDialog.visible = true
}

async function copyCurl() {
  await navigator.clipboard.writeText(curlDialog.content)
  ElMessage.success('已复制')
}

function defaultKickConfig(): KickApiConfig {
  return {
    botId: 0,
    useAllChats: true,
    chatIds: [],
    permanentBanDefault: false,
  }
}

function cloneKick(kick?: KickApiConfig | null): KickApiConfig {
  return {
    botId: kick?.botId ?? 0,
    useAllChats: kick?.useAllChats ?? true,
    chatIds: [...(kick?.chatIds || [])],
    permanentBanDefault: kick?.permanentBanDefault ?? false,
  }
}

function botLabel(bot: BotOption) {
  const username = bot.username ? `@${bot.username}` : ''
  const state = bot.isActive ? '' : '（停用）'
  return `${bot.name} ${username}${state}`.trim()
}

function generateKey() {
  const bytes = new Uint8Array(32)
  crypto.getRandomValues(bytes)
  let binary = ''
  bytes.forEach((byte) => { binary += String.fromCharCode(byte) })
  return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/g, '')
}

onMounted(load)
</script>

<style scoped>
.toolbar-title {
  font-weight: 650;
}

.toolbar-spacer {
  flex: 1;
}

.full {
  width: 100%;
}

.key-row {
  display: flex;
  gap: 8px;
  width: 100%;
}

.detail-pre {
  margin: 0;
  white-space: pre-wrap;
  word-break: break-word;
  color: var(--tp-text);
  background: #111827;
  border: 1px solid var(--tp-border);
  border-radius: 4px;
  padding: 12px;
}

.ml-2 {
  margin-left: 8px;
}

@media (max-width: 700px) {
  .key-row {
    flex-direction: column;
  }
}
</style>
