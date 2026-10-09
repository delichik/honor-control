import { useCallback, useRef, useState } from 'react'

/**
 * 电池图形 —— 设计稿里的 `.battery-gauge`：**电池就是电池**。
 *
 * 外壳 + 正极 + 内槽，内槽里只有三样东西：电量填充、充电窗口（斜纹）、两个阈值标记。
 * 电量百分比与温度读数不在这里，而是放在电池右侧（首页）或卡片别处（设置页），
 * 这样电池本体只承载"电量 + 阈值"这一件事。
 *
 * 一根 0–100 的轴同时表达三件事：当前电量、充电窗口、两个阈值的位置。
 * `interactive` 打开后可以拖动标记（设置页用），首页只读；两种模式渲染完全一致。
 */
export function BatteryGauge({
  percent = 0,
  startPercent = 40,
  stopPercent = 70,
  charging = false,
  interactive = false,
  showAxis = true,
  onChangeStart,
  onChangeStop,
}) {
  const innerRef = useRef(null)
  const [dragging, setDragging] = useState(null)

  const hasPercent = typeof percent === 'number' && Number.isFinite(percent)
  const hasWindow =
    typeof startPercent === 'number' && Number.isFinite(startPercent) &&
    typeof stopPercent === 'number' && Number.isFinite(stopPercent)
  const canEdit = interactive && hasWindow
  const soc = Math.min(100, Math.max(0, hasPercent ? percent : 0))
  const start = Math.min(100, Math.max(0, startPercent ?? 0))
  const stop = Math.min(100, Math.max(0, stopPercent ?? 100))
  const windowWidth = Math.max(0, stop - start)

  // 电量填充的三套配色：低电量优先于充电态（设计稿里 soc<=15 排在最前）
  const fillTone = soc <= 15 ? 'low' : charging ? 'charging' : 'normal'

  const percentAt = useCallback((clientX) => {
    const rect = innerRef.current?.getBoundingClientRect()
    if (!rect || rect.width === 0) return null
    return ((clientX - rect.left) / rect.width) * 100
  }, [])

  const handlePointerDown = (event) => {
    if (!canEdit) return
    const value = percentAt(event.clientX)
    if (value === null) return
    // 选离指针更近的标记，避免两个标记挨在一起时抢焦点
    const target = Math.abs(value - start) <= Math.abs(value - stop) ? 'start' : 'stop'
    setDragging(target)
    innerRef.current?.setPointerCapture(event.pointerId)
  }

  const handlePointerMove = (event) => {
    if (!dragging) return
    const raw = percentAt(event.clientX)
    if (raw === null) return
    const value = Math.round(Math.min(100, Math.max(0, raw)))
    // 拖动时保持 start < stop，与服务端的校验规则一致
    if (dragging === 'start') onChangeStart?.(Math.min(value, stop - 1))
    else onChangeStop?.(Math.max(value, start + 1))
  }

  const handlePointerUp = (event) => {
    if (!dragging) return
    setDragging(null)
    innerRef.current?.releasePointerCapture(event.pointerId)
  }

  const edgeFor = (value) => (value < 12 ? 'left' : value > 88 ? 'right' : undefined)

  return (
    <div className="hc-battery-gauge">
      <div className="hc-shell-wrap">
        <div className="hc-battery-case">
          <div
            ref={innerRef}
            className="hc-battery-inner"
            onPointerDown={handlePointerDown}
            onPointerMove={handlePointerMove}
            onPointerUp={handlePointerUp}
            onPointerCancel={handlePointerUp}
            role="img"
            aria-label={`${hasPercent ? `当前电量 ${Math.round(soc)}%` : '当前电量未知'}${hasWindow ? `，充电窗口 ${start}% 到 ${stop}%` : '，充电阈值未知'}`}
          >
            {hasPercent ? <div className="hc-soc-fill" data-tone={fillTone} style={{ width: `${soc}%` }} /> : null}
            {hasWindow ? (
              <>
                <div
                  className="hc-bat-window"
                  style={{ left: `${start}%`, width: `${windowWidth}%` }}
                  hidden={windowWidth <= 0}
                />
                <div
                  className={canEdit ? 'hc-marker hc-marker--start hc-marker--interactive' : 'hc-marker hc-marker--start'}
                  style={{ left: `${start}%` }}
                  data-drag={dragging === 'start' ? 'true' : undefined}
                >
                  <span className="hc-flag hc-flag--start" data-edge={edgeFor(start)}>
                    开始充电 {start}%
                  </span>
                </div>
                <div
                  className={canEdit ? 'hc-marker hc-marker--stop hc-marker--interactive' : 'hc-marker hc-marker--stop'}
                  style={{ left: `${stop}%` }}
                  data-drag={dragging === 'stop' ? 'true' : undefined}
                >
                  <span className="hc-flag hc-flag--stop hc-flag--up" data-edge={edgeFor(stop)}>
                    停止充电 {stop}%
                  </span>
                </div>
              </>
            ) : null}
          </div>
          <div className="hc-battery-pole" />
        </div>
      </div>

      {showAxis ? (
        <div className="hc-bat-axis">
          {[0, 25, 50, 75, 100].map((tick) => (
            <i key={tick} style={{ left: `${tick}%` }}>
              {tick}
            </i>
          ))}
        </div>
      ) : null}
    </div>
  )
}
