# UI styles

FinGuitar has two looks that share one brand. The shop is bright and white ("Showroom"). The admin area, including the sign-in page and the chat, is dark ("Backstage"). Both use Tailwind CSS v4, built when the app is built. Nothing is loaded from a CDN, so the app works offline.

## The two themes

| Area | Theme | Background | Text | Accent |
|---|---|---|---|---|
| Storefront | Showroom (white) | `#FFFFFF` | `#14161A` | Solid red-orange `#B4231B` for buttons and focus rings |
| Admin panel, sign-in, chat | Backstage (dark) | `#0B0D10` | `#E6E8EB` | Amber `#F59E0B` for buttons, focus rings and the active menu item |

The sunburst gradient (amber to red) appears only as decoration on the home-page hero band. **No text sits on the gradient.** The hero text is on a solid white panel on top of it.

Each page picks its theme with a class on the `<body>`: `theme-showroom` or `theme-backstage`. The same component class (for example `.btn-primary`) looks right in both, because the theme class decides the colours.

### Contrast

Text and key controls meet WCAG 2.1 AA (4.5:1 for text, 3:1 for focus rings):

- Showroom primary button: white on `#B4231B`, about 6.6:1.
- Backstage primary button: `#0B0D10` on `#F59E0B`, about 9:1.
- Danger button: white on `#E11D48`, about 4.7:1 (white on the lighter `#F43F5E` would fail, so the lighter rose is used only for borders).
- Every focus ring uses the accent of its theme and stays visible on its background.

Order status is never shown by colour alone: every badge has a text label.

## Fonts

Both fonts are self-hosted in `wwwroot/fonts/` as `woff2` files, with their licence files (SIL Open Font License):

- **Fraunces** for display headings and the wordmark.
- **Inter** for all body text and the whole admin panel.

## Component classes

Defined in `Styles/app.css` with `@apply`:

| Class | Use |
|---|---|
| `.btn`, `.btn-primary`, `.btn-ghost`, `.btn-danger` | Buttons |
| `.card` | Bordered panel |
| `.input` | Text boxes, selects |
| `.badge` and `.badge-pending`, `-dispatched`, `-delivered`, `-cancelled`, `-neutral`, `-chat`, `-twice`, `-ok`, `-fail` | Small status labels |
| `.data-table` | Tables (sticky header, hover rows, `.num` for right-aligned numbers) |
| `.chat-bubble-user`, `.chat-bubble-assistant`, `.proposal-card` | Chat |

**Class names are always written in full.** Tailwind finds classes by reading the source files, so a name built at run time (such as `"badge-" + status`) would be missing from the CSS. Order status badges use a C# `switch` (`UiHelpers.StatusBadge`) that returns whole strings like `"badge badge-pending"`. Tailwind is told to scan `Pages/**/*.cshtml`, `wwwroot/js/**/*.js`, `Services/**/*.cs` and `Chat/**/*.cs`.

## Responsive and accessible

- Layouts work from 375 px phone width up, with no sideways scrolling. On phones the admin sidebar becomes a top bar with a Menu button.
- Every page has a "Skip to content" link, and every control can be reached with the keyboard.
- Animations (card lift, thinking dots) stop when the visitor prefers reduced motion.

## Rebuilding the CSS

Run these in `src/FinGuitar.Web` (Node 22 or newer):

```
npm ci
npm run css:build     # one build, writes wwwroot/css/app.css
npm run css:watch     # rebuilds while you edit
```

`wwwroot/css/app.css` is generated and is not committed. Docker builds it in its own stage, so Docker users never need Node.
