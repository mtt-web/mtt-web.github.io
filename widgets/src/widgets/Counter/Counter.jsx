import React, { useState } from 'react';

/**
 * Counter - A simple interactive counter widget to demonstrate React state
 * Perfect for testing React component embedding in blog posts
 */
export default function Counter({ initialValue = 0, step = 1 }) {
  const [count, setCount] = useState(initialValue);

  return (
    <div style={{
      display: 'flex',
      flexDirection: 'column',
      alignItems: 'center',
      gap: '1rem',
      padding: '2rem',
      background: 'linear-gradient(135deg, #667eea 0%, #764ba2 100%)',
      borderRadius: '12px',
      color: 'white',
      fontFamily: 'system-ui, -apple-system, sans-serif',
      maxWidth: '300px',
      margin: '0 auto',
      boxShadow: '0 8px 16px rgba(0,0,0,0.2)'
    }}>
      <h3 style={{ margin: 0, fontSize: '1.25rem', fontWeight: 600 }}>
        Interactive Counter
      </h3>

      <div style={{
        fontSize: '3rem',
        fontWeight: 'bold',
        margin: '1rem 0',
        minWidth: '120px',
        textAlign: 'center',
        fontVariantNumeric: 'tabular-nums'
      }}>
        {count}
      </div>

      <div style={{ display: 'flex', gap: '0.75rem' }}>
        <button
          onClick={() => setCount(count - step)}
          style={{
            padding: '0.75rem 1.5rem',
            fontSize: '1.25rem',
            fontWeight: 'bold',
            border: 'none',
            borderRadius: '8px',
            background: 'rgba(255, 255, 255, 0.2)',
            color: 'white',
            cursor: 'pointer',
            transition: 'all 0.2s',
            backdropFilter: 'blur(10px)'
          }}
          onMouseOver={(e) => e.target.style.background = 'rgba(255, 255, 255, 0.3)'}
          onMouseOut={(e) => e.target.style.background = 'rgba(255, 255, 255, 0.2)'}
        >
          −
        </button>

        <button
          onClick={() => setCount(initialValue)}
          style={{
            padding: '0.75rem 1.5rem',
            fontSize: '0.9rem',
            fontWeight: 600,
            border: 'none',
            borderRadius: '8px',
            background: 'rgba(255, 255, 255, 0.2)',
            color: 'white',
            cursor: 'pointer',
            transition: 'all 0.2s',
            backdropFilter: 'blur(10px)'
          }}
          onMouseOver={(e) => e.target.style.background = 'rgba(255, 255, 255, 0.3)'}
          onMouseOut={(e) => e.target.style.background = 'rgba(255, 255, 255, 0.2)'}
        >
          Reset
        </button>

        <button
          onClick={() => setCount(count + step)}
          style={{
            padding: '0.75rem 1.5rem',
            fontSize: '1.25rem',
            fontWeight: 'bold',
            border: 'none',
            borderRadius: '8px',
            background: 'rgba(255, 255, 255, 0.2)',
            color: 'white',
            cursor: 'pointer',
            transition: 'all 0.2s',
            backdropFilter: 'blur(10px)'
          }}
          onMouseOver={(e) => e.target.style.background = 'rgba(255, 255, 255, 0.3)'}
          onMouseOut={(e) => e.target.style.background = 'rgba(255, 255, 255, 0.2)'}
        >
          +
        </button>
      </div>

      <div style={{
        fontSize: '0.85rem',
        opacity: 0.9,
        marginTop: '0.5rem'
      }}>
        Step size: {step}
      </div>
    </div>
  );
}
