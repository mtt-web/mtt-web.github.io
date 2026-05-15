import React, { useEffect, useState } from 'react';
import Plot from '../Plot/Plot.jsx';

/**
 * PopulationChart - Log-log scatter plot comparing population to unique visits
 * This component is designed to be embedded in blog posts
 */
export default function PopulationChart() {
  const [data, setData] = useState([]);
  const [error, setError] = useState(null);

  // Load data on mount
  useEffect(() => {
    const fetchJsonWithDebug = async (url) => {
      try {
        console.debug('[PopulationChart] Fetching', url, 'from', window.location.origin);
        const res = await fetch(url, { cache: 'no-store' });
        console.debug('[PopulationChart] Response', url, res.status, res.statusText, '->', res.url);
        const text = await res.text();
        if (!res.ok) {
          console.error('[PopulationChart] HTTP error', url, res.status, res.statusText, 'Body preview:', text.slice(0, 200));
          throw new Error(`HTTP ${res.status} ${res.statusText}`);
        }
        try {
          return JSON.parse(text);
        } catch (e) {
          console.error('[PopulationChart] JSON parse failed for', url, 'Body preview:', text.slice(0, 200));
          throw e;
        }
      } catch (e) {
        console.error('[PopulationChart] Fetch failed:', url, e);
        throw e;
      }
    };

    Promise.all([
      fetchJsonWithDebug('/widgets/dist/country_uniques.json'),
      fetchJsonWithDebug('/widgets/dist/country_population.json')
    ])
      .then(([uniquesMap, popMap]) => {
        const entries = Object.entries(uniquesMap);
        const rows = entries
          .map(([country, uniques]) => {
            const population = popMap[country];
            return {
              country,
              uniques: Number(uniques),
              population: Number(population)
            };
          })
          .filter(d =>
            Number.isFinite(d.uniques) &&
            Number.isFinite(d.population) &&
            d.uniques > 0 &&
            d.population > 0
          );
        setData(rows);
      })
      .catch(err => {
        console.error('[PopulationChart] Failed to load analytics data', err);
        setError('Failed to load analytics data.');
      });
  }, []);

  if (error) {
    return <div style={{ padding: '1rem', color: '#666' }}>{error}</div>;
  }

  return (
    <Plot
      data={data}
      x={d => d.population}
      y={d => d.uniques}
      xScale="log"
      yScale="log"
      xLabel="Population"
      yLabel="Unique visits"
      title="Population vs Unique Visits (log–log)"
      color="#4a90e2"
      radius={3.5}
      tooltip={d => `${d.country}<br>Population: ${d.population.toLocaleString()}<br>Uniques: ${d.uniques.toLocaleString()}`}
      height={500}
    />
  );
}
