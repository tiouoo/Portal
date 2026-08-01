import { createServer } from 'vite'
import { renderToString } from '@vue/server-renderer'
import { readFile, writeFile, mkdir, readdir } from 'node:fs/promises'
import path from 'node:path'

const root = process.cwd()
const outDir = path.resolve(root, 'dist')
const baseUrl = 'https://portal.tiouo.cc'

const routes = [
  { path: '/', output: 'index.html' },
  {
    path: '/macos-install',
    output: path.join('macos-install', 'index.html'),
    schemaOrg: {
      '@context': 'https://schema.org',
      '@type': 'HowTo',
      name: 'Portal macOS 安装与解除系统限制',
      description:
        '解决 macOS 提示 Portal 已损坏、无法打开或文件损坏的问题：选择正确的安装包并解除系统隔离限制。',
      totalTime: 'PT5M',
      step: [
        {
          '@type': 'HowToStep',
          name: '选择正确的安装包',
          text: 'Apple 芯片（M1/M2/M3/M4）下载 arm64 文件，Intel 芯片下载 x64 文件。'
        },
        {
          '@type': 'HowToStep',
          name: '安装到应用程序文件夹',
          text: '双击打开 DMG 镜像，将 Portal 图标拖动到“应用程序”文件夹；app.zip 先解压再移动。'
        },
        {
          '@type': 'HowToStep',
          name: '在终端执行命令解除限制',
          text: '打开终端执行 sudo xattr -rd com.apple.quarantine /Applications/Portal.app，输入开机密码后回车。'
        }
      ]
    }
  }
]

const server = await createServer({
  root,
  logLevel: 'error',
  server: { middlewareMode: true },
  appType: 'custom',
})

try {
  const { createApp } = await server.ssrLoadModule('/src/main.js')
  const template = await readFile(path.join(outDir, 'index.html'), 'utf-8')

  const assetFiles = await readdir(path.join(outDir, 'assets'))
  const assetMap = new Map()
  for (const file of assetFiles) {
    const match = file.match(/^(.+?)-[A-Za-z0-9_-]{8}\.(\w+)$/)
    if (match) assetMap.set(`${match[1]}.${match[2]}`, `/assets/${file}`)
  }
  const inlineable = [
    { source: 'src/assets/create.jpg', mime: 'image/jpeg' }
  ]
  for (const { source, mime } of inlineable) {
    try {
      const data = await readFile(path.join(root, source))
      assetMap.set(path.basename(source), `data:${mime};base64,${data.toString('base64')}`)
    } catch { /* ignore missing */ }
  }
  const remapAssets = (html) => {
    for (const [name, hashed] of assetMap) {
      html = html.replaceAll(`/src/assets/${name}`, hashed)
    }
    return html
  }

  for (const route of routes) {
    const { app, router } = await createApp()
    router.push(route.path)
    await router.isReady()
    const body = await renderToString(app)
    const meta = router.currentRoute.value.meta

    const title = meta.title
    const description = meta.description
    const canonical = route.path === '/' ? `${baseUrl}/` : `${baseUrl}${route.path}/`
    const ogTitle = meta.ogTitle ?? title
    const ogDescription = meta.ogDescription ?? description

    let html = template
      .replace(/<title>[^<]*<\/title>/, `<title>${title}</title>`)
      .replace(
        /<meta\s+name="description"\s+content="[^"]*"\s*\/?>/,
        `<meta name="description" content="${description}" />`
      )
      .replace(/<link rel="canonical" href="[^"]*"\s*\/?>/,
        `<link rel="canonical" href="${canonical}" />`)
      .replace(
        /<meta property="og:title" content="[^"]*"\s*\/?>/,
        `<meta property="og:title" content="${ogTitle}" />`
      )
      .replace(
        /<meta property="og:description" content="[^"]*"\s*\/?>/,
        `<meta property="og:description" content="${ogDescription}" />`
      )
      .replace(
        /<meta property="og:url" content="[^"]*"\s*\/?>/,
        `<meta property="og:url" content="${canonical}" />`
      )
      .replace(
        /<meta name="twitter:title" content="[^"]*"\s*\/?>/,
        `<meta name="twitter:title" content="${ogTitle}" />`
      )
      .replace(
        /<meta name="twitter:description" content="[^"]*"\s*\/?>/,
        `<meta name="twitter:description" content="${ogDescription}" />`
      )
      .replace('<div id="app"></div>', `<div id="app">${body}</div>`)

    if (route.schemaOrg) {
      html = html.replace(
        '</head>',
        `    <script type="application/ld+json">${JSON.stringify(route.schemaOrg)}</script>\n  </head>`
      )
    }

    html = remapAssets(html)

    const outputPath = path.join(outDir, route.output)
    await mkdir(path.dirname(outputPath), { recursive: true })
    await writeFile(outputPath, html, 'utf-8')
    console.log(`Prerendered ${route.path} -> ${route.output} (${Buffer.byteLength(body, 'utf-8')} bytes of content)`)
  }
} finally {
  await server.close()
}
