import React from 'react';
import { createRoot } from 'react-dom/client';
import BiasBreakdown from './BiasBreakdown.jsx';

// Auto-mount to any element with id starting with "bias-breakdown-root"
document.querySelectorAll('[id^="bias-breakdown-root"]').forEach((rootElement) => {
  const segmentsData = rootElement.dataset.segments;
  const total = parseFloat(rootElement.dataset.total) || 5.3;
  let segments = [];

  if (segmentsData) {
    try {
      segments = JSON.parse(segmentsData);
    } catch (e) {
      console.error('Failed to parse segments data:', e);
    }
  }

  const root = createRoot(rootElement);
  root.render(<BiasBreakdown segments={segments} total={total} />);
});

export default BiasBreakdown;
