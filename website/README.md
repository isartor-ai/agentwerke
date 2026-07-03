# Agentwerke marketing website

The public marketing site for **Agentwerke by Isartor AI**, the
**Governed Lights-Out Software Factory**.
All product copy is derived from the repository documentation
([README](../README.md), [architecture](../docs/architecture.md),
[security-model](../docs/security-model.md),
[deployment-auth-data-residency](../docs/deployment-auth-data-residency.md),
[functional-specification](../docs/functional-specification.md)).

## Stack

Static and framework-free: semantic HTML, one CSS file, and a small
progressive-enhancement script. No build step, no toolchain, no runtime — the
site is ultra-fast and trivially cacheable on any static/CDN host.

**Brand.** Colors, typography, the `[]` logo mark, and the sharp-cornered
industrial styling mirror the Agentwerke product UI (`web/src/index.css`): cyan
`#00dce5` brand + lime `#c3f400` accent on a near-black `#0e0e0f` canvas, with
**Inter** (body) and **JetBrains Mono** (marks, labels, chips). Those two fonts
are loaded from Google Fonts — the only external request. Self-host them under
`assets/` and swap the `<link>` in `index.html` if you need zero third-party
requests.

```
website/
├── index.html            # the full single-page site (all sections)
├── robots.txt
├── sitemap.xml
├── assets/
│   ├── styles.css        # design system: dark-first, light theme, responsive
│   ├── main.js           # theme toggle, mobile menu, scroll-reveal, year
│   ├── favicon.svg
│   └── og-image.svg      # Open Graph / social preview
└── README.md
```

### Features

- **Dark-first premium theme** with an accessible light mode. The toggle honors
  `prefers-color-scheme` on first visit and persists the user's choice.
- **Responsive** from ~320px to widescreen; the comparison table collapses to
  stacked rows on mobile and the nav becomes a burger menu.
- **Accessible**: semantic landmarks, a skip link, visible focus states, ARIA
  roles on the comparison table and diagram, and `prefers-reduced-motion`
  support.
- **SEO + Open Graph**: title/description, canonical URL, OG and Twitter cards,
  and `SoftwareApplication` JSON-LD.

## Local development

No dependencies required — just serve the folder over HTTP so relative asset
paths and the JSON-LD resolve correctly.

```bash
cd website

# any static server works; pick one:
python3 -m http.server 5173        # → http://localhost:5173
# or
npx serve .                        # → http://localhost:3000
```

Opening `index.html` directly via `file://` mostly works, but a local server is
recommended (it matches production path resolution).

## Deployment

The site is fully static. Deploy `website/` to any static host or CDN.

### GitHub Pages (configured)

`.github/workflows/pages.yml` publishes `website/` to GitHub Pages on every push
to `main` that touches `website/**` (and on manual `workflow_dispatch`). It uses
the official Actions pipeline (`configure-pages` → `upload-pages-artifact` →
`deploy-pages`) — no branch juggling, no `gh-pages` branch.

One-time setup:

1. **Repo Settings → Pages → Build and deployment → Source = "GitHub Actions".**
2. **Custom domain.** `website/CNAME` pins `agentwerke.de`, so it ships in every
   deploy. Configure DNS at your registrar:
   - Apex `agentwerke.de` → four `A` records to GitHub Pages:
     `185.199.108.153`, `185.199.109.153`, `185.199.110.153`, `185.199.111.153`
     (add the matching `AAAA` records for IPv6 if desired, or an `ALIAS`/`ANAME`
     if your DNS supports it at the apex).
   - `www.agentwerke.de` → `CNAME` to `<org-or-user>.github.io`.
3. In Settings → Pages, tick **Enforce HTTPS** once the certificate is issued.

Until DNS resolves to GitHub Pages, the custom domain won't serve — that's
expected for a pinned custom domain.

### Other hosts

- **Netlify / Vercel / Cloudflare Pages** — set the publish/output directory to
  `website` and leave the build command empty.
- **S3 + CloudFront / nginx** — copy the folder to the web root.

For any non-Pages host, point the `agentwerke.de` apex (and `www`) DNS at that host
and enable HTTPS. (`website/CNAME` is GitHub-Pages-specific and is ignored by
other static hosts.)

### Before launch — founder review checklist

Copy is grounded in the repo docs; a few items need a human decision. Search the
source for `REVIEW:` comments. Currently:

- **Contact / access address** — `mailto:hello@agentwerke.de` is a placeholder used
  by the "Request access" and "Contact" links. Replace with the real address or
  an access/demo form.
- **Docs link target** — "Read the docs" points at `/tree/main/docs` in GitHub.
  Repoint to `docs.agentwerke.de` once a hosted docs site exists.
- **GitHub repo link** — repository links point to `isartor-ai/agentwerke`.
  Keep them aligned if the organization changes the public repo location again.
- **OG image** — `og-image.svg` is vector. Some social scrapers prefer a
  1200×630 raster; export a PNG and update the `og:image` / `twitter:image`
  URLs if broader preview coverage is needed.

No unsupported product claims (e.g. specific compliance certifications) are made
anywhere on the site. If you add any, cite the source of record.
