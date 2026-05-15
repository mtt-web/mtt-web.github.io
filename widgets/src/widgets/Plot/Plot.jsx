import React, { useEffect, useMemo, useRef, useState } from 'react';
import * as d3 from 'd3';

/**
 * Plot - Reusable responsive scatter plot with linear/log scales and tooltips.
 * Props:
 *  - data: Array<any>
 *  - x: (d) => number
 *  - y: (d) => number
 *  - xLabel?: string
 *  - yLabel?: string
 *  - title?: string
 *  - color?: string | ((d) => string)
 *  - radius?: number | ((d) => number)
 *  - xScale?: 'linear' | 'log'
 *  - yScale?: 'linear' | 'log'
 *  - tooltip?: (d) => string (HTML allowed)
 *  - margins?: { top: number, right: number, bottom: number, left: number }
 *  - height?: number (default 500)
 */
export default function Plot({
  data,
  x,
  y,
  xLabel,
  yLabel,
  title,
  color = '#3b82f6',
  radius = 4,
  xScale = 'linear',
  yScale = 'linear',
  tooltip,
  margins = { top: 32, right: 24, bottom: 48, left: 64 },
  height = 500,
  useFlags = false,
  flag, // (d) => string (emoji or short label) used when useFlags=true
  baseline = null, // if provided, draws a line y = baseline * x (for log-log correlation plots)
}) {
  const containerRef = useRef(null);
  const svgRef = useRef(null);
  const tooltipRef = useRef(null);
  const zoomRef = useRef(null);
  const [width, setWidth] = useState(800);

  // Responsive width via ResizeObserver
  useEffect(() => {
    if (!containerRef.current) return;
    const ro = new ResizeObserver(entries => {
      for (const entry of entries) {
        const nextWidth = entry.contentRect.width;
        if (Number.isFinite(nextWidth)) setWidth(nextWidth);
      }
    });
    ro.observe(containerRef.current);
    return () => ro.disconnect();
  }, []);

  const processed = useMemo(() => {
    return (data || []).filter(d => {
      const xv = x(d);
      const yv = y(d);
      const xvOk = Number.isFinite(xv) && (xScale === 'log' ? xv > 0 : true);
      const yvOk = Number.isFinite(yv) && (yScale === 'log' ? yv > 0 : true);
      return xvOk && yvOk;
    });
  }, [data, x, y, xScale, yScale]);

  useEffect(() => {
    const svg = d3.select(svgRef.current);
    svg.selectAll('*').remove();
    if (!processed.length) return;

    const innerWidth = Math.max(0, width - margins.left - margins.right);
    const innerHeight = Math.max(0, height - margins.top - margins.bottom);

    const mainGroup = svg
      .attr('width', width)
      .attr('height', height)
      .append('g')
      .attr('transform', `translate(${margins.left},${margins.top})`);

    // Add clip path to prevent points from going outside the plot area
    svg.append('defs')
      .append('clipPath')
      .attr('id', `clip-${Math.random().toString(36).substr(2, 9)}`)
      .append('rect')
      .attr('width', innerWidth)
      .attr('height', innerHeight);

    // Add zoom rect first (will be in the background)
    const zoomRect = mainGroup.append('rect')
      .attr('width', innerWidth)
      .attr('height', innerHeight)
      .style('fill', 'none')
      .style('pointer-events', 'all')
      .style('cursor', 'move');

    const g = mainGroup.append('g');

    const xVals = processed.map(d => x(d));
    const yVals = processed.map(d => y(d));

    const xScaleObj = (xScale === 'log' ? d3.scaleLog() : d3.scaleLinear())
      .domain(d3.extent(xVals))
      .nice()
      .range([0, innerWidth]);

    const yScaleObj = (yScale === 'log' ? d3.scaleLog() : d3.scaleLinear())
      .domain(d3.extent(yVals))
      .nice()
      .range([innerHeight, 0]);

    const xAxis = d3.axisBottom(xScaleObj).ticks(8, xScale === 'log' ? '~s' : undefined);
    const yAxis = d3.axisLeft(yScaleObj).ticks(8, yScale === 'log' ? '~s' : undefined);

    const xAxisGroup = g.append('g')
      .attr('class', 'x-axis')
      .attr('transform', `translate(0,${innerHeight})`)
      .call(xAxis);

    const yAxisGroup = g.append('g')
      .attr('class', 'y-axis')
      .call(yAxis);

    if (xLabel) {
      g.append('text')
        .attr('x', innerWidth)
        .attr('y', innerHeight + 36)
        .attr('text-anchor', 'end')
        .attr('fill', '#374151')
        .attr('font-size', 12)
        .text(xLabel);
    }

    if (yLabel) {
      g.append('text')
        .attr('transform', 'rotate(-90)')
        .attr('x', -12)
        .attr('y', -48)
        .attr('text-anchor', 'end')
        .attr('fill', '#374151')
        .attr('font-size', 12)
        .text(yLabel);
    }

    if (title) {
      g.append('text')
        .attr('x', 0)
        .attr('y', -8)
        .attr('text-anchor', 'start')
        .attr('fill', '#111827')
        .attr('font-size', 14)
        .attr('font-weight', 600)
        .text(title);
    }

    // Draw baseline if provided (y = baseline * x)
    if (baseline !== null && xScale === 'log' && yScale === 'log') {
      const [xmin, xmax] = xScaleObj.domain();
      const y1 = baseline * xmin;
      const y2 = baseline * xmax;

      g.append('line')
        .attr('class', 'baseline-line')
        .attr('x1', xScaleObj(xmin))
        .attr('y1', yScaleObj(y1))
        .attr('x2', xScaleObj(xmax))
        .attr('y2', yScaleObj(y2))
        .attr('stroke', '#ef4444')
        .attr('stroke-width', 2)
        .attr('stroke-dasharray', '6,3')
        .style('opacity', 0.7);

      // Add baseline label
      g.append('text')
        .attr('class', 'baseline-text')
        .attr('x', xScaleObj(xmax) - 10)
        .attr('y', yScaleObj(y2) - 10)
        .attr('text-anchor', 'end')
        .attr('fill', '#ef4444')
        .attr('font-size', 11)
        .attr('font-weight', 500)
        .text('Baseline');
    }

    const colorFn = typeof color === 'function' ? color : () => color;
    const rFn = typeof radius === 'function' ? radius : () => radius;

    const points = g.append('g').attr('fill-opacity', 0.95);

    // Hover guideline for constant y/x on log-log (slope 1): y = kx
    const guideline = g.append('line')
      .attr('stroke', '#9ca3af')
      .attr('stroke-dasharray', '4,4')
      .attr('stroke-width', 1)
      .style('opacity', 0);

    // Track current scales (will be updated during zoom)
    let currentXScale = xScaleObj;
    let currentYScale = yScaleObj;

    const showGuideline = (d) => {
      if (!(xScale === 'log' && yScale === 'log')) return; // only defined on log-log
      const xv = x(d);
      const yv = y(d);
      if (!Number.isFinite(xv) || !Number.isFinite(yv) || xv <= 0 || yv <= 0) return;
      const k = yv / xv;
      const [xmin, xmax] = currentXScale.domain();
      const y1 = k * xmin;
      const y2 = k * xmax;
      guideline
        .attr('x1', currentXScale(xmin))
        .attr('y1', currentYScale(y1))
        .attr('x2', currentXScale(xmax))
        .attr('y2', currentYScale(y2))
        .style('opacity', 1);
    };

    const hideGuideline = () => {
      guideline.style('opacity', 0);
    };

    if (useFlags && typeof flag === 'function') {
      points
        .selectAll('text')
        .data(processed)
        .enter()
        .append('text')
        .attr('x', d => xScaleObj(x(d)))
        .attr('y', d => yScaleObj(y(d)))
        .attr('text-anchor', 'middle')
        .attr('dominant-baseline', 'central')
        .attr('font-size', d => Math.max(15, rFn(d) * 4.5))
        .text(d => flag(d) || '•')
        .on('mouseenter', function (event, d) {
          if (tooltipRef.current && tooltip) {
            const html = tooltip(d);
            const tt = tooltipRef.current;
            tt.innerHTML = html;
            tt.style.opacity = '1';
            tt.style.visibility = 'visible';
          }
          showGuideline(d);
        })
        .on('mousemove', function (event) {
          if (!tooltipRef.current) return;
          const tt = tooltipRef.current;
          const pad = 10;
          tt.style.left = `${event.offsetX + margins.left + pad}px`;
          tt.style.top = `${event.offsetY + margins.top + pad}px`;
        })
        .on('mouseleave', function () {
          if (tooltipRef.current) {
            const tt = tooltipRef.current;
            tt.style.opacity = '0';
            tt.style.visibility = 'hidden';
          }
          hideGuideline();
        });
    } else {
      points
        .selectAll('circle')
        .data(processed)
        .enter()
        .append('circle')
        .attr('cx', d => xScaleObj(x(d)))
        .attr('cy', d => yScaleObj(y(d)))
        .attr('r', d => rFn(d))
        .attr('fill', d => colorFn(d))
        .attr('stroke', 'white')
        .attr('stroke-width', 0.8)
        .on('mouseenter', function (event, d) {
          if (tooltipRef.current && tooltip) {
            const html = tooltip(d);
            const tt = tooltipRef.current;
            tt.innerHTML = html;
            tt.style.opacity = '1';
            tt.style.visibility = 'visible';
          }
          showGuideline(d);
        })
        .on('mousemove', function (event) {
          if (!tooltipRef.current) return;
          const tt = tooltipRef.current;
          const pad = 10;
          tt.style.left = `${event.offsetX + margins.left + pad}px`;
          tt.style.top = `${event.offsetY + margins.top + pad}px`;
        })
        .on('mouseleave', function () {
          if (tooltipRef.current) {
            const tt = tooltipRef.current;
            tt.style.opacity = '0';
            tt.style.visibility = 'hidden';
          }
          hideGuideline();
        });
    }

    // Add zoom and pan behavior
    const zoom = d3.zoom()
      .scaleExtent([1, 20]) // Allow zooming from 1x to 20x
      .on('zoom', (event) => {
        const transform = event.transform;

        // Create new scales based on the zoom transform
        const newXScale = transform.rescaleX(xScaleObj);
        const newYScale = transform.rescaleY(yScaleObj);

        // Update current scales for guideline
        currentXScale = newXScale;
        currentYScale = newYScale;

        // Update axes
        g.select('.x-axis').call(xAxis.scale(newXScale));
        g.select('.y-axis').call(yAxis.scale(newYScale));

        // Update points positions (flags/circles stay same size)
        if (useFlags && typeof flag === 'function') {
          points.selectAll('text')
            .attr('x', d => newXScale(x(d)))
            .attr('y', d => newYScale(y(d)));
        } else {
          points.selectAll('circle')
            .attr('cx', d => newXScale(x(d)))
            .attr('cy', d => newYScale(y(d)));
        }

        // Update baseline if present
        if (baseline !== null && xScale === 'log' && yScale === 'log') {
          const [xmin, xmax] = newXScale.domain();
          const y1 = baseline * xmin;
          const y2 = baseline * xmax;

          g.select('.baseline-line')
            .attr('x1', newXScale(xmin))
            .attr('y1', newYScale(y1))
            .attr('x2', newXScale(xmax))
            .attr('y2', newYScale(y2));

          g.select('.baseline-text')
            .attr('x', newXScale(xmax) - 10)
            .attr('y', newYScale(y2) - 10);
        }
      });

    // Apply zoom behavior to the zoom rect
    zoomRect.call(zoom);

    // Store zoom behavior in ref for programmatic control
    zoomRef.current = zoom;
  }, [processed, width, height, margins, x, y, xScale, yScale, color, radius, xLabel, yLabel, title, baseline, useFlags, flag, tooltip]);

  return (
    <div
      ref={containerRef}
      style={{
        width: '100%',
        height: `${height}px`,
        position: 'relative',
      }}
    >
      <svg ref={svgRef} style={{ width: '100%', height: '100%', display: 'block' }} />
      <div
        ref={tooltipRef}
        style={{
          position: 'absolute',
          pointerEvents: 'none',
          background: 'white',
          border: '1px solid #e5e7eb',
          borderRadius: 6,
          padding: '6px 8px',
          fontSize: 12,
          color: '#111827',
          boxShadow: '0 4px 6px -1px rgb(0 0 0 / 0.1), 0 2px 4px -2px rgb(0 0 0 / 0.1)',
          opacity: 0,
          visibility: 'hidden',
          transition: 'opacity 0.15s ease, visibility 0.15s ease',
          zIndex: 10,
        }}
      />
    </div>
  );
}


