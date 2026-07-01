<template>
  <el-dialog
    v-model="visible"
    :title="title"
    width="620px"
    class="risk-warning-dialog"
    append-to-body
    :close-on-click-modal="false"
    :close-on-press-escape="false"
    @closed="onClosed"
  >
    <el-alert type="warning" :closable="false" show-icon>
      <template #title>
        <div class="risk-title">风控警告</div>
      </template>
      <div class="risk-message">{{ message || '存在风控风险' }}</div>
      <div v-if="detailedMessage" class="risk-detail">{{ detailedMessage }}</div>
    </el-alert>

    <template v-if="showRecommendations">
      <div class="section-title">建议操作：</div>
      <ul class="recommendations">
        <li>等待满 24 小时后再进行敏感操作</li>
        <li>降低操作频率，避免被 Telegram 风控系统标记</li>
        <li>优先使用低风险操作替代</li>
      </ul>
    </template>

    <template v-if="riskyAccounts.length">
      <div class="section-title">风险账号列表：</div>
      <div class="risk-account-list">
        <el-tag v-for="account in riskyAccounts" :key="account.id" type="warning" effect="plain">
          {{ accountLabel(account) }}
        </el-tag>
      </div>
    </template>

    <template #footer>
      <el-button @click="finish('cancel')">取消操作</el-button>
      <el-button v-if="showExcludeOption && riskyAccounts.length" type="info" @click="finish('exclude')">
        排除风险账号后继续
      </el-button>
      <el-button type="warning" @click="finish('continue')">我了解风险，继续操作</el-button>
    </template>
  </el-dialog>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { ElAlert, ElButton, ElDialog, ElTag } from 'element-plus'
import type { RiskAccount } from '@/api/types'

const props = withDefaults(
  defineProps<{
    title?: string
    message?: string
    detailedMessage?: string
    showRecommendations?: boolean
    showExcludeOption?: boolean
    riskyAccounts?: RiskAccount[]
  }>(),
  {
    title: '风控警告',
    message: '存在风控风险',
    detailedMessage: '',
    showRecommendations: true,
    showExcludeOption: false,
    riskyAccounts: () => [],
  },
)

const emit = defineEmits<{
  cancel: []
  continue: []
  exclude: []
}>()

const visible = ref(true)
let finished = false

function finish(action: 'cancel' | 'continue' | 'exclude') {
  if (finished) return
  finished = true
  visible.value = false
  if (action === 'continue') emit('continue')
  else if (action === 'exclude') emit('exclude')
  else emit('cancel')
}

function onClosed() {
  if (!finished) emit('cancel')
}

function accountLabel(account: RiskAccount) {
  const hours = account.riskReferenceHours == null ? null : `${account.riskReferenceHours.toFixed(1)} 小时`
  const source = account.isEstimated ? '导入' : '登录'
  return hours ? `${account.displayPhone}（${source} ${hours}）` : account.displayPhone
}
</script>

<style scoped>
.risk-title {
  font-weight: 700;
}

.risk-message {
  margin-top: 4px;
  white-space: pre-wrap;
}

.risk-detail {
  margin-top: 8px;
  white-space: pre-wrap;
}

.section-title {
  margin-top: 16px;
  margin-bottom: 8px;
  font-weight: 700;
}

.recommendations {
  margin: 0;
  padding-left: 20px;
  line-height: 1.9;
}

.risk-account-list {
  display: flex;
  max-height: 200px;
  gap: 8px;
  flex-wrap: wrap;
  overflow: auto;
  padding: 10px;
  border: 1px solid var(--el-border-color);
  border-radius: 4px;
  background: var(--el-fill-color-lighter);
}
</style>
