import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// The dev server proxies the SignalR hub to the ASP.NET Core backend on :5080,
// so the client can talk to "/hub/game" with no CORS in development.
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      '/hub': {
        target: 'http://localhost:5080',
        ws: true,
        changeOrigin: true,
      },
    },
  },
});
