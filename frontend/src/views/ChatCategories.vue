<template>
  <div class="category-grid">
    <el-card shadow="never" class="page-card">
      <template #header><span>{{ editingId ? '编辑分类' : '添加分类' }}</span></template>
      <el-form label-width="80px">
        <el-form-item label="分类名称"><el-input v-model="form.name" /></el-form-item>
        <el-form-item label="描述"><el-input v-model="form.description" type="textarea" :rows="3" /></el-form-item>
        <el-form-item>
          <el-button v-if="editingId" @click="cancelEdit">取消</el-button>
          <el-button type="primary" :disabled="!form.name.trim()" @click="saveCategory">
            {{ editingId ? '保存修改' : '添加分类' }}
          </el-button>
        </el-form-item>
      </el-form>
    </el-card>

    <div>
      <el-card shadow="never" class="page-card">
        <template #header><span>分类列表</span></template>
        <el-table v-loading="loading" :data="categories" stripe>
          <el-table-column prop="name" label="分类名称" min-width="160" />
          <el-table-column prop="description" label="描述" min-width="180" />
          <el-table-column prop="itemCount" :label="`${kindName}数量`" width="110" />
          <el-table-column label="操作" width="150">
            <template #default="{ row }">
              <el-button link type="primary" @click="beginEdit(row)">编辑</el-button>
              <el-button link type="danger" @click="deleteCategory(row)">删除</el-button>
            </template>
          </el-table-column>
        </el-table>
      </el-card>

      <el-card shadow="never" class="page-card mt-4">
        <template #header><span>分类绑定{{ kindName }}</span></template>
        <div class="toolbar mb-3">
          <el-select v-model="assignCategoryId" class="filter" placeholder="选择分类" @change="restoreSelection">
            <el-option label="-- 请选择分类 --" :value="0" />
            <el-option v-for="category in categories" :key="category.id" :label="category.name" :value="category.id" />
          </el-select>
          <el-select v-model="accountId" class="resource-filter" placeholder="筛选账号" @change="loadResources">
            <el-option label="全部账号" :value="0" />
            <el-option v-for="account in accounts" :key="account.id" :label="accountLabel(account)" :value="account.id" />
          </el-select>
          <el-button :loading="resourceLoading" @click="loadResources">刷新</el-button>
        </div>
        <el-table ref="tableRef" v-loading="resourceLoading" :data="resources" row-key="id" height="420" @selection-change="onSelectionChange">
          <el-table-column type="selection" width="46" />
          <el-table-column prop="title" :label="`${kindName}名称`" min-width="200" />
          <el-table-column label="用户名" width="150">
            <template #default="{ row }">{{ row.username ? `@${row.username}` : '-' }}</template>
          </el-table-column>
          <el-table-column label="创建账号" width="150">
            <template #default="{ row }">{{ row.creatorDisplayPhone || (row.creatorAccountId ? `账号 ${row.creatorAccountId}` : '非系统创建') }}</template>
          </el-table-column>
          <el-table-column label="当前分类" width="140">
            <template #default="{ row }">{{ rowCategoryName(row) || '未分类' }}</template>
          </el-table-column>
        </el-table>
        <div class="mt-4">
          <el-button type="primary" :disabled="assignCategoryId <= 0 || resourceLoading" @click="saveAssignments">
            保存勾选到分类
          </el-button>
        </div>
      </el-card>
    </div>
  </div>
</template>

<script setup lang="ts">
import { nextTick, onMounted, reactive, ref } from 'vue'
import { ElMessage, ElMessageBox, type TableInstance } from 'element-plus'
import { panelApi } from '@/api/panel'
import type { ChannelListItem, GroupListItem, OperationAccount, SimpleCategory } from '@/api/types'

type Kind = 'channel' | 'group'
type Resource = ChannelListItem | GroupListItem
const props = defineProps<{ kind: Kind }>()
const kindName = props.kind === 'channel' ? '频道' : '群组'

const loading = ref(false)
const resourceLoading = ref(false)
const categories = ref<SimpleCategory[]>([])
const accounts = ref<OperationAccount[]>([])
const resources = ref<Resource[]>([])
const tableRef = ref<TableInstance>()
const selectedIds = ref<number[]>([])
const editingId = ref(0)
const assignCategoryId = ref(0)
const accountId = ref(0)
const form = reactive({ name: '', description: '' })

function accountLabel(account: OperationAccount) {
  const name = account.nickname || account.displayPhone
  return account.username ? `${name} (@${account.username})` : name
}

function rowCategoryId(row: Resource) {
  return props.kind === 'channel' ? (row as ChannelListItem).groupId : (row as GroupListItem).categoryId
}

function rowCategoryName(row: Resource) {
  return props.kind === 'channel' ? (row as ChannelListItem).groupName : (row as GroupListItem).categoryName
}

async function loadCategories() {
  loading.value = true
  try {
    categories.value = props.kind === 'channel' ? await panelApi.channelGroups() : await panelApi.groupCategories()
  } finally {
    loading.value = false
  }
}

async function loadResources() {
  resourceLoading.value = true
  try {
    resources.value = await loadAllResources()
    await restoreSelection()
  } finally {
    resourceLoading.value = false
  }
}

async function loadAllResources() {
  const pageSize = 500
  const items: Resource[] = []
  let currentPage = 1
  let total = 0

  do {
    const params = {
      page: currentPage,
      pageSize,
      accountId: accountId.value || null,
      filterType: 'all',
      membershipRole: 'all',
      search: '',
    }
    const result = props.kind === 'channel'
      ? await panelApi.channels({ ...params, groupId: -1 })
      : await panelApi.groups({ ...params, categoryId: -1 })

    items.push(...result.items)
    total = result.total
    currentPage += 1
  } while (items.length < total)

  return items
}

function beginEdit(category: SimpleCategory) {
  editingId.value = category.id
  form.name = category.name
  form.description = category.description || ''
}

function cancelEdit() {
  editingId.value = 0
  form.name = ''
  form.description = ''
}

async function saveCategory() {
  const payload = { name: form.name.trim(), description: form.description.trim() || null }
  if (props.kind === 'channel') {
    if (editingId.value) await panelApi.updateChannelGroup(editingId.value, payload)
    else await panelApi.createChannelGroup(payload)
  } else {
    if (editingId.value) await panelApi.updateGroupCategory(editingId.value, payload)
    else await panelApi.createGroupCategory(payload)
  }
  cancelEdit()
  ElMessage.success('分类已保存')
  await loadCategories()
}

async function deleteCategory(category: SimpleCategory) {
  await ElMessageBox.confirm(`确定删除分类 ${category.name} 吗？关联的${kindName}将变为未分类。`, '确认删除', { type: 'warning' })
  if (props.kind === 'channel') await panelApi.deleteChannelGroup(category.id)
  else await panelApi.deleteGroupCategory(category.id)
  ElMessage.success('分类已删除')
  await loadCategories()
  await loadResources()
}

async function restoreSelection() {
  await nextTick()
  tableRef.value?.clearSelection()
  if (assignCategoryId.value <= 0) return
  resources.value.forEach((row) => {
    if (rowCategoryId(row) === assignCategoryId.value) tableRef.value?.toggleRowSelection(row, true)
  })
}

function onSelectionChange(selection: Resource[]) {
  selectedIds.value = selection.map((x) => x.id)
}

async function saveAssignments() {
  const scopeIds = resources.value.map((x) => x.id)
  if (props.kind === 'channel') await panelApi.saveChannelGroupAssignments(assignCategoryId.value, scopeIds, selectedIds.value)
  else await panelApi.saveGroupCategoryAssignments(assignCategoryId.value, scopeIds, selectedIds.value)
  ElMessage.success('分类绑定已保存')
  await loadCategories()
  await loadResources()
}

onMounted(async () => {
  accounts.value = await panelApi.operationAccounts()
  await loadCategories()
  await loadResources()
})
</script>

<style scoped>
.category-grid {
  display: grid;
  grid-template-columns: minmax(280px, 360px) minmax(0, 1fr);
  gap: 16px;
}

.resource-filter {
  width: 240px;
}

@media (max-width: 900px) {
  .category-grid {
    grid-template-columns: 1fr;
  }
}
</style>
