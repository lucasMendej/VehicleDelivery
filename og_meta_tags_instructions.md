# Directive: Required Open Graph (OG) Meta Tags for Web Pages

To ensure links share cleanly in chat platforms (e.g., Microsoft Teams, Slack, WhatsApp) and social channels, **every public or entry-level HTML page must contain valid Open Graph metadata**.

---

## 1. Required Meta Tags Checklist

Include the following tags inside the `<head>` element of the document:

```html
<!-- Primary Metadata -->
<title>Page Title (50-60 characters)</title>
<meta name="description" content="Clear, concise summary of the page context." />

<!-- Open Graph Tags -->
<meta property="og:title" content="Descriptive Page Title" />
<meta property="og:description" content="Engaging summary of what the user will find on this page (100-150 chars)." />
<meta property="og:image" content="https://your-domain.com/static/og-image.png" />
<meta property="og:url" content="https://your-domain.com/current-path" />
<meta property="og:type" content="website" />
<meta property="og:site_name" content="Application or Brand Name" />

<!-- Fallback / Twitter Card Tags -->
<meta name="twitter:card" content="summary_large_image" />
<meta name="twitter:title" content="Descriptive Page Title" />
<meta name="twitter:description" content="Engaging summary of what the user will find on this page." />
<meta name="twitter:image" content="https://your-domain.com/static/og-image.png" />
```

---

## 2. Requirements for Optimal UX & Rendering

* **Image Accessibility:** The `og:image` URL **must be publicly accessible** via HTTPS without authentication, even if the main application page requires a login.
* **Absolute URLs:** Always use absolute paths for images (`https://...`), never relative paths (`/assets/...`).
* **Image Dimensions:**
  * Recommended resolution: **1200 x 630 px** (1.91:1 ratio).
  * Minimum resolution: **600 x 315 px**.
* **Protected/Authenticated Pages:** Ensure the HTML `<head>` section is served on the initial request prior to client-side auth redirects so scrapers can read the metadata.