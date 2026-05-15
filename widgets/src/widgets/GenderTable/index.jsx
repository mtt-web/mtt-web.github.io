import React from 'react';
import { createRoot } from 'react-dom/client';
import GenderTable from './GenderTable.jsx';

// Auto-mount to any element with id="gender-table-root"
const rootElement = document.getElementById('gender-table-root');
if (rootElement) {
  // Read config from data attributes
  const channelsData = rootElement.dataset.channels;
  let channels = [];

  if (channelsData) {
    try {
      channels = JSON.parse(channelsData);
    } catch (e) {
      console.error('Failed to parse channels data:', e);
    }
  }

  const root = createRoot(rootElement);
  root.render(<GenderTable channels={channels} />);
}

// Also export for programmatic use
export default GenderTable;
