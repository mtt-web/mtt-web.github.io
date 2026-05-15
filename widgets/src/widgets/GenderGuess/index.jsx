import React from 'react';
import { createRoot } from 'react-dom/client';
import GenderGuess from './GenderGuess.jsx';

// Auto-mount to any element with id="gender-guess-root"
const rootElement = document.getElementById('gender-guess-root');
if (rootElement) {
  // Read config from data attributes
  const correctAnswer = parseFloat(rootElement.dataset.answer) || 97.5;

  const root = createRoot(rootElement);
  root.render(<GenderGuess correctAnswer={correctAnswer} />);
}

// Also export for programmatic use
export default GenderGuess;
