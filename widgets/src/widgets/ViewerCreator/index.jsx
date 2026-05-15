import React from 'react';
import { createRoot } from 'react-dom/client';
import ViewerCreator from './ViewerCreator.jsx';

// Auto-mount to any element with id="viewer-creator-root"
const rootElement = document.getElementById('viewer-creator-root');
if (rootElement) {
  const root = createRoot(rootElement);
  root.render(<ViewerCreator />);
}

export default ViewerCreator;
