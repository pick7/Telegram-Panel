<template>
  <div class="account-login-page">
    <el-card shadow="never" class="login-flow-card">
      <template #header>
        <div class="card-header">
          <span>{{ stepTitle }}</span>
          <el-tag v-if="loginId > 0" type="info" effect="plain">会话 {{ loginId }}</el-tag>
        </div>
      </template>

      <el-steps :active="stepIndex" finish-status="success" class="steps">
        <el-step title="手机号" />
        <el-step title="验证码" />
        <el-step title="二级密码" />
        <el-step title="完成" />
      </el-steps>

      <div v-if="currentStep === 'phone'" class="step-body">
        <el-alert
          v-if="telegramApiChecked && !telegramApiConfigured"
          type="error"
          :closable="false"
          show-icon
          class="mb-3"
        >
          <template #title>未配置全局 Telegram API（ApiId/ApiHash），无法登录。</template>
          <div class="login-api-warning">
            <span>当前生效 ApiId：{{ effectiveApiId || '未配置' }}</span>
            <el-button size="small" type="primary" @click="router.push('/settings')">去系统设置配置</el-button>
          </div>
        </el-alert>
        <el-alert
          title="手机号需包含国家代码，例如 +8613800138000。系统会使用全局 Telegram API 配置发送验证码。"
          type="info"
          :closable="false"
          show-icon
        />
        <el-form label-position="top" class="mt-4">
          <el-form-item label="手机号">
            <el-input v-model="phone" placeholder="+86xxxxxxxxxx" @keyup.enter="next" />
          </el-form-item>
        </el-form>
      </div>

      <div v-else-if="currentStep === 'code'" class="step-body">
        <el-alert
          :title="`验证码已发送到 ${phone}`"
          description="验证码通常优先发送到已登录设备的 Telegram 系统通知（777000）；没有可用设备时才可能发送短信或电话。"
          type="info"
          :closable="false"
          show-icon
        />
        <el-form label-position="top" class="mt-4">
          <el-form-item label="验证码">
            <el-input v-model="code" placeholder="12345" @keyup.enter="next" />
          </el-form-item>
        </el-form>
        <el-button :loading="logging" :disabled="loginId === 0" @click="resendCode">重新发送验证码（尝试切换通道）</el-button>
      </div>

      <div v-else-if="currentStep === 'password'" class="step-body">
        <el-alert title="此账号启用了两步验证，请输入密码。" type="info" :closable="false" show-icon />
        <el-form label-position="top" class="mt-4">
          <el-form-item label="两步验证密码">
            <el-input v-model="password" type="password" show-password @keyup.enter="next" />
          </el-form-item>
        </el-form>
      </div>

      <div v-else class="step-body">
        <el-result icon="success" title="登录成功并保存到数据库">
          <template #sub-title>
            <div v-if="savedAccount" class="success-lines">
              <div>手机号：{{ savedAccount.displayPhone }}</div>
              <div>用户名：{{ savedAccount.username ? `@${savedAccount.username}` : '无' }}</div>
              <div>用户ID：{{ savedAccount.userId }}</div>
            </div>
          </template>
          <template #extra>
            <el-button type="primary" @click="router.push('/accounts')">查看账号列表</el-button>
            <el-button @click="reset">继续登录下一个账号</el-button>
          </template>
        </el-result>
      </div>

      <template #footer>
        <div class="footer-actions">
          <el-button :disabled="logging" @click="reset">重置</el-button>
          <div class="spacer" />
          <el-button v-if="currentStep === 'code' || currentStep === 'password'" :disabled="logging" @click="previous">上一步</el-button>
          <el-button v-if="currentStep !== 'done'" type="primary" :loading="logging" :disabled="currentStep === 'phone' && telegramApiChecked && !telegramApiConfigured" @click="next">
            {{ primaryButtonText }}
          </el-button>
        </div>
      </template>
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { panelApi } from '@/api/panel'
import type { AccountListItem, AccountLoginResponse } from '@/api/types'

type LoginStep = 'phone' | 'code' | 'password' | 'done'

const router = useRouter()
const logging = ref(false)
const phone = ref('')
const code = ref('')
const password = ref('')
const loginId = ref(0)
const currentStep = ref<LoginStep>('phone')
const savedAccount = ref<AccountListItem | null>(null)
const telegramApiChecked = ref(false)
const telegramApiConfigured = ref(true)
const effectiveApiId = ref('')

const stepIndex = computed(() => {
  if (currentStep.value === 'phone') return 0
  if (currentStep.value === 'code') return 1
  if (currentStep.value === 'password') return 2
  return 4
})

const stepTitle = computed(() => {
  if (currentStep.value === 'phone') return '步骤 1/3：输入手机号并发送验证码'
  if (currentStep.value === 'code') return '步骤 2/3：输入验证码'
  if (currentStep.value === 'password') return '步骤 3/3：输入两步验证密码'
  return '完成'
})

const primaryButtonText = computed(() => {
  if (currentStep.value === 'phone') return '发送验证码'
  if (currentStep.value === 'code') return '验证验证码'
  if (currentStep.value === 'password') return '验证密码'
  return '下一步'
})

async function next() {
  if (logging.value) return
  if (currentStep.value === 'phone' && telegramApiChecked.value && !telegramApiConfigured.value) {
    ElMessage.warning('请先配置全局 Telegram API')
    return
  }

  logging.value = true
  try {
    if (currentStep.value === 'phone') {
      if (!phone.value.trim()) {
        ElMessage.warning('请输入手机号（包含国家代码）')
        return
      }
      const response = await panelApi.startAccountLogin({ phone: phone.value, loginId: loginId.value })
      handleLoginResponse(response)
      return
    }

    if (currentStep.value === 'code') {
      if (loginId.value <= 0) {
        ElMessage.warning('请先发送验证码')
        currentStep.value = 'phone'
        return
      }
      if (!code.value.trim()) {
        ElMessage.warning('请输入验证码')
        return
      }
      const response = await panelApi.submitAccountLoginCode(loginId.value, code.value)
      handleLoginResponse(response)
      return
    }

    if (currentStep.value === 'password') {
      if (loginId.value <= 0) {
        ElMessage.warning('请先发送验证码')
        currentStep.value = 'phone'
        return
      }
      if (!password.value.trim()) {
        ElMessage.warning('请输入两步验证密码')
        return
      }
      const response = await panelApi.submitAccountLoginPassword(loginId.value, password.value)
      handleLoginResponse(response)
    }
  } finally {
    logging.value = false
  }
}

async function loadTelegramApiStatus() {
  try {
    const settings = await panelApi.settings()
    const apiId = (settings.telegram.apiId || '').trim()
    const apiHash = (settings.telegram.apiHash || '').trim()
    effectiveApiId.value = (settings.system.effectiveApiId || apiId || '').trim()
    telegramApiConfigured.value = !!apiId && !!apiHash
  } catch {
    telegramApiConfigured.value = true
  } finally {
    telegramApiChecked.value = true
  }
}

async function resendCode() {
  if (loginId.value <= 0) {
    ElMessage.warning('请先在上一步发送验证码')
    return
  }

  logging.value = true
  try {
    const response = await panelApi.resendAccountLoginCode(loginId.value)
    handleLoginResponse(response, false)
    ElMessage.info(response.message || '已请求重新发送验证码')
  } finally {
    logging.value = false
  }
}

function handleLoginResponse(response: AccountLoginResponse, showMessage = true) {
  loginId.value = response.loginId

  if (response.success) {
    savedAccount.value = response.account || null
    currentStep.value = 'done'
    if (showMessage) ElMessage.success(response.message || '登录成功')
    loginId.value = 0
    return
  }

  if (response.nextStep === 'code') {
    currentStep.value = 'code'
    if (showMessage) ElMessage.info(response.message || '请输入验证码')
    return
  }

  if (response.nextStep === 'password') {
    currentStep.value = 'password'
    if (showMessage) ElMessage.info(response.message || '请输入两步验证密码')
    return
  }

  if (response.nextStep === 'signup') {
    ElMessage.error(response.message || '该手机号需要注册新账号，当前面板不处理注册流程')
    return
  }

  if (response.nextStep === 'email' || response.nextStep === 'email_code') {
    ElMessage.error(response.message || '该账号需要邮箱验证，请使用已保存会话或在 Telegram 客户端完成验证后再导入')
    return
  }

  ElMessage.error(response.message || '登录失败')
}

function previous() {
  if (currentStep.value === 'code') currentStep.value = 'phone'
  else if (currentStep.value === 'password') currentStep.value = 'code'
}

async function reset() {
  if (loginId.value > 0) {
    try {
      await panelApi.resetAccountLogin(loginId.value)
    } catch {
      // 错误已由拦截器提示
    }
  }

  loginId.value = 0
  phone.value = ''
  code.value = ''
  password.value = ''
  savedAccount.value = null
  currentStep.value = 'phone'
}

onBeforeUnmount(() => {
  if (loginId.value > 0) {
    panelApi.resetAccountLogin(loginId.value).catch(() => {})
  }
})

onMounted(loadTelegramApiStatus)
</script>

<style scoped>
.account-login-page {
  min-height: calc(100vh - 96px);
  display: flex;
  justify-content: center;
  align-items: flex-start;
}

.login-flow-card {
  width: min(720px, 100%);
  border-color: var(--tp-border);
}

.steps {
  margin-bottom: 24px;
}

.step-body {
  min-height: 220px;
}

.footer-actions {
  display: flex;
  align-items: center;
  gap: 8px;
}

.spacer {
  flex: 1;
}

.success-lines {
  display: grid;
  gap: 4px;
  color: var(--tp-muted);
}

.login-api-warning {
  display: flex;
  align-items: center;
  gap: 12px;
  flex-wrap: wrap;
  margin-top: 6px;
}
</style>
