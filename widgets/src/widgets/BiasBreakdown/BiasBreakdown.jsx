import React from 'react';

/**
 * BiasBreakdown - Shows a horizontal stacked bar chart breaking down gender bias factors
 */
export default function BiasBreakdown({ segments = [], total = 5.3 }) {
  const colors = [
    '#4a90e2', // blue
    '#e91e63', // pink
    '#4caf50', // green
    '#ff9800', // orange
    '#9c27b0', // purple
  ];

  const totalValue = segments.reduce((sum, seg) => sum + seg.value, 0);
  const barWidth = 600;
  const barHeight = 60;

  return (
    <div style={{
      maxWidth: '700px',
      margin: '2rem auto',
      padding: '1.5rem',
      fontFamily: 'system-ui, -apple-system, sans-serif',
    }}>
      <div style={{
        marginBottom: '1rem',
        fontSize: '0.95rem',
        color: '#666',
        textAlign: 'center',
      }}>
        Total discrepancy: {total} bits (odds ~40:1)
      </div>

      <svg width="100%" height={barHeight + 40} viewBox={`0 0 ${barWidth} ${barHeight + 40}`}>
        {segments.map((segment, index) => {
          const startX = segments
            .slice(0, index)
            .reduce((sum, s) => sum + (s.value / totalValue) * barWidth, 0);
          const width = (segment.value / totalValue) * barWidth;

          return (
            <g key={index}>
              <rect
                x={startX}
                y={10}
                width={width}
                height={barHeight}
                fill={colors[index % colors.length]}
                opacity={0.8}
              />
              <text
                x={startX + width / 2}
                y={barHeight + 30}
                textAnchor="middle"
                fontSize="11"
                fill="#333"
              >
                {segment.value} bit{segment.value !== 1 ? 's' : ''}
              </text>
            </g>
          );
        })}
      </svg>

      <div style={{ marginTop: '1.5rem' }}>
        {segments.map((segment, index) => (
          <div key={index} style={{
            display: 'flex',
            alignItems: 'center',
            marginBottom: '0.5rem',
            fontSize: '0.9rem',
          }}>
            <div style={{
              width: '16px',
              height: '16px',
              backgroundColor: colors[index % colors.length],
              opacity: 0.8,
              marginRight: '0.75rem',
              borderRadius: '2px',
            }}></div>
            <div style={{ flex: 1 }}>
              <strong>{segment.label}:</strong> {segment.value} bit{segment.value !== 1 ? 's' : ''}
              {segment.description && (
                <span style={{ color: '#666', marginLeft: '0.5rem' }}>
                  — {segment.description}
                </span>
              )}
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}
