import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// 面板前端由 Tauri 承载：产物输出到 dist（与 src-tauri/tauri.conf.json 的 frontendDist 对应），
// dev server 固定 1420 端口（与 tauri.conf.json 的 devUrl 对应），端口被占用时直接失败而不是静默换端口。
export default defineConfig({
  plugins: [react()],
  clearScreen: false,
  server: {
    port: 1420,
    strictPort: true,
  },
  build: {
    outDir: 'dist',
    emptyOutDir: true,
    // 目标环境是 Windows 11 上的 WebView2（常青版），不需要为老浏览器降级。
    target: 'chrome120',
  },
})
