// 架构守卫：确保"示例数据"和"原生调用"不会散落到代码各处。
//
// 三条规则（违反即构建失败）：
// 1. 只有白名单里的文件可以引用 data/mock/*——假数据必须从 data/queries.js 一个口子注入；
// 2. 除 data/ 之外不许直接 import @tauri-apps/api——原生调用只能走 data/transport.js；
// 3. data/transport.js 必须用**动态** import 加载假服务，否则 mock 代码会被打进 Tauri 产物。
//
// 用法：node scripts/verify-architecture.mjs
import { readFileSync, readdirSync, statSync } from 'node:fs'
import { join, relative, sep } from 'node:path'

const SRC = new URL('../src/', import.meta.url).pathname.replace(/^\//, '')

/** 允许引用 data/mock/* 的文件（相对 src/，统一用 / 分隔） */
const MOCK_ALLOWED = new Set([
  'data/queries.js', // 唯一的示例数据注入点
  'data/transport.js', // 无 Tauri 运行时加载假服务
  'components/MockBadge.jsx', // 角标要读"为什么是示例"
  'components/DataSourcePanel.jsx', // 数据来源面板要列出清单
])
const MOCK_PREFIX = 'data/mock/'
const ALLOWED_TAURI_DIRS = ['data/']

function walk(dir) {
  const out = []
  for (const entry of readdirSync(dir)) {
    const full = join(dir, entry)
    if (statSync(full).isDirectory()) out.push(...walk(full))
    else if (/\.jsx?$/.test(entry)) out.push(full)
  }
  return out
}

const problems = []

for (const file of walk(SRC)) {
  const rel = relative(SRC, file).split(sep).join('/')
  const code = readFileSync(file, 'utf8')

  // 规则 1：data/mock 的引用白名单
  if (!rel.startsWith(MOCK_PREFIX) && !MOCK_ALLOWED.has(rel)) {
    if (/from\s+['"][^'"]*\/mock\//.test(code) || /import\(\s*['"][^'"]*\/mock\//.test(code)) {
      problems.push(`${rel}: 引用了 data/mock/*，但不在白名单里（假数据只允许从 data/queries.js 注入）`)
    }
  }

  // 规则 2：原生调用只能出现在 data/ 下
  if (/from\s+['"]@tauri-apps\//.test(code) && !ALLOWED_TAURI_DIRS.some((prefix) => rel.startsWith(prefix))) {
    problems.push(`${rel}: 直接引用了 @tauri-apps/*，原生调用必须走 data/transport.js`)
  }
}

// 规则 3：假服务必须是动态 import（否则会被静态打包进桌面产物）
const transport = readFileSync(join(SRC, 'data', 'transport.js'), 'utf8')
if (!/await\s+import\(\s*['"][^'"]*mock\/transport\.js['"]\s*\)/.test(transport)) {
  problems.push('data/transport.js: 假服务必须用 await import(...) 动态加载，避免进入 Tauri 产物')
}

if (problems.length > 0) {
  console.error('架构校验失败：')
  for (const problem of problems) console.error(`  - ${problem}`)
  process.exit(1)
}

console.log('architecture: OK')
