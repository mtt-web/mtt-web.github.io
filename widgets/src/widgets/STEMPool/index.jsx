import React from 'react';
import { createRoot } from 'react-dom/client';
import STEMPool from './STEMPool.jsx';

// Auto-mount to any element with id="stem-pool-root"
const rootElement = document.getElementById('stem-pool-root');
if (rootElement) {
  const root = createRoot(rootElement);
  root.render(<STEMPool />);
}

export default STEMPool;
