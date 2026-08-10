---
applyTo: "src/Cosmorph.Web/**/*.ts,src/Cosmorph.Web/**/*.tsx,src/Cosmorph.Web/**/*.css,src/Cosmorph.Web/**/*.html"
---

# Cosmorph browser spectator instructions

The Observatory is a browser-first spectator application. It must make the state of a living world legible and interesting from a fully zoomed-out spherical view.

## Client boundary

- Use React with strict TypeScript and Three.js.
- Keep simulation authority, AI calls, Azure SDKs, credentials, and storage access on the server.
- Fetch versioned spectator DTOs from same-origin API endpoints.
- Start with snapshot plus cursor-based event polling and conditional requests. Interpolate visual change locally.
- Slow or pause polling when the tab is hidden. Recover from missed events by requesting from the last committed sequence.
- Do not add SignalR, WebSockets, a global state framework, or a design system until measured requirements justify them.

## Planet rendering

- Render a spherical, rotatable planet with continuous orbit-to-regional zoom.
- Generate a DataTexture or equivalent GPU-friendly texture from the versioned cell grid.
- Keep the authoritative data encoding separate from its color palette.
- At maximum zoom-out, encode:
  - base hue for biome;
  - saturation for ecological vitality;
  - redundant texture or pattern for drought, disease, fire, flood, or other dominant stress;
  - restrained animation for current change such as storms, migration, blooms, or spreading damage;
  - optional outlines or pulses for selected regions and significant events.
- Never encode important state through color alone. Provide a color-blind palette, texture redundancy, labels, and a visible legend.
- Prefer a cartoon storybook-atlas style: simplified land masses, outlined coasts, soft atmosphere, chunky clouds, low-detail silhouettes, and controlled animation.
- Preserve a stable camera and avoid effects that cause motion sickness. Respect prefers-reduced-motion.

## Spectator experience

The first vertical slice needs:

- A world selector that cannot leak private-world existence.
- World age, season, chapter, health summary, and last update time.
- A clickable Chronicle feed.
- Selecting an event rotates the globe toward its location without forcing a zoom.
- Play/pause cinematic tour mode.
- Overlay controls for default condition, biome, vitality, climate, population pressure, and recent change.
- A concise explanation panel answering what a color or texture means from structured state, without making a model call.

Do not implement free-form spectator chat or direct influence in the initial core milestone. Future interaction must use structured, rate-limited objects and server-side authorization.

## Performance and quality

- Maintain smooth interaction on a typical integrated-GPU laptop and degrade gracefully on mobile.
- Avoid React re-renders inside the animation loop. Keep Three.js objects behind focused components and update GPU resources deliberately.
- Dispose textures, geometries, materials, listeners, and animation handles.
- Use instancing and level-of-detail before increasing object counts.
- Validate DTOs at the network boundary.
- Provide loading, stale, empty, disconnected, and corrupt-state experiences.
- Test data-to-color and data-to-pattern mappings as pure functions.
- Include accessible non-canvas summaries for important world state and events.

