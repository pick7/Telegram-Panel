import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import test from 'node:test'

const proxiesSource = await readFile(new URL('../src/views/Proxies.vue', import.meta.url), 'utf8')
const apiSource = await readFile(new URL('../src/api/panel.ts', import.meta.url), 'utf8')
const typesSource = await readFile(new URL('../src/api/types.ts', import.meta.url), 'utf8')

function section(startMarker, endMarker) {
  const start = proxiesSource.indexOf(startMarker)
  const end = proxiesSource.indexOf(endMarker, start)
  assert.notEqual(start, -1, `未找到 ${startMarker}`)
  assert.notEqual(end, -1, `未找到 ${endMarker}`)
  return proxiesSource.slice(start, end)
}

test('代理管理页提供账号全局代理配置入口和协议选择', () => {
  assert.match(proxiesSource, /全局代理 · \{\{ globalProxyLabel \}\}/)
  assert.match(proxiesSource, /title="账号全局代理设置"/)
  assert.match(proxiesSource, /value="http">HTTP/)
  assert.match(proxiesSource, /value="socks5">SOCKS5/)
  assert.match(proxiesSource, /value="mtproto">MTProxy/)
  assert.match(proxiesSource, /panelApi\.globalProxySettings\(\)/)
})

test('保存全局代理会调用后台设置接口并明确首次连接与失败闭锁语义', () => {
  assert.match(apiSource, /get<GlobalProxySettings>\('\/settings\/global-proxy'/)
  assert.match(apiSource, /post<OperationResult>\('\/settings\/global-proxy'/)
  assert.match(typesSource, /globalProxy: GlobalProxySettings/)
  assert.match(proxiesSource, /不会先直连再切换/)
  assert.match(proxiesSource, /不会静默回退到面板直连/)
  assert.match(proxiesSource, /panelApi\.saveGlobalProxySettings\(payload\)/)
})

test('全局代理凭据只返回存在标记并支持留空保持或显式清除', () => {
  const responseType = typesSource.match(/export interface GlobalProxySettings \{([\s\S]*?)\n\}/)
  assert.ok(responseType)
  assert.match(typesSource, /hasPassword: boolean/)
  assert.match(typesSource, /hasSecret: boolean/)
  assert.doesNotMatch(responseType[1], /\bpassword\b|\bsecret\b/)
  assert.match(proxiesSource, /password: ''/)
  assert.match(proxiesSource, /secret: ''/)
  assert.match(proxiesSource, /留空保持原密码/)
  assert.match(proxiesSource, /清除已保存的密码/)
  assert.match(proxiesSource, /留空保持原 Secret/)
})

test('全局代理弹窗的全部关闭路径都会销毁凭据表单', () => {
  const resetBody = section('function resetGlobalProxyDialog()', 'function closeGlobalProxyDialog()')
  const closeBody = section('function closeGlobalProxyDialog()', 'async function saveGlobalProxy()')
  const beforeCloseBody = section(
    'function beforeGlobalProxyDialogClose(',
    'function beforeImportDialogClose(',
  )
  const saveBody = section('async function saveGlobalProxy()', 'function openCreate()')

  assert.match(resetBody, /globalProxyDialog\.form = blankGlobalProxyForm\(\)/)
  assert.match(closeBody, /resetGlobalProxyDialog\(\)[\s\S]*globalProxyDialog\.visible = false/)
  assert.match(beforeCloseBody, /resetGlobalProxyDialog\(\)[\s\S]*done\(\)/)
  assert.match(saveBody, /panelApi\.saveGlobalProxySettings\(payload\)[\s\S]*globalProxyDialog\.visible = false[\s\S]*resetGlobalProxyDialog\(\)/)
  assert.match(proxiesSource, /@closed="resetGlobalProxyDialog"/)
  assert.match(proxiesSource, /@click="closeGlobalProxyDialog">取消/)
})
