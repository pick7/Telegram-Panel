<template>
  <div class="admin-password-page">
    <el-card shadow="never" class="page-card password-card">
      <template #header>修改后台密码</template>

      <el-alert
        v-if="auth.me?.mustChangePassword"
        title="当前仍为初始密码，为安全起见请立即修改。"
        type="warning"
        :closable="false"
        show-icon
        class="mb-3"
      />

      <el-alert
        v-if="auth.me && !auth.me.authEnabled"
        title="后台验证未启用"
        type="warning"
        :closable="false"
        show-icon
        class="mb-3"
      />

      <div class="current-user">当前账号：{{ auth.me?.username || 'admin' }}</div>

      <el-form label-position="top">
        <el-form-item label="当前密码">
          <el-input v-model="form.currentPassword" type="password" show-password autocomplete="current-password" />
        </el-form-item>
        <el-form-item label="新密码">
          <el-input v-model="form.newPassword" type="password" show-password autocomplete="new-password" />
        </el-form-item>
        <el-form-item label="确认新密码">
          <el-input v-model="form.confirmPassword" type="password" show-password autocomplete="new-password" @keyup.enter="save" />
        </el-form-item>
      </el-form>

      <div class="button-row">
        <el-button type="primary" :loading="saving" @click="save">保存</el-button>
      </div>
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { panelApi } from '@/api/panel'
import { useAuthStore } from '@/stores/auth'

const route = useRoute()
const router = useRouter()
const auth = useAuthStore()
const saving = ref(false)
const form = reactive({
  currentPassword: '',
  newPassword: '',
  confirmPassword: '',
})

onMounted(async () => {
  if (!auth.me) await auth.fetchMe()
})

async function save() {
  if (!auth.me?.authEnabled) {
    ElMessage.warning('后台验证未启用')
    return
  }
  if (!form.currentPassword.trim()) {
    ElMessage.warning('请输入当前密码')
    return
  }
  if (form.newPassword.trim().length < 6) {
    ElMessage.warning('新密码至少 6 位')
    return
  }
  if (form.newPassword !== form.confirmPassword) {
    ElMessage.warning('两次输入的新密码不一致')
    return
  }

  saving.value = true
  try {
    const result = await panelApi.changeAdminPassword({
      currentPassword: form.currentPassword,
      newPassword: form.newPassword,
    })
    form.currentPassword = ''
    form.newPassword = ''
    form.confirmPassword = ''
    await auth.fetchMe()
    ElMessage.success(result.message || '密码已更新')
    const returnUrl = typeof route.query.returnUrl === 'string' && route.query.returnUrl.startsWith('/') ? route.query.returnUrl : '/dashboard'
    await router.replace(returnUrl)
  } finally {
    saving.value = false
  }
}
</script>

<style scoped>
.admin-password-page {
  min-width: 0;
}

.password-card {
  max-width: 720px;
}

.current-user {
  margin-bottom: 14px;
  color: var(--tp-muted);
}
</style>
