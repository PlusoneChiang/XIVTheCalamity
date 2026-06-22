import { defineConfig } from 'vite';
import { viteSingleFile } from 'vite-plugin-singlefile';
import path from 'path';

export default defineConfig({
  root: path.resolve(__dirname, 'src/renderer'),
  build: {
    outDir: path.resolve(__dirname, 'dist'),
    emptyOutDir: true,
    assetsInlineLimit: 100000000, // inline everything
    rollupOptions: {
      input: {
        login: path.resolve(__dirname, 'src/renderer/pages/login/index.html'),
        settings: path.resolve(__dirname, 'src/renderer/pages/settings/settings.html'),
      },
    },
  },
  plugins: [
    viteSingleFile()
  ],
});
