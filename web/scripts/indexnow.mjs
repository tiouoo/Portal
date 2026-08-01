import { readFile, readdir } from 'node:fs/promises'
import path from 'node:path'

const root = process.cwd()
const publicDir = path.join(root, 'public')

const keyFile = (await readdir(publicDir)).find((f) =>
  /^[a-zA-Z0-9-]{8,128}\.txt$/.test(f) && f !== 'robots.txt'
)
if (!keyFile) throw new Error('未找到 IndexNow key 文件（public/ 下的 {key}.txt）')
const key = keyFile.replace(/\.txt$/, '')

const sitemap = await readFile(path.join(root, 'dist', 'sitemap.xml'), 'utf-8')
const urls = [...sitemap.matchAll(/<loc>([^<]+)<\/loc>/g)].map((m) => m[1])
if (urls.length === 0) throw new Error('sitemap 中没有 URL，请先执行 npm run build')
const host = new URL(urls[0]).host

const payload = { host, key, keyLocation: `https://${host}/${key}.txt`, urlList: urls }
console.log(`Submitting ${urls.length} URLs to IndexNow (key: ${key})`)

for (const endpoint of ['https://api.indexnow.org/indexnow', 'https://www.bing.com/indexnow']) {
  const res = await fetch(endpoint, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json; charset=utf-8' },
    body: JSON.stringify(payload),
  })
  console.log(`${endpoint} -> ${res.status} ${res.statusText}`)
}
