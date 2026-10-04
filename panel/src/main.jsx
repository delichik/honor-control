import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { FluentProvider } from '@fluentui/react-components'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import App from './App.jsx'
import { ThemeTokensContext, useAppTheme } from './app/theme.js'
import './styles/global.css'

/**
 * 查询客户端配置。
 *
 * 重试策略刻意保守：管道通信的失败原因（服务未安装/未启动/版本不一致）基本都是"重试也没用"，
 * 重试只会让界面多转几秒。真正的恢复路径是设置页的"启动服务"。
 */
const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      retry: 0,
      refetchOnWindowFocus: true,
      staleTime: 0,
    },
  },
})

function Root() {
  const { theme } = useAppTheme()
  return (
    <FluentProvider theme={theme} style={{ height: '100%' }}>
      {/* SVG / 图表需要字面量色值（var() 在表现属性里无效），这里把当前主题传下去。 */}
      <ThemeTokensContext.Provider value={theme}>
        <QueryClientProvider client={queryClient}>
          <App />
        </QueryClientProvider>
      </ThemeTokensContext.Provider>
    </FluentProvider>
  )
}

createRoot(document.getElementById('root')).render(
  <StrictMode>
    <Root />
  </StrictMode>,
)
