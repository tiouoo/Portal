import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import { readFileSync, writeFileSync, unlinkSync } from 'node:fs'
import { join } from 'node:path'

function inlineCss() {
  return {
    name: 'portal-inline-css',
    apply: 'build',
    writeBundle({ dir }) {
      const indexPath = join(dir, 'index.html')
      const html = readFileSync(indexPath, 'utf8')
      const match = html.match(
        /<link[^>]*rel="stylesheet"[^>]*href="([^"]+)"[^>]*>/
      )
      if (!match) return
      const cssPath = join(dir, match[1].replace(/^\//, ''))
      const css = readFileSync(cssPath, 'utf8')
      writeFileSync(indexPath, html.replace(match[0], `<style>${css}</style>`))
      unlinkSync(cssPath)
    }
  }
}

export default defineConfig({
  plugins: [vue(), inlineCss()],
  server: {
    port: 5174,
    strictPort: true
  }
})
