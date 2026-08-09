import { build } from 'vite';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const root = path.resolve(__dirname, '..');
const distDir = path.join(root, 'dist');

const routes = [
  { path: '/', output: 'index.html' },
  { path: '/macos-install', output: 'macos-install/index.html' },
];

async function prerender() {
  process.chdir(root);

  console.log('\n📦 [1/5] Building client assets...');

  await build({
    configFile: 'vite.config.ts',
    build: {
      outDir: distDir,
      cssCodeSplit: false,
      emptyOutDir: true,
    },
  });

  console.log('📝 [2/5] Extracting CSS for inlining...');
  let cssContent = '';
  const assetsDir = path.join(distDir, 'assets');
  if (fs.existsSync(assetsDir)) {
    const files = fs.readdirSync(assetsDir).filter((f) => f.endsWith('.css'));
    if (files.length > 0) {
      const cssFile = path.join(assetsDir, files[0]);
      cssContent = fs.readFileSync(cssFile, 'utf-8');
      fs.unlinkSync(cssFile);
      console.log(`   Inlined ${files[0]} (${(cssContent.length / 1024).toFixed(1)} KB)`);
    }
  }

  console.log('🔧 [3/5] Building SSR bundle...');

  await build({
    configFile: 'vite.config.ts',
    build: {
      outDir: path.join(distDir, 'server'),
      emptyOutDir: false,
      ssr: path.resolve(root, 'src/entry-server.ts'),
      rollupOptions: {
        external: ['vue', 'vue-router'],
        output: {
          format: 'esm',
        },
      },
    },
  });

  console.log('🎨 [4/5] Prerendering routes...');
  const ssrEntry = path.join(distDir, 'server', 'entry-server.js');
  const { render } = await import(pathToFileURL(ssrEntry).href);

  for (const route of routes) {
    process.stdout.write(`    → ${route.path} ... `);
    const appHtml = await render(route.path);

    const templatePath = path.join(distDir, 'index.html');
    let html = fs.readFileSync(templatePath, 'utf-8');

    html = html.replace(
      /<div[^>]*id="app"[^>]*>[\s\S]*?<\/div>/,
      `<div id="app">${appHtml}</div>`
    );

    if (cssContent) {
      html = html.replace(
        /<link[^>]*rel="stylesheet"[^>]*>/,
        `<style>${cssContent}</style>`
      );
    }

    const outputDir = path.dirname(path.join(distDir, route.output));
    if (!fs.existsSync(outputDir)) fs.mkdirSync(outputDir, { recursive: true });
    fs.writeFileSync(path.join(distDir, route.output), html);

    console.log('✓');
  }

  console.log('🧹 [5/5] Cleaning up...');
  const serverDir = path.join(distDir, 'server');
  if (fs.existsSync(serverDir)) fs.rmSync(serverDir, { recursive: true });

  if (fs.existsSync(path.join(distDir, 'assets'))) {
    const dir = fs.readdirSync(path.join(distDir, 'assets'));
    if (dir.length === 0) fs.rmdirSync(path.join(distDir, 'assets'));
  }

  console.log('\n✅ Prerender complete!');
  console.log(`   Output: ${distDir}/`);
  console.log(`   Routes: ${routes.map((r) => r.path).join(', ')}\n`);
}

prerender().catch((err) => {
  console.error('\n❌ Prerender failed:\n', err);
  process.exit(1);
});
