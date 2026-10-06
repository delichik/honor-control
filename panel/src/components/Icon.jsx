/**
 * 图标：逐条照搬设计稿的内联 SVG（viewBox 0 0 20 20、1.5 描边、线性）。
 *
 * 为什么不用 @fluentui/react-icons：设计稿里的图标是同一套 Fluent 线性风格的手绘版，
 * 尺寸（16×16 视觉盒）与笔画粗细都按卡片标题的 14px 字号调过；换成图标字体或另一套
 * 图标库，笔画粗细与留白会对不上，卡片标题的观感立刻走样。
 *
 * 每个图标的子元素与设计稿的 <symbol> 一一对应，不要"顺手简化"路径。
 */
const ICONS = {
  menu: <path d="M3 5.5h14M3 10h14M3 14.5h14" />,
  home: <path d="M3.5 8.9 10 3.6l6.5 5.3V16a.9.9 0 0 1-.9.9h-3.2v-4.6h-4.8v4.6H4.4a.9.9 0 0 1-.9-.9z" />,
  battery: (
    <>
      <rect x="2.2" y="6" width="13.2" height="8" rx="2.2" />
      <path d="M17.6 8.6v2.8" />
      <path d="M10.6 7.6 8 10.4h2.6L9.9 12.9" />
    </>
  ),
  plug: (
    <>
      <path d="M7 2.8v4.4M13 2.8v4.4" />
      <path d="M4.6 7.2h10.8v2.6a5.4 5.4 0 0 1-10.8 0z" />
      <path d="M10 15.2v2.2" />
    </>
  ),
  gauge: (
    <>
      <path d="M2.8 14.4a7.6 7.6 0 1 1 14.4 0" />
      <path d="M10 13.6 13.6 8.4" />
      <circle cx="10" cy="14.4" r="1.5" />
    </>
  ),
  sliders: (
    <>
      <path d="M3 6.2h8.4M15.2 6.2H17M3 13.8h3.4M10.2 13.8H17" />
      <circle cx="13.2" cy="6.2" r="2" />
      <circle cx="8.2" cy="13.8" r="2" />
    </>
  ),
  settings: (
    <>
      <circle cx="10" cy="10" r="2.4" />
      <path d="M10 2.4v2.2M10 15.4v2.2M17.6 10h-2.2M4.6 10H2.4M15.4 4.6l-1.6 1.6M6.2 13.8l-1.6 1.6M15.4 15.4l-1.6-1.6M6.2 6.2 4.6 4.6" />
    </>
  ),
  thermo: (
    <>
      <path d="M8.2 11.4V4.6a1.8 1.8 0 0 1 3.6 0v6.8a3.2 3.2 0 1 1-3.6 0z" />
      <path d="M14.6 6.6h2.6M14.6 10h2M14.6 13.4h1.4" />
    </>
  ),
  chip: (
    <>
      <rect x="5.2" y="5.2" width="9.6" height="9.6" rx="2" />
      <rect x="8" y="8" width="4" height="4" rx="1" />
      <path d="M8 2.6v2.6M12 2.6v2.6M8 14.8v2.6M12 14.8v2.6M2.6 8h2.6M2.6 12h2.6M14.8 8h2.6M14.8 12h2.6" />
    </>
  ),
  fan: (
    <>
      <circle cx="10" cy="10" r="1.9" />
      <path d="M10.4 8.1c-.6-1.9-.1-4.3 1.4-4.4 1.5-.1 2 2.2.6 3.6" />
      <path d="M11.8 11c1.8-.7 4.2-.5 4.5 1 .3 1.5-1.9 2.3-3.5 1.1" />
      <path d="M8.2 11c-1.8-.7-4.2-.5-4.5 1-.3 1.5 1.9 2.3 3.5 1.1" />
      <path d="M9.6 8.1c.6-1.9.1-4.3-1.4-4.4" />
    </>
  ),
  bolt: <path d="M11.4 2.4 4.6 11h4.2l-.9 6.6L14.9 9h-4.3z" />,
  'bolt-filled': <path d="M11.6 2 5 11.2h4.3l-.8 6.8L15 8.8h-4.4z" />,
  sparkle: (
    <>
      <path d="M10 2.6l1.7 4.7 4.7 1.7-4.7 1.7L10 15.4l-1.7-4.7L3.6 9l4.7-1.7z" />
      <path d="M15.6 13.4l.7 1.9 1.9.7-1.9.7-.7 1.9-.7-1.9-1.9-.7 1.9-.7z" />
    </>
  ),
  chart: (
    <>
      <path d="M3.4 3v13.6h13.2" />
      <path d="M6.2 12.8l3.2-4.2 2.8 2.4 3.6-5" />
    </>
  ),
  refresh: (
    <>
      <path d="M16.4 9.2a6.6 6.6 0 1 1-2-4.7" />
      <path d="M16.6 2.8v4.4h-4.4" />
    </>
  ),
  sun: (
    <>
      <circle cx="10" cy="10" r="3.4" />
      <path d="M10 2.2v1.8M10 16v1.8M17.8 10H16M4 10H2.2M15.5 4.5l-1.3 1.3M5.8 14.2l-1.3 1.3M15.5 15.5l-1.3-1.3M5.8 5.8 4.5 4.5" />
    </>
  ),
  moon: <path d="M16.4 12.2A7 7 0 0 1 7.8 3.6a7 7 0 1 0 8.6 8.6z" />,
  'check-circle': (
    <>
      <circle cx="10" cy="10" r="7.4" />
      <path d="M6.8 10.2 9 12.4l4.2-4.6" />
    </>
  ),
  info: (
    <>
      <circle cx="10" cy="10" r="7.4" />
      <path d="M10 9v4.4M10 6.6v.01" />
    </>
  ),
  warn: (
    <>
      <path d="M9.1 3.3 2.5 15a1 1 0 0 0 .9 1.5h13.2a1 1 0 0 0 .9-1.5L10.9 3.3a1 1 0 0 0-1.8 0z" />
      <path d="M10 7.6V11M10 13.4v.01" />
    </>
  ),
  chevron: <path d="M7.6 4.6 13 10l-5.4 5.4" />,
  'arrow-left': (
    <>
      <path d="M16 10H4" />
      <path d="M9 5l-5 5 5 5" />
    </>
  ),
  'arrow-right': (
    <>
      <path d="M4 10h12" />
      <path d="M11 5l5 5-5 5" />
    </>
  ),
}

/** 实心图标：设计稿用 fill=currentColor + stroke=none 单独处理。 */
const FILLED = new Set(['bolt-filled'])

export function Icon({ name, size = 16, className, style, title }) {
  const children = ICONS[name]
  if (!children) return null

  const classes = ['hc-icon']
  if (size === 20) classes.push('hc-icon--20')
  if (FILLED.has(name)) classes.push('hc-icon--filled')
  if (className) classes.push(className)

  return (
    <svg
      className={classes.join(' ')}
      viewBox="0 0 20 20"
      style={style}
      role={title ? 'img' : undefined}
      aria-label={title}
      aria-hidden={title ? undefined : 'true'}
      focusable="false"
    >
      {children}
    </svg>
  )
}

export const ICON_NAMES = Object.keys(ICONS)
