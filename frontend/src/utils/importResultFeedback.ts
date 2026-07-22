import type { ImportResult } from '@/api/types'

export type ImportResultState = 'success' | 'partial' | 'failed'

export interface ImportResultFeedback {
  state: ImportResultState
  label: string
  tagType: 'success' | 'warning' | 'danger'
  message: string
}

export interface ImportFeedbackSummary {
  succeeded: number
  partial: number
  failed: number
}

export function getImportResultFeedback(result: Pick<ImportResult, 'success' | 'error'>): ImportResultFeedback {
  const error = result.error?.trim()

  if (!result.success) {
    return {
      state: 'failed',
      label: '失败',
      tagType: 'danger',
      message: error || '导入失败',
    }
  }

  if (error) {
    return {
      state: 'partial',
      label: '部分成功',
      tagType: 'warning',
      message: error,
    }
  }

  return {
    state: 'success',
    label: '成功',
    tagType: 'success',
    message: '成功',
  }
}

export function summarizeImportResults(
  results: ReadonlyArray<Pick<ImportResult, 'success' | 'error'>>,
): ImportFeedbackSummary {
  const summary: ImportFeedbackSummary = { succeeded: 0, partial: 0, failed: 0 }

  for (const result of results) {
    const state = getImportResultFeedback(result).state
    if (state === 'success') summary.succeeded += 1
    else if (state === 'partial') summary.partial += 1
    else summary.failed += 1
  }

  return summary
}
