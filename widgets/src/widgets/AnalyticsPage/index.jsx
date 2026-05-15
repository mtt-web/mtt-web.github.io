import React from 'react';
import { createRoot } from 'react-dom/client';
import FlagScatter from './FlagScatter.jsx';

// Hydrate custom tags <SiteDataPlot/> and <YouTubeDataPlot/>
function mountCustom(tagName, props) {
  const nodes = document.getElementsByTagName(tagName);
  Array.from(nodes).forEach(node => {
    const container = document.createElement('div');
    node.replaceWith(container);
    const root = createRoot(container);
    root.render(<FlagScatter {...props} />);
  });
}

mountCustom('sitedataplot', { source: 'site' });
mountCustom('youtubedataplot', { source: 'youtube' });

export default FlagScatter;


