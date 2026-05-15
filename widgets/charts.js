// Simple reusable chart helper for log-log scatter plots using D3.
// Exposes window.AnalyticsCharts.renderLogLogScatter(opts) and returns a redraw() function.

window.AnalyticsCharts = (function () {
  function renderLogLogScatter(opts) {
    const {
      containerId, // string id of a wrapper div
      svgId,       // string id of the svg inside container
      data,        // array of objects
      xAccessor,   // fn(d) => number (x value)
      yAccessor,   // fn(d) => number (y value)
      xLabel = 'X',
      yLabel = 'Y',
      pointTextAccessor, // fn(d) => string (e.g., flag emoji)
      formatTooltip,     // fn(d) => html string
      hoverRateLine = false, // if true, draw constant-rate line through nearest point
      baseDiagonal = false,  // if true, draw y = x reference line
      includeOneInDomain = false, // if true, force 1 into both axes domains
      margins = { top: 24, right: 22, bottom: 48, left: 66 },
    } = opts;

    const container = document.getElementById(containerId);
    const svg = d3.select('#' + svgId);

    // Ensure a local tooltip per container
    let tooltip = container.querySelector('.tooltip');
    if (!tooltip) {
      tooltip = document.createElement('div');
      tooltip.className = 'tooltip';
      tooltip.setAttribute('aria-hidden', 'true');
      container.appendChild(tooltip);
    }
    const tipSel = d3.select(tooltip);

    function draw() {
      const width = container.clientWidth;
      const height = container.clientHeight;
      svg.attr('viewBox', `0 0 ${width} ${height}`);
      svg.selectAll('*').remove();

      const innerW = Math.max(100, width - margins.left - margins.right);
      const innerH = Math.max(100, height - margins.top - margins.bottom);

      const g = svg.append('g').attr('transform', `translate(${margins.left},${margins.top})`);

      let xDomain = d3.extent(data, xAccessor);
      let yDomain = d3.extent(data, yAccessor);
      if (includeOneInDomain) {
        xDomain = [Math.min(xDomain[0], 1), Math.max(xDomain[1], 1)];
        yDomain = [Math.min(yDomain[0], 1), Math.max(yDomain[1], 1)];
      }
      const x = d3.scaleLog().domain(xDomain).nice().range([0, innerW]);
      const y = d3.scaleLog().domain(yDomain).nice().range([innerH, 0]);

      const xAxis = d3.axisBottom(x).ticks(8, d3.format('.2s'));
      const yAxis = d3.axisLeft(y).ticks(8, d3.format('.2s'));

      g.append('g').attr('class', 'grid').attr('transform', `translate(0,${innerH})`).call(d3.axisBottom(x).tickSize(-innerH).tickFormat(''));
      g.append('g').attr('class', 'grid').call(d3.axisLeft(y).tickSize(-innerW).tickFormat(''));

      g.append('g').attr('class', 'axis').attr('transform', `translate(0,${innerH})`).call(xAxis)
        .call(g => g.append('text').attr('x', innerW).attr('y', 36).attr('fill', 'currentColor').attr('text-anchor', 'end').attr('font-size', 12).text(xLabel));

      g.append('g').attr('class', 'axis').call(yAxis)
        .call(g => g.append('text').attr('x', 0).attr('y', -10).attr('fill', 'currentColor').attr('text-anchor', 'start').attr('font-size', 12).text(yLabel));

      if (baseDiagonal) {
        const v0 = Math.max(x.domain()[0], y.domain()[0]);
        const v1 = Math.min(x.domain()[1], y.domain()[1]);
        if (v1 > v0) {
          g.append('line')
            .attr('x1', x(v0)).attr('y1', y(v0))
            .attr('x2', x(v1)).attr('y2', y(v1))
            .attr('stroke', 'currentColor')
            .attr('stroke-opacity', 0.35)
            .attr('stroke-dasharray', '6 4');
        }
      }

      const fontSize = Math.max(10, Math.min(18, Math.sqrt(innerW * innerH) / 120));
      g.append('g').selectAll('text').data(data).join('text')
        .attr('x', d => x(xAccessor(d)))
        .attr('y', d => y(yAccessor(d)))
        .attr('text-anchor', 'middle')
        .attr('dominant-baseline', 'central')
        .attr('font-size', fontSize)
        .text(d => pointTextAccessor ? pointTextAccessor(d) : '•')
        .on('mouseenter', function (event, d) {
          const [px, py] = d3.pointer(event, container);
          tipSel.style('left', px + 'px').style('top', py + 'px').style('opacity', 1).attr('aria-hidden', 'false')
            .html(formatTooltip ? formatTooltip(d) : '');
        })
        .on('mousemove', function (event) {
          const [px, py] = d3.pointer(event, container);
          tipSel.style('left', px + 'px').style('top', py + 'px');
        })
        .on('mouseleave', function () { tipSel.style('opacity', 0).attr('aria-hidden', 'true'); });

      if (hoverRateLine) {
        const ref = g.append('g');
        const refLine = ref.append('line').attr('stroke', 'currentColor').attr('stroke-opacity', 0.25).attr('stroke-dasharray', '4 3').style('display', 'none');
        g.append('rect').attr('fill', 'transparent').attr('pointer-events', 'all').attr('width', innerW).attr('height', innerH)
          .on('mousemove', function (event) {
            const [mx, my] = d3.pointer(event, this);
            const xv = x.invert(mx);
            const yv = y.invert(my);
            let best = null, bestDist = Infinity;
            for (const d of data) {
              const dx = Math.log10(xAccessor(d)) - Math.log10(xv);
              const dy = Math.log10(yAccessor(d)) - Math.log10(yv);
              const dist = dx * dx + dy * dy;
              if (dist < bestDist) { bestDist = dist; best = d; }
            }
            if (!best) return;
            const rate = yAccessor(best) / xAccessor(best);
            const [x0, x1] = x.domain();
            refLine.attr('x1', x(x0)).attr('y1', y(rate * x0)).attr('x2', x(x1)).attr('y2', y(rate * x1)).style('display', '');
          })
          .on('mouseleave', function () { refLine.style('display', 'none'); });
      }

      return { x, y };
    }

    function redraw() { draw(); }
    draw();
    return redraw;
  }

  return { renderLogLogScatter };
})();


