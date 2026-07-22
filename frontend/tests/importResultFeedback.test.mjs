import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import test from 'node:test'
import ts from 'typescript'

const sourceUrl = new URL('../src/utils/importResultFeedback.ts', import.meta.url)
const source = await readFile(sourceUrl, 'utf8')
const transpiled = ts.transpileModule(source, {
  compilerOptions: {
    module: ts.ModuleKind.ESNext,
    target: ts.ScriptTarget.ES2020,
  },
}).outputText
const moduleUrl = `data:text/javascript;base64,${Buffer.from(transpiled).toString('base64')}`
const { getImportResultFeedback, summarizeImportResults } = await import(moduleUrl)

test('账号导入成功但代理绑定失败时显示部分成功并保留错误', () => {
  const feedback = getImportResultFeedback({
    success: true,
    error: '账号已导入，但代理设置失败：WARP 创建失败',
  })

  assert.deepEqual(feedback, {
    state: 'partial',
    label: '部分成功',
    tagType: 'warning',
    message: '账号已导入，但代理设置失败：WARP 创建失败',
  })
})

test('完整成功和导入失败使用不同反馈级别', () => {
  assert.equal(getImportResultFeedback({ success: true, error: null }).state, 'success')
  assert.deepEqual(getImportResultFeedback({ success: false, error: 'Session 无效' }), {
    state: 'failed',
    label: '失败',
    tagType: 'danger',
    message: 'Session 无效',
  })
})

test('汇总分别统计完整成功、部分成功和失败', () => {
  const summary = summarizeImportResults([
    { success: true, error: null },
    { success: true, error: '代理设置失败' },
    { success: false, error: '导入失败' },
  ])

  assert.deepEqual(summary, { succeeded: 1, partial: 1, failed: 1 })
})
