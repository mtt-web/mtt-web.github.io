import React, { useEffect, useState } from 'react';
import * as d3 from 'd3';
import Plot from '../Plot/Plot.jsx';

/**
 * GeographyCharts - Multiple log-log scatter plots for geography analytics
 * This component is designed to be embedded in blog posts
 */
export default function GeographyCharts() {
  const [data, setData] = useState(null);
  const [error, setError] = useState(null);

  useEffect(() => {
    // Load uniques (site), views (YouTube), population, and GDP data
    const uniquesPath = '/widgets/dist/country_uniques.json';
    const tableCsvPath = '/widgets/dist/Geography 2021-08-22_2025-10-17 Polylog/Table data.csv';
    const populationPath = '/widgets/dist/country_population.json';
    const gdpPath = '/widgets/dist/country_gdp_ppp.json';
    const flagsPath = '/widgets/dist/country_flags.json';
    const namesPath = '/widgets/dist/country_name_map.json';

    const fetchJsonWithDebug = async (url) => {
      try {
        console.log('[GeographyCharts] Fetching JSON', url, 'from', window.location.origin);
        const res = await fetch(url, { cache: 'no-store' });
        console.log('[GeographyCharts] Response', url, res.status, res.statusText, '->', res.url);
        const text = await res.text();
        if (!res.ok) {
          console.error('[GeographyCharts] HTTP error', url, res.status, res.statusText, 'Body preview:', text.slice(0, 200));
          throw new Error(`HTTP ${res.status} ${res.statusText}`);
        }
        try {
          return JSON.parse(text);
        } catch (e) {
          console.error('[GeographyCharts] JSON parse failed for', url, 'Body preview:', text.slice(0, 200));
          throw e;
        }
      } catch (e) {
        console.error('[GeographyCharts] Fetch failed:', url, e);
        throw e;
      }
    };

    Promise.all([
      fetchJsonWithDebug(uniquesPath),
      d3.csv(tableCsvPath, d3.autoType).then(rows => {
        console.log('[GeographyCharts] Loaded CSV', tableCsvPath, 'rows:', rows.length);
        return rows;
      }),
      fetchJsonWithDebug(populationPath),
      fetchJsonWithDebug(gdpPath),
      fetchJsonWithDebug(flagsPath),
      fetchJsonWithDebug(namesPath)
    ])
      .then(([uniquesMap, tableRows, populationMap, gdpMap, flagsMap, names]) => {
        console.log('[GeographyCharts] Data ready. uniques:', Object.keys(uniquesMap || {}).length, 'pop:', Object.keys(populationMap || {}).length, 'gdp:', Object.keys(gdpMap || {}).length);
        setData({ uniquesMap, tableRows, populationMap, gdpMap, flagsMap, names });
        console.log('[GeographyCharts] setData called.');
      })
      .catch(err => {
        console.error('[GeographyCharts] Failed to load geography data', err);
        setError('Failed to load geography data.');
      });
  }, []);

  // Unconditional hook: log SVG count after data renders
  useEffect(() => {
    if (!data) return;
    try {
      const root = document.getElementById('geography-charts-root');
      const count = root ? root.querySelectorAll('svg').length : 0;
      console.log('[GeographyCharts] SVG elements in root:', count);
    } catch (e) {
      // ignore
    }
  }, [data]);

  if (error) {
    return <div style={{ padding: '1rem', color: '#666' }}>{error}</div>;
  }

  if (!data) {
    console.log('[GeographyCharts] Loading… waiting for data');
    return <div style={{ padding: '1rem', color: '#666' }}>Loading…</div>;
  }

  const { uniquesMap, tableRows, populationMap, gdpMap, flagsMap, names } = data;
  console.log('[GeographyCharts] Rendering with rows:', tableRows?.length, 'uniques countries:', Object.keys(uniquesMap || {}).length, 'pop countries:', Object.keys(populationMap || {}).length, 'gdp countries:', Object.keys(gdpMap || {}).length);
  const rows = tableRows
    .filter(d => d.Geography && d.Geography !== 'Total')
    .map(d => ({
      country: d.Geography,
      views: Number(d.Views),
      avgViewSec: parseAvgDurationToSeconds(d['Average view duration']),
      population: Number(populationMap[d.Geography]),
      gdp: Number(gdpMap[d.Geography])
    }))
    .filter(d => Number.isFinite(d.views) && Number.isFinite(d.population) && d.views > 0 && d.population > 0);

  const uniquesRows = Object.entries(uniquesMap || {})
    .map(([country, uniques]) => ({
      country,
      uniques: Number(uniques),
      population: Number(populationMap[country])
    }))
    .filter(d => Number.isFinite(d.uniques) && Number.isFinite(d.population) && d.uniques > 0 && d.population > 0);

  const gdpViewsRows = rows
    .filter(d => Number.isFinite(d.gdp) && d.gdp > 0)
    .map(d => ({
      country: d.country,
      gdp: d.gdp,
      views: d.views
    }));

  const rates = rows
    .map(d => ({
      country: d.country,
      rate: d.views / d.population,
      population: d.population,
      views: d.views
    }))
    .filter(d => d.rate > 0);

  return (
    <div style={{ width: '100%' }}>
      <h3 style={{ margin: '0 0 0.5rem', color: '#111827' }}>Site uniques vs Population</h3>
      <div style={{ marginBottom: '2rem' }}>
        <Plot
          data={uniquesRows}
          x={d => d.population}
          y={d => d.uniques}
          xScale="log"
          yScale="log"
          xLabel="Population"
          yLabel="Site uniques"
          title="Site uniques vs Population (log–log)"
          color="#0ea5e9"
          radius={4}
          useFlags={true}
          flag={d => flagForName(flagsMap, names, d.country)}
          tooltip={d => `${d.country}<br>Population: ${d.population.toLocaleString()}<br>Uniques: ${d.uniques.toLocaleString()}`}
          height={480}
        />
      </div>
      <h3 style={{ margin: '0 0 0.5rem', color: '#111827' }}>YouTube views vs Population</h3>
      <div style={{ marginBottom: '2rem' }}>
        <Plot
          data={rows}
          x={d => d.population}
          y={d => d.views}
          xScale="log"
          yScale="log"
          xLabel="Population"
          yLabel="YouTube views"
          title="YouTube views vs Population (log–log)"
          color="#10b981"
          radius={4}
          useFlags={true}
          flag={d => flagForName(flagsMap, names, d.country)}
          tooltip={d => `${d.country}<br>Population: ${d.population.toLocaleString()}<br>Views: ${d.views.toLocaleString()}`}
          height={480}
        />
      </div>
      <h3 style={{ margin: '0 0 0.5rem', color: '#111827' }}>YouTube views vs GDP (PPP) per capita</h3>
      <div>
        <Plot
          data={gdpViewsRows}
          x={d => d.gdp}
          y={d => d.views}
          xScale="log"
          yScale="log"
          xLabel="GDP (PPP) per capita (Int$)"
          yLabel="YouTube views"
          title="YouTube views vs GDP (PPP) per capita (log–log)"
          color="#8b5cf6"
          radius={4}
          useFlags={true}
          flag={d => flagForName(flagsMap, names, d.country)}
          tooltip={d => `${d.country}<br>GDP: $${d.gdp.toLocaleString()}<br>Views: ${d.views.toLocaleString()}`}
          height={480}
        />
      </div>
    </div>
  );
}

function flagForName(flagsMap, names, countryName) {
  if (!flagsMap || !names) return '';
  const { iso2ToName, aliasesToName } = names || {};
  // Direct name match
  for (const [code, name] of Object.entries(iso2ToName || {})) {
    if (name === countryName) return flagsMap[code] || '';
  }
  // Alias remap
  const canonical = (aliasesToName && aliasesToName[countryName]) || countryName;
  for (const [code, name] of Object.entries(iso2ToName || {})) {
    if (name === canonical) return flagsMap[code] || '';
  }
  return '';
}

function parseAvgDurationToSeconds(hhmmss) {
  if (!hhmmss) return NaN;
  const parts = String(hhmmss).split(':').map(Number);
  if (parts.length === 3) {
    const [h, m, s] = parts;
    return (h || 0) * 3600 + (m || 0) * 60 + (s || 0);
  }
  if (parts.length === 2) {
    const [m, s] = parts;
    return (m || 0) * 60 + (s || 0);
  }
  return Number(hhmmss);
}

function formatSeconds(total) {
  if (!Number.isFinite(total)) return '';
  const h = Math.floor(total / 3600);
  const m = Math.floor((total % 3600) / 60);
  const s = Math.floor(total % 60);
  if (h > 0) return `${h}h ${m}m ${s}s`;
  if (m > 0) return `${m}m ${s}s`;
  return `${s}s`;
}

