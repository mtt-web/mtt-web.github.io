# GitHub Actions Workflows

## Jekyll Deployment with Widgets

**File**: `build-and-deploy.yml`

This workflow automatically builds React widgets and deploys the Jekyll site to GitHub Pages.

### Workflow Steps

1. **Checkout code**
2. **Setup Node.js** - Install Node 20 and cache npm dependencies
3. **Install widget dependencies** - Run `npm ci` in widgets/
4. **Build widgets** - Run `npm run build` to create dist/ bundles
5. **Setup Ruby** - Install Ruby 3.2 and bundle dependencies
6. **Setup Pages** - Configure GitHub Pages deployment
7. **Build Jekyll** - Build site with widgets/dist/ included
8. **Upload artifact** - Package built site
9. **Deploy to Pages** - Deploy to GitHub Pages environment

### Triggers

- Automatically on push to `main` branch
- Manually via Actions tab (workflow_dispatch)

### Requirements

To use this workflow, ensure:

1. **GitHub Pages source is set to "GitHub Actions"**:
   - Go to Settings → Pages → Source
   - Select "GitHub Actions" (NOT "Deploy from a branch")
   - This disables the default GitHub Pages Jekyll workflow

2. **GitHub Actions permissions** are set correctly:
   - Settings → Actions → General → Workflow permissions → Read and write permissions

**Important**: Setting source to "GitHub Actions" automatically disables any legacy "pages-build-deployment" workflow that GitHub creates for branch-based deployment.

### Customization

Edit `build-and-deploy.yml` to:
- Change Node/Ruby versions
- Add additional build steps
- Modify deployment settings
- Add environment variables

See main [README.md](../../README.md) for full documentation.
