import React, { useEffect, useState } from 'react';
import * as d3 from 'd3';
import Plot from '../Plot/Plot.jsx';

export default function CountryScatter({ dataType }) {
  const [data, setData] = useState(null);
  const [error, setError] = useState(null);
  const [baseline, setBaseline] = useState(null);

  useEffect(() => {
    const csvPath = '/widgets/dist/country_analytics.csv';

    d3.csv(csvPath, d3.autoType)
      .then(rows => {
        // Filter out rows without the required data
        const filtered = rows.filter(row => {
          if (dataType === 'correlation') {
            // For correlation plot, need both YT and site visits (with >10 site visits)
            return row['YouTube Views'] && Number.isFinite(row['YouTube Views']) && row['YouTube Views'] > 0 &&
                   row['Site Visits'] && Number.isFinite(row['Site Visits']) && row['Site Visits'] > 10;
          } else if (dataType === 'wiki-youtube') {
            // For wikipedia-youtube correlation, need both
            return row['YouTube Views'] && Number.isFinite(row['YouTube Views']) && row['YouTube Views'] > 0 &&
                   row['Wikipedia Views'] && Number.isFinite(row['Wikipedia Views']) && row['Wikipedia Views'] > 0;
          } else if (dataType === 'per-capita') {
            // For per capita correlation, need population, wiki views, and YT views
            return row.Population && Number.isFinite(row.Population) && row.Population > 0 &&
                   row['YouTube Views'] && Number.isFinite(row['YouTube Views']) && row['YouTube Views'] > 0 &&
                   row['Wikipedia Views'] && Number.isFinite(row['Wikipedia Views']) && row['Wikipedia Views'] > 0;
          } else if (dataType === 'gdp-youtube') {
            // For GDP-YouTube correlation, need GDP, YT views, and population
            return row['GDP (PPP) per capita'] && Number.isFinite(row['GDP (PPP) per capita']) && row['GDP (PPP) per capita'] > 0 &&
                   row['YouTube Views'] && Number.isFinite(row['YouTube Views']) && row['YouTube Views'] > 0 &&
                   row.Population && Number.isFinite(row.Population) && row.Population > 0;
          } else {
            const hasPopulation = row.Population && Number.isFinite(row.Population) && row.Population > 0;
            if (dataType === 'youtube') {
              return hasPopulation && row['YouTube Views'] && Number.isFinite(row['YouTube Views']) && row['YouTube Views'] > 0;
            } else if (dataType === 'site') {
              return hasPopulation && row['Site Visits'] && Number.isFinite(row['Site Visits']) && row['Site Visits'] > 0;
            }
          }
          return false;
        });

        // Calculate baseline ratio for correlation plots
        if (dataType === 'correlation') {
          const totalYT = filtered.reduce((sum, row) => sum + row['YouTube Views'], 0);
          const totalSite = filtered.reduce((sum, row) => sum + row['Site Visits'], 0);
          const ratio = totalYT / totalSite;
          setBaseline({ ratio, totalYT, totalSite });
        } else if (dataType === 'wiki-youtube') {
          const totalYT = filtered.reduce((sum, row) => sum + row['YouTube Views'], 0);
          const totalWiki = filtered.reduce((sum, row) => sum + row['Wikipedia Views'], 0);
          const ratio = totalYT / totalWiki;
          setBaseline({ ratio, totalYT, totalWiki });
        } else if (dataType === 'per-capita') {
          const totalPop = filtered.reduce((sum, row) => sum + row.Population, 0);
          const totalYT = filtered.reduce((sum, row) => sum + row['YouTube Views'], 0);
          const totalWiki = filtered.reduce((sum, row) => sum + row['Wikipedia Views'], 0);
          const ytPerCapita = totalYT / totalPop;
          const wikiPerCapita = totalWiki / totalPop;
          const ratio = ytPerCapita / wikiPerCapita;
          setBaseline({ ratio, ytPerCapita, wikiPerCapita });
        }

        setData(filtered);
      })
      .catch(err => {
        console.error('[CountryScatter] Failed to load data', err);
        setError('Failed to load data');
      });
  }, [dataType]);

  if (error) return <div style={{ padding: '1rem', color: '#666' }}>{error}</div>;
  if (!data) return <div style={{ padding: '1rem', color: '#666' }}>Loading…</div>;

  const isCorrelation = dataType === 'correlation';
  const isWikiYouTube = dataType === 'wiki-youtube';
  const isPerCapita = dataType === 'per-capita';
  const isGdpYouTube = dataType === 'gdp-youtube';
  const isYouTube = dataType === 'youtube';

  let xLabel, yLabel, title, color, xAccessor, yAccessor;

  if (isCorrelation) {
    xLabel = 'Site Visits';
    yLabel = 'YouTube Views';
    title = 'YouTube Views vs Site Visits (log–log)';
    color = '#a855f7'; // purple
    xAccessor = d => d['Site Visits'];
    yAccessor = d => d['YouTube Views'];
  } else if (isWikiYouTube) {
    xLabel = 'Wikipedia Views';
    yLabel = 'YouTube Views';
    title = 'YouTube Views vs Wikipedia Views (log–log)';
    color = '#f59e0b'; // amber/orange
    xAccessor = d => d['Wikipedia Views'];
    yAccessor = d => d['YouTube Views'];
  } else if (isPerCapita) {
    xLabel = 'Wikipedia Views per Capita';
    yLabel = 'YouTube Views per Capita';
    title = 'Per Capita: YouTube vs Wikipedia (log–log)';
    color = '#ec4899'; // pink
    xAccessor = d => d['Wikipedia Views'] / d.Population;
    yAccessor = d => d['YouTube Views'] / d.Population;
  } else if (isGdpYouTube) {
    xLabel = 'GDP (PPP) per capita';
    yLabel = 'YouTube Views per Capita';
    title = 'YouTube Views per Capita vs GDP per Capita (log–log)';
    color = '#06b6d4'; // cyan
    xAccessor = d => d['GDP (PPP) per capita'];
    yAccessor = d => d['YouTube Views'] / d.Population;
  } else {
    xLabel = 'Population';
    yLabel = isYouTube ? 'YouTube Views' : 'Site Visits';
    title = isYouTube
      ? 'YouTube Views vs Population (log–log)'
      : 'Site Visits vs Population (log–log)';
    color = isYouTube ? '#10b981' : '#0ea5e9';
    xAccessor = d => d.Population;
    yAccessor = isYouTube
      ? d => d['YouTube Views']
      : d => d['Site Visits'];
  }

  // Enhanced tooltip with flag and ratio
  const tooltipContent = d => {
    if (isCorrelation) {
      const siteVisits = d['Site Visits'];
      const ytViews = d['YouTube Views'];
      const ratio = siteVisits > 0 ? ytViews / siteVisits : 0;
      const vsBaseline = baseline ? ((ratio / baseline.ratio - 1) * 100).toFixed(1) : 0;
      const aboveBelow = vsBaseline > 0 ? 'above' : 'below';

      return `
        <div style="font-weight: 600; margin-bottom: 4px;">${d.Flag} ${d.Country}</div>
        <div>Site Visits: ${siteVisits.toLocaleString()}</div>
        <div>YouTube Views: ${ytViews.toLocaleString()}</div>
        <div style="margin-top: 4px; padding-top: 4px; border-top: 1px solid #e5e7eb; font-size: 11px; color: #6b7280;">
          Ratio: ${ratio.toFixed(1)} YT per site visit
        </div>
        <div style="font-size: 11px; color: #6b7280;">
          ${Math.abs(vsBaseline)}% ${aboveBelow} baseline
        </div>
      `;
    } else if (isWikiYouTube) {
      const wikiViews = d['Wikipedia Views'];
      const ytViews = d['YouTube Views'];
      const ratio = wikiViews > 0 ? ytViews / wikiViews : 0;
      const vsBaseline = baseline ? ((ratio / baseline.ratio - 1) * 100).toFixed(1) : 0;
      const aboveBelow = vsBaseline > 0 ? 'above' : 'below';

      return `
        <div style="font-weight: 600; margin-bottom: 4px;">${d.Flag} ${d.Country}</div>
        <div>Wikipedia Views: ${wikiViews.toLocaleString()}</div>
        <div>YouTube Views: ${ytViews.toLocaleString()}</div>
        <div style="margin-top: 4px; padding-top: 4px; border-top: 1px solid #e5e7eb; font-size: 11px; color: #6b7280;">
          Ratio: ${ratio.toFixed(5)} YT per Wiki view
        </div>
        <div style="font-size: 11px; color: #6b7280;">
          ${Math.abs(vsBaseline)}% ${aboveBelow} baseline
        </div>
      `;
    } else if (isPerCapita) {
      const pop = d.Population;
      const wikiViews = d['Wikipedia Views'];
      const ytViews = d['YouTube Views'];
      const wikiPerCapita = wikiViews / pop;
      const ytPerCapita = ytViews / pop;
      const ratio = wikiPerCapita > 0 ? ytPerCapita / wikiPerCapita : 0;
      const vsBaseline = baseline ? ((ratio / baseline.ratio - 1) * 100).toFixed(1) : 0;
      const aboveBelow = vsBaseline > 0 ? 'above' : 'below';

      return `
        <div style="font-weight: 600; margin-bottom: 4px;">${d.Flag} ${d.Country}</div>
        <div>Population: ${pop.toLocaleString()}</div>
        <div>Wiki per capita: ${(wikiPerCapita * 1000000).toFixed(1)} per million</div>
        <div>YouTube per capita: ${(ytPerCapita * 1000000).toFixed(1)} per million</div>
        <div style="margin-top: 4px; padding-top: 4px; border-top: 1px solid #e5e7eb; font-size: 11px; color: #6b7280;">
          Ratio: ${ratio.toFixed(3)} YT/Wiki per capita
        </div>
        <div style="font-size: 11px; color: #6b7280;">
          ${Math.abs(vsBaseline)}% ${aboveBelow} baseline
        </div>
      `;
    } else if (isGdpYouTube) {
      const gdp = d['GDP (PPP) per capita'];
      const ytViews = d['YouTube Views'];
      const pop = d.Population;
      const ytPerCapita = ytViews / pop;
      const ytPerMillion = (ytPerCapita * 1000000).toFixed(1);

      return `
        <div style="font-weight: 600; margin-bottom: 4px;">${d.Flag} ${d.Country}</div>
        <div>GDP per capita: $${gdp.toLocaleString()}</div>
        <div>Population: ${pop.toLocaleString()}</div>
        <div>YouTube Views: ${ytViews.toLocaleString()}</div>
        <div style="margin-top: 4px; padding-top: 4px; border-top: 1px solid #e5e7eb; font-size: 11px; color: #6b7280;">
          ${ytPerMillion} YT views per million
        </div>
      `;
    } else {
      const pop = d.Population;
      const metric = yAccessor(d);
      const perCapita = pop > 0 ? metric / pop : 0;
      const perMillion = (perCapita * 1000000).toFixed(1);

      return `
        <div style="font-weight: 600; margin-bottom: 4px;">${d.Flag} ${d.Country}</div>
        <div>Population: ${pop.toLocaleString()}</div>
        <div>${yLabel}: ${metric.toLocaleString()}</div>
        <div style="margin-top: 4px; padding-top: 4px; border-top: 1px solid #e5e7eb; font-size: 11px; color: #6b7280;">
          ${perMillion} per million
        </div>
      `;
    }
  };

  return (
    <Plot
      data={data}
      x={xAccessor}
      y={yAccessor}
      xScale="log"
      yScale="log"
      xLabel={xLabel}
      yLabel={yLabel}
      title={title}
      color={color}
      radius={4}
      useFlags={true}
      flag={d => d.Flag || '•'}
      tooltip={tooltipContent}
      height={480}
      baseline={(isCorrelation || isWikiYouTube || isPerCapita) && baseline ? baseline.ratio : null}
    />
  );
}
