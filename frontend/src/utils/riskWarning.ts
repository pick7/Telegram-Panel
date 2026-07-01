import { createVNode, render } from 'vue'
import RiskWarningDialog from '@/components/RiskWarningDialog.vue'
import { panelApi } from '@/api/panel'
import type { ChatMembershipRiskResult, RiskAccount } from '@/api/types'

export type RiskWarningAction = 'continue' | 'exclude' | 'cancel'

export interface RiskWarningOptions {
  title?: string
  message?: string
  detailedMessage?: string
  showRecommendations?: boolean
  showExcludeOption?: boolean
  riskyAccounts?: RiskAccount[]
}

export async function showRiskWarning(options: RiskWarningOptions): Promise<RiskWarningAction> {
  return new Promise((resolve) => {
    const container = document.createElement('div')
    document.body.appendChild(container)

    let resolved = false
    const close = (action: RiskWarningAction) => {
      if (resolved) return
      resolved = true
      render(null, container)
      container.remove()
      resolve(action)
    }

    const vnode = createVNode(RiskWarningDialog, {
      ...options,
      onCancel: () => close('cancel'),
      onContinue: () => close('continue'),
      onExclude: () => close('exclude'),
    })

    render(vnode, container)
  })
}

export async function confirmChatMembershipRisk(ids: number[], title = '批量操作风控警告'): Promise<number[] | null> {
  let risk: ChatMembershipRiskResult
  try {
    risk = await panelApi.checkChatMembershipRisk(ids)
  } catch {
    return null
  }

  if (!risk.hasRiskyAccounts) return ids

  const action = await showRiskWarning({
    title,
    message: risk.message,
    detailedMessage: risk.detailedMessage,
    riskyAccounts: risk.riskyAccounts,
    showExcludeOption: risk.safeAccountIds.length > 0,
  })

  if (action === 'continue') return ids
  if (action === 'exclude') return risk.safeAccountIds.length > 0 ? risk.safeAccountIds : null
  return null
}
