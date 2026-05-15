import React, { useState } from 'react';

/**
 * GenderGuess - Interactive quiz widget for guessing gender distribution
 * Features a slider and submit button, reveals answer on submission
 */
export default function GenderGuess({ correctAnswer = 97.5, onSubmit }) {
  const [guess, setGuess] = useState(50);
  const [submitted, setSubmitted] = useState(false);
  const [difference, setDifference] = useState(null);

  const handleSubmit = () => {
    const diff = Math.abs(guess - correctAnswer);
    setDifference(diff);
    setSubmitted(true);

    // Call the onSubmit callback if provided (for revealing content)
    if (onSubmit) {
      onSubmit(guess, correctAnswer);
    }

    // Dispatch a custom event for the page to listen to
    window.dispatchEvent(new CustomEvent('genderGuessSubmitted', {
      detail: { guess, correctAnswer, difference: diff }
    }));
  };

  const getFeedback = () => {
    if (difference === 0) return "Perfect! You got it exactly right!";
    if (difference < 2) return "Wow! Incredibly close!";
    if (difference < 5) return "Very close!";
    if (difference < 10) return "Pretty close!";
    if (difference < 20) return "Not too far off.";
    return "Quite a bit off, but an understandable guess!";
  };

  return (
    <div style={{
      display: 'flex',
      flexDirection: 'column',
      alignItems: 'center',
      gap: '1rem',
      padding: '1.5rem',
      background: 'linear-gradient(135deg, #667eea 0%, #764ba2 100%)',
      borderRadius: '12px',
      color: 'white',
      fontFamily: 'system-ui, -apple-system, sans-serif',
      maxWidth: '500px',
      margin: '1.5rem auto',
      boxShadow: '0 8px 16px rgba(0,0,0,0.2)'
    }}>
      <h3 style={{ margin: 0, fontSize: '1.15rem', fontWeight: 600, textAlign: 'center' }}>
        What % of viewers are male?
      </h3>

      {!submitted ? (
        <>
          <div style={{
            fontSize: '2.5rem',
            fontWeight: 'bold',
            margin: '0.5rem 0',
            fontVariantNumeric: 'tabular-nums'
          }}>
            {guess.toFixed(1)}%
          </div>

          <div style={{ width: '100%', padding: '0 1rem' }}>
            <input
              type="range"
              min="0"
              max="100"
              step="0.5"
              value={guess}
              onChange={(e) => setGuess(parseFloat(e.target.value))}
              style={{
                width: '100%',
                height: '8px',
                borderRadius: '4px',
                outline: 'none',
                cursor: 'pointer',
                appearance: 'none',
                background: 'rgba(255, 255, 255, 0.3)'
              }}
            />
            <div style={{
              display: 'flex',
              justifyContent: 'space-between',
              fontSize: '0.85rem',
              marginTop: '0.5rem',
              opacity: 0.8
            }}>
              <span>0%</span>
              <span>50%</span>
              <span>100%</span>
            </div>
          </div>

          <button
            onClick={handleSubmit}
            style={{
              padding: '0.6rem 1.8rem',
              fontSize: '1rem',
              fontWeight: 600,
              border: 'none',
              borderRadius: '8px',
              background: 'rgba(255, 255, 255, 0.9)',
              color: '#667eea',
              cursor: 'pointer',
              transition: 'all 0.2s',
              boxShadow: '0 4px 8px rgba(0,0,0,0.2)'
            }}
            onMouseOver={(e) => {
              e.target.style.background = 'white';
              e.target.style.transform = 'translateY(-2px)';
            }}
            onMouseOut={(e) => {
              e.target.style.background = 'rgba(255, 255, 255, 0.9)';
              e.target.style.transform = 'translateY(0)';
            }}
          >
            Test
          </button>
        </>
      ) : (
        <div style={{
          background: 'rgba(255, 255, 255, 0.2)',
          padding: '1rem',
          borderRadius: '8px',
          width: '100%',
          backdropFilter: 'blur(10px)',
          textAlign: 'center'
        }}>
          <div style={{ marginBottom: '0.5rem', fontSize: '1rem' }}>
            Your guess: {guess.toFixed(1)}%
          </div>
          <div style={{ fontSize: '1rem' }}>
            Actual: {correctAnswer}%
          </div>
        </div>
      )}
    </div>
  );
}
