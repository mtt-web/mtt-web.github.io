# Widget System Architecture - Quick Reference

**ALWAYS read [README.md](../README.md) for complete, detailed documentation.**

This file provides a quick overview for AI assistants. All details, best practices, and edge cases are in the root README.md.

---

## Project Structure

### Two-Stage Build System

1. **Widgets** (npm/Vite/React) → `widgets/dist/*.js`
2. **Jekyll** (Ruby/Markdown) → `_site/` (includes widgets/dist/)

### Key Directories

```
/
├── _posts/              # Jekyll blog posts (embed widgets here)
├── widgets/             # React widget build system
│   ├── src/widgets/    # Widget source code (commit this)
│   ├── dist/           # Built bundles (DO NOT commit - built by GitHub Actions)
│   ├── public/         # Static data (JSON/CSV)
│   ├── preview.html    # Dev preview (loads from src/)
│   └── package.json    # npm dependencies
├── .github/workflows/build-and-deploy.yml  # Auto-builds widgets + Jekyll
└── README.md           # COMPLETE DOCUMENTATION (read this!)
```

---

## Widget System Principles

### Independence
- Each widget is completely independent
- Lives in `widgets/src/widgets/WidgetName/`
- Has its own entry point in `vite.config.js`
- Builds to single file: `dist/widget-name.js`

### Two-File Pattern (ALWAYS follow this)

**1. `WidgetName.jsx`** - The React component
```jsx
export default function WidgetName({ config }) {
  return <div>Widget UI</div>;
}
```

**2. `index.jsx`** - Entry point (auto-mount)
```jsx
import { createRoot } from 'react-dom/client';
import WidgetName from './WidgetName.jsx';

const root = document.getElementById('widget-name-root');
if (root) {
  const config = root.dataset.config;
  createRoot(root).render(<WidgetName config={config} />);
}
export default WidgetName;
```

### Critical Path Rules

**ALWAYS use absolute paths from site root:**
```jsx
// ✅ CORRECT
fetch('/widgets/data.json')

// ❌ WRONG (breaks when embedded in nested blog posts)
fetch('./data.json')
fetch('../public/data.json')
```

**Why?** Widgets at `/widgets/dist/` are embedded in posts at `/blog/cat/2025/01/18/title/`

---

## Development Commands

### Widget Development

```bash
cd widgets

# Install (first time)
npm install

# Development (hot reload)
npm run dev              # Opens preview.html at localhost:3000

# Build for production
npm run build            # Creates dist/*.js

# Test production build
npm run preview          # Serves dist/ at localhost:4173

# Clean
npm run clean            # Removes dist/
```

### Jekyll (optional local testing)

```bash
# In repo root
bundle exec jekyll serve  # localhost:4000
```

---

## Deployment (GitHub Actions)

### Workflow: `.github/workflows/build-and-deploy.yml`

**On push to `main`:**
1. Install Node.js dependencies (`npm install` in widgets/)
2. Build widgets (`npm run build` in widgets/)
3. Install Ruby/Jekyll dependencies
4. Build Jekyll site (includes widgets/dist/)
5. Deploy to GitHub Pages

**Important:**
- Uses `npm install` (not `npm ci`) to avoid stale lockfile issues
- DO NOT commit `widgets/dist/` - built fresh every deployment
- Only commit `widgets/src/` source code
- Build artifacts ignored in both `.gitignore` files

**Repository Setup:**
- GitHub Pages source must be set to "GitHub Actions" (not "Deploy from a branch")
- This disables the default Pages Jekyll workflow in favor of our custom one

---

## Preview Pages (Keep These Updated!)

### `widgets/preview.html`
- Shows ALL widgets for development testing
- Loads from `src/` (not `dist/`)
- Used with `npm run dev`
- **Update when adding new widgets**

### `widgets/preview-dist.html`
- Shows ALL widgets from built bundles
- Loads from `dist/`
- Used with `npm run preview`
- **Update when adding new widgets**

### Update Checklist for New Widgets

When adding a widget, update:
1. ✅ `vite.config.js` - add entry point
2. ✅ `preview.html` - add widget div + script (src path)
3. ✅ `preview-dist.html` - add widget div + script (dist path)
4. ✅ README.md "Available Widgets" section

---

## Adding a New Widget (Quick Steps)

1. Create `widgets/src/widgets/NewWidget/`
2. Create `NewWidget.jsx` (component) + `index.jsx` (entry)
3. Add to `vite.config.js`:
   ```js
   'new-widget': resolve(__dirname, 'src/widgets/NewWidget/index.jsx')
   ```
4. Update both preview files
5. Test: `npm run dev`
6. Build: `npm run build`
7. Test built: `npm run preview`

**Full instructions in README.md**

---

## Embedding in Blog Posts

```markdown
---
layout: single
title: My Post
categories: blog
---

Some text...

<div id="widget-name-root" data-config="value"></div>
<script type="module" src="/widgets/dist/widget-name.js"></script>

More text...
```

---

## Current Widgets (as of Oct 2025)

| Widget | File | Bundle | Root ID |
|--------|------|--------|---------|
| PopulationChart | `src/widgets/PopulationChart/` | `dist/population-chart.js` | `population-chart-root` |
| GeographyCharts | `src/widgets/GeographyCharts/` | `dist/geography-charts.js` | `geography-charts-root` |
| Counter | `src/widgets/Counter/` | `dist/counter-widget.js` | `counter-widget-root` |
| FollowingEyes | `src/widgets/FollowingEyes/` | `dist/following-eyes-widget.js` | `following-eyes-widget-root` |

---

## Common Pitfalls to Avoid

### 1. Relative Paths in Widgets
❌ Never use `./` or `../` for data fetching
✅ Always use absolute `/widgets/` paths

### 2. Forgetting to Update Previews
When adding a widget, update both `preview.html` and `preview-dist.html`

### 3. Committing dist/
Don't commit `widgets/dist/` - it's built by GitHub Actions

### 4. Changing Bundle Filenames
Blog posts hardcode paths like `/widgets/dist/counter-widget.js`
Renaming breaks all existing blog posts using that widget

### 5. Changing Root Element IDs
Blog posts use specific IDs like `counter-widget-root`
Changing the ID breaks all existing instances

### 6. Modifying Vite Base Path
Must stay `/widgets/` to match directory structure

---

## Jekyll Configuration Notes

### Excluded from Jekyll Build
- `vendor/` ← **CRITICAL**: Prevents Jekyll from processing gem files (causes "Invalid date" errors)
- `.bundle/`
- `Gemfile`, `Gemfile.lock`
- `widgets/node_modules/`
- `widgets/src/`
- `widgets/public/`
- `widgets/package*.json`
- `widgets/vite.config.js`
- `widgets/preview*.html`
- `widgets/README.md`

### Included in Jekyll Build
- `widgets/dist/` ← Only this directory is served

### No `.nojekyll` File
We WANT Jekyll processing (the whole point is Jekyll + widgets)
GitHub Actions explicitly builds Jekyll

---

## Documentation Hierarchy

1. **README.md (ROOT)** ← Primary source of truth, comprehensive
2. **This file** ← Quick reference for AI assistants
3. **`widgets/README.md`** ← Points to root README
4. **`.github/workflows/README.md`** ← Workflow-specific docs

**When in doubt, consult README.md for complete information.**

---

## Key Configuration Files

- **`widgets/vite.config.js`** - Widget build config, entry points
- **`widgets/package.json`** - npm dependencies and scripts
- **`_config.yml`** - Jekyll config (exclusions, theme, plugins)
- **`.github/workflows/build-and-deploy.yml`** - Build and deploy workflow
- **`widgets/.gitignore`** - Ignores `dist/` and `node_modules/`
- **`.gitignore`** - Ignores `/_site` and `/widgets/dist/`

---

## Future-Proofing Reminders

1. **Keep widgets independent** - no cross-dependencies
2. **Maintain two-file pattern** - component + entry point
3. **Update preview pages** - when adding/removing widgets
4. **Document in README.md** - not just in code comments
5. **Test both dev and production** - `npm run dev` and `npm run preview`
6. **Use absolute paths** - critical for embedded widgets
7. **Don't commit build artifacts** - GitHub Actions handles it

---

## Tech Stack Summary

- **Jekyll** 4.x - Static site generator
- **Ruby** 3.2 - Jekyll runtime
- **Node.js** 20 - Widget build tools
- **Vite** 6.x - Widget bundler
- **React** 18.x - Widget framework
- **D3.js** 7.x - Data visualization (some widgets)
- **GitHub Actions** - CI/CD pipeline

---

**Remember: This is a quick reference. See [README.md](../README.md) for complete documentation, best practices, debugging tips, and detailed explanations.**
