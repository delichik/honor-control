/**
 * 前端冒烟测试：把构建产物真正跑起来，确认它能渲染。
 *
 * 为什么需要它：`vite build` 只能保证语法与依赖解析。有一次面板在 WebView 里白屏，
 * 原因是一个运行时错误（读了 undefined 的字段），构建完全通过、CI 全绿，用户打开却是空白。
 * 这类问题只有"加载页面并检查 DOM / 控制台"才能拦住。
 *
 * 做法：启动 `vite preview` 提供 dist，用无头 Edge/Chrome 打开它，然后断言
 *   1) #root 里确实有内容（不是空壳）；
 *   2) 页面里出现了应用自己的文案；
 *   3) 控制台没有未捕获异常。
 *
 * 用法：npm run smoke（需要先 npm run build）
 */
import { spawn } from 'node:child_process'
import { existsSync } from 'node:fs'
import { setTimeout as delay } from 'node:timers/promises'

const PORT = 4173
const URL = `http://localhost:${PORT}/`

/** 找一个可用的无头浏览器。CI 的 windows 运行器自带 Edge。 */
function resolveBrowser() {
  const candidates = [
    process.env.SMOKE_BROWSER,
    'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe',
    'C:\\Program Files\\Microsoft\\Edge\\Application\\msedge.exe',
    'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe',
    'C:\\Program Files (x86)\\Google\\Chrome\\Application\\chrome.exe',
    '/usr/bin/google-chrome',
    '/usr/bin/chromium',
  ].filter(Boolean)

  for (const candidate of candidates) {
    if (existsSync(candidate)) return candidate
  }
  return null
}

function run(command, args, options = {}) {
  return new Promise((resolve) => {
    const child = spawn(command, args, { stdio: ['ignore', 'pipe', 'pipe'], ...options })
    let stdout = ''
    let stderr = ''
    child.stdout.on('data', (chunk) => { stdout += chunk })
    child.stderr.on('data', (chunk) => { stderr += chunk })
    child.on('error', (error) => resolve({ code: -1, stdout, stderr: stderr + String(error) }))
    child.on('close', (code) => resolve({ code, stdout, stderr }))
  })
}

async function waitForServer(attempts = 40) {
  for (let index = 0; index < attempts; index += 1) {
    try {
      const response = await fetch(URL)
      if (response.ok) return true
    } catch {
      // 还没起来，继续等
    }
    await delay(500)
  }
  return false
}

const browser = resolveBrowser()
if (!browser) {
  console.error('smoke: 找不到可用的浏览器（可用 SMOKE_BROWSER 环境变量指定路径）。')
  process.exit(1)
}

if (!existsSync('dist/index.html')) {
  console.error('smoke: dist/index.html 不存在，请先运行 npm run build。')
  process.exit(1)
}

// 用 vite preview 提供构建产物；端口固定，避免和别的进程撞车时静默换端口。
const server = spawn(
  process.execPath,
  ['node_modules/vite/bin/vite.js', 'preview', '--port', String(PORT), '--strictPort'],
  { stdio: ['ignore', 'pipe', 'pipe'] },
)
let serverOutput = ''
server.stdout.on('data', (chunk) => { serverOutput += chunk })
server.stderr.on('data', (chunk) => { serverOutput += chunk })

let failed = false
try {
  if (!(await waitForServer())) {
    console.error('smoke: vite preview 没有在预期时间内就绪。\n' + serverOutput)
    process.exit(1)
  }

  // 首次加载要拉 1 MB 出头的 bundle，冷启动偶尔会慢于 virtual-time-budget；
  // 单次失败不足以说明界面坏了，因此最多检查三轮，任一轮通过即算通过。
  let lastResult = null
  for (let attempt = 1; attempt <= 3; attempt += 1) {
    lastResult = await run(browser, [
      '--headless=new',
      '--disable-gpu',
      '--no-first-run',
      '--no-default-browser-check',
      '--user-data-dir=' + process.env.TEMP + '\\honorcontrol-smoke-profile-' + attempt,
      '--virtual-time-budget=15000',
      '--enable-logging=stderr',
      '--v=1',
      '--dump-dom',
      URL,
    ])

    const attemptDom = lastResult.stdout
    const rendered = ['Honor Control', '首页', '电池设置'].every((marker) => attemptDom.includes(marker))
    const clean = !lastResult.stderr
      .split('\n')
      .some((line) => line.includes('CONSOLE') && /Uncaught|TypeError|ReferenceError/.test(line))

    if (rendered && clean) break
    if (attempt < 3) await delay(1500)
  }

  const result = lastResult
  const dom = result.stdout
  const rootIndex = dom.indexOf('id="root"')
  const rootContent = rootIndex >= 0 ? dom.slice(rootIndex, dom.indexOf('<script', rootIndex)) : ''

  // 用"只有 React 渲染后才会出现的文案"作为判据，而不是靠 div 嵌套计数：
  // 导航项与标题都来自组件树，它们在 DOM 里出现即说明应用真的挂载了。
  const markers = ['Honor Control', '首页', '电池设置']
  const missing = markers.filter((marker) => !dom.includes(marker))

  if (missing.length > 0) {
    console.error(`smoke: 页面里缺少这些标记文案：${missing.join('、')}，说明界面没有真正挂载。`)
    failed = true
  }

  if (!rootContent || rootContent.length < 400) {
    console.error(`smoke: #root 内容过短（${rootContent.length} 字符）。`)
    failed = true
  }

  const consoleErrors = result.stderr
    .split('\n')
    .filter((line) => line.includes('CONSOLE') && /Uncaught|TypeError|ReferenceError/.test(line))
  if (consoleErrors.length > 0) {
    console.error('smoke: 控制台出现未捕获异常：')
    for (const line of consoleErrors.slice(0, 5)) console.error('  ' + line.trim())
    failed = true
  }

  if (failed) {
    // 失败时把现场打出来，否则在 CI 上只能看到"渲染失败"四个字。
    console.error('smoke: DOM 长度 ' + dom.length + '；控制台输出（前 10 行）：')
    for (const line of result.stderr.split('\n').filter((l) => l.includes('CONSOLE')).slice(0, 10)) {
      console.error('  ' + line.trim())
    }
    console.error('smoke: DOM 片段：' + dom.slice(0, 400).replace(/\s+/g, ' '))
  } else {
    console.log('smoke: OK（页面已渲染，控制台无未捕获异常）')
  }
} finally {
  server.kill()
}

process.exit(failed ? 1 : 0)
