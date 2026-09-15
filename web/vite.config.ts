import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// The API allows exactly one origin, the Static Web App hostname, so a request
// straight from the dev server is refused. Proxying makes the browser issue a
// same-origin request that Vite forwards, so no CORS is involved and no
// infrastructure change is needed. The alternative is adding this origin to the
// bicep devOrigins parameter, which costs a deploy every time it changes.
const api = process.env.VITE_API_ORIGIN ?? 'https://func-idealpoints.azurewebsites.net';

export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      '/api': {
        target: api,
        changeOrigin: true,
      },
    },
  },
});
