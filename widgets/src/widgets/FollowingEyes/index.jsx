import React from 'react';
import { createRoot } from 'react-dom/client';
import FollowingEyes from './FollowingEyes.jsx';

// Auto-mount to any element with id="following-eyes-widget-root"
const rootElement = document.getElementById('following-eyes-widget-root');
if (rootElement) {
  const root = createRoot(rootElement);
  root.render(<FollowingEyes />);
}

// Also export for programmatic use
export default FollowingEyes;
