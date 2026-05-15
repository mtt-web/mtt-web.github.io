import React from 'react';
import { createRoot } from 'react-dom/client';
import PopulationChart from './PopulationChart.jsx';

// Auto-mount to any element with id="population-chart-root"
const rootElement = document.getElementById('population-chart-root');
if (rootElement) {
  console.debug('[PopulationChart] Mounting into #population-chart-root');
  const root = createRoot(rootElement);
  root.render(<PopulationChart />);
} else {
  console.warn('[PopulationChart] #population-chart-root not found on page');
}

// Also export for programmatic use
export default PopulationChart;
