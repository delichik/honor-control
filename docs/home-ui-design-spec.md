# 电池与电源控制台 · 界面视觉规格（面板实现用）

**来源设计稿**：`C:\Users\illya\Documents\deepseek-harness\default-workspace\battery-console-winui.html`（共 2367 行，只读，未修改）

> **这份文档服务于 `panel/`（React + Fluent UI + Tauri）**：首页与其余页面都按它实现。
> 文中少数"XAML 落地提示"可以直接忽略——设计稿本身就是 HTML/CSS，所有 px 数值对 CSS 同样成立。

- 第 1–786 行：CSS（设计 token、控件、卡片、首页三张卡的专属样式、媒体查询）
- 第 787–1360 行：HTML 结构（图标库、窗口外壳、导航、首页与其它页）
- 第 1362–2367 行：JS（母线几何、风扇叶片与转速、曲线/图表绘制、读数格式化、状态机）

**本文档行号全部指向上述文件。** 所有尺寸、坐标、颜色、公式均直接取自该文件代码，未做任何"优化"或补全；凡属推算而得的数值，文中均标注「推导」并给出算式。CSS 的 `px` 与 XAML 的有效像素（epx）按 1:1 对应。

**默认主题**：`<html lang="zh-CN" data-theme="dark">`，即深色为默认（L2）。

---

## 目录

1. [整体框架](#1-整体框架)
2. [卡片通用规格与主题色板](#2-卡片通用规格与主题色板)
3. [首页三张卡片](#3-首页三张卡片)
4. [首页之外的页面清单](#4-首页之外的页面清单)
5. [交互与状态](#5-交互与状态)
6. [原稿中未被使用 / 需实现者留意的点](#6-原稿中未被使用--需实现者留意的点)
7. [附录：首页用到的图标](#7-附录首页用到的图标)

---

## 1. 整体框架

### 1.1 窗口与外层

| 项 | 值 | 来源 |
|---|---|---|
| 窗口尺寸 | `width = min(1400px, 100%)`，`height = min(900px, 100%)` | L161–L162 |
| 窗口圆角 | 8px（`--radius-window`） | L25, L164 |
| 窗口背景 | `var(--mica)`（深色 `rgba(26,26,28,.80)` / 浅色 `rgba(243,243,243,.82)`） | L37, L90, L166 |
| 背景模糊 | `blur(64px) saturate(1.6)` | L167–L168 |
| 窗口阴影 | `--shadow-window`：深色 `0 32px 80px rgba(0,0,0,.60), 0 2px 8px rgba(0,0,0,.40)`；浅色 `0 32px 80px rgba(0,0,0,.28), 0 2px 8px rgba(0,0,0,.16)` | L79, L132, L169 |
| 窗口描边 | `outline: 1px solid var(--card-stroke)`，`outline-offset: -1px` | L170–L171 |
| 窗口布局 | 纵向 flex 列；`shell` 为 `flex:1` | L163, L198 |
| 桌面底（窗口外） | `body` 内边距 22px，内容居中（`display:grid; place-items:center`），背景 `--wallpaper`（4 层 radial-gradient + 1 层 linear-gradient，`background-attachment:fixed`） | L138–L150 |
| 默认字体 | `--font-text`：`"Segoe UI Variable Text","Segoe UI",system-ui,-apple-system,"Microsoft YaHei UI","Microsoft YaHei",sans-serif`；基准 `14px/20px` | L12, L140–L141 |
| 大数字字体 | `--font-display`：`"Segoe UI Variable Display","Segoe UI Variable Text","Segoe UI",system-ui,"Microsoft YaHei UI",sans-serif` | L13 |

`--wallpaper` 完整值：深色 L30–L35、浅色 L83–L88（4 个 radial-gradient 停靠点分别为 `1100px 620px at 18% 6%`、`900px 560px at 82% 0%`、`760px 620px at 62% 96%`、`600px 420px at 2% 88%`，末尾 `linear-gradient(160deg,…)`）。

### 1.2 标题栏

| 元素 | 规格 | 来源 |
|---|---|---|
| `.titlebar` | 高 32px，`flex: 0 0 32px`；`align-items:center`；`justify-content:space-between`；`padding-left:14px`；`user-select:none` | L174–L179 |
| `.tb-left` | flex 行，`gap:9px`，`min-width:0` | L180 |
| `.tb-icon` | 16×16，颜色 `var(--accent)` | L181 |
| `.tb-title` | 12px/16px，`var(--text-secondary)`，单行省略号 | L182 |
| `.caption` | 高 32px | L183 |
| `.cap-btn` | 46×32，透明背景；图标 `10×10`、`stroke-width:1`、`fill:none` | L184–L189 |
| 关闭按钮悬停/按下 | 悬停背景 `#c42b1c` + 文字 `#fff`；按下 `#b3251a` + `#fff`（固定值，不随主题） | L192–L193 |
| 其它按钮悬停/按下 | `var(--subtle-hover)` / `var(--subtle-pressed)` | L190–L191 |

标题栏文字为「电池与电源控制台 — 首页」（L825）；左侧图标是行内 SVG（viewBox `0 0 20 20`，`stroke-width:1.5`，L822–L824）。

### 1.3 导航栏（NavigationView）

| 元素 | 规格 | 来源 |
|---|---|---|
| `.navview` | 宽 `--nav-w: 232px`，`flex: 0 0 232px`；背景 `var(--layer)`；纵向 flex；`padding: 4px 0 8px`；`overflow:hidden` | L27, L199–L206 |
| 折叠态 | `width:48px; flex-basis:48px`；`.nav-label/.nav-group-title/.nav-foot-text` 透明；`.nav-item` 居中且 `padding:0`；`.nav-foot{gap:0;padding:0;margin-inline:6px;justify-content:center}` | L207–L212, L247 |
| `.nav-top` | `padding: 0 4px 6px`，`gap:2px` | L214 |
| `.nav-toggle` | 40×36，圆角 4px，透明背景，图标用 `.icon--20`（20×20） | L215–L219, L154 |
| `.nav-list` | `padding:0 4px`，纵向 `gap:2px`，纵向滚动 | L224 |
| `.nav-group-title` | 12px/16px，`var(--text-tertiary)`，`padding: 14px 12px 6px` | L225–L228 |
| `.nav-item` | 高 36px，`gap:12px`，`padding:0 12px`，圆角 4px，字号 14px | L229–L235 |
| 选中项 | 背景 `var(--card-stroke)`；左侧指示条 `3px × 16px`、圆角 2px、颜色 `var(--accent)`、垂直居中、`left:0`；图标变 `var(--accent)` | L239–L244 |
| 悬停/按下 | `var(--subtle-hover)` / `var(--subtle-pressed)` | L237–L238 |
| `.nav-foot` | 上边框 `1px solid var(--divider)`，`padding-top:10px`，`margin-top:6px`，`margin-inline:12px`，`gap:10px` | L245–L246 |
| 头像 `.avatar` | 28×28 圆，`linear-gradient(135deg, var(--accent), #a06cff)`，文字 11px/600，颜色 `#08121c` | L248–L252 |
| 底部文字 | 12px/16px `var(--text-secondary)`；其中 `b` 为块级 13px/600 `var(--text-primary)` | L253–L254 |

导航项顺序（L841–L849）：**首页**（无分组）→ 分组「控制」：电池设置、性能设置 → 分组「监控」：充放电历史、功耗历史 → `flex:1` 占位 → **设置**（贴底）。每项带图标 + 文字，`data-title`/`data-sub` 同时作为页面标题与副标题（L2045）。

### 1.4 内容区与页面头

| 元素 | 规格 | 来源 |
|---|---|---|
| `.content` | `flex:1`，`min-width:0`，纵向 flex，`overflow:hidden` | L257 |
| `.page-head` | `padding: 20px 28px 12px`；`align-items:flex-start`；`justify-content:space-between`；`gap:16px` | L258–L260 |
| `.page-title` | `--font-display`，28px/36px，字重 600，`letter-spacing:-.01em` | L261 |
| `.page-sub` | 12px/16px，`var(--text-tertiary)`，`margin-top:2px` | L262 |
| `.page-actions` | 行内，`gap:8px`，`padding-top:4px`（首页为「主题切换 ToggleButton」+「刷新 Button」） | L263, L864–L870 |
| `.page-scroll` | `flex:1`，纵向滚动，`padding: 4px 28px 28px` | L264 |
| 页面切换动画 | `pageIn` 250ms：`opacity 0 → 1`、`translateY(6px) → none`；同一时刻只有一个 `.page.is-active` | L266–L268 |

首页头部实际文本：标题「首页」，副标题「实时监控电池、电源与性能状态」（L861–L862）。

按钮规格：`.btn` 高 32px、`padding: 0 12px`、圆角 4px、边框 `1px solid var(--ctrl-stroke)`、背景 `var(--ctrl-fill)`、字号 14px、`gap:8px`、`box-shadow: 0 1px 0 0 var(--card-stroke)`（L330–L337）；`.tbtn`（主题切换）高 32px、`padding:0 10px`、圆角 4px、透明边框、`gap:7px`，`aria-pressed="true"` 时背景与边框均为 `var(--card-stroke)`、图标 `var(--accent)`（L349–L357）。

### 1.5 栅格与 spN

```css
.grid { display:grid; grid-template-columns:repeat(12, minmax(0,1fr)); gap:12px }   /* L437 */
.sp3{span 3} .sp4{span 4} .sp5{span 5} .sp6{span 6} .sp7{span 7} .sp8{span 8} .sp12{span 12}  /* L438–L439 */
```

- **12 列栅格，列间距（gap）12px**，列宽 `minmax(0,1fr)`。
- **`spN` 的含义**：该元素横跨 N 列，实际宽度 = `N × 列宽 + (N−1) × 12`。注意原稿只定义了 `sp3/sp4/sp5/sp6/sp7/sp8/sp12` 七个类（L438–L439）。
- **在 1400px 窗口下的推算（推导）**：
  - 内容区宽 `1400 − 232（导航） = 1168`
  - 栅格可用宽 `1168 − 28×2（page-scroll 左右内边距） = 1112`
  - 列宽 `(1112 − 11×12) / 12 = 980/12 = 81.667px`
  - `sp3 = 269.00`、`sp4 = 362.67`、`sp5 = 456.33`、`sp6 = 550.00`、`sp7 = 643.67`、`sp8 = 737.33`、`sp12 = 1112.00`
  - 卡片内容盒（`.card` 为 `border-box`，`padding:16px` + 边框 1px）宽 = `1112 − 2 − 32 = 1078`

### 1.6 响应式断点

| 断点 | 规则 | 来源 |
|---|---|---|
| `max-width:1180px` | `.sp3/.sp4/.sp5/.sp7 → span 6`；`.sp8 → span 12`；导航强制折叠为 48px（文字透明、项居中） | L773–L779 |
| `max-width:880px` | `.sp3/.sp4/.sp5/.sp6/.sp7/.sp8 → span 12`；`.navview{display:none}` | L780–L784 |

（媒体查询按**视口宽度**判定，不是窗口宽度；窗口本身是 `min(1400px,100%)`。）

### 1.7 滚动条（Fluent 风格）

`width/height: 14px`；滑块 `background-color: var(--ctrl-strong)`、`background-clip: content-box`、`border: 5px solid transparent`、`border-radius: 8px`；悬停 `background-color: var(--text-secondary)`、`border: 4px solid transparent`；轨道透明。作用于 `.page-scroll` 与 `.nav-list`（L766–L771）。

### 1.8 动效 token

| 变量 | 值 | 来源 |
|---|---|---|
| `--ease-out` | `cubic-bezier(0,0,0,1)`（FluentEaseOut） | L16 |
| `--ease-in` | `cubic-bezier(1,0,1,1)`（FluentEaseIn；全文件未被使用） | L17 |
| `--dur-fast` | 150ms | L18 |
| `--dur` | 250ms | L19 |
| `--dur-slow` | 350ms | L20 |
| `--radius-sm` | 3px（未使用） | L22 |
| `--radius` | 4px（控件圆角） | L23 |
| `--radius-lg` | 7px（卡片圆角） | L24 |
| `--radius-window` | 8px | L25 |

---

## 2. 卡片通用规格与主题色板

### 2.1 卡片盒

```css
.card{ border-radius:var(--radius-lg); background:var(--card); border:1px solid var(--card-stroke);
       padding:16px; display:flex; flex-direction:column; gap:14px; min-width:0; }   /* L440–L448 */
.card--alt{ background:var(--card-secondary) }                                        /* L449，全稿未使用 */
```

| 项 | 值 | 来源 |
|---|---|---|
| 圆角 | 7px | L441 |
| 背景 | `var(--card)` | L442 |
| 描边 | `1px solid var(--card-stroke)` | L443 |
| 内边距 | 16px（四边） | L444 |
| 内部布局 | 纵向 flex，**子项间距 14px** | L445 |
| 过渡 | 背景/边框色 350ms `--ease-out` | L447 |

### 2.2 卡片头部

| 元素 | 规格 | 来源 |
|---|---|---|
| `.card-head` | flex 行，`align-items:center`，`justify-content:space-between`，`gap:10px` | L450 |
| `.card-title` | flex 行，`gap:8px`，**字号 14px / 行高 20px / 字重 600** | L451 |
| `.card-title .icon` | **16×16**（`.icon` 基础尺寸），颜色 `var(--accent)`；`stroke-width:1.5`、`fill:none`、`stroke-linecap/linejoin:round` | L153, L452 |
| `.card-note`（右上角附注） | 12px/16px，`var(--text-tertiary)` | L453 |
| `.eta`（右上角预计时间） | 12px/16px，`var(--text-tertiary)`，`font-variant-numeric:tabular-nums`，`white-space:nowrap` | L643 |
| `.card-head-right` | flex 行 `gap:14px`（**定义于 L642，首页未使用**） | L642 |
| `.legend` / `.legend span` / `.legend i` | flex 换行 `gap:4px 14px` / 12px/16px `--text-tertiary` + `gap:6px` / 8×8 圆角 2px `currentColor`（**定义于 L639–L641，首页未使用**） | L639–L641 |

### 2.3 pill 徽标

```css
.pill{ display:inline-flex; align-items:center; gap:6px; height:24px; padding:0 10px; border-radius:999px;
       font-size:12px; line-height:16px; font-weight:600; white-space:nowrap;
       background:var(--card-stroke); color:var(--text-secondary); }   /* L456–L461 */
.pill .dot{ width:6px; height:6px; border-radius:50%; background:currentColor }   /* L462 */
```

| 语气 | 背景 | 文字 | 来源 |
|---|---|---|---|
| 默认 | `var(--card-stroke)` | `var(--text-secondary)` | L459 |
| `data-tone="success"` | `var(--success-bg)` | `var(--success)` | L463 |
| `data-tone="caution"` | `var(--caution-bg)` | `var(--caution)` | L464 |
| `data-tone="critical"` | `var(--critical-bg)` | `var(--critical)` | L465 |
| `data-tone="accent"` | `var(--accent-subtle)` | `var(--accent)` | L466 |
| success 的 `.dot` | `animation: pulse 1.8s var(--ease-out) infinite`（`opacity 1 → .25 → 1`，L467–L468） | — | L467 |

> pill 在首页**不出现**（首页只有 `.eta` 与 `.card-note`），它用于「电池设置」页（L1111）。

### 2.4 卡片内通用排版

| 类 | 规格 | 来源 |
|---|---|---|
| `.metric` | 行内基线对齐，`gap:6px`，`--font-display`，600，`letter-spacing:-.02em` | L471 |
| `.metric .num` | 40px/48px，tabular-nums | L472 |
| `.metric .unit` | 16px/24px，`--text-tertiary`，600 | L473 |
| `.metric--sm .num` | 28px/34px | L474 |
| `.caption` | 12px/16px `--text-tertiary`；`b` 为 `--text-secondary`/600 | L475–L476 |
| `.kv` | 行内两端对齐，`gap:12px`，13px/18px；`.k` = `--text-tertiary`，`.v` = `--text-primary`/600/tabular-nums | L477–L479 |

### 2.5 主题色板（CSS 变量 → 深色 / 浅色）

**定义位置**：`:root` L11–L28，`[data-theme="dark"]` L29–L81，`[data-theme="light"]` L82–L134。

| 变量 | dark | light |
|---|---|---|
| `--mica` | `rgba(26,26,28,.80)` | `rgba(243,243,243,.82)` |
| `--bg-base` | `#202020` | `#f3f3f3` |
| `--layer` | `rgba(58,58,58,.32)` | `rgba(255,255,255,.50)` |
| `--card` | `rgba(255,255,255,.0512)` | `rgba(255,255,255,.70)` |
| `--card-secondary` | `rgba(255,255,255,.0325)` | `rgba(255,255,255,.50)` |
| `--card-stroke` | `rgba(255,255,255,.0698)` | `rgba(0,0,0,.0578)` |
| `--card-stroke-strong` | `rgba(255,255,255,.0939)` | `rgba(0,0,0,.10)` |
| `--divider` | `rgba(255,255,255,.0837)` | `rgba(0,0,0,.0803)` |
| `--ctrl-fill` | `rgba(255,255,255,.0605)` | `rgba(255,255,255,.70)` |
| `--ctrl-fill-secondary` | `rgba(255,255,255,.0837)` | `rgba(249,249,249,.50)` |
| `--ctrl-fill-tertiary` | `rgba(255,255,255,.0325)` | `rgba(249,249,249,.30)` |
| `--ctrl-fill-disabled` | `rgba(255,255,255,.0421)` | `rgba(249,249,249,.30)` |
| `--ctrl-stroke` | `rgba(255,255,255,.0698)` | `rgba(0,0,0,.0578)` |
| `--ctrl-stroke-secondary` | `rgba(255,255,255,.0939)` | `rgba(0,0,0,.1622)` |
| `--ctrl-stroke-disabled` | `rgba(255,255,255,.0698)` | `rgba(0,0,0,.0578)` |
| `--ctrl-solid` | `#454545` | `#ffffff` |
| `--ctrl-strong` | `rgba(255,255,255,.5442)` | `rgba(0,0,0,.4458)` |
| `--subtle-hover` | `rgba(255,255,255,.0605)` | `rgba(0,0,0,.0373)` |
| `--subtle-pressed` | `rgba(255,255,255,.0419)` | `rgba(0,0,0,.0241)` |
| `--text-primary` | `#ffffff` | `rgba(0,0,0,.8956)` |
| `--text-secondary` | `rgba(255,255,255,.786)` | `rgba(0,0,0,.6063)` |
| `--text-tertiary` | `rgba(255,255,255,.5442)` | `rgba(0,0,0,.4458)` |
| `--text-disabled` | `rgba(255,255,255,.3628)` | `rgba(0,0,0,.3614)` |
| `--accent` | `#60cdff` | `#005fb8` |
| `--accent-hover` | `#7ad8ff` | `#0a6cc9` |
| `--accent-pressed` | `#4db8e8` | `#00529e` |
| `--accent-text` | `#000000` | `#ffffff` |
| `--accent-subtle` | `rgba(96,205,255,.12)` | `rgba(0,95,184,.10)` |
| `--accent-subtle-2` | `rgba(96,205,255,.06)` | `rgba(0,95,184,.05)` |
| `--hatch` | `rgba(96,205,255,.30)` | `rgba(0,95,184,.26)` |
| `--wire` | `rgba(255,255,255,.26)` | `rgba(0,0,0,.22)` |
| `--success` | `#6ccb5f` | `#0f7b0f` |
| `--success-bg` | `rgba(108,203,95,.10)` | `rgba(15,123,15,.08)` |
| `--caution` | `#fce100` | `#9d5d00` |
| `--caution-bg` | `rgba(252,225,0,.10)` | `rgba(157,93,0,.08)` |
| `--critical` | `#ff99a4` | `#c42b1c` |
| `--critical-bg` | `rgba(255,153,164,.11)` | `rgba(196,43,28,.08)` |
| `--neutral` | `#9d9d9d` | `#616161` |
| `--temp-grad` | `linear-gradient(90deg,#4cc2ff 0%,#6ccb5f 26%,#b8d94a 50%,#fce100 68%,#ff9f43 84%,#ff5f56 100%)` | `linear-gradient(90deg,#0f8fd6 0%,#0f7b0f 26%,#7a8a12 50%,#c9a800 68%,#d97316 84%,#c42b1c 100%)` |
| `--needle-halo` | `rgba(0,0,0,.45)` | `rgba(255,255,255,.95)` |
| `--smoke` | `rgba(0,0,0,.30)`（全稿未使用） | `rgba(255,255,255,.30)`（未使用） |
| `--shadow-flyout` | `0 8px 16px rgba(0,0,0,.26)`（未使用） | `0 8px 16px rgba(0,0,0,.14)`（未使用） |
| `--shadow-window` | `0 32px 80px rgba(0,0,0,.60), 0 2px 8px rgba(0,0,0,.40)` | `0 32px 80px rgba(0,0,0,.28), 0 2px 8px rgba(0,0,0,.16)` |
| `--chart-grid` | `rgba(255,255,255,.06)` | `rgba(0,0,0,.06)` |

> 主题切换只切换 `document.documentElement.dataset.theme`，图表在切换后重绘以重新读取 CSS 变量（L1861–L1870）。

---

## 3. 首页三张卡片

首页栅格中的元素顺序（L874–L1102）：

| 顺序 | 元素 | 跨列 | 行号 |
|---|---|---|---|
| 1 | `#alertRow`（InfoBar 行，默认 `display:none`） | sp12 | L879–L884 |
| 2 | `#powerCard` 电源 | sp12 | L887–L961 |
| 3 | `#thermalCard` 性能与散热 | sp12 | L963–L991 |
| 4 | `#fanTpl` 风扇模板（不渲染自身） | — | L993–L1006 |
| 5 | 「电池输入功率」图表卡 | sp12 | L1008–L1014 |
| 6 | 演示控制台 Expander | sp12 | L1017–L1100 |

三张 `.card` 的推算高度（在 1400px 窗口、默认数据下）：
- 电源卡 `1+16+20+14+208+16+1 = 276px`（head 高取 card-title 的 20px）
- 性能与散热卡 `1+16+40+14+210+14+62+16+1 = 374px`（head 高取 SelectorBar 的 40px；body 210 由曲线 SVG 决定；fan-row 62 由转子决定）
- 电池输入功率卡 `1+16+20+14+170+16+1 = 238px`

### 3.1 电源卡 `#powerCard`（L887–L961，样式 L504–L645）

#### 3.1.1 卡片头

- 左：`ic-plug` 图标（16×16，`--accent`）+ 「电源」（14px/20px/600）。
- 右：`.eta#etaValue`，初始文本「≈ 21 分钟」（L890）。

ETA 计算（L1672–L1681）：
- 充电中且 `soc < stop`：`mins = max(1, round((stop − soc)/100 × 83.0 / max(batt, 0.5) × 60))`，文本 `"≈ " + mins + " 分钟"`；`CAP_WH = 83.0`（L1388）。
- 放电中：`mins = round(soc/100 × 83.0 / max(|batt|,0.5) × 60)`；`mins ≥ 60` 时文本 `${floor(mins/60)} 小时 ${mins%60} 分`，否则 `mins + " 分钟"`。
- 其它（idle）：文本清空（`""`，元素仍在，宽度自适应）。

#### 3.1.2 母线行整体栅格

```css
.bus{ display:flex; align-items:stretch }                       /* L504 */
.bus-side{ flex:0 0 168px; display:flex; flex-direction:column; justify-content:space-around }  /* L505 */
.bus-wire{ position:relative; flex:1 1 190px; min-width:150px } /* L517 */
.bus > .battery-gauge{ flex:0 0 430px }                         /* L563 */
.battery-info{ flex:0 0 auto; … padding-left:30px; padding-top:12px }  /* L630–L633 */
```

`.bus` 的四个子项（顺序见 L893–L960），**子项之间没有 gap**：

| 子项 | 宽 | 竖向对齐 |
|---|---|---|
| `.bus-side` | 固定 **168px** | 纵向 flex，`justify-content:space-around` |
| `.bus-wire` | `flex: 1 1 190px`，`min-width:150px`（吃掉剩余空间） | 拉伸；SVG `position:absolute; inset:0` |
| `.battery-gauge` | 固定 **430px** | 纵向 flex，`justify-content:center`，`gap:10px` |
| `.battery-info` | `auto`（内容宽 + `padding-left:30px`） | 纵向 flex，`justify-content:center`，`gap:20px`，`padding-top:12px` |

- 母线行总高（`align-items:stretch`，由最高子项决定，**推导**）：`.battery-gauge` = `shell-wrap(56 + 122) + gap 10 + bat-axis(16+4) = 208px`；`.bus-side` = 2×(8+18+8+2) = 72px；`.battery-info` = 12 + 2×54 + 20 = 140px → **行高 H = 208px**。
- `.bus-wire` 宽（推导）：`W = 1078(卡片内容盒) − 168 − 430 − battery-info实测宽`。纯代数示例（非实测）：若 `battery-info` 为 100px，则 `W = 380`。
- 因为 `.bus-node` 两行等高（36px）且 `space-around`，两个节点的垂直中心**正好落在 25% 与 75%**（推导：自由空间 `208−72=136`，四段各 34 → 节点 1 占 y 34–70（中心 52 = 25%），节点 2 占 y 138–174（中心 156 = 75%））。母线的 `uy/ly` 就是照此取的。

`.bus-side` 内两个节点（L895–L902）：

| 项 | 规格 | 来源 |
|---|---|---|
| `.bus-node` | flex 行，`gap:8px`，`padding:8px 12px`，圆角 6px，背景 `var(--card-secondary)`，边框 `1px solid var(--card-stroke-strong)`，13px/18px，`var(--text-secondary)` | L506–L512 |
| 高度 | `8+8+18+2 = 36px`（推导） | L508, L510 |
| 图标 | 15×15，`var(--text-tertiary)` | L513 |
| 未接入电源 | `[data-off="true"]{ opacity:.45 }`（JS 按 `S.plug` 设置，L1664） | L514 |
| 内容 | 「电源适配器」+ `ic-plug`；「系统负载」+ `ic-gauge` | L896–L901 |

#### 3.1.3 母线 SVG 几何

标记（L906）：`<svg id="busSvg" viewBox="0 0 200 200" preserveAspectRatio="none">`；`.bus-wire svg{ position:absolute; inset:0; width:100%; height:100%; overflow:visible }`（L518）。

**运行时 viewBox 被 JS 改写为像素坐标**（L1440–L1465）：`viewBox="0 0 W H"`，其中 `W = round(clientWidth)`、`H = round(clientHeight)`。改写后 viewBox 与元素像素尺寸一致，`preserveAspectRatio="none"` 实际不产生缩放——这是原稿的明确意图（注释「几何直接写在像素坐标里，虚线不会被非等比缩放拉长」L1437–L1438）。若 `W < 60 || H < 80` 则整段跳过不画（L1443）。

关键点（L1449–L1450）：

```
mx = round(W / 2)        /* 母线交汇点 x */
uy = round(H * 0.25)     /* 适配器支路 y */
my = round(H * 0.50)     /* 电池支路 y（母线主干） */
ly = round(H * 0.75)     /* 系统负载支路 y */
```

代入 H = 208：`uy = 52`、`my = 104`、`ly = 156`（推导）。

各 path 的 `d`（L1452–L1464，`set(".类名", d)`）：

| 类 | d | 含义 |
|---|---|---|
| `.w-ac` | `M0 uy H mx` | 适配器 → 交汇点（实线） |
| `.w-dn` | `M mx uy V my` | 交汇点下沉到主干（实线） |
| `.w-bat` | `M mx my H W` | 主干 → 电池右端（实线） |
| `.w-dn2` | `M mx my V ly` | 主干下沉到负载支路（实线） |
| `.w-ld` | `M mx ly H 0` | 负载支路向左（实线） |
| `.s-ac` | `M0 uy H mx` | 流动段（虚线动画） |
| `.s-dn` | `M mx uy V my` | 流动段 |
| `.s-bat` | `M mx my H W` | 流动段（**颜色为 `--success`**） |
| `.s-rev` | `M W my H mx` | 放电时从电池流回交汇点 |
| `.s-dn2` | `M mx my V ly` | 流动段 |
| `.s-ld` | `M mx ly H 0` | 流动段 |
| `.ghost--charge path` | `M ${W-5} ${my} l-9 -5.5 v11 z` | 充电箭头：尖端 (W−5, my) 指向右（电池），底边在 `x = W−14`，半高 5.5 |
| `.ghost--discharge path` | `M ${mx+5} ${my} l9 -5.5 v11 z` | 放电箭头：尖端 (mx+5, my) 指向左（母线），底边在 `x = mx+14`，半高 5.5 |

线样式：

| 类 | 描边 | 线宽 | 其它 | 来源 |
|---|---|---|---|---|
| `.wire` | `var(--wire)` | 2 | `fill:none`，`stroke-linecap/linejoin:round`，`transition:stroke 250ms` | L519–L523 |
| `.pulse` | `var(--accent)` | 3 | `stroke-dasharray: 2 12`，`opacity:0`，`stroke-linecap:round`，`transition:opacity 250ms` | L524–L527 |
| `.pulse.s-bat` | `var(--success)` | 3 | 覆盖上一行的颜色（同为 0,2,0 特异性，规则在后） | L528 |
| `.ghost` | — | — | `opacity:0`，`transition:opacity 250ms`；`fill` 由行内属性给定：`ghost--charge` = `var(--success)`、`ghost--discharge` = `var(--accent)` | L529, L918–L919 |
| 虚线动画 | `@keyframes dashFwd{ to{ stroke-dashoffset:-28 } }`，`animation: dashFwd 1s linear infinite` | — | 虚线段周期 `2+12 = 14px`，1s 位移 28px = **2 个周期/秒，沿路径速度 28px/s** | L542, L645 |

> XAML 落地提示（非规格）：`viewBox` 逐像素重算等价于「用 Canvas 直接按像素布局 Path」，或 `Path` + `StrokeDashArray="2,12"` + 对 `StrokeDashOffset` 做 0 → −28、1s、`LinearEasing`、`RepeatBehavior="Forever"` 的 Storyboard。

#### 3.1.4 线路上的功率标签

```css
.wire-val{ position:absolute; font-size:13px; line-height:16px; white-space:nowrap;
           font-variant-numeric:tabular-nums; color:var(--text-tertiary) }   /* L553–L556 */
.wire-val b{ font-weight:600; color:var(--text-primary) }                     /* L557 */
.wire-val--ac  { left:8px;  top:25%; transform:translateY(-128%) }            /* L558 */
.wire-val--load{ left:8px;  top:75%; transform:translateY(28%) }              /* L559 */
.wire-val--batt{ right:4px; top:50%; transform:translateY(-128%) }            /* L560 */
```

- 定位参照 `.bus-wire`（`position:relative`）。标签是绝对定位块，行高 16px → 高 16px。
- 代入 H = 208 的推算：`--ac` 顶 = `0.25×208 − 0.28×16 = 52 − 20.48 = 31.52`，底 47.52 → **压在线路上方 4.48px**；`--load` 顶 = `156 + 4.48 = 160.48`，底 176.48 → **压在线路下方 4.48px**；`--batt` 顶 = `104 − 20.48 = 83.52`，底 99.52 → 在电池线上方 4.48px。
- 文本：`<b>96.0</b> W`、`<b>50.8</b> W`、`<b>+45.2</b> W`（L921–L923）。数值格式统一 `toFixed(1)`（`nf(v,d=1)`，L1367）：`acNum = nf(S.ac)`、`loadNum = nf(load())`、`battNum = (batt>=0 ? "+" : "") + nf(batt)`（L1657–L1660）。
- 电池功率数字颜色（L1661）：`batt > 0.6` → `var(--success)`；`batt < −0.6` → `var(--caution)`；否则 `var(--text-primary)`。**另两个标签颜色恒定**。

#### 3.1.5 电池外壳与电量

```css
.shell-wrap{ position:relative; padding-top:56px }                    /* L565 */
.battery-case{ position:relative; padding:9px; border-radius:13px;
               border:2px solid var(--card-stroke-strong); background:var(--card-secondary) }  /* L566–L570 */
.battery-pole{ position:absolute; right:-13px; top:50%; transform:translateY(-50%);
               width:11px; height:40px; border-radius:0 7px 7px 0; background:var(--card-stroke-strong) }  /* L571–L575 */
.battery-inner{ position:relative; height:100px; border-radius:8px; background:var(--ctrl-fill-tertiary) } /* L576–L579 */
.soc-fill{ position:absolute; left:0; top:0; bottom:0; width:62%; border-radius:7px 0 0 7px;
           background:linear-gradient(90deg,var(--accent-subtle),var(--accent));
           transition:width 350ms var(--ease-out), background 250ms var(--ease-out) }   /* L580–L584 */
```

| 部件 | 规格 | 来源 |
|---|---|---|
| `.shell-wrap` | 顶部预留 **56px**（给旗标标签用） | L565 |
| `.battery-case` | 内边距 9px；圆角 13px；**边框 2px `var(--card-stroke-strong)`**；背景 `var(--card-secondary)` | L566–L570 |
| 外壳外形尺寸 | `W=430`（固定）、`H = 2+9+100+9+2 = 122px` | 推导 |
| 电池正极柱 `.battery-pole` | 11×40px；相对 `.battery-case` 的 padding box `right:-13px`（= 恰好从边框外沿起向外 11px）；垂直居中；圆角 `0 7px 7px 0`；颜色 `var(--card-stroke-strong)` | L571–L575 |
| `.battery-inner`（内槽） | 高 **100px**；圆角 8px；背景 `var(--ctrl-fill-tertiary)`；宽 = `430−2×2−2×9 = 408px`（推导） | L576–L579 |
| `.soc-fill`（电量填充） | 左贴齐、上下贴齐；宽度 = `clamp(S.soc,0,100)%`（初始 62%）；圆角 `7px 0 0 7px`（比内槽小 1px 以嵌合）；宽/背景色过渡 350ms/250ms | L580–L584, L1957 |
| 默认渐变 | `linear-gradient(90deg, var(--accent-subtle), var(--accent))` | L582 |

**电量填充的配色由 JS 覆盖**（L1950–L1958，写死 rgba，两套主题通用）：

| 条件 | background |
|---|---|
| `S.soc <= 15` | `linear-gradient(90deg, rgba(255,95,86,.35), var(--critical))` |
| `state() === "charging"` | `linear-gradient(90deg, rgba(108,203,95,.30), var(--success))` |
| 其它 | `linear-gradient(90deg, var(--accent-subtle), var(--accent))` |

#### 3.1.6 充电窗口（斜纹区）

```css
.bat-window{ position:absolute; top:0; bottom:0;
  background-color:var(--accent-subtle-2);
  background-image:repeating-linear-gradient(135deg, var(--hatch) 0 3px, transparent 3px 8px);
  border-left:1px solid var(--accent); border-right:1px solid var(--accent);
  transition:left 350ms var(--ease-out), width 350ms var(--ease-out) }   /* L585–L591 */
```

- 位置由 JS 设置：`left = S.start%`、`width = (S.stop − S.start)%`（L1960–L1961），相对 `.battery-inner`。
- 默认 `start=75`、`stop=80`（L1377–L1378）→ `left:75%; width:5%`，即宽度 408×5% = **20.4px**（推导）。
- 斜纹参数：**135°、条纹宽 3px、周期 8px（3px 有色 + 5px 透明）**；条纹色 `--hatch`（深 `rgba(96,205,255,.30)` / 浅 `rgba(0,95,184,.26)`）；底色 `--accent-subtle-2`；左右各 1px `var(--accent)` 竖边。

#### 3.1.7 两个阈值标记与旗标

```css
.marker{ position:absolute; top:-11px; bottom:-11px; width:2px; border-radius:2px;
         background:var(--text-primary); cursor:ew-resize; z-index:2;
         transition:left 250ms var(--ease-out) }                        /* L592–L596 */
.marker::after{ content:""; position:absolute; left:50%; top:-5px; width:11px; height:11px;
         border-radius:50%; background:var(--text-primary); transform:translateX(-50%);
         box-shadow:0 0 0 2px var(--needle-halo);
         transition:transform 150ms var(--ease-out) }                    /* L597–L602 */
.marker:hover::after,.marker[data-drag="true"]::after{ transform:translateX(-50%) scale(1.35) }  /* L603 */
.marker[data-drag="true"]{ transition:none }                             /* L604 */
.marker--start{ background:var(--success) }  .marker--start::after{ background:var(--success) }  /* L605–L606 */
.marker--stop { background:var(--critical) } .marker--stop::after { background:var(--critical) } /* L607–L608 */
```

| 项 | 规格 | 来源 |
|---|---|---|
| 竖条 | 宽 **2px**，圆角 2px；`top:-11px`、`bottom:-11px`（相对 `.battery-inner`，上下各探出 11px，因此正好与外壳上沿齐平，因为内槽缩进 = 2px 边框 + 9px 内边距 = 11px） | L593 |
| 竖条位置 | `left = S.start%` / `left = S.stop%`（相对内槽宽 408px）；推导：默认 75% → 306px、80% → 326.4px | L1962–L1963 |
| 竖条颜色 | start = `var(--success)`；stop = `var(--critical)` | L605–L608 |
| 圆形把手 `::after` | **11×11**，圆角 50%，水平居中（`left:50%` + `translateX(-50%)`），`top:-5px`（相对竖条顶）；外发光 `box-shadow: 0 0 0 2px var(--needle-halo)` | L597–L602 |
| 悬停 / 拖动 | `scale(1.35)`；拖动中取消 left 过渡 | L603–L604 |

旗标（标签）：

```css
.flag{ position:absolute; left:1px; bottom:calc(100% + 8px); transform:translateX(-50%);
       font-size:11px; line-height:16px; font-weight:600; white-space:nowrap;
       padding:1px 8px; border-radius:999px; font-variant-numeric:tabular-nums; pointer-events:none }  /* L609–L614 */
.flag--start{ background:var(--success-bg);  color:var(--success) }    /* L615 */
.flag--stop { background:var(--critical-bg); color:var(--critical) }   /* L616 */
.flag--up   { bottom:calc(100% + 30px) }                               /* L617（用于 stop，避免与 start 重叠） */
.flag[data-edge="left"] { transform:translateX(-12%) }                 /* L618 */
.flag[data-edge="right"]{ transform:translateX(-88%) }                 /* L619 */
```

- 旗标是 `.marker` 的子元素；`left:1px` = 2px 竖条的中心，配合 `translateX(-50%)` 实现与标记线对齐。
- 旗标高度 = `1+16+1 = 18px`（推导）。
- 竖直位置（相对标记线顶部；标记线顶 = 内槽顶 −11px = 外壳顶 +0px）：`--start` 底边在外壳顶上方 8px、顶边在 26px；`--stop`（`flag--up`）底边在上方 30px、顶边在 48px。`.shell-wrap` 的 56px 顶部内边距正好容纳（余 8px）。
- 文本由 JS 生成：`开始充电 ${S.start}%` / `停止充电 ${S.stop}%`（L1964–L1966）→ 默认「开始充电 75%」「停止充电 80%」。
- 边缘避让：`start < 12` → `data-edge="left"`；`start > 88` → `data-edge="right"`；否则属性为空串（走基准 `translateX(-50%)`）。stop 同规则（L1967–L1968）。
- 拖动阈值时 `start` 被限制在 `[40, 99]`，`stop` 在 `[41, 100]`，且保证 `stop > start`（L1972–L1986）。

#### 3.1.8 电池刻度轴

```css
.bat-axis{ position:relative; height:16px; margin-top:4px }               /* L620 */
.bat-axis i{ position:absolute; top:0; transform:translateX(-50%);
             font-size:11px; line-height:16px; font-style:normal;
             color:var(--text-tertiary); font-variant-numeric:tabular-nums }   /* L621–L625 */
.bat-axis i:first-child{ transform:none }          /* 左端不偏移 */        /* L626 */
.bat-axis i:last-child { transform:translateX(-100%) }  /* 右端右对齐 */   /* L627 */
```

- 5 个刻度：文本 `0 / 25 / 50 / 75 / 100`，位置 `left: 0 / 25% / 50% / 75% / 100%`（L943–L946）。
- 轴容器高 16px、`margin-top:4px`，宽度 = `.battery-gauge` 宽 430px。
- **原稿的实际行为（推导，供实现者复刻）**：刻度轴参照的是 430px 的 gauge 宽，而标记/斜纹窗口参照的是 408px 的内槽宽（内槽左右各内缩 11px），两者不是同一坐标系——例如「75」刻度位于 x=322.5px，而 75% 的标记位于 x=317px。规格按原稿保留该差异。

#### 3.1.9 右侧读数（battery-info / binfo）

```css
.battery-info{ flex:0 0 auto; display:flex; flex-direction:column; justify-content:center;
               gap:20px; padding-left:30px; padding-top:12px }         /* L630–L633 */
.binfo{ display:flex; flex-direction:column; gap:1px }                    /* L634 */
.binfo-v{ display:flex; align-items:baseline; font-family:var(--font-display);
          font-weight:600; letter-spacing:-.03em }                        /* L635 */
.binfo-v b{ font-size:32px; line-height:38px; font-variant-numeric:tabular-nums }  /* L636 */
.binfo-v i{ font-style:normal; font-size:15px; line-height:20px;
            color:var(--text-tertiary); margin-left:2px }                 /* L637 */
.binfo-k{ font-size:11px; line-height:15px; color:var(--text-tertiary) }  /* L638 */
```

| 层级 | 字号/行高 | 颜色 | 来源 |
|---|---|---|---|
| 主数值 `b` | **32px / 38px**，`--font-display`，600，`letter-spacing:-.03em`，tabular-nums | 随状态 | L635–L636 |
| 单位 `i` | **15px / 20px**，非斜体，`margin-left:2px` | `var(--text-tertiary)` | L637 |
| 说明 `k` | **11px / 15px** | `var(--text-tertiary)` | L638 |

- 两组内容（L951–L958）：`62` + `%` + 「当前电量」；`34.6` + `°C` + 「电池温度」。数值格式：电量取整（`Math.round(S.soc)`，L1653），温度一位小数（`nf(S.temp)`，L1669）。
- 基线对齐：数值与单位 `align-items:baseline`。
- 电池温度数值颜色（L1670）：`temp >= 55` → `var(--critical)`；`>= 45` → `var(--caution)`；否则 `var(--text-primary)`。
- 推导的竖向位置（行高 208px、内容块 `2×54 + 20 = 128px`、`padding-top:12px`）：内容自 `y = 12 + (208−12−128)/2 = 46` 起排布；第一组占 y 46–100，第二组占 y 120–174。该推算以「`.binfo-v` 的行盒高度等于其 `line-height` 38px」为前提；实现时按 Auto 高度排布，实际像素可能相差 1–2px。

### 3.2 性能与散热卡 `#thermalCard`（L963–L991，样式 L652–L730）

#### 3.2.1 卡片头里的 SelectorBar

```css
.selectorbar{ position:relative; display:inline-flex; padding:3px; gap:2px;
              border-radius:var(--radius); background:var(--ctrl-fill-tertiary);
              border:1px solid var(--card-stroke) }                       /* L379–L382 */
.selectorbar-pill{ position:absolute; top:3px; bottom:3px; border-radius:3px;
              background:var(--accent); transition:left 250ms var(--ease-out), width 250ms var(--ease-out) }  /* L383–L386 */
.sb-item{ position:relative; z-index:1; height:32px; padding:0 16px; border:0; background:transparent;
          color:var(--text-secondary); font-size:14px; border-radius:3px;
          display:inline-flex; align-items:center; gap:8px;
          transition:color 250ms var(--ease-out), background 150ms var(--ease-out) }   /* L387–L392 */
.sb-item:hover{ background:var(--subtle-hover) }                          /* L393 */
.sb-item[aria-selected="true"]{ color:var(--accent-text); font-weight:600 } /* L394 */
.sb-item[aria-selected="true"]:hover{ background:transparent }            /* L395 */
```

| 项 | 值 | 来源 |
|---|---|---|
| 外框 | 内边距 **3px**、`gap:2px`、圆角 **4px**、背景 `var(--ctrl-fill-tertiary)`、边框 `1px solid var(--card-stroke)` | L380–L381 |
| 整体高度（推导） | `32 + 3×2 + 1×2 = 40px` | L388 |
| 分段按钮 | 高 **32px**、左右内边距 **16px**、圆角 3px、14px、图标与文字 `gap:8px`；未选中文字 `var(--text-secondary)` | L388–L391 |
| 选中态 | 文字 `var(--accent-text)`（深色 `#000000` / 浅色 `#ffffff`）+ **字重 600**；悬停不再变色块 | L394–L395 |
| 滑块 | 上下各内缩 3px（即高 34px），圆角 **3px**，纯色 `var(--accent)`，位置与宽度由 JS 写成 `left = offsetLeft`、`width = offsetWidth`，250ms 过渡 | L384–L385, L1896–L1898 |
| 图标 | `.icon` 16×16（`ic-sparkle` / `ic-bolt-filled`） | L968–L969 |

分段内容：**智能**（`data-mode="smart"`，`aria-selected="true"`，`ic-sparkle`）、**高性能**（`data-mode="high"`，`ic-bolt-filled`）（L966–L970）。

#### 3.2.2 thermal-body 两栏

```css
.thermal-body{ display:flex; align-items:center; gap:30px }   /* L652 */
.sensor-list{ flex:0 0 400px; display:flex; flex-direction:column; gap:16px }  /* L653 */
.curve-wrap { flex:1; min-width:0 }                            /* L677 */
```

- 左栏固定 **400px**，两栏间距 **30px**，右栏吃剩余宽度 → 在 1400px 窗口下曲线区宽 `1078 − 400 − 30 = 648px`（推导）。
- `align-items:center`：两栏在垂直方向居中。

#### 3.2.3 传感器列表

DOM 结构由 JS 生成（L1479–L1481）：`<i class="s-dot">` + `<span class="s-name">` + `<div class="s-bar"><div class="s-needle"></div></div>` + `<span class="s-val">`。

```css
.sensor-item{ display:grid; grid-template-columns:9px 40px minmax(0,1fr) 74px;
              align-items:center; gap:11px }                          /* L654 */
.sensor-item .s-dot{ width:9px; height:9px; border-radius:50%;
              background:currentColor; box-shadow:0 0 0 3px var(--card) }  /* L655 */
.sensor-item .s-name{ font-size:12px; line-height:16px; color:var(--text-tertiary) }  /* L656 */
.s-bar{ position:relative; height:10px; border-radius:5px; background:var(--temp-grad);
        box-shadow:inset 0 0 0 1px var(--card-stroke) }               /* L657–L661 */
.s-bar::after{ content:""; position:absolute; left:55%; top:-2px; bottom:-2px;
        width:1.5px; background:rgba(0,0,0,.42) }   /* 过热参考线 55 °C */  /* L662–L665 */
.s-needle{ position:absolute; top:-6px; bottom:-6px; left:0; width:3px; border-radius:2px;
        background:var(--text-primary); transform:translateX(-50%);
        box-shadow:0 0 0 2px var(--needle-halo), 0 1px 4px rgba(0,0,0,.4);
        transition:left 350ms var(--ease-out) }                        /* L666–L671 */
.sensor-item .s-val{ font-size:14px; line-height:18px; font-weight:600; text-align:right;
        font-variant-numeric:tabular-nums; transition:color 250ms var(--ease-out) }  /* L672–L675 */
```

| 部件 | 尺寸 / 位置 | 来源 |
|---|---|---|
| 行栅格 | 列宽 `9px / 40px / 1fr / 74px`，列间距 **11px**；推导：1fr（温度条）= `400−9−40−74−11×3 = 244px` | L654 |
| 行高（推导） | 取最高子项 `s-val` 的 18px | L673 |
| 色点 `.s-dot` | **9×9** 圆，颜色 = `currentColor`（由 JS 按色阶写入），外圈 `0 0 0 3px var(--card)` 形成与卡片同色的描边隔离 | L655 |
| 名称 `.s-name` | 12px/16px `var(--text-tertiary)`；内容 `CPU / GPU / SSD` | L656, L1394–L1398 |
| 温度条 `.s-bar` | 高 **10px**、圆角 **5px**、背景为**固定色阶贴图** `var(--temp-grad)`，内阴影 1px `var(--card-stroke)` | L658–L660 |
| 55 °C 参考线 | 固定位于 **left: 55%**（= 244×0.55 = **134.2px**），宽 1.5px，上下各溢出 2px，颜色 `rgba(0,0,0,.42)`（固定值） | L663–L664 |
| 指针 `.s-needle` | 宽 **3px**、圆角 2px、上下各溢出 6px（总高 22px）、`translateX(-50%)`；`left = clamp(temp,0,100)%`；白色（`--text-primary`）+ 双阴影 | L667–L670, L1593 |
| 读数 `.s-val` | 14px/18px、600、右对齐、tabular-nums；文本 `温度.toFixed(1) + " °C"` | L673, L1594 |

**色阶（阈值分段）** —— 由 `tone = [var(--success), var(--caution), var(--critical)]` 与 `lvl = v>=hot ? 2 : (v>=warn ? 1 : 0)` 决定（L1587, L1591）；色点、指针读数颜色、曲线上的标记同色：

| 传感器 | warn | hot | `< warn`（lvl 0） | `[warn, hot)`（lvl 1） | `>= hot`（lvl 2） |
|---|---|---|---|---|---|
| CPU | 70 | 85 | `var(--success)` | `var(--caution)` | `var(--critical)` |
| GPU | 75 | 88 | 同上 | 同上 | 同上 |
| SSD | 55 | 70 | 同上 | 同上 | 同上 |

阈值定义见 L1394–L1398；两套主题的具体颜色：深色 `#6ccb5f` / `#fce100` / `#ff99a4`，浅色 `#0f7b0f` / `#9d5d00` / `#c42b1c`（L68–L73, L121–L126）。

温度条背景色阶的具体色标（百分比 → 颜色）：

| 位置 | 0% | 26% | 50% | 68% | 84% | 100% |
|---|---|---|---|---|---|---|
| 深色 | `#4cc2ff` | `#6ccb5f` | `#b8d94a` | `#fce100` | `#ff9f43` | `#ff5f56` |
| 浅色 | `#0f8fd6` | `#0f7b0f` | `#7a8a12` | `#c9a800` | `#d97316` | `#c42b1c` |

（来源 L75 与 L128。该渐变是**纯装饰性固定色阶**，除 55% 参考线外不做温度数值映射；指针位置用的是 `temp%`。）

传感器可用性：只有 `S.avail[id]` 为真的传感器才出现在列表里（L1470）；三者为空且无风扇时整张卡 `display:none`（L1575）。

#### 3.2.4 风扇曲线 SVG

标记（L979）：`<svg class="curve" id="curveSvg" viewBox="0 0 560 210">`；`.curve{ width:100%; height:210px; display:block; overflow:visible }`（L678）。

**运行时 viewBox 被改写**为 `0 0 W 210`，其中 `W = max(360, curveWrap.clientWidth || 560)`（L1582–L1584）。1400px 窗口下 `W = 648`（推导）。

绘图参数（L1535–L1537）：

```js
const CP = { padL:46, padR:16, padT:16, padB:28, h:210 };
const tx = (t, W) => CP.padL + clamp(t,0,100)/100 * (W - CP.padL - CP.padR);   // = 46 + t/100*(W-62)
const ry = r      => (CP.h - CP.padB) - clamp(r,0,FAN_MAX)/FAN_MAX * (CP.h - CP.padB - CP.padT);
                  // = 182 - r/6000 * 166
```

- 绘图区：x ∈ [46, W−16]，y ∈ [16, 182]。代入 W=648：`tx(0)=46`、`tx(25)=192.5`、`tx(50)=339`、`tx(75)=485.5`、`tx(100)=632`；`ry(0)=182`、`ry(3000)=99`、`ry(6000)=16`。
- 横轴：温度 0–100 °C；纵轴：转速 0–6000 RPM。

网格（L1543–L1568）：

| 方向 | 刻度 | 线 | 文字 |
|---|---|---|---|
| 横轴 | `t = 0, 25, 50, 75, 100` | `line(x1=x, y1=16, x2=x, y2=182)` | `y = 210−8 = 202`；锚点：t=0 为 `start`，t=100 为 `end`，其余 `middle`；文本 `0/25/50/75` 与 **`100 °C`**（单位只挂在末端刻度） |
| 纵轴 | `r = 0, 3000, 6000` | `line(x1=46, y1=y, x2=W−16, y2=y)` | `x = 46−8 = 38`，`y = ry(r)+4`，锚点 `end`；文本 `0` / `3k` / `6k`（`r/1000 + "k"`） |
| 单位 | — | — | 文本 `RPM`，`x=38`、`y = 16−4 = 12`，锚点 `end` |

样式：`.curve-grid line{ stroke:var(--chart-grid); stroke-width:1 }`、`.curve-grid text{ font-size:11px; fill:var(--text-tertiary); font-family:var(--font-text) }`（L679–L680）。

曲线与填充：

```css
.curve-area{ fill:var(--accent); opacity:.10 }                              /* L681 */
.curve-line{ fill:none; stroke:var(--accent); stroke-width:2.5;
             stroke-linecap:round; stroke-linejoin:round;
             transition:d 350ms var(--ease-out) }                           /* L682–L686 */
```

- 曲线路径（L1600–L1605）：`t` 从 0 到 100 每 **1 °C** 取一点，`d += (t ? "L" : "M") + tx(t).toFixed(1) + " " + ry(curveRpm(t)).toFixed(1)` → **101 个点的折线**（不是贝塞尔平滑）。
- 面积路径：`d + "L" + tx(100) + " 182" + "L" + tx(0) + " 182" + "Z"`。
- 无可用风扇时两者都置空（L1606–L1607）。

转速策略 `curveRpm(t)`（L1526–L1533）：

| 模式 | 公式 |
|---|---|
| `max` | `6000` |
| `custom` | `clamp(interpPoints(S.fanPoints, t), 0, 6000)`，控制点默认 `[{35,1200},{50,1800},{65,3000},{80,4400},{95,5600}]`（L1384），区间内**线性插值**，区间外取端点值（L1516–L1525） |
| `auto` + `smart` | `lo=48, hi=100`，`k = clamp((t−48)/52, 0, 1)`，`rpm = 1100 + k^1.15 × (6000−1100)` |
| `auto` + `high` | `lo=42, hi=96`，`k = clamp((t−42)/54, 0, 1)`，其余同上 |

`auto + smart` 的取值校验表（按上式计算）：

| t (°C) | 35 | 48 | 50 | 65 | 80 | 88 | 100 |
|---|---|---|---|---|---|---|---|
| k | 0 | 0 | 0.0385 | 0.3269 | 0.6154 | 0.7692 | 1 |
| RPM | 1100.0 | 1100.0 | 1215.5 | 2454.6 | 3903.6 | 4723.8 | 6000.0 |

传感器在曲线上的竖线与点（L1609–L1621）：

```css
.curve-mark line{ stroke-width:1.5; stroke-dasharray:3 4; opacity:.75 }   /* L687 */
.curve-mark circle{ stroke:var(--card); stroke-width:2 }                  /* L688 */
```

- 每个可用传感器生成一条 `line`：`x1=x2=tx(v)`，`y1=16`，`y2=182`，`stroke = tone[lvl]`（与列表色点同色）；虚线 `3 4`、线宽 1.5、不透明度 0.75。
- 若存在可用风扇，再在曲线上的同 x 处画一个 `circle`：`cx=tx(v)`、`cy=ry(curveRpm(v))`、**r=4**、`fill=tone[lvl]`，描边 `var(--card)` 2px。

实际工作点光环（L1623–L1630）：

```css
.op-halo{ fill:none; stroke:var(--accent); stroke-width:2; opacity:.4 }   /* L689 */
.op-dot { fill:var(--accent); stroke:var(--card); stroke-width:2.5 }      /* L690 */
```

- 标记（L984–L985）：`<circle class="op-halo" r="10">`、`<circle class="op-dot" r="4.5">`。
- 位置：`cx = tx(maxT)`（各可用传感器温度的**最大值**），`cy = ry(avg)`（可用风扇转速的**算术平均**）（L1624–L1630）。
- 隐藏方式：`cx = cy = -99`（移出绘图区），条件是「无可用风扇」或「无可用传感器」（L1625）。

#### 3.2.5 风扇行

```css
.fan-row{ display:flex; flex-wrap:wrap; gap:16px 44px }                   /* L692 */
.fan-mini{ display:flex; align-items:center; gap:14px }                   /* L693 */
.fan-rotor{ width:62px; height:62px; flex:0 0 62px; border-radius:50%;
            display:grid; place-items:center;
            background:var(--card-secondary); border:1px solid var(--card-stroke) }   /* L694–L698 */
.fan-mini .rotor-svg{ width:50px; height:50px }                           /* L699 */
.rotor{ transform-box:view-box; transform-origin:32px 32px;
        animation:spin var(--spin,2s) linear infinite; fill:var(--accent) }  /* L700–L704 */
.rotor.is-still{ animation-play-state:paused }                            /* L705 */
@keyframes spin{ to{ transform:rotate(360deg) } }                         /* L706 */
.fan-ring{ fill:none; stroke:var(--card-stroke-strong); stroke-width:1.5 } /* L711 */
.fan-hub{ fill:var(--card-stroke-strong) }                                 /* L712 */
.fan-pin{ fill:var(--wire) }                                               /* L713 */
.fan-name{ font-size:12px; line-height:16px; color:var(--text-tertiary); white-space:nowrap }  /* L714 */
.fan-rpm{ font-size:12px; line-height:28px; color:var(--text-tertiary);
          font-variant-numeric:tabular-nums; white-space:nowrap }          /* L715–L718 */
.fan-rpm b{ font-family:var(--font-display); font-size:24px; line-height:28px; font-weight:600;
            letter-spacing:-.02em; color:var(--text-primary); margin-right:3px }  /* L719–L722 */
```

- 行间距：换行时行距 **16px**，同排风扇之间 **44px**。
- 单个风扇：转子 62×62（圆、1px `--card-stroke` 边框、背景 `--card-secondary`）→ 与文字 `gap:14px` → 名称（12px/16px，`--text-tertiary`，内容「风扇 1/2/3」，L1504）→ 转速。
- 转速排版：`<b>1,400</b> RPM`，数字 24px/28px `--font-display` 600 `letter-spacing:-.02em` 白色，与「RPM」间距 3px；「RPM」与外围行高 12px/28px `--text-tertiary`。数字用 `Math.round` 后按 `en-US` 千分位格式化（L1368, L1637）。

**转子 SVG（viewBox `0 0 64 64`，渲染为 50×50，缩放系数 0.78125）**（L996–L1001）：

| 元素 | 参数 | 渲染尺寸（推导） | 来源 |
|---|---|---|---|
| 外框 `circle.fan-ring` | `cx=32 cy=32 r=29.5`，`fill:none`，`stroke=var(--card-stroke-strong)`，`stroke-width:1.5` | r = 23.05px，线宽 1.17px | L997, L711 |
| 叶片组 `g.rotor` | 5 片 path，围绕 (32,32) 每片旋转 `i × 72°`；`transform-origin: 32px 32px`；`fill=var(--accent)` | — | L700–L704, L1429–L1434 |
| 轮毂 `circle.fan-hub` | `cx=32 cy=32 r=7`，`fill=var(--card-stroke-strong)` | r = 5.47px | L999, L712 |
| 中心销 `circle.fan-pin` | `cx=32 cy=32 r=2.2`，`fill=var(--wire)` | r = 1.72px | L1000, L713 |

**叶片路径算法**（L1413–L1435）：`bladePath(cx,cy,r0,r1,a0,a1,sweep)`，角度以度为单位、`P(r,deg) = [cx + r·cos, cy + r·sin]`，返回

```
M rf Q c1 tf Q c2 tb Q c3 rb Z
   rf = P(r0, a0)                       rb = P(r0, a1)
   tf = P(r1, a0+sweep)                 tb = P(r1, a1+sweep)
   c1 = P((r0+r1)/2 + 2.5, a0 + sweep×0.42)      /* 前缘外凸 */
   c2 = P(r1 + 1.5, (a0+a1)/2 + sweep)           /* 尖端 */
   c3 = P(r0 + 4, a1 + sweep×0.42)               /* 后缘内凹 */
```

调用参数：`bladePath(32, 32, 7, 26, 0, 22, 48)`，共 **5 片**，每片 `rotate(i×72 32 32)`（L1426–L1434）。坐标保留 2 位小数。代入后的单片 `d`（推导值，可直接用于校验）：

```
M39.00 32.00 Q49.84 38.55 49.40 51.32 Q46.16 55.57 40.89 56.43 Q40.15 39.38 38.49 34.62 Z
```

**转速 → 旋转周期映射**（L1633–L1643）：

```js
ratio = clamp(rpm / 6000, 0, 1);
dur   = 2.6 - Math.pow(ratio, 0.75) * 2.35;    // 秒/转
rotor.style.setProperty("--spin", dur.toFixed(2) + "s");
rotor.classList.toggle("is-still", rpm < 120);
rotor.style.fill = ratio > 0.85 ? "var(--caution)" : "var(--accent)";
```

| rpm | ratio | 周期（秒/转） | 视觉转速（转/秒） |
|---|---|---|---|
| 0 | 0 | 2.60（且 `rpm<120` → 暂停） | 0 |
| 120 | 0.02 | 2.48 | 0.404 |
| 1300 | 0.2167 | 1.85 | 0.539 |
| 1400 | 0.2333 | 1.81 | 0.552 |
| 3000 | 0.5 | 1.20 | 0.831 |
| 6000 | 1 | **0.25** | 4 |

- 转速 < 120 RPM：`is-still` → `animation-play-state: paused`（转子静止）。
- `ratio > 0.85`（即 rpm > 5100）时叶片填充改为 `var(--caution)`，否则 `var(--accent)`。
- 减弱动效：`@media (prefers-reduced-motion: reduce){ .rotor{ animation-duration: calc(var(--spin,2s) × 3) } }`（L708–L710）——降速而非停转。

### 3.3 电池输入功率卡（L1008–L1014，绘制逻辑 L1724–L1777）

- 卡片：`section.card.sp12`；头左 `ic-chart` + 「电池输入功率」，头右 `.card-note` = 「最近 60 秒」（L1010–L1011）。
- 图表容器：`.chart-wrap{ position:relative; height:170px }`，`canvas{ width:100%; height:100%; display:block }`（L735–L736）→ 在 1400px 窗口下画布 CSS 尺寸 **1078 × 170**（推导）。

绘制规则（`drawChart()`，L1725–L1777）：

1. **设备像素比**：`cv.width = clientWidth × dpr`、`cv.height = clientHeight × dpr`，`ctx.setTransform(dpr,0,0,dpr,0,0)`；随后 `clearRect`（L1726–L1731）。
2. **内边距**：`padL=44, padR=12, padT=12, padB=22` → 绘图区 `iw = w−56 = 1022`、`ih = h−34 = 136`（推导，w=1078/h=170）。
3. **数值范围**：`MAXP = 100`（L1391），即纵轴固定 **±100 W**。
4. **Y 映射**：`Y(v) = padT + ih/2 − clamp(v,−100,100)/100 × (ih/2)` = `80 − v×0.68`（推导）。
5. **网格线**：`v = [−100, −50, 0, 50, 100]`，`strokeStyle = var(--chart-grid)`、`lineWidth = 1`、绘制在 `y + 0.5`（像素对齐）；**零线用虚线 `setLineDash([4,4])`，其余为实线**；每条线画完后复位 `setLineDash([])`（L1743–L1748）。
   - 代入：y = 148 / 114 / 80 / 46 / 12。
6. **刻度文字**：字体 `11px "Segoe UI Variable Text","Segoe UI",system-ui,sans-serif`，`textBaseline="middle"`，`fillStyle = var(--text-tertiary)`，右对齐于 `x = padL−8 = 36`，内容 `(v>0 ? "+" : "") + v` → `-100 / -50 / 0 / +50 / +100`（L1741–L1751）。
7. **X 轴标注**：`"−60 s"` 左对齐于 `(44, h−8 = 162)`；`"现在"` 右对齐于 `(w−12 = 1066, 162)`（L1752–L1753）。
8. **数据窗口**：`history` 每 0.4s 采样一次（L1831–L1835），保留最近 400 个；绘图取 **N = 180** 个点，`step = iw/(N−1) = 1022/179 = 5.709px`，最新点落在 `x = padL + iw = 1066`（L1756–L1757）。样本不足 2 个时只画坐标轴就返回（L1755）。
9. **面积填充**：竖直线性渐变 `createLinearGradient(0, padT, 0, padT+ih)`，`0% → accent+"55"`（透明度 0x55 ≈ 33.3%）、`100% → accent+"00"`（全透明）；路径为 `moveTo(第一点 x, Y(0))` → 依次 `lineTo` 各数据点 → `lineTo(最后点 x, Y(0))` → `closePath` → `fill`。填充基线固定为**零线**（L1759–L1765）。
10. **曲线描边**：`strokeStyle = var(--accent)`、`lineWidth = 2`、`lineJoin = "round"`、`lineCap = "round"`；**逐点直线连接，不做平滑/样条**（L1767–L1770）。
11. **末端点**：实心圆 `arc(最后点, 4)` 填充 `--accent`；再套一圈 `arc(最后点, 7)`，`strokeStyle = accent+"44"`（≈26.7% 透明）、`lineWidth = 2`、`stroke()`（L1772–L1776）。
12. **颜色在每次绘制时从 CSS 变量读取**（`--chart-grid` / `--accent` / `--text-tertiary`），因此主题切换后重绘即变色（L1735–L1738, L1866）。

### 3.4 首页其余元素

**InfoBar 行 `#alertRow`**（L879–L884，样式 L482–L497）：默认 `style="display:none"`，跨 12 列，内部 `.infobar#statusBar`。

| 项 | 规格 | 来源 |
|---|---|---|
| `.infobar` | 行内 flex，`align-items:flex-start`，`gap:12px`，`padding:12px 14px`，圆角 4px，背景 `var(--card-secondary)`，边框 `1px solid var(--card-stroke)` | L482–L486 |
| 图标 | 20×20，`margin-top:1px`，默认 `var(--text-secondary)` | L487 |
| 标题 | 14px/600/20px | L488 |
| 消息 | 12px/16px `var(--text-tertiary)`，`margin-top:1px`（**首页未渲染消息节点**） | L489 |
| 语气 | 四种 `data-tone`（success/caution/critical/accent）统一：背景取对应 `--*-bg` 或 `--accent-subtle`，**边框透明**，图标取对应主色 | L490–L497 |

内容由 JS 写入：`statusTitle` 文本 + `statusIcon` 的 `href`（L1692–L1696），图标为 `#ic-warn` 或 `#ic-info`。

**演示控制台 Expander**（L1017–L1100，样式 L739–L763）：`div.sp12` 包 `.expander[data-open="true"]`。

| 项 | 规格 | 来源 |
|---|---|---|
| `.expander` | 圆角 7px，背景 `var(--card-secondary)`，边框 `1px solid var(--card-stroke)`，`overflow:hidden` | L739 |
| 头部按钮 | 高由内容撑开，`padding:12px 14px`，`gap:12px`；悬停 `var(--subtle-hover)` | L740–L745 |
| 头部内容 | `ic-settings`（`--accent`）+ 「演示控制台」（14px/600）+ `.caption` 说明 + 右侧 `ic-chevron`（`margin-left:auto`，展开时 `rotate(90deg)`，250ms） | L746–L747, L1020–L1023 |
| 展开动画 | `.expander-body` 用 `grid-template-rows: 0fr → 1fr`，250ms `--ease-out` | L748–L751 |
| 内边距 | `padding: 4px 14px 16px`，纵向 `gap:14px` | L753 |
| 分组标题 `.ctl-group` | 12px/16px/600 `--text-tertiary`，`letter-spacing:.04em`，下边框 1px `--divider` | L754–L758 |
| 控件栅格 `.ctl-grid` | `repeat(auto-fit, minmax(238px,1fr))`，`gap:12px 22px` | L759 |
| ToggleSwitch | 轨道 40×20 圆角 10px；滑块 12×12（选中后 14×14，`left:23px`），选中轨道 `var(--accent)` | L361–L375 |
| Slider | 轨道高 4px 圆角 2px，已填充部分 `var(--accent)`，滑块 20×20 圆形带双层内阴影；数值文本 `min-width:60px`、右对齐、14px/600 | L399–L426 |
| Chip | 12px/16px，`padding:3px 9px`，圆角 999px，背景 `var(--card-stroke)`；`data-on="true"` → 背景 `--accent-subtle`、文字 `--accent`、600 | L725–L730 |

---

## 4. 首页之外的页面清单（一句话级）

| 页面 | `data-page` | 标题 / 副标题 | 内容概述 |
|---|---|---|---|
| 首页 | `home` | 首页 / 实时监控电池、电源与性能状态 | 本文档描述的对象（L874–L1102） |
| 电池设置 | `battery` | 电池设置 / 充电阈值与电池保养 | 充电阈值卡（sp7，复用同一电池本体组件）+ 电池保养设置卡（sp5）（L1106–L1196） |
| 性能设置 | `perf` | 性能设置 / 性能模式、风扇曲线与功耗上限 | 性能模式单选卡（sp5）+ 功耗限制卡（sp7）+ 可拖拽的风扇曲线编辑卡（sp12，viewBox `0 0 600 230`，控制点 r=7）（L1199–L1276） |
| 充放电历史 | `hist-batt` | 充放电历史 / 电池输入功率随时间变化 | 时间范围 SelectorBar（1 小时 / 24 小时 / 7 天）+ 大图表卡（`chart-wrap--lg`，高 300px）+ 4 个统计卡（sp3）（L1279–L1301） |
| 功耗历史 | `hist-ac` | 功耗历史 / 电源功率与系统负载随时间变化 | 同上结构，双序列（电源功率实线填充 + 系统负载虚线）+ 3 个统计卡（sp4）（L1304–L1328） |
| 设置 | `about` | 设置 / 设备与关于信息 | 设备信息卡（sp7）+ 关于卡（sp5）（L1331–L1356） |

---

## 5. 交互与状态

### 5.1 状态判定（母线状态机）

```js
const state = () => S.batt > 0.6 ? "charging" : (S.batt < -0.6 ? "discharging" : "idle");   // L1402
$("busWire").dataset.state = !S.plug ? "discharging"
      : (charging ? "charging" : (st === "discharging" ? "discharging" : "idle"));          // L1665
```

- 阈值：`batt > +0.6 W` → charging；`batt < −0.6 W` → discharging；否则 idle。
- **未接入适配器时强制为 `discharging`**（不管 batt 正负）。
- CSS 中定义了 `[data-state="off"]` 规则，但 JS **从不写入 `"off"`**（L543, L548）——它是留给实现者的状态位。

### 5.2 `data-state` 相关规则全表

**（a）虚线流动段与可见性**（L531–L543）：

```css
.bus-wire[data-state="charging"]    .s-ac,
.bus-wire[data-state="charging"]    .s-dn,
.bus-wire[data-state="charging"]    .s-bat,
.bus-wire[data-state="charging"]    .s-dn2,
.bus-wire[data-state="charging"]    .s-ld,
.bus-wire[data-state="idle"]        .s-ac,
.bus-wire[data-state="idle"]        .s-dn,
.bus-wire[data-state="idle"]        .s-dn2,
.bus-wire[data-state="idle"]        .s-ld,
.bus-wire[data-state="discharging"] .s-rev,
.bus-wire[data-state="discharging"] .s-dn2,
.bus-wire[data-state="discharging"] .s-ld
    { opacity:1; animation:dashFwd 1s linear infinite }

.bus-wire[data-state="off"] .pulse{ opacity:0; animation:none }
```

| 状态 | 参与流动的 `.pulse` 段 | 静止（透明）的段 |
|---|---|---|
| `charging` | `s-ac`、`s-dn`、**`s-bat`（绿色）**、`s-dn2`、`s-ld` | `s-rev` |
| `idle` | `s-ac`、`s-dn`、`s-dn2`、`s-ld`（**不经过电池**） | `s-bat`、`s-rev` |
| `discharging` | `s-rev`、`s-dn2`、`s-ld` | `s-ac`、`s-dn`、`s-bat` |
| `off` | 无（单个 `.pulse` 被显式置 `opacity:0; animation:none`） | 全部 |

所有 `.pulse` 的基础态都是 `opacity:0`（L526），因此上表「静止」即不可见；透明度切换带 250ms 过渡（L526）。动画参数统一为 `dashFwd 1s linear infinite`（L542, L645），虚线 `stroke-dasharray:2 12`、线宽 3、颜色 `var(--accent)`（`s-bat` 例外 = `var(--success)`，L524–L528）。

**（b）未参与当前回路的实线变暗**（L545–L548）：

```css
.bus-wire[data-state="idle"]        .w-bat,
.bus-wire[data-state="discharging"] .w-ac,
.bus-wire[data-state="discharging"] .w-dn,
.bus-wire[data-state="off"]         .wire
    { stroke: var(--card-stroke) }
```

| 状态 | 被改色的实线 | 其余实线 |
|---|---|---|
| `charging` | 无 | 全部 `var(--wire)`，线宽 2 |
| `idle` | `w-bat`（电池支路） | `w-ac`、`w-dn`、`w-dn2`、`w-ld` 为 `var(--wire)` |
| `discharging` | `w-ac`、`w-dn`（适配器支路） | `w-bat`、`w-dn2`、`w-ld` 为 `var(--wire)` |
| `off` | 全部 `.wire` | — |

改色用的是**描边颜色**（降到 `--card-stroke`：深色 `rgba(255,255,255,.0698)` / 浅色 `rgba(0,0,0,.0578)`），不是 `opacity`。

**（c）方向箭头 ghost**（L549–L550，形状见 3.1.3）：

```css
.bus-wire[data-state="charging"]    .ghost--charge   { opacity:1 }   /* fill = var(--success) */
.bus-wire[data-state="discharging"] .ghost--discharge{ opacity:1 }   /* fill = var(--accent)  */
```

基础态 `opacity:0`（L529），过渡 250ms；三角形静止不动（无动画）。

**（d）标签与节点的颜色/透明度**：

| 对象 | 规则 | 来源 |
|---|---|---|
| `#acNum`、`#loadNum`（`b` 元素） | 恒为 `var(--text-primary)`（`.wire-val b`） | L557 |
| `#battNum` | `batt > 0.6` → `var(--success)`；`batt < −0.6` → `var(--caution)`；否则 `var(--text-primary)`；文本带 `+`/`−` 号 | L1659–L1661 |
| `.wire-val` 本体 | 恒为 `var(--text-tertiary)` | L556 |
| `#acNode` | `data-off = !S.plug` → `opacity:.45` （250ms 过渡） | L514, L1664 |
| `#loadNode` | 不随状态变化 | — |
| 电量填充 / 充电窗口 | 见 3.1.5 / 3.1.6（颜色与宽度由 `S.soc` / `S.start` / `S.stop` 决定） | L1950–L1969 |

### 5.3 InfoBar 出现条件

默认隐藏（标记里 `style="display:none"`，L879）。每帧重算，**只有异常才显示**（L1686–L1696）：

| 优先级 | 条件 | tone | 图标 | 标题 |
|---|---|---|---|---|
| 1 | `soc <= 15 && !charging` | `critical` | `#ic-warn` | 电量极低，请接入电源 |
| 2 | `temp >= 55` | `critical` | `#ic-warn` | 电池温度过高 |
| 3 | `!plug && soc <= 30` | `caution` | `#ic-info` | 未接入电源 |
| — | 以上都不满足 | — | — | `#alertRow` 置 `display:none` |

> 注意实现细节：三个条件用 `if / else if` 顺序判定，命中第一个即停（L1688–L1690）。正常的充电/放电/空闲状态**不**显示 InfoBar，由母线流向与电池填充表达（注释 L878, L1686）。

### 5.4 其它交互与刷新节奏

| 交互 | 行为 | 来源 |
|---|---|---|
| 导航折叠 | `#navToggle` 切换 `data-collapsed`（232px ↔ 48px，250ms） | L1851–L1854 |
| 页面切换 | 点击导航项 → 切换 `.is-active`、更新页面标题/副标题、下一帧重绘母线/曲线/图表 | L2040–L2056 |
| 主题切换 | `data-theme` 在 `dark`/`light` 间切换；按钮文案「浅色/深色」与图标 `#ic-sun`/`#ic-moon` 同步；`requestAnimationFrame(drawChart)` 重绘 | L1861–L1870 |
| 刷新按钮 | 清空历史，把 `S` 复位为 `soc:62, temp:34.6, batt:45.2, ac:96, start:75, stop:80, plug:true, auto:true, cpu:52, gpu:48, ssd:40`，风扇 `[1400,1300,0]`，并预填 180 个历史点 | L1873–L1883 |
| 阈值拖动 | 在 `.battery-inner` 上按指针位置换算百分比（`(clientX − left)/width × 100`，clamp 0–100），`start ∈ [40,99]`、`stop ∈ [41,100]`，且强制 `stop > start` | L1988–L2014, L1972–L1986 |
| 性能模式 | 智能/高性能按钮同时驱动首页 SelectorBar 与性能设置页的单选卡；切换后强制重画曲线网格 | L1892–L1902, L2204–L2205 |
| 传感器可用性 | 演示控制台的 chip 切换 `S.avail`，不可用传感器/风扇从界面消失；全无时整张散热卡 `display:none` | L1913–L1922, L1575 |
| 渲染节奏 | `render()` 约每 0.05s 一次（≈20Hz）；历史采样每 0.4s 一次；`requestAnimationFrame` 主循环，`dt` 上限 0.1s | L1826–L1837 |
| 尺寸变化 | 母线/曲线/图表各自用 `ResizeObserver` 重新计算几何 | L2342–L2353 |

---

## 6. 原稿中未被使用 / 需实现者留意的点

以下均为对原稿代码的事实核对结果，列出以便实现者判断（**不建议擅自"修正"**）：

1. **`--violet` 未定义**：L1319 与 L2174 引用了 `var(--violet)`，但 `:root`/dark/light 都没有定义它；L2174 有兜底 `|| "#a78bfa"`，L1319 没有。仅影响「功耗历史」页。
2. **定义了但从未使用的类/变量**：`.card--alt`（L449）、`.card-head-right`（L642）、`.legend` 系列（L639–L641）、`.soc-body`（仅在 L783 媒体查询中出现，无对应元素）、`--smoke`（L77/L130）、`--shadow-flyout`（L78/L131）、`--bg-base`（L38/L91）、`--radius-sm`（L22）、`--ease-in`（L17）。首页用到的 `--bg-base`/`--radius-sm` 为零。
3. **`[data-state="off"]` 是死状态**：CSS 定义了（L543, L548），但 JS 的 L1665 只会产生 `charging` / `discharging` / `idle`。
4. **图表窗口与附注不一致**：卡片附注写「最近 60 秒」（L1011），而实现是每 0.4s 采样一次、绘图窗口 `N = 180`（L1756）→ 覆盖 `180 × 0.4 = 72s` 数据。
5. **`.s-bar` 的色阶是装饰性的**：渐变（L75/L128）不随阈值分段，唯一的位置映射是固定 55% 处的参考线（L663）；指针位置用的是 `temp%`（L1593）。
6. **刻度轴与内槽坐标系不同**：`.bat-axis` 参照 430px 的 gauge 宽度，而标记/斜纹参照 408px 的内槽宽度（差 11px），见 3.1.8 的推算。
7. **InfoBar 只有标题**：`.infobar-msg` 样式存在（L489），但首页 DOM 未渲染消息节点（L880–L883），JS 也只写 `statusTitle`（L1694）。
8. **母线 SVG 不缩放**：`preserveAspectRatio="none"` 保留在标记里（L906），但 JS 每次都把 `viewBox` 改成元素的实际像素尺寸（L1448），因此实际是 1:1 像素绘制；若实现者沿用固定 viewBox 会出现虚线被拉伸的问题（原稿注释 L516、L1437–L1438 明确回避了这一点）。

---

## 7. 附录：首页用到的图标

统一规格：`.icon{ width:16px; height:16px; flex:0 0 auto; stroke:currentColor; fill:none; stroke-width:1.5;
stroke-linecap:round; stroke-linejoin:round; display:block }`（L153）；`.icon--20` 为 20×20（L154）；`.icon--filled{ fill:currentColor; stroke:none }`（L155）。所有 symbol 的 viewBox 均为 `0 0 20 20`（L792–L814）。

| id | 首页用途 | path 数据 | 来源 |
|---|---|---|---|
| `ic-menu` | 导航折叠按钮（20×20） | `M3 5.5h14M3 10h14M3 14.5h14` | L792 |
| `ic-home` | 导航「首页」 | `M3.5 8.9 10 3.6l6.5 5.3V16a.9.9 0 0 1-.9.9h-3.2v-4.6h-4.8v4.6H4.4a.9.9 0 0 1-.9-.9z` | L793 |
| `ic-battery` | 导航「充放电历史」 | `<rect x="2.2" y="6" width="13.2" height="8" rx="2.2"/><path d="M17.6 8.6v2.8"/><path d="M10.6 7.6 8 10.4h2.6L9.9 12.9"/>` | L794 |
| `ic-plug` | 电源卡标题、电源适配器节点、标题栏（行内重绘） | `<path d="M7 2.8v4.4M13 2.8v4.4"/><path d="M4.6 7.2h10.8v2.6a5.4 5.4 0 0 1-10.8 0z"/><path d="M10 15.2v2.2"/>` | L795 |
| `ic-gauge` | 系统负载节点、散热卡标题、导航「性能设置」 | `<path d="M2.8 14.4a7.6 7.6 0 1 1 14.4 0"/><path d="M10 13.6 13.6 8.4"/><circle cx="10" cy="14.4" r="1.5"/>` | L796 |
| `ic-sliders` | 导航「电池设置」 | `<path d="M3 6.2h8.4M15.2 6.2H17M3 13.8h3.4M10.2 13.8H17"/><circle cx="13.2" cy="6.2" r="2"/><circle cx="8.2" cy="13.8" r="2"/>` | L797 |
| `ic-settings` | 导航「设置」、演示控制台头部 | `<circle cx="10" cy="10" r="2.4"/>` + 8 段刻度线 | L798 |
| `ic-sparkle` | 智能模式分段按钮 | `M10 2.6l1.7 4.7 4.7 1.7-4.7 1.7L10 15.4l-1.7-4.7L3.6 9l4.7-1.7z` + 小星 | L803 |
| `ic-bolt-filled` | 高性能模式分段按钮（实心） | `M11.6 2 5 11.2h4.3l-.8 6.8L15 8.8h-4.4z`（`fill="currentColor" stroke="none"`） | L814 |
| `ic-chart` | 「电池输入功率」卡标题、导航「功耗历史」 | `<path d="M3.4 3v13.6h13.2"/><path d="M6.2 12.8l3.2-4.2 2.8 2.4 3.6-5"/>` | L804 |
| `ic-refresh` | 刷新按钮 | `<path d="M16.4 9.2a6.6 6.6 0 1 1-2-4.7"/><path d="M16.6 2.8v4.4h-4.4"/>` | L805 |
| `ic-sun` / `ic-moon` | 主题切换按钮（初始 `#ic-sun`） | L806 / L807 | L806–L807 |
| `ic-info` | InfoBar `caution` | `<circle cx="10" cy="10" r="7.4"/><path d="M10 9v4.4M10 6.6v.01"/>` | L809 |
| `ic-warn` | InfoBar `critical` | 三角 + `M10 7.6V11M10 13.4v.01` | L810 |
| `ic-chevron` | 演示控制台展开箭头 | `M7.6 4.6 13 10l-5.4 5.4` | L811 |

标题栏左侧图标为行内 SVG（非 symbol 引用），viewBox `0 0 20 20`、`fill:none`、`stroke-width:1.5`、`stroke-linecap/linejoin:round`，路径与 `ic-battery` 相同（L822–L824）。
