import React from 'react';
import { createRoot } from 'react-dom/client';
import CountryScatter from './CountryScatter.jsx';

// Mount YouTube plot
const youtubeRoot = document.getElementById('country-scatter-youtube');
if (youtubeRoot) {
  const root = createRoot(youtubeRoot);
  root.render(<CountryScatter dataType="youtube" />);
}

// Mount Site visits plot
const siteRoot = document.getElementById('country-scatter-site');
if (siteRoot) {
  const root = createRoot(siteRoot);
  root.render(<CountryScatter dataType="site" />);
}

// Mount Correlation plot
const correlationRoot = document.getElementById('country-scatter-correlation');
if (correlationRoot) {
  const root = createRoot(correlationRoot);
  root.render(<CountryScatter dataType="correlation" />);
}

// Mount Wiki-YouTube correlation plot
const wikiYoutubeRoot = document.getElementById('country-scatter-wiki-youtube');
if (wikiYoutubeRoot) {
  const root = createRoot(wikiYoutubeRoot);
  root.render(<CountryScatter dataType="wiki-youtube" />);
}

// Mount Per Capita correlation plot
const perCapitaRoot = document.getElementById('country-scatter-per-capita');
if (perCapitaRoot) {
  const root = createRoot(perCapitaRoot);
  root.render(<CountryScatter dataType="per-capita" />);
}

// Mount GDP-YouTube correlation plot
const gdpYoutubeRoot = document.getElementById('country-scatter-gdp-youtube');
if (gdpYoutubeRoot) {
  const root = createRoot(gdpYoutubeRoot);
  root.render(<CountryScatter dataType="gdp-youtube" />);
}

export default CountryScatter;
