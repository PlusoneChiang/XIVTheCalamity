const { build } = require('vite');
const { viteSingleFile } = require('vite-plugin-singlefile');
const path = require('path');

async function main() {
  console.log('Building login page...');
  await build({
    configFile: false,
    root: path.resolve(__dirname, '../src/renderer'),
    build: {
      outDir: path.resolve(__dirname, '../dist'),
      emptyOutDir: true,
      assetsInlineLimit: 100000000,
      rollupOptions: {
        input: {
          login: path.resolve(__dirname, '../src/renderer/pages/login/index.html'),
        },
      },
    },
    plugins: [
      viteSingleFile()
    ],
  });

  console.log('Building settings page...');
  await build({
    configFile: false,
    root: path.resolve(__dirname, '../src/renderer'),
    build: {
      outDir: path.resolve(__dirname, '../dist'),
      emptyOutDir: false,
      assetsInlineLimit: 100000000,
      rollupOptions: {
        input: {
          settings: path.resolve(__dirname, '../src/renderer/pages/settings/settings.html'),
        },
      },
    },
    plugins: [
      viteSingleFile()
    ],
  });

  console.log('Copying built files to dist root and cleaning up...');
  const fs = require('fs');
  fs.copyFileSync(
    path.resolve(__dirname, '../dist/pages/login/index.html'),
    path.resolve(__dirname, '../dist/login.html')
  );
  fs.copyFileSync(
    path.resolve(__dirname, '../dist/pages/settings/settings.html'),
    path.resolve(__dirname, '../dist/settings.html')
  );
  fs.rmSync(path.resolve(__dirname, '../dist/pages'), { recursive: true, force: true });

  console.log('Build completed successfully!');
}

main().catch(err => {
  console.error('Build failed:', err);
  process.exit(1);
});
