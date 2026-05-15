import React, { useEffect, useState } from 'react';
import * as d3 from 'd3';
import Plot from '../Plot/Plot.jsx';

export default function FlagScatter({ source }) {
  const [data, setData] = useState(null);
  const [error, setError] = useState(null);

  useEffect(() => {
    const uniquesPath = '/widgets/dist/country_uniques.json';
    const tableCsvPath = '/widgets/dist/Geography 2021-08-22_2025-10-17 Polylog/Table data.csv';
    const populationPath = '/widgets/dist/country_population.json';
    const flagsPath = '/widgets/dist/country_flags.json';
    const namesPath = '/widgets/dist/country_name_map.json';

    const fetchJson = (url) => fetch(url, { cache: 'no-store' }).then(r => r.json());

    const promises = [
      fetchJson(populationPath),
      fetchJson(flagsPath),
      fetchJson(namesPath),
      source === 'site' ? fetchJson(uniquesPath) : d3.csv(tableCsvPath, d3.autoType)
    ];

    Promise.all(promises).then(([populationMap, flagsMap, names, fourth]) => {
      if (source === 'site') {
        const uniquesMap = fourth;
        const points = Object.entries(uniquesMap || {})
          .map(([country, uniques]) => ({
            country,
            y: Number(uniques),
            x: Number(populationMap[country])
          }))
          .filter(d => Number.isFinite(d.x) && Number.isFinite(d.y) && d.x > 0 && d.y > 0);
        setData({ points, flagsMap, names, yLabel: 'Site uniques', title: 'Site uniques vs Population (log–log)', color: '#0ea5e9' });
      } else {
        const tableRows = fourth;
        const points = (tableRows || [])
          .filter(r => r.Geography && r.Geography !== 'Total')
          .map(r => ({
            country: r.Geography,
            y: Number(r.Views),
            x: Number(populationMap[r.Geography])
          }))
          .filter(d => Number.isFinite(d.x) && Number.isFinite(d.y) && d.x > 0 && d.y > 0);
        setData({ points, flagsMap, names, yLabel: 'YouTube views', title: 'YouTube views vs Population (log–log)', color: '#10b981' });
      }
    }).catch(err => {
      console.error('[FlagScatter] Failed to load data', err);
      setError('Failed to load data');
    });
  }, [source]);

  if (error) return <div style={{ padding: '1rem', color: '#666' }}>{error}</div>;
  if (!data) return <div style={{ padding: '1rem', color: '#666' }}>Loading…</div>;

  const { points, flagsMap, names, yLabel, title, color } = data;

  return (
    <Plot
      data={points}
      x={d => d.x}
      y={d => d.y}
      xScale="log"
      yScale="log"
      xLabel="Population"
      yLabel={yLabel}
      title={title}
      color={color}
      radius={4}
      useFlags={true}
      flag={d => flagForName(flagsMap, names, d.country)}
      tooltip={d => `${d.country}<br>Population: ${d.x.toLocaleString()}<br>${yLabel}: ${d.y.toLocaleString()}`}
      height={480}
    />
  );
}

function flagForName(flagsMap, names, countryName) {
  if (!flagsMap || !names) return '';
  const { iso2ToName, aliasesToName } = names || {};
  for (const [code, name] of Object.entries(iso2ToName || {})) {
    if (name === countryName) return flagsMap[code] || '';
  }
  const canonical = (aliasesToName && aliasesToName[countryName]) || countryName;
  for (const [code, name] of Object.entries(iso2ToName || {})) {
    if (name === canonical) return flagsMap[code] || '';
  }
  return '';
}


