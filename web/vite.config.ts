import { defineConfig, loadEnv } from 'vite';
import react from '@vitejs/plugin-react';

// https://vitejs.dev/config/
export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), '');
  const apiBaseUrl = env.VITE_API_BASE_URL;

  return {
    plugins: [react()],
    server: {
      proxy: apiBaseUrl
        ? undefined
        : {
            '/api': {
              // Override with VITE_PROXY_TARGET to point the dev server at a
              // different backend (e.g. the demo-nvidia stack on :8087).
              target: env.VITE_PROXY_TARGET || 'http://localhost:5232',
              changeOrigin: true,
              secure: false,
            },
          },
    },
    test: {
      environment: 'jsdom',
      globals: true,
      setupFiles: './src/test/setup.ts',
      css: true,
      coverage: {
        reporter: ['text', 'html'],
      },
    },
  };
});
