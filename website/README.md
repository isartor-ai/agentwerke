# autofac.de — marketing website

The public marketing site for **Autofac**, the governed AI software factory.
All product copy is derived from the repository documentation
([README](../README.md), [architecture](../docs/architecture.md),
[security-model](../docs/security-model.md),
[deployment-auth-data-residency](../docs/deployment-auth-data-residency.md),
[functional-specification](../docs/functional-specification.md)).

## Stack

Zero-dependency, static, and framework-free: semantic HTML, one CSS file, and a
small progressive-enhancement script. No build step, no toolchain, no runtime.
This keeps the site ultra-fast, trivially cacheable on any static/CDN host, and
free of supply-chain surface — a deliberate fit for an enterprise/security
audience.

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

The site is fully static. Deploy `website/` to any static host or CDN:

- **Netlify / Vercel / Cloudflare Pages** — set the publish/output directory to
  `website` and leave the build command empty.
- **GitHub Pages** — publish the `website/` subfolder.
- **S3 + CloudFront / nginx** — copy the folder to the web root.

Point the `autofac.de` apex (and `www`) DNS at the host and enable HTTPS.

### Before launch — founder review checklist

Copy is grounded in the repo docs; a few items need a human decision. Search the
source for `REVIEW:` comments. Currently:

- **Contact / access address** — `mailto:hello@autofac.de` is a placeholder used
  by the "Request access" and "Contact" links. Replace with the real address or
  an access/demo form.
- **Docs link target** — "Read the docs" points at `/tree/main/docs` in GitHub.
  Repoint to `docs.autofac.de` once a hosted docs site exists.
- **OG image** — `og-image.svg` is vector. Some social scrapers prefer a
  1200×630 raster; export a PNG and update the `og:image` / `twitter:image`
  URLs if broader preview coverage is needed.

No unsupported product claims (e.g. specific compliance certifications) are made
anywhere on the site. If you add any, cite the source of record.
