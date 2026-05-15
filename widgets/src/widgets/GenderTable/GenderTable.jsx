import React from 'react';

/**
 * GenderTable - Displays gender distribution data for various channels
 * Shows visual bars comparing male vs female viewership percentages
 */
export default function GenderTable({ channels = [] }) {
  const styles = {
    table: {
      width: '100%',
      maxWidth: '800px',
      margin: '2rem auto',
      borderCollapse: 'collapse',
      fontFamily: 'system-ui, -apple-system, sans-serif',
    },
    th: {
      background: '#f5f5f5',
      padding: '0.75rem',
      textAlign: 'left',
      fontWeight: 600,
      borderBottom: '2px solid #ddd',
    },
    td: {
      padding: '0.75rem',
      borderBottom: '1px solid #eee',
    },
    tdLast: {
      padding: '0.75rem',
      borderBottom: '1px solid #eee',
      minWidth: '320px',
    },
    barContainer: {
      width: '100%',
      minWidth: '240px',
      height: '40px',
      background: '#f0f0f0',
      borderRadius: '4px',
      overflow: 'visible',
      position: 'relative',
      display: 'flex',
    },
    barMale: {
      background: 'linear-gradient(135deg, #4a90e2 0%, #357abd 100%)',
      height: '100%',
      display: 'flex',
      alignItems: 'center',
      justifyContent: 'center',
      color: 'white',
      fontWeight: 600,
      fontSize: '0.85rem',
      whiteSpace: 'nowrap',
      overflow: 'visible',
    },
    barFemale: {
      background: 'linear-gradient(135deg, #e91e63 0%, #c2185b 100%)',
      height: '100%',
      display: 'flex',
      alignItems: 'center',
      justifyContent: 'center',
      color: 'white',
      fontWeight: 600,
      fontSize: '0.85rem',
      whiteSpace: 'nowrap',
      overflow: 'visible',
    },
  };

  return (
    <table style={styles.table}>
      <thead>
        <tr>
          <th style={styles.th}>Channel</th>
          <th style={styles.th}>Host</th>
          <th style={styles.th}>Topic</th>
          <th style={styles.th}>Viewership</th>
        </tr>
      </thead>
      <tbody>
        {channels.map((channel, index) => {
          const malePercent = channel.malePercent;
          const femalePercent = 100 - malePercent;
          // Use smaller font for very small percentages
          const maleFontSize = malePercent < 20 ? '0.5rem' : '0.85rem';

          return (
            <tr key={index}>
              <td style={styles.td}>{channel.name}</td>
              <td style={styles.td}>{channel.host}</td>
              <td style={styles.td}>{channel.topic}</td>
              <td style={styles.tdLast}>
                <div style={styles.barContainer}>
                  <div style={{ ...styles.barMale, width: `${malePercent}%`, fontSize: maleFontSize }}>
                    {malePercent}% M
                  </div>
                  <div style={{ ...styles.barFemale, width: `${femalePercent}%` }}>
                    F
                  </div>
                </div>
              </td>
            </tr>
          );
        })}
      </tbody>
    </table>
  );
}
