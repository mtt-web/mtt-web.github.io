# Standalone React Demos

This document describes how to create and work with standalone React demos in the `teaching/` directory.

## What are Standalone Demos?

Standalone demos are full React applications that run as separate pages, accessed via URLs like `/teaching/demo-name/build/`. They are different from the widget system (see `/README.md`), which embeds small React components into blog posts.

## When to Use Standalone Demos vs Widgets

| Use Case | Solution |
|----------|----------|
| Full interactive teaching tool with complex UI | **Standalone Demo** |
| Simple visualization embedded in blog post | **Widget** |
| Multi-page application | **Standalone Demo** |
| Single component with minimal state | **Widget** |
| Needs routing or complex navigation | **Standalone Demo** |
| Should be inline with markdown content | **Widget** |

## Existing Standalone Demos

- **pagerank** (`teaching/pagerank/`) - PageRank visualization for Wikipedia
- **markov_chains** (`teaching/markov_chains/`) - Markov chain examples (vanilla JS)
- **3sat-random-walk** (`teaching/3sat-random-walk/`) - Random walk on 3SAT
- **metropolis-demo** (`teaching/metropolis-demo/`) - Metropolis sampling demo
- **landscape-demo** (`teaching/landscape-demo/`) - Landscape sampling with 3D visualization
- **markov-chain-demo** (`teaching/markov-chain-demo/`) - HMM map-matching demo

## Creating a New Standalone Demo

### Step 1: Create Directory Structure

```bash
cd teaching
mkdir -p my-demo/public my-demo/src
```

### Step 2: Create package.json

```json
{
  "name": "my-demo",
  "version": "1.0.0",
  "private": true,
  "homepage": ".",
  "dependencies": {
    "react": "^18.2.0",
    "react-dom": "^18.2.0",
    "react-scripts": "5.0.1"
  },
  "scripts": {
    "start": "DISABLE_ESLINT_PLUGIN=true react-scripts start",
    "build": "react-scripts build",
    "test": "react-scripts test",
    "eject": "react-scripts eject"
  },
  "eslintConfig": {
    "extends": ["react-app"]
  },
  "browserslist": {
    "production": [">0.2%", "not dead", "not op_mini all"],
    "development": ["last 1 chrome version", "last 1 firefox version", "last 1 safari version"]
  }
}
```

### Step 3: Create public/index.html

```html
<!DOCTYPE html>
<html lang="en">
  <head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <title>My Demo</title>
  </head>
  <body>
    <noscript>You need to enable JavaScript to run this app.</noscript>
    <div id="root"></div>
  </body>
</html>
```

### Step 4: Create src/index.js

```javascript
import React from 'react';
import ReactDOM from 'react-dom/client';
import App from './App';

const root = ReactDOM.createRoot(document.getElementById('root'));
root.render(
  <React.StrictMode>
    <App />
  </React.StrictMode>
);
```

### Step 5: Create src/App.js

```javascript
import React, { useState } from 'react';
import './App.css';

export default function App() {
  const [count, setCount] = useState(0);

  return (
    <div className="App">
      <h1>My Demo</h1>
      <button onClick={() => setCount(count + 1)}>
        Count: {count}
      </button>
    </div>
  );
}
```

### Step 6: Create src/App.css

```css
.App {
  min-height: 100vh;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  font-family: sans-serif;
}

button {
  padding: 1rem 2rem;
  font-size: 1.2rem;
  cursor: pointer;
}
```

### Step 7: Create .gitignore

```
# Dependencies
/node_modules
/.pnp
.pnp.js

# Testing
/coverage

# Production build
/build

# Misc
.DS_Store
npm-debug.log*
```

### Step 8: Update _config.yml

Add your demo's node_modules to the exclusion list in `_config.yml`:

```yaml
exclude:
  # ... existing entries ...
  - teaching/my-demo/node_modules/
```

**Important:** Only exclude `node_modules/`, NOT the entire demo directory. The `build/` directory must be accessible to Jekyll.

### Step 9: Install and Build

```bash
cd teaching/my-demo
npm install
npm run build
```

This creates a `build/` directory with the compiled static files.

### Step 10: Link from Teaching Page

In your teaching page markdown (e.g., `teaching/2025/probability-2.md`):

```markdown
# Demos

- [My Demo](/teaching/my-demo/build/)
```

## Development Workflow

### Local Development

```bash
cd teaching/my-demo
npm start
```

This opens the demo at http://localhost:3000 with hot reload.

### Building for Production

```bash
npm run build
```

Creates optimized static files in `build/`.

### Testing Production Build Locally

```bash
# In repository root
bundle exec jekyll serve
```

Then visit http://localhost:4000/teaching/my-demo/build/

## Adding Dependencies

You can add any npm packages to your demo:

```bash
cd teaching/my-demo
npm install d3          # For data visualization
npm install three       # For 3D graphics
npm install katex       # For math rendering
npm install react-katex # React wrapper for KaTeX
```

Update `package.json` dependencies and commit the changes.

## Common Patterns

### Using KaTeX for Math

```bash
npm install katex react-katex
```

```javascript
import { InlineMath, BlockMath } from 'react-katex';
import 'katex/dist/katex.min.css';

function MyComponent() {
  return (
    <div>
      <InlineMath math="E = mc^2" />
      <BlockMath math="\int_0^1 x^2 dx" />
    </div>
  );
}
```

### Loading Data Files

Place data files in `public/`:

```
my-demo/
  public/
    data.json
    graph.csv
```

Load them with absolute paths:

```javascript
fetch('/teaching/my-demo/build/data.json')
  .then(r => r.json())
  .then(data => console.log(data));
```

**Important:** Use the full path including `/teaching/my-demo/build/` because that's where the files will be when served through Jekyll.

Alternatively, during development, react-scripts serves files from `public/` at the root, so you can use:

```javascript
fetch(process.env.PUBLIC_URL + '/data.json')
  .then(r => r.json())
  .then(data => console.log(data));
```

The `PUBLIC_URL` environment variable automatically resolves to `.` (current directory) during build, thanks to `"homepage": "."` in `package.json`.

### Using SVG for Visualizations

```javascript
function MyViz() {
  return (
    <svg width={400} height={300}>
      <circle cx={200} cy={150} r={50} fill="blue" />
      <line x1={0} y1={0} x2={400} y2={300} stroke="red" strokeWidth={2} />
    </svg>
  );
}
```

### Interactive Controls

```javascript
function Controls() {
  const [value, setValue] = useState(50);

  return (
    <div>
      <label>
        Parameter: {value}
        <input
          type="range"
          min={0}
          max={100}
          value={value}
          onChange={e => setValue(parseInt(e.target.value))}
        />
      </label>
    </div>
  );
}
```

## File Structure Reference

```
teaching/my-demo/
├── public/
│   ├── index.html          # HTML template
│   └── data.json           # Optional static data
├── src/
│   ├── index.js            # Entry point
│   ├── App.js              # Main component
│   ├── App.css             # Styles
│   └── OtherComponent.js   # Additional components
├── build/                  # Built files (created by npm run build)
│   ├── index.html
│   ├── static/
│   │   ├── js/
│   │   └── css/
│   └── data.json           # Copied from public/
├── node_modules/           # Dependencies (excluded from Jekyll)
├── package.json            # npm config
├── package-lock.json       # Dependency lock
└── .gitignore              # Git ignore rules
```

## Deployment

### Manual Deployment

1. Build locally: `npm run build`
2. Commit the `build/` directory
3. Push to GitHub
4. GitHub Pages serves the files

### Automatic Deployment (Recommended)

Set up a GitHub Actions workflow to build the demo automatically (similar to the widget system). Example:

```yaml
# .github/workflows/build-demos.yml
name: Build Teaching Demos
on:
  push:
    branches: [main]
    paths:
      - 'teaching/*/src/**'
      - 'teaching/*/package.json'

jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v3
      - uses: actions/setup-node@v3
        with:
          node-version: '20'

      - name: Build markov-chain-demo
        run: |
          cd teaching/markov-chain-demo
          npm install
          npm run build

      # Add more demos here as needed

      - name: Commit build outputs
        run: |
          git config user.name github-actions
          git config user.email github-actions@github.com
          git add teaching/*/build
          git commit -m "Build teaching demos" || echo "No changes"
          git push
```

## Troubleshooting

### Demo doesn't load

1. Check that `build/` directory exists: `ls teaching/my-demo/build/`
2. Verify `_config.yml` only excludes `node_modules/`, not the entire demo
3. Check browser console for errors
4. Ensure paths in `fetch()` calls are absolute

### Styles not working

1. Make sure CSS is imported in `App.js`: `import './App.css';`
2. Check that CSS file is in `src/`, not `public/`
3. Rebuild: `npm run build`

### Jekyll build fails

1. Ensure `node_modules/` is in `_config.yml` exclusions
2. Check that you're not committing `node_modules/` to git (`.gitignore` should prevent this)
3. Run `bundle exec jekyll serve` locally to see error details

### Data files not loading

1. Place data files in `public/`, not `src/`
2. Use `process.env.PUBLIC_URL + '/data.json'` for portability
3. Check the Network tab in browser dev tools to see the actual request URL

## Best Practices

1. **Keep demos independent**: Each demo should be self-contained
2. **Document your demo**: Add a comment block at the top of `App.js` explaining what the demo does
3. **Use meaningful names**: Choose descriptive directory and variable names
4. **Commit build output**: Unlike the widget system, commit the `build/` directory for teaching demos
5. **Test locally**: Always run `npm run build` and test with Jekyll before pushing
6. **Add to teaching page**: Don't forget to link your demo from the relevant teaching page

## Comparison with Widget System

| Feature | Standalone Demo | Widget System |
|---------|----------------|---------------|
| **Location** | `teaching/demo-name/` | `widgets/src/widgets/` |
| **Build tool** | react-scripts (CRA) | Vite |
| **Build output** | `build/` directory | `dist/*.js` bundles |
| **Commit build?** | Yes | No (built by CI/CD) |
| **Access URL** | `/teaching/demo-name/build/` | N/A (embedded inline) |
| **Embedding** | Link from markdown | `<div>` + `<script>` tag |
| **Use case** | Full teaching app | Small inline visualization |
| **Routing** | Supported | Not applicable |
| **Data loading** | Via `public/` directory | Via `widgets/public/` |

## See Also

- Root `README.md` - Complete project documentation including widget system
- `.claude/widget-system-overview.md` - Widget system quick reference
- Existing demos for examples: `teaching/metropolis-demo/`, `teaching/landscape-demo/`
