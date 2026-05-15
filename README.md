# Václav Rozhoň's Personal Website

This is a Jekyll-based static website with embedded React widgets for interactive visualizations and components.

> **For AI Assistants**: Quick reference guide at [`.claude/widget-system-overview.md`](.claude/widget-system-overview.md). Always read this README for complete, authoritative documentation.

---

## Jekyll Site Architecture

### Site Structure

```
/
├── _posts/              # Blog posts (Markdown with YAML frontmatter)
├── _pages/              # Static pages
├── assets/              # Images, CSS, etc.
├── teaching/            # Teaching materials (some have their own node_modules)
├── widgets/             # React widgets build system (see Widget System section)
└── _config.yml          # Jekyll configuration
```

### Key Jekyll Configuration

- **Theme**: Uses remote theme `mmistakes/minimal-mistakes@4.26.2` with "air" skin
- **Math**: KaTeX engine enabled for math rendering in markdown
- **Plugins**: jekyll-feed, jekyll-include-cache

### Important Exclusions from Jekyll Build

The `_config.yml` excludes the following from Jekyll processing:
- `widgets/node_modules/`, `widgets/src/`, `widgets/public/`
- `widgets/package.json`, `widgets/*.json`, `widgets/*.html`, `widgets/*.js`
- All widget documentation and preview files
- Teaching materials' node_modules

**Critical**: Only `widgets/dist/` is included in the Jekyll build. This contains the compiled widget bundles.

### Blog Post Format

Posts live in `_posts/` with filename format: `YYYY-MM-DD-title.md`

```markdown
---
layout: single
title: "Post Title"
date: 2025-01-18 10:00:00 +0100
categories: blog
---

Your content here with embedded widgets...
```

---

## Widget System Architecture

### Design Philosophy

The widget system allows embedding **independent, standalone React components** into static Jekyll blog posts. Each widget:
- Is a self-contained React application
- Builds to a single JavaScript bundle
- Auto-mounts to a specific DOM element when loaded
- Can be configured via `data-*` attributes
- Works without any global React runtime or SPA infrastructure

### Widget Package Directory Structure

```
widgets/
├── src/
│   ├── widgets/              # All embeddable widgets (each is independent)
│   │   ├── PopulationChart/
│   │   │   ├── PopulationChart.jsx    # The React component
│   │   │   └── index.jsx              # Entry point (auto-mount logic)
│   │   ├── GeographyCharts/
│   │   ├── Counter/
│   │   └── FollowingEyes/
│   └── utils/                # Shared utilities (e.g., countryHelpers.js)
├── public/                   # Static data files (JSON, CSV)
├── dist/                     # Built bundles (committed to git, served by Jekyll)
│   ├── population-chart.js
│   ├── geography-charts.js
│   ├── counter-widget.js
│   ├── following-eyes-widget.js
│   └── assets/
├── preview.html              # Dev preview (loads from src/ with hot reload)
├── preview-dist.html         # Production preview (loads from dist/)
├── package.json
├── vite.config.js
└── node_modules/
```

### Build System (Vite)

**Critical Configuration** (`vite.config.js`):
- **Base path**: `/widgets/` (must match Jekyll directory structure)
- **Multiple entry points**: Each widget gets its own entry in `rollupOptions.input`
- **No hashes**: `entryFileNames: '[name].js'` for predictable filenames
- **Public directory**: Data files from `public/` are copied to `dist/`

**Entry Point Pattern**:
```javascript
input: {
  'widget-name': resolve(__dirname, 'src/widgets/WidgetName/index.jsx'),
}
```

### Widget Anatomy

Every widget follows this two-file pattern:

#### 1. `WidgetName.jsx` - The React Component

```jsx
import React, { useState } from 'react';

export default function WidgetName({ configProp = 'default' }) {
  const [state, setState] = useState(0);

  return (
    <div>
      {/* Your widget UI */}
    </div>
  );
}
```

#### 2. `index.jsx` - Auto-Mount Entry Point

```jsx
import React from 'react';
import { createRoot } from 'react-dom/client';
import WidgetName from './WidgetName.jsx';

// Auto-mount to specific div ID
const rootElement = document.getElementById('widget-name-root');
if (rootElement) {
  // Read configuration from data attributes
  const configProp = rootElement.dataset.config || 'default';

  const root = createRoot(rootElement);
  root.render(<WidgetName configProp={configProp} />);
}

// Also export for programmatic use
export default WidgetName;
```

**Why this pattern?**
- `index.jsx` executes immediately when the script loads
- Searches for the specific root div ID in the DOM
- If found, creates a React root and mounts the component
- This allows static HTML to "become React" without any global setup

### Data Loading Patterns

Widgets that need data should fetch from `/widgets/` path:

```jsx
// Correct - absolute path from site root
fetch('/widgets/data.json').then(r => r.json())

// Incorrect - relative paths won't work when embedded in blog posts
fetch('./data.json').then(r => r.json())
```

**Why?** Blog posts are served from `/blog/category/date/title/`, but widgets are always at `/widgets/dist/`. Use absolute paths.

### Configuration via Data Attributes

Widgets can read configuration from the mounting div:

```html
<!-- In blog post -->
<div id="counter-widget-root" data-initial="10" data-step="5"></div>
<script type="module" src="/widgets/dist/counter-widget.js"></script>
```

```jsx
// In index.jsx
const initialValue = parseInt(rootElement.dataset.initial) || 0;
const step = parseInt(rootElement.dataset.step) || 1;
root.render(<Counter initialValue={initialValue} step={step} />);
```

---

## Development Workflow

### Initial Setup

```bash
cd widgets
npm install
```

### Daily Development

1. **Start dev server**:
   ```bash
   npm run dev
   ```
   - Opens `preview.html` at http://localhost:3000
   - Hot-reloads on any source file change
   - Shows all widgets in one page for easy testing

2. **Edit widget source**:
   - Make changes in `src/widgets/YourWidget/YourWidget.jsx`
   - Changes appear instantly in browser

3. **Test with real data**:
   - Add JSON/CSV files to `public/`
   - They'll be available at `/filename.json` in dev mode

### Testing Production Build

```bash
npm run build         # Build all widgets to dist/
npm run preview       # Serve dist/ at http://localhost:4173
```

Open http://localhost:4173/preview-dist.html to test built bundles.

### Preview Files

- **`preview.html`**: Loads widgets from `src/` (dev mode, hot reload)
- **`preview-dist.html`**: Loads widgets from `dist/` (production test)

Both show all widgets on one page with embedding instructions.

---

## Creating New Widgets

### Step-by-Step Guide

1. **Create widget directory**:
   ```bash
   mkdir -p src/widgets/MyWidget
   ```

2. **Create the component** (`src/widgets/MyWidget/MyWidget.jsx`):
   ```jsx
   import React from 'react';

   export default function MyWidget({ someProp = 'default' }) {
     return (
       <div style={{ padding: '2rem', background: '#f0f0f0' }}>
         <h3>My Widget</h3>
         <p>Prop value: {someProp}</p>
       </div>
     );
   }
   ```

3. **Create the entry point** (`src/widgets/MyWidget/index.jsx`):
   ```jsx
   import React from 'react';
   import { createRoot } from 'react-dom/client';
   import MyWidget from './MyWidget.jsx';

   const rootElement = document.getElementById('my-widget-root');
   if (rootElement) {
     const someProp = rootElement.dataset.prop || 'default';
     const root = createRoot(rootElement);
     root.render(<MyWidget someProp={someProp} />);
   }

   export default MyWidget;
   ```

4. **Add to Vite config** (`vite.config.js`):
   ```javascript
   export default defineConfig({
     // ...
     build: {
       rollupOptions: {
         input: {
           // ... existing widgets
           'my-widget': resolve(__dirname, 'src/widgets/MyWidget/index.jsx'),
         },
       },
     },
   });
   ```

5. **Test in dev**:
   ```bash
   npm run dev
   ```
   Add to `preview.html`:
   ```html
   <div id="my-widget-root" data-prop="test"></div>
   <script type="module" src="/src/widgets/MyWidget/index.jsx"></script>
   ```

6. **Build**:
   ```bash
   npm run build
   ```
   Creates `dist/my-widget.js`

7. **Use in blog post**:
   ```markdown
   ---
   title: My Post
   ---

   <div id="my-widget-root" data-prop="custom"></div>
   <script type="module" src="/widgets/dist/my-widget.js"></script>
   ```

### Widget Development Best Practices

1. **Keep widgets independent**: Each widget should work standalone without depending on other widgets
2. **Use absolute paths for data**: Always `/widgets/data.json`, never `./data.json`
3. **Handle missing DOM gracefully**: Entry point should check `if (rootElement)` before mounting
4. **Configure via data attributes**: Don't hardcode values; let users customize via `data-*`
5. **Include error states**: Show user-friendly messages when data fails to load
6. **Test both dev and production**: Run `npm run preview` before committing

---

## Available Widgets

### PopulationChart
**File**: `src/widgets/PopulationChart/`
**Bundle**: `dist/population-chart.js`
**Root ID**: `population-chart-root`

Log-log scatter plot using D3.js comparing country population to unique website visits.

```html
<div id="population-chart-root"></div>
<script type="module" src="/widgets/dist/population-chart.js"></script>
```

### GeographyCharts
**File**: `src/widgets/GeographyCharts/`
**Bundle**: `dist/geography-charts.js`
**Root ID**: `geography-charts-root`

Multiple D3.js visualizations showing YouTube views and site analytics across countries.

```html
<div id="geography-charts-root"></div>
<script type="module" src="/widgets/dist/geography-charts.js"></script>
```

### Counter
**File**: `src/widgets/Counter/`
**Bundle**: `dist/counter-widget.js`
**Root ID**: `counter-widget-root`

Interactive counter demonstrating React state management.

**Configuration**:
- `data-initial`: Starting value (default: 0)
- `data-step`: Increment amount (default: 1)

```html
<div id="counter-widget-root" data-initial="10" data-step="5"></div>
<script type="module" src="/widgets/dist/counter-widget.js"></script>
```

### FollowingEyes
**File**: `src/widgets/FollowingEyes/`
**Bundle**: `dist/following-eyes-widget.js`
**Root ID**: `following-eyes-widget-root`

Animated eyes that follow the cursor around the screen.

```html
<div id="following-eyes-widget-root"></div>
<script type="module" src="/widgets/dist/following-eyes-widget.js"></script>
```

---

## Deployment

### Automated GitHub Actions Workflow

**Widgets are built automatically on push to `main` branch** via GitHub Actions workflow (`.github/workflows/build-and-deploy.yml`).

The workflow:
1. Checks out the code
2. Installs Node.js dependencies (`npm install` in `widgets/`)
3. Builds widgets (`npm run build`)
4. Installs Ruby/Jekyll dependencies
5. Builds Jekyll site (which includes the built `widgets/dist/`)
6. Deploys to GitHub Pages

**You don't need to commit `dist/` files** - they're built fresh on every deployment.

### Local Testing (Before Pushing)

While GitHub Actions builds widgets automatically, you should test locally before pushing:

```bash
cd widgets
npm run build           # Build all widgets to dist/
npm run preview         # Test built bundles locally
```

Optionally, test with Jekyll:
```bash
# In repo root
bundle exec jekyll serve
# Visit http://localhost:4000
```

**Workflow**:
1. Develop widgets with `npm run dev` (hot reload)
2. Build and test with `npm run build && npm run preview`
3. Commit widget source code changes (in `src/`)
4. Push to `main` branch
5. GitHub Actions automatically builds and deploys

### What Gets Deployed

- Jekyll processes markdown files and serves static HTML
- Built widget bundles are served as static assets from `widgets/dist/`
- When a blog post loads, the browser:
  1. Renders the static HTML from Jekyll
  2. Finds `<script type="module" src="/widgets/dist/widget.js">`
  3. Loads and executes the widget bundle
  4. Widget auto-mounts to its target div
  5. React takes over that specific div only

### GitHub Pages

The site is deployed via GitHub Pages with GitHub Actions. The workflow:
- Runs on every push to `main`
- Can be triggered manually from Actions tab
- Builds widgets fresh every time (no stale bundles)
- Only `widgets/src/` source files need to be committed

**No `.nojekyll` file needed**: The GitHub Actions workflow explicitly builds Jekyll and uploads the result. The `.nojekyll` file is only needed when you want GitHub Pages to serve raw HTML without Jekyll processing, which is not our case.

**Git ignore configuration**:
- Root `.gitignore` excludes `/_site` (Jekyll build output) and `/widgets/dist/`
- `widgets/.gitignore` also excludes `dist/` and `node_modules/`
- Both `dist/` exclusions ensure build artifacts aren't committed

### Repository Setup Steps

To enable the automated workflow on a new repository:

1. **Set GitHub Pages source to "GitHub Actions"**:
   - Go to repository Settings → Pages
   - Under "Build and deployment" → Source
   - Select **"GitHub Actions"** (NOT "Deploy from a branch")
   - This disables the automatic legacy Jekyll workflow

2. **Verify Actions permissions**:
   - Settings → Actions → General
   - Workflow permissions → "Read and write permissions"

3. **Push to `main` branch**:
   - The workflow will trigger automatically
   - Check Actions tab to monitor build progress
   - Look for "Deploy Jekyll site with widgets to Pages" workflow

**Why this matters**: GitHub's default "Deploy from a branch" setting runs Jekyll automatically but doesn't know about our widgets build step. Our custom workflow handles both widget building and Jekyll in the correct order.

---

## Critical Technical Details

### Paths and Jekyll Integration

**Never use relative paths in widgets**:
```jsx
// ❌ WRONG - breaks when embedded in blog posts
fetch('./data.json')
fetch('../public/data.json')

// ✅ CORRECT - absolute path from site root
fetch('/widgets/data.json')
fetch('/widgets/geography-data.csv')
```

**Why?** Blog posts are served from nested paths like `/blog/category/2025/01/18/post-title/`, but widgets always live at `/widgets/dist/`. Relative paths resolve from the blog post's location, not the widget's.

### Vite Base Path Configuration

`vite.config.js` must have `base: '/widgets/'` to match the directory structure. This ensures:
- Asset paths in built bundles are correct
- Dev server serves files from the right path
- Production builds work identically to dev

### Jekyll Exclusions

The `_config.yml` excludes several directories from Jekyll processing:
```yaml
exclude:
  - vendor/          # Bundler gem installations (CRITICAL - prevents build errors)
  - .bundle/         # Bundler config
  - Gemfile          # Ruby dependencies
  - Gemfile.lock     # Ruby lockfile
  - widgets/node_modules/
  - widgets/src/
  - widgets/public/
  # ... and other widget dev files
```

**Critical**: The `vendor/` exclusion prevents Jekyll from trying to process gem source files, which causes build errors like "Invalid date" errors from gem template files.

If you add new directories/files to `widgets/`, update the exclusions accordingly.

**Only `widgets/dist/` should be included** in Jekyll builds.

### GitHub Actions Workflow

The deployment workflow is in `.github/workflows/build-and-deploy.yml`. Key points:

- **Triggers**: Runs on push to `main` or manual trigger
- **Node version**: Currently set to Node 20
- **Ruby version**: Currently set to Ruby 3.2
- **Cache**: Uses npm cache based on `widgets/package-lock.json`

If you need to modify the workflow (e.g., change Node version, add build steps):
1. Edit `.github/workflows/build-and-deploy.yml`
2. Test locally first: `cd widgets && npm run build`
3. Commit and push - workflow runs automatically
4. Check Actions tab on GitHub for build status

**Important**: The workflow uses `npm install` to avoid stale lockfile issues. The `package-lock.json` file is still useful for local development consistency.

### Multiple Widget Instances

Currently, each widget type can only be used once per page (same root ID). To support multiple instances:

1. Modify `index.jsx` to find all matching elements:
   ```jsx
   const elements = document.querySelectorAll('.my-widget-root');
   elements.forEach(el => {
     const root = createRoot(el);
     root.render(<MyWidget />);
   });
   ```

2. Use classes instead of IDs in blog posts:
   ```html
   <div class="my-widget-root"></div>
   <div class="my-widget-root"></div>
   ```

### Breaking Changes to Avoid

1. **Changing widget bundle filenames**: Blog posts hardcode paths like `/widgets/dist/counter-widget.js`. Renaming breaks all posts using that widget.

2. **Changing root element IDs**: Blog posts use specific IDs. Changing `counter-widget-root` breaks all counters.

3. **Removing `dist/` from git**: Jekyll serves these as static files. Without them, widgets won't load.

4. **Changing base path in Vite**: Must match actual directory structure (`/widgets/`).

5. **Using relative paths for data**: Will break when widgets are embedded in nested blog post URLs.

### Adding Dependencies

For widget-specific dependencies:
```bash
cd widgets
npm install d3          # For data visualization
npm install framer-motion  # For animations
```

For shared utilities, add to `src/utils/` and import in multiple widgets.

### Debugging

1. **Widget doesn't appear**: Check browser console for errors. Likely causes:
   - Wrong root element ID
   - Data fetch failed (check network tab)
   - Build didn't run (`npm run build`)

2. **Data not loading**: Check absolute paths (`/widgets/data.json`)

3. **Hot reload not working**: Make sure you're using `npm run dev`, not `npm run preview`

4. **Production build differs from dev**: Clear dist and rebuild:
   ```bash
   npm run clean
   npm run build
   npm run preview
   ```

---

## Example Blog Post

```markdown
---
layout: single
title: "My Analysis"
date: 2025-01-18
categories: blog
---

Here's some text about my analysis.

## Interactive Visualization

<div id="population-chart-root"></div>
<script type="module" src="/widgets/dist/population-chart.js"></script>

You can see that countries with larger populations...

## Try This Interactive Demo

<div id="counter-widget-root" data-initial="100" data-step="10"></div>
<script type="module" src="/widgets/dist/counter-widget.js"></script>

Click the buttons to change the value!
```

---

## Quick Reference

| Task | Command |
|------|---------|
| Install dependencies | `cd widgets && npm install` |
| Start dev server | `npm run dev` |
| Build for production | `npm run build` |
| Preview production build | `npm run preview` |
| Clean build artifacts | `npm run clean` |
| Test Jekyll locally | `bundle exec jekyll serve` |

## File Locations

- Widget source: `widgets/src/widgets/`
- Built bundles: `widgets/dist/`
- Data files (JSON/CSV): `widgets/public/` → copied to `widgets/dist/`
- Blog posts: `_posts/YYYY-MM-DD-title.md`
- Jekyll config: `_config.yml`
- Vite config: `widgets/vite.config.js`
