class UndirectedGraphWalk extends MarkovChain {
    constructor() {
        // Small undirected graph with 5 edges over 5 nodes
        // Edges: (0-1), (1-2), (2-0) forming a triangle, plus tail (2-3-4)
        const numNodes = 5;
        const states = Array.from({ length: numNodes }, (_, i) => i);
        const stateNames = states.map(i => i.toString());

        // Adjacency list for undirected graph
        const edges = [
            [0, 1],
            [1, 2],
            [2, 0],
            [2, 3],
            [3, 4]
        ];
        const neighbors = Array.from({ length: numNodes }, () => []);
        for (const [u, v] of edges) {
            neighbors[u].push(v);
            neighbors[v].push(u);
        }

        // Build transition matrix: simple random walk
        const transitionMatrix = [];
        for (let i = 0; i < numNodes; i++) {
            const row = new Array(numNodes).fill(0);
            const deg = neighbors[i].length || 1;
            for (const j of neighbors[i]) {
                row[j] = 1 / deg;
            }
            transitionMatrix.push(row);
        }

        super({
            name: "Random Walk on Undirected Graph",
            description: "Simple random walk on a small undirected graph with 5 edges",
            states: states,
            stateNames: stateNames,
            transitionMatrix: transitionMatrix,
            // default initial distribution: uniform
        });

        this.neighbors = neighbors;
        this.degrees = neighbors.map(ns => ns.length);
    }

    getNodePositions(centerX, centerY, radius) {
        // Place triangle (0,1,2) around center and tail (3,4) to the right
        const r = Math.max(120, radius);
        return [
            { x: centerX + r * Math.cos(-Math.PI / 2), y: centerY + r * Math.sin(-Math.PI / 2) }, // 0 top
            { x: centerX + r * Math.cos( 30 * Math.PI / 180), y: centerY + r * Math.sin( 30 * Math.PI / 180) }, // 1 bottom-right
            { x: centerX + r * Math.cos( 150 * Math.PI / 180), y: centerY + r * Math.sin( 150 * Math.PI / 180) }, // 2 bottom-left
            { x: centerX + r * 1.8, y: centerY }, // 3 to the right
            { x: centerX + r * 2.5, y: centerY }  // 4 far right
        ];
    }

    getTheoreticalSteadyState() {
        // For connected undirected graphs, stationary distribution is proportional to degree
        const totalDegree = this.degrees.reduce((a, b) => a + b, 0) || 1;
        return this.degrees.map(d => d / totalDegree);
    }

    getRenderConfig() {
        return {
            canvasHeight: 420,
            showStats: true,
            showTransitionMatrix: true,
            showEdgeLabels: false
        };
    }

    getEdgeRenderMode() {
        return 'curved_all';
    }
}

chainModules.push(UndirectedGraphWalk);




