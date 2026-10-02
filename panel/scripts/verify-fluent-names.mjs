// 校验脚本：检查 src/ 里用到的 Fluent token 与图标名是否真实存在。
// 背景：JS 项目里写错 token 名不会报错，只会静默变成 undefined → 样式失效。
// 用法：node scripts/verify-fluent-names.mjs
import { readFileSync, readdirSync, statSync } from 'node:fs'
import { join } from 'node:path'
import { tokens } from '@fluentui/react-components'
import * as icons from '@fluentui/react-icons'

const SRC = new URL('../src/', import.meta.url).pathname.replace(/^\//, '')

function walk(dir) {
  const out = []
  for (const entry of readdirSync(dir)) {
    const full = join(dir, entry)
    if (statSync(full).isDirectory()) out.push(...walk(full))
    else if (/\.jsx?$/.test(entry)) out.push(full)
  }
  return out
}

const files = walk(SRC)
const missingTokens = new Map()
const missingIcons = new Map()

for (const file of files) {
  const code = readFileSync(file, 'utf8')

  for (const match of code.matchAll(/tokens\.([A-Za-z0-9_]+)/g)) {
    const name = match[1]
    if (!(name in tokens)) {
      if (!missingTokens.has(name)) missingTokens.set(name, [])
      missingTokens.get(name).push(file)
    }
  }

  for (const match of code.matchAll(/import\s*\{([^}]+)\}\s*from\s*'@fluentui\/react-icons'/g)) {
    for (const raw of match[1].split(',')) {
      const name = raw.trim().split(/\s+as\s+/)[0].trim()
      if (!name) continue
      if (!(name in icons)) {
        if (!missingIcons.has(name)) missingIcons.set(name, [])
        missingIcons.get(name).push(file)
      }
    }
  }
}

const report = (label, map) => {
  if (map.size === 0) {
    console.log(`${label}: OK`)
    return
  }
  console.log(`${label}: ${map.size} 个不存在的名字`)
  for (const [name, where] of map) {
    console.log(`  - ${name}  (${[...new Set(where)].map((p) => p.replace(/\\/g, '/').split('/src/')[1]).join(', ')})`)
  }
}

report('tokens', missingTokens)
report('icons', missingIcons)
process.exit(missingTokens.size + missingIcons.size === 0 ? 0 : 1)
