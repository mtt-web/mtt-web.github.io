import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import { resolve } from 'path'

// https://vitejs.dev/config/
export default defineConfig({
  plugins: [react()],
  base: '/widgets/',
  build: {
    outDir: 'dist',
    assetsDir: 'assets',
    rollupOptions: {
      input: {
        // All widgets/embeddable components
        'population-chart': resolve(__dirname, 'src/widgets/PopulationChart/index.jsx'),
        'geography-charts': resolve(__dirname, 'src/widgets/GeographyCharts/index.jsx'),
        'analytics-page': resolve(__dirname, 'src/widgets/AnalyticsPage/index.jsx'),
        'counter-widget': resolve(__dirname, 'src/widgets/Counter/index.jsx'),
        'following-eyes-widget': resolve(__dirname, 'src/widgets/FollowingEyes/index.jsx'),
        'gender-guess': resolve(__dirname, 'src/widgets/GenderGuess/index.jsx'),
        'gender-table': resolve(__dirname, 'src/widgets/GenderTable/index.jsx'),
        'bias-breakdown': resolve(__dirname, 'src/widgets/BiasBreakdown/index.jsx'),
        'stem-pool': resolve(__dirname, 'src/widgets/STEMPool/index.jsx'),
        'viewer-creator': resolve(__dirname, 'src/widgets/ViewerCreator/index.jsx'),
        'country-scatter': resolve(__dirname, 'src/widgets/CountryScatter/index.jsx'),
      },
      output: {
        // Predictable names without hashes for easy embedding
        entryFileNames: '[name].js',
        chunkFileNames: 'assets/[name].js',
        assetFileNames: 'assets/[name].[ext]'
      }
    },
    // Copy JSON data files to dist
    copyPublicDir: true
  },
  publicDir: 'public',
  server: {
    port: 3000,
    open: '/preview.html'
  },
  // Serve public files at root in dev mode to match production paths
  preview: {
    port: 4173
  }
})
