# Docs Site Deployment

The documentation site source lives in `docs.autofac.de/`. It builds to static files and is designed for GitHub Pages.

## Local commands

```bash
cd docs.autofac.de
npm ci
npm run dev
npm run build
npm run preview
```

## GitHub Pages workflow

The workflow at `.github/workflows/docs-pages.yml`:

1. Runs on changes to `docs.autofac.de/**` and the workflow file.
2. Installs docs dependencies with `npm ci`.
3. Builds the site with `npm run build`.
4. Uploads `docs.autofac.de/.vitepress/dist`.
5. Deploys the artifact to GitHub Pages on non-pull-request events.

Pull requests build the docs but do not deploy them.

## Repository settings

In GitHub repository settings:

1. Open Pages.
2. Set Build and deployment source to GitHub Actions.
3. Configure the custom domain as `docs.autofac.de`.
4. Enable HTTPS when GitHub allows it.

GitHub's Pages settings or API must own the custom domain setting. A `CNAME` file in the artifact is useful documentation, but for an Actions-backed Pages deployment it does not by itself configure the repository custom domain.

## DNS

For the `docs.autofac.de` subdomain, create a DNS `CNAME` record that points to the GitHub Pages default domain for the organization or user, normally:

```text
isartor-ai.github.io
```

Do not include the repository name in the DNS target.

DNS changes can take time to propagate. Verify the record before enabling production links.

## Build output

VitePress writes generated files to:

```text
docs.autofac.de/.vitepress/dist
```

That directory is ignored by git and should not be committed.

## Release checklist

- `npm ci` passes in `docs.autofac.de`.
- `npm run build` passes with no broken internal links.
- Local preview loads home, manual, admin, developer, and reference pages.
- GitHub repository Pages source is GitHub Actions.
- Custom domain is configured in repository settings.
- DNS points `docs.autofac.de` to the GitHub Pages default domain.
- HTTPS is enforced after the certificate is available.
