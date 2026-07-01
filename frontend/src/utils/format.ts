import type { BatchTask } from '@/api/types'

export function formatTime(value?: string | null, emptyText = '-') {
  if (!value) return emptyText
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return emptyText
  return date.toLocaleString('zh-CN', { hour12: false })
}

export function taskProgress(task: Pick<BatchTask, 'total' | 'completed'>) {
  if (!task.total || task.total <= 0) return 0
  return Math.max(0, Math.min(100, Math.round((task.completed / task.total) * 100)))
}
