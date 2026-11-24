/// <reference types="vitest/config" />
import react from '@vitejs/plugin-react';
import { defineConfig, type Plugin } from 'vite';
import { PRODUCT_NAME } from './src/config/product.ts';

const API_ORIGIN = 'http://localhost:8080';

// 產品名稱還是暫定的；index.html 的標題也從同一個常數來，改名時只改一個地方。
function productNamePlugin(): Plugin {
  return {
    name: 'pm-tool-product-name',
    transformIndexHtml: (html) => html.replaceAll('%PRODUCT_NAME%', PRODUCT_NAME),
  };
}

export default defineConfig({
  plugins: [react(), productNamePlugin()],
  server: {
    proxy: {
      // 即時更新（SignalR）走 WebSocket：要放在 /v1 前面，代理才會做協定升級。
      '/v1/live': { target: API_ORIGIN, ws: true },
      '/v1': API_ORIGIN,
      '/health': API_ORIGIN,
    },
  },
  build: {
    outDir: 'dist',
    // 正式環境的內容安全政策不允許 data: 網址，資源一律輸出成獨立的檔案。
    assetsInlineLimit: 0,
    // 最低支援的瀏覽器（docs/DESIGN.md 第 12 節）：light-dark() 要 Chrome／Edge 123、Firefox 120、Safari 17.5，
    // :has() 要 Firefox 121。預設的目標比較舊，建置會把 light-dark() 改寫成模擬的變數，手動選的主題就不準了。
    cssTarget: ['chrome123', 'edge123', 'firefox121', 'safari17.5', 'ios17.5'],
    sourcemap: false,
  },
  test: {
    environment: 'jsdom',
    setupFiles: ['src/test/setup.ts'],
    restoreMocks: true,
    // 預設 5 秒：整套（約 2,600 個）加上覆蓋率一起跑時，互動多的測試（設定頁、變更集、時間軸畫 1,000 列）偶爾超過，
    // 每次是不同的那幾個，單獨跑都過（2026-11-18 實測）。放寬到 15 秒，真的卡住的測試照樣會被抓到。
    testTimeout: 15_000,
    coverage: {
      provider: 'v8',
      include: ['src/**/*.{ts,tsx}'],
      exclude: ['src/test/**', 'src/**/*.test.{ts,tsx}', 'src/main.tsx', 'src/**/*.d.ts'],
      reporter: ['text-summary', 'text'],
      thresholds: { lines: 80 },
    },
  },
});
