import React from 'react';
import { createRoot } from 'react-dom/client';
import Counter from './Counter.jsx';

// Auto-mount to any element with id="counter-widget-root"
const rootElement = document.getElementById('counter-widget-root');
if (rootElement) {
  // Read config from data attributes
  const initialValue = parseInt(rootElement.dataset.initial) || 0;
  const step = parseInt(rootElement.dataset.step) || 1;

  const root = createRoot(rootElement);
  root.render(<Counter initialValue={initialValue} step={step} />);
}

// Also export for programmatic use
export default Counter;
