<template>
  <div>
    <el-card shadow="never" class="page-card">
      <template #header><span>Fragment 用户名监控</span></template>
      <div class="stack">
        <el-alert
          title="定时抢注模式：系统会持续监控用户名状态，一旦检测到 Unavailable，就会从所选分类的私密频道池里挑一个频道，直接切成公开并占用该用户名。"
          type="info"
          :closable="false"
          show-icon
        />

        <el-form label-position="top">
          <el-form-item label="监控用户名列表（一行一个）">
            <el-input
              v-model="form.usernamesText"
              type="textarea"
              :rows="8"
              placeholder="example1&#10;example2&#10;example3"
              :disabled="loading || submitting"
            />
            <div class="cell-sub">已输入 {{ parsedUsernames.length }} 个用户名</div>
          </el-form-item>

          <el-divider />

          <div class="section-title">选择目标频道分类</div>
          <el-alert
            title="用户名可注册时，系统只会使用选中分类下的私密频道。频道必须是你创建的，且当前没有公开用户名。"
            type="warning"
            :closable="false"
            show-icon
            class="mb-3"
          />

          <div class="toolbar mb-3">
            <span class="muted">选择分类：{{ selectedGroupIds.length }} / {{ groups.length }}</span>
            <div class="toolbar-spacer" />
            <el-button :disabled="loading || groups.length === 0" @click="selectAllGroups">全选</el-button>
            <el-button :disabled="loading || selectedGroupIds.length === 0" @click="clearGroups">清空</el-button>
          </div>

          <el-table
            ref="groupTableRef"
            v-loading="loading"
            :data="groups"
            row-key="id"
            stripe
            class="mb-3"
            @selection-change="onGroupSelectionChange"
          >
            <el-table-column type="selection" width="48" />
            <el-table-column prop="name" label="分类名称" min-width="180" />
            <el-table-column prop="privateChannelCount" label="可用私密频道数" width="150" />
            <el-table-column prop="description" label="说明" min-width="220">
              <template #default="{ row }">{{ row.description || '-' }}</template>
            </el-table-column>
          </el-table>

          <el-empty v-if="!loading && groups.length === 0" description="暂无频道分类，请先创建频道分类或同步频道数据" />

          <el-divider />

          <div class="section-title">监控设置</div>
          <el-row :gutter="12">
            <el-col :xs="24" :sm="8">
              <el-form-item label="检查间隔（秒）">
                <el-input-number v-model="form.checkIntervalSeconds" :min="60" :max="3600" :step="30" class="full" :disabled="submitting" />
                <div class="cell-sub">每轮监控结束后的等待时间，建议 300 秒</div>
              </el-form-item>
            </el-col>
            <el-col :xs="24" :sm="8">
              <el-form-item label="查询延迟（毫秒）">
                <el-input-number v-model="form.queryDelayMs" :min="500" :max="5000" :step="100" class="full" :disabled="submitting" />
                <div class="cell-sub">同一轮内逐个检查用户名时的间隔</div>
              </el-form-item>
            </el-col>
            <el-col :xs="24" :sm="8">
              <el-form-item label="运行时长（小时）">
                <el-input-number v-model="form.durationHours" :min="0" :max="720" :step="1" class="full" :disabled="submitting" />
                <div class="cell-sub">0 表示持续运行，程序重启后会尝试恢复</div>
              </el-form-item>
            </el-col>
          </el-row>
        </el-form>

        <div class="toolbar">
          <el-button type="success" :icon="CirclePlus" :loading="submitting" :disabled="!canSubmit" @click="submit">
            创建监控任务
          </el-button>
          <el-button :icon="Tickets" @click="router.push('/tasks')">查看任务中心</el-button>
        </div>
      </div>
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { useRouter } from 'vue-router'
import { CirclePlus, Tickets } from '@element-plus/icons-vue'
import { ElMessage } from 'element-plus'
import type { TableInstance } from 'element-plus'
import { panelApi } from '@/api/panel'
import type { FragmentChannelGroupOption } from '@/api/types'

const router = useRouter()
const loading = ref(false)
const submitting = ref(false)
const groups = ref<FragmentChannelGroupOption[]>([])
const selectedGroupIds = ref<number[]>([])
const groupTableRef = ref<TableInstance>()

const form = reactive({
  usernamesText: '',
  checkIntervalSeconds: 300,
  queryDelayMs: 1500,
  durationHours: 0,
})

const parsedUsernames = computed(() => {
  const seen = new Set<string>()
  const result: string[] = []
  form.usernamesText
    .split(/\s+/)
    .map((x) => x.trim().replace(/^@+/, '').toLowerCase())
    .filter(Boolean)
    .forEach((name) => {
      if (!/^[a-z][a-z0-9_]{4,31}$/.test(name)) return
      if (seen.has(name)) return
      seen.add(name)
      result.push(name)
    })
  return result
})

const canSubmit = computed(() => !loading.value && !submitting.value && parsedUsernames.value.length > 0 && selectedGroupIds.value.length > 0)

async function load() {
  loading.value = true
  try {
    groups.value = await panelApi.fragmentUsernameChannelGroups()
  } finally {
    loading.value = false
  }
}

function onGroupSelectionChange(selection: FragmentChannelGroupOption[]) {
  selectedGroupIds.value = selection.map((x) => x.id)
}

function selectAllGroups() {
  groups.value.forEach((row) => groupTableRef.value?.toggleRowSelection(row, true))
}

function clearGroups() {
  groupTableRef.value?.clearSelection()
}

async function submit() {
  if (parsedUsernames.value.length === 0) {
    ElMessage.warning('请输入至少一个合法用户名')
    return
  }
  if (selectedGroupIds.value.length === 0) {
    ElMessage.warning('请至少选择一个频道分类')
    return
  }

  submitting.value = true
  try {
    const task = await panelApi.createFragmentUsernameTask({
      usernames: parsedUsernames.value,
      targetGroupIds: selectedGroupIds.value,
      checkIntervalSeconds: form.checkIntervalSeconds,
      queryDelayMs: form.queryDelayMs,
      durationHours: form.durationHours,
    })
    ElMessage.success(`监控任务已创建（ID: ${task.id}），将自动开始运行`)
    router.push('/tasks')
  } finally {
    submitting.value = false
  }
}

onMounted(load)
</script>
