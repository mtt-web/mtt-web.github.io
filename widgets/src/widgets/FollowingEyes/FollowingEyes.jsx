import React, { useState, useEffect, useRef } from 'react';

/**
 * FollowingEyes - Eyes that follow the cursor around the screen
 * A fun interactive widget to demonstrate event handling and animations
 */
export default function FollowingEyes() {
  const [mousePos, setMousePos] = useState({ x: 0, y: 0 });
  const containerRef = useRef(null);

  useEffect(() => {
    const handleMouseMove = (e) => {
      setMousePos({ x: e.clientX, y: e.clientY });
    };

    window.addEventListener('mousemove', handleMouseMove);
    return () => window.removeEventListener('mousemove', handleMouseMove);
  }, []);

  const calculateEyePosition = (eyeX, eyeY) => {
    if (!containerRef.current) return { x: 0, y: 0 };

    const rect = containerRef.current.getBoundingClientRect();
    const eyeCenterX = eyeX + rect.left;
    const eyeCenterY = eyeY + rect.top;

    const angle = Math.atan2(mousePos.y - eyeCenterY, mousePos.x - eyeCenterX);
    const distance = Math.min(12, Math.hypot(mousePos.x - eyeCenterX, mousePos.y - eyeCenterY) / 20);

    return {
      x: Math.cos(angle) * distance,
      y: Math.sin(angle) * distance
    };
  };

  const leftPupil = calculateEyePosition(80, 100);
  const rightPupil = calculateEyePosition(220, 100);

  return (
    <div
      ref={containerRef}
      style={{
        width: '300px',
        height: '200px',
        margin: '0 auto',
        background: 'linear-gradient(135deg, #f093fb 0%, #f5576c 100%)',
        borderRadius: '16px',
        position: 'relative',
        boxShadow: '0 8px 16px rgba(0,0,0,0.2)',
        overflow: 'hidden'
      }}
    >
      {/* Left Eye */}
      <div style={{
        position: 'absolute',
        left: '50px',
        top: '70px',
        width: '60px',
        height: '60px',
        background: 'white',
        borderRadius: '50%',
        boxShadow: 'inset 0 2px 8px rgba(0,0,0,0.1)',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center'
      }}>
        <div style={{
          width: '24px',
          height: '24px',
          background: '#333',
          borderRadius: '50%',
          transform: `translate(${leftPupil.x}px, ${leftPupil.y}px)`,
          transition: 'transform 0.1s ease-out',
          position: 'relative'
        }}>
          <div style={{
            position: 'absolute',
            top: '6px',
            left: '6px',
            width: '8px',
            height: '8px',
            background: 'white',
            borderRadius: '50%',
            opacity: 0.9
          }} />
        </div>
      </div>

      {/* Right Eye */}
      <div style={{
        position: 'absolute',
        left: '190px',
        top: '70px',
        width: '60px',
        height: '60px',
        background: 'white',
        borderRadius: '50%',
        boxShadow: 'inset 0 2px 8px rgba(0,0,0,0.1)',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center'
      }}>
        <div style={{
          width: '24px',
          height: '24px',
          background: '#333',
          borderRadius: '50%',
          transform: `translate(${rightPupil.x}px, ${rightPupil.y}px)`,
          transition: 'transform 0.1s ease-out',
          position: 'relative'
        }}>
          <div style={{
            position: 'absolute',
            top: '6px',
            left: '6px',
            width: '8px',
            height: '8px',
            background: 'white',
            borderRadius: '50%',
            opacity: 0.9
          }} />
        </div>
      </div>

      {/* Mouth */}
      <div style={{
        position: 'absolute',
        left: '50%',
        bottom: '40px',
        transform: 'translateX(-50%)',
        width: '80px',
        height: '40px',
        borderBottom: '4px solid white',
        borderRadius: '0 0 80px 80px',
        opacity: 0.8
      }} />

      {/* Title */}
      <div style={{
        position: 'absolute',
        top: '12px',
        left: '50%',
        transform: 'translateX(-50%)',
        color: 'white',
        fontSize: '0.9rem',
        fontWeight: 600,
        fontFamily: 'system-ui, -apple-system, sans-serif',
        textShadow: '0 2px 4px rgba(0,0,0,0.2)',
        whiteSpace: 'nowrap'
      }}>
        Move your cursor! 👀
      </div>
    </div>
  );
}
