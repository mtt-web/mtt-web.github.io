class ThrowingFrisbee extends MarkovChain {
    constructor() {
        super({
            name: "Throwing Frisbee",
            description: "3 players (Alice, Bob, Charlie) throwing a frisbee. Each player throws to a random neighbor.",
            states: [0, 1, 2],
            stateNames: ['Alice', 'Bob', 'Charlie'],
            transitionMatrix: [
                [0, 0.5, 0.5],    // Alice throws to Bob or Charlie (equal probability)
                [0.5, 0, 0.5],    // Bob throws to Alice or Charlie (equal probability)
                [0.5, 0.5, 0]     // Charlie throws to Alice or Bob (equal probability)
            ],
            initialDistribution: [1.0, 0.0, 0.0]  // Alice starts with frisbee
        });
    }

    getNodePositions(centerX, centerY, radius) {
        // Position nodes in a triangle
        const triangleRadius = 120;
        return [
            {
                x: centerX,
                y: centerY - triangleRadius
            }, // Alice at top
            {
                x: centerX - triangleRadius * Math.cos(Math.PI/6),
                y: centerY + triangleRadius * Math.sin(Math.PI/6)
            }, // Bob at bottom left
            {
                x: centerX + triangleRadius * Math.cos(Math.PI/6),
                y: centerY + triangleRadius * Math.sin(Math.PI/6)
            }  // Charlie at bottom right
        ];
    }

    getTheoreticalSteadyState() {
        // All players are equivalent, so steady state is uniform
        return [1/3, 1/3, 1/3];
    }

    getRenderConfig() {
        return {
            canvasHeight: 400,
            showStats: true,
            showTransitionMatrix: true,
            showEdgeLabels: true
        };
    }

    getEdgeRenderMode() {
        return 'curved_all'; // Use curved edges to avoid overlapping arrows
    }
}

chainModules.push(ThrowingFrisbee);