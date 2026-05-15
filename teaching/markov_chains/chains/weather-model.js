class WeatherModel extends MarkovChain {
    constructor() {
        const states = [0, 1, 2];
        const stateNames = ['Sunny', 'Cloudy', 'Rainy'];
        const transitionMatrix = [
            [0.7, 0.2, 0.1],
            [0.3, 0.4, 0.3],
            [0.2, 0.4, 0.4]
        ];

        // Default start state: Sunny (0)
        const defaultStartState = 0;
        const initialDistribution = new Array(states.length).fill(0);
        initialDistribution[defaultStartState] = 1.0;

        super({
            name: "Weather Model",
            description: "A simple weather prediction model with three states",
            states: states,
            stateNames: stateNames,
            transitionMatrix: transitionMatrix,
            initialDistribution: initialDistribution
        });

        this.startState = defaultStartState;
    }

    getTheoreticalSteadyState() {
        return [0.438, 0.287, 0.275];
    }

    getCustomControls() {
        return [
            {
                type: 'slider',
                label: 'Start state',
                value: this.startState,
                min: 0,
                max: this.states.length - 1,
                step: 1,
                onChange: (value) => this.updateStartState(Math.round(value))
            }
        ];
    }

    updateStartState(newIndex) {
        const clamped = Math.max(0, Math.min(this.states.length - 1, Math.round(newIndex)));
        this.startState = clamped;
        this.initialDistribution = new Array(this.states.length).fill(0);
        this.initialDistribution[clamped] = 1.0;
        this.reset();
    }
}

chainModules.push(WeatherModel);