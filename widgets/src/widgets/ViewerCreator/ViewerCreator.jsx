import React, { useState } from 'react';

/**
 * ViewerCreator - Visualizes the "birds of a feather" effect
 * Shows 10 viewers and 10 creators (9 male, 1 female each)
 * Demonstrates how same-gender preference affects viewership ratios
 */
export default function ViewerCreator() {
  const [tooltip, setTooltip] = useState(null);

  const iconSize = 24;
  const spacing = 50;
  const maleColor = '#4a90e2';
  const femaleColor = '#e91e63';
  const crossColor = '#8a63d2'; // violet for male-female connections

  const viewerY = 50;
  const creatorY = 180;
  const startX = 30;

  // Calculate viewing percentages
  // Each creator should get 10% of total audience (100% viewing time)
  // For Physics Girl to be 80% male / 20% female:
  // 9 male viewers contribute 80%, 1 female viewer contributes 20%
  const femaleViewerToFemaleCreator = 20.0;
  const maleViewerToFemaleCreator = 80.0 / 9; // 8.89%

  // Female viewer sends remaining time to male creators
  // 9 male creators get (100 - 20) / 9 = 8.89% each
  const femaleViewerToMaleCreator = (100 - femaleViewerToFemaleCreator) / 9; // 8.89%

  // For male creator to get 100%: 9 male viewers send (100 - 8.89) / 9 = 10.12%
  const maleViewerToMaleCreator = (100 - femaleViewerToMaleCreator) / 9; // 10.12%

  // Each creator now gets exactly 100% of viewing time (10% of total audience)
  const creatorAudience = 100.0;

  // Calculate gender splits for creators
  const maleCreatorMalePercent = (9 * maleViewerToMaleCreator) / creatorAudience; // 91.11%
  const maleCreatorFemalePercent = (1 * femaleViewerToMaleCreator) / creatorAudience; // 8.89%
  const femaleCreatorMalePercent = (9 * maleViewerToFemaleCreator) / creatorAudience; // 80.0%
  const femaleCreatorFemalePercent = (1 * femaleViewerToFemaleCreator) / creatorAudience; // 20.0%

  return (
    <div style={{
      maxWidth: '700px',
      margin: '2rem auto',
      padding: '1.5rem',
      fontFamily: 'system-ui, -apple-system, sans-serif',
      position: 'relative',
    }}>
      <div style={{
        fontSize: '0.95rem',
        color: '#666',
        marginBottom: '1.5rem',
        textAlign: 'center',
      }}>
        10 viewers × 10 creators (9 male, 1 female each)
      </div>

      {/* Tooltip */}
      {tooltip && (
        <div style={{
          position: 'absolute',
          left: tooltip.x,
          top: tooltip.y,
          background: 'rgba(0, 0, 0, 0.85)',
          color: 'white',
          padding: '8px 12px',
          borderRadius: '4px',
          fontSize: '13px',
          pointerEvents: 'none',
          zIndex: 1000,
          whiteSpace: 'nowrap',
          transform: 'translate(-50%, -100%)',
          marginTop: '-8px',
        }}>
          {tooltip.text}
        </div>
      )}

      <svg width="100%" height="280" viewBox="0 0 550 280"
           onMouseLeave={() => setTooltip(null)}>
        {/* Connection lines - draw all pairings with color coding */}
        {/* Define genders: viewers 0-8 male, 9 female; creators 0-8 male, 9 female */}
        {Array.from({ length: 10 }).map((_, vi) => (
          Array.from({ length: 10 }).map((__, ci) => {
            const viewerX = startX + vi * spacing;
            const creatorX = startX + ci * spacing;
            const isViewerFemale = vi === 9;
            const isCreatorFemale = ci === 9;

            // Calculate percentage for this edge
            let edgePercent;
            if (isViewerFemale && isCreatorFemale) {
              edgePercent = femaleViewerToFemaleCreator;
            } else if (isViewerFemale && !isCreatorFemale) {
              edgePercent = femaleViewerToMaleCreator;
            } else if (!isViewerFemale && isCreatorFemale) {
              edgePercent = maleViewerToFemaleCreator;
            } else {
              edgePercent = maleViewerToMaleCreator;
            }

            // Color: red for female-female, violet for cross, blue for male-male
            const strokeColor = isViewerFemale && isCreatorFemale
              ? femaleColor
              : (!isViewerFemale && !isCreatorFemale)
                ? maleColor
                : crossColor;
            // Thickness hinting (optional): stronger for same-gender, lighter for cross
            const strokeWidth = isViewerFemale && isCreatorFemale
              ? 2.5
              : (!isViewerFemale && !isCreatorFemale)
                ? 1.5
                : 1;
            const opacity = isViewerFemale && isCreatorFemale
              ? 0.45
              : (!isViewerFemale && !isCreatorFemale)
                ? 0.25
                : 0.25;
            return (
              <line
                key={`v${vi}-c${ci}`}
                x1={viewerX}
                y1={viewerY}
                x2={creatorX}
                y2={creatorY}
                stroke={strokeColor}
                strokeWidth={strokeWidth}
                opacity={opacity}
              />
            );
          })
        ))}

        {/* Viewers row */}
        <text x={startX} y="25" fontSize="13" fontWeight="600" fill="#333">
          ↓ Viewers ↓
        </text>

        {/* 9 male viewers */}
        {Array.from({ length: 9 }).map((_, i) => {
          const maleViewerToMale = (9 * maleViewerToMaleCreator).toFixed(1);
          const maleViewerToFemale = maleViewerToFemaleCreator.toFixed(1);
          return (
            <circle
              key={`male-viewer-${i}`}
              cx={startX + i * spacing}
              cy={viewerY}
              r={iconSize / 2}
              fill={maleColor}
              opacity={0.8}
              stroke={maleColor}
              strokeWidth="1.5"
              style={{ cursor: 'pointer' }}
              onMouseEnter={(e) => {
                const rect = e.currentTarget.getBoundingClientRect();
                const container = e.currentTarget.closest('div');
                const containerRect = container.getBoundingClientRect();
                setTooltip({
                  x: rect.left + rect.width / 2 - containerRect.left,
                  y: rect.top - containerRect.top,
                  text: `Sends ${maleViewerToMale}% to male, ${maleViewerToFemale}% to female creators`
                });
              }}
            />
          );
        })}

        {/* 1 female viewer */}
        <circle
          cx={startX + 9 * spacing}
          cy={viewerY}
          r={iconSize / 2}
          fill={femaleColor}
          opacity={0.8}
          stroke={femaleColor}
          strokeWidth="1.5"
          style={{ cursor: 'pointer' }}
          onMouseEnter={(e) => {
            const femaleViewerToMale = (9 * femaleViewerToMaleCreator).toFixed(1);
            const femaleViewerToFemale = femaleViewerToFemaleCreator.toFixed(1);
            const rect = e.currentTarget.getBoundingClientRect();
            const container = e.currentTarget.closest('div');
            const containerRect = container.getBoundingClientRect();
            setTooltip({
              x: rect.left + rect.width / 2 - containerRect.left,
              y: rect.top - containerRect.top,
              text: `Sends ${femaleViewerToMale}% to male, ${femaleViewerToFemale}% to female creators`
            });
          }}
        />

        {/* Creators row */}
        <text x={startX} y="210" fontSize="13" fontWeight="600" fill="#333">
          ↑ Creators ↑
        </text>

        {/* 9 male creators */}
        {Array.from({ length: 9 }).map((_, i) => (
          <rect
            key={`male-creator-${i}`}
            x={startX + i * spacing - iconSize / 2}
            y={creatorY - iconSize / 2}
            width={iconSize}
            height={iconSize}
            fill={maleColor}
            opacity={0.8}
            stroke={maleColor}
            strokeWidth="1.5"
            style={{ cursor: 'pointer' }}
            onMouseEnter={(e) => {
              const rect = e.currentTarget.getBoundingClientRect();
              const container = e.currentTarget.closest('div');
              const containerRect = container.getBoundingClientRect();
              setTooltip({
                x: rect.left + rect.width / 2 - containerRect.left,
                y: rect.top - containerRect.top,
                text: `10% audience | ${maleCreatorMalePercent.toFixed(1)}% M / ${maleCreatorFemalePercent.toFixed(1)}% F`
              });
            }}
          />
        ))}

        {/* 1 female creator */}
        <rect
          x={startX + 9 * spacing - iconSize / 2}
          y={creatorY - iconSize / 2}
          width={iconSize}
          height={iconSize}
          fill={femaleColor}
          opacity={0.8}
          stroke={femaleColor}
          strokeWidth="1.5"
          style={{ cursor: 'pointer' }}
          onMouseEnter={(e) => {
            const rect = e.currentTarget.getBoundingClientRect();
            const container = e.currentTarget.closest('div');
            const containerRect = container.getBoundingClientRect();
            setTooltip({
              x: rect.left + rect.width / 2 - containerRect.left,
              y: rect.top - containerRect.top,
              text: `10% audience | ${femaleCreatorMalePercent.toFixed(1)}% M / ${femaleCreatorFemalePercent.toFixed(1)}% F`
            });
          }}
        />

        {/* Legend */}
        <g>
          <rect x="210" y="232" width="18" height="18" fill={femaleColor} opacity="0.8" stroke={femaleColor} strokeWidth="1.5" />
          <text x="235" y="245" fontSize="16" fill="#555">= Physics Girl</text>
        </g>
      </svg>
    </div>
  );
}
