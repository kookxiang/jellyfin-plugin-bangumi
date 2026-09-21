# Bangumi settings component

Jellyfin loads `<jellyfin-plugin-bangumi></jellyfin-plugin-bangumi>` from its existing plugin configuration route. The element owns an open Shadow DOM, so its styles and native controls are independent of Jellyfin's legacy `emby-*` upgrades. The `--jf-*` theme properties inherit across the boundary. Emby continues to use its original frontend.

## Development

Requires Node.js 22.12+ and npm.

```sh
cd web
npm ci
npm run dev
```

Open `/test/preview.html` on the printed local URL. This is a **mock API preview**, not a server configuration page. It deliberately applies hostile document CSS to verify isolation, supports light/dark themes, and has a browser regression button covering saving, validation, user menus, regex generation, dialogs and lifecycle. No real server is contacted. Add `?production` to verify the built bundle instead of development modules.

```sh
npm run lint
npm run format:check
npm test
npm run build
```

The .NET project builds the frontend incrementally and embeds `dist/bangumi.js` as `Jellyfin.Plugin.Bangumi.Configuration.Main.js`. The single ES module contains its CSS, HTML and components. It uses Jellyfin’s existing Material Icons font and injected `ApiClient` / `Dashboard`; no mock data or preview assets are included. `dotnet build -p:SkipWebBuild=true` is available when the web bundle has already been built. Generated bundles and node_modules are not committed.

CI builds and tests the frontend first, then passes `-p:SkipWebBuild=true` to .NET test/publish so the same bundle is embedded without another npm invocation. The project reports a clear error if the prebuilt bundle is missing. For a local release package:

```sh
npm ci
npm run build
npm test
dotnet publish ../Jellyfin.Plugin.Bangumi -c Release -p:SkipWebBuild=true -o ../publish
```

The installed plugin serves the embedded module through `configurationpage?name=Plugin.Bangumi.Configuration.Script`. Only `test/preview.js` injects mock services; open Jellyfin’s plugin configuration page to use real accounts, settings and media-library APIs. The preview server is not required by the installed plugin.

## Live development inside Jellyfin

Build/install the plugin once with `dotnet build -c Debug`. Set this environment variable **on the Jellyfin server process** before starting it:

```sh
export BANGUMI_WEB_DEV_SERVER=http://127.0.0.1:8765
```

Start `npm run dev` in `web/`, then open the normal Bangumi settings page in Jellyfin. Only builds with the `DEBUG` constant use this variable. With it unset, Debug uses the embedded bundle too; Release always uses the embedded bundle. The same switch covers legacy tool entry pages. Invalid dev URLs produce a configuration error instead of silently using a stale bundle.

The browser loads TypeScript, templates and styles directly from Vite, with real Jellyfin APIs and accounts. Vite reloads the page on source changes (custom elements are not replaced in place), so frontend edits need no C# rebuild. Unsaved form edits may be lost on reload. Changes to C# still require rebuilding/restarting Jellyfin. Restart Jellyfin and reload the page when enabling/disabling this mode.

For another device, use an address reachable **from the browser** for `BANGUMI_WEB_DEV_SERVER` and start Vite with `npm run dev -- --host 0.0.0.0`. If Jellyfin is not accessed via localhost/127.0.0.1, set `BANGUMI_JELLYFIN_ORIGIN` on the **Vite process** to its exact origin (e.g. `http://192.168.1.10:8096`) to allow cross-origin module loading. An HTTPS Jellyfin page requires an HTTPS dev server to avoid mixed-content blocking.

Oxlint checks frontend correctness with warnings treated as failures. Run `npm run lint:fix` for safe automatic fixes. Oxfmt owns formatting: run `npm run format` to format the frontend or `npm run format:check` to check it. Generated `dist/`, dependencies and the npm lockfile are excluded from formatting; import sorting is not enabled. CI runs both checks before building.

## Responsibilities

- `pages/`: Jellyfin configuration and legacy tool-route entry HTML, embedded directly by the .NET project with stable resource names. All Jellyfin frontend files live in `web/`; server `Tools/` folders contain backend code only.

- `src/main.ts`: custom-element registration, mounting, host page events and cleanup. Repeated script loads are safe; an evicted page mounts again via `connectedCallback`.
- `src/host.ts`: Jellyfin API access, stale-request invalidation and native dialogs. A future Emby adapter belongs here, not in the form components.
- `src/settings.html`: existing settings and tools markup, preserving field IDs and descriptions.
- `src/controller.ts`: migrated configuration, OAuth, archive, regex and media-library interactions.
- `src/configuration.ts`: typed configuration merge that preserves undisplayed fields.
- `src/layout.css`, `theme.css`, `host-icons.css`: layout, native control appearance and host icon references, all inside the shadow tree.

Native inputs remain in the same shadow-tree form, so browser validation, label association and submission need no ElementInternals wrapper or duplicated field state. API/configuration data is not interpolated into component templates. On page hide, global listeners are removed and late API results are invalidated; on disconnection, page listeners are also removed. OAuth popup completion still uses the existing host protocol.

## Checkbox rows

`<bangumi-checkbox>` owns the rounded setting-row layout and checked, mixed,
focus and disabled appearance in its own Shadow DOM. Its `control` and `label`
slots accept a native checkbox and an explicitly associated label. They remain
in the parent form tree, so `.checked`, `.disabled`, Space, change events and
configuration serialization retain native behavior. Use `aria-labelledby` for
the short title and `aria-describedby` for the optional help text. No mirrored
component state or synthetic click handling is needed.

## Action buttons

`<bangumi-button variant="primary|secondary|quiet|danger">` styles a slotted
native `<button>`. Add `icon` for a square icon-only action and give the button
an `aria-label`. The host uses `display: contents`: native submission, disabled,
visibility and existing event listeners still belong to the actual button.
Navigation tabs retain their separate selection styles. Save displays a busy
label and prevents repeated submission while the request is running.

### Section navigation

`<bangumi-navigation>` owns the sidebar presentation and wraps native navigation buttons. The controller remains the single source of truth for section changes and updates `aria-current="page"` alongside the visible panel. Desktop uses a sticky rail with a tinted pill selection; at 900px and below the entries scroll horizontally without wrapping. Native Tab / Enter / Space behavior is preserved.

### Selects and text fields

`<bangumi-select>` wraps a native single select, retaining its ID, options, configuration value and change events. Its shadow tree renders a combobox and a themed listbox using the Popover API (top layer, light dismissal). Arrow keys, Home/End, Enter/Space, Escape, Tab and prefix typing work without opening the OS picker. Option mutations refresh automatically; call `refresh()` after assigning `.value` in code. Disconnecting removes listeners and observers. Text inputs and textareas share the outlined, rounded field style in `theme.css`.

## TypeScript and tools

Production source and Vite configuration use TypeScript. `npm run build` runs `tsc --noEmit` before bundling; `npm run typecheck` checks types independently. The migrated settings controller currently uses non-strict checking; new tool responses and service boundaries have explicit interfaces.

`src/tools/index.ts` owns the tool catalogue. Each tool is a custom element in its own TypeScript module; `tool.ts` provides API access, busy state and feedback. Add a component and catalogue entry to introduce another tool. Tool state lives outside configuration collection, and the tools section has no global Save action. Old Jellyfin tool URLs load the same components through thin compatibility shells. Emby is unchanged.

`<bangumi-checkbox-group aria-label="…">` groups related checkbox rows into one rounded surface with separators. It only controls presentation; native input state and label behavior stay with each checkbox.

`<bangumi-segmented-select>` presents a short single-select list as a pill-shaped native radio group. It shares the select value / `refresh()` contract with `<bangumi-select>` and retains native keyboard navigation.

Select options can provide `data-description="…"` for secondary text shown only in the expanded listbox. The trigger retains the option title. Popovers use a 140ms entrance and 120ms exit fade/slide, with discrete display/overlay transitions, and respect reduced-motion preferences. The trigger is a native popover invoker so repeat clicks toggle correctly alongside light-dismiss.

Icons use Jellyfin’s existing Material Icons font with ligature names. `host-icons.css` only bridges typography into shadow roots; production includes no icon paths or font assets. The standalone preview imports the matching iconfont as a development-only host fixture.

Directory numbering preview lives in `components/episode-preview.ts`; editor layout lives in `media-config.css`. The read-only `MediaLibrary/Preview` endpoint samples an indexed episode and detects its number once using the saved parser settings. Offset and correction edits calculate immediately in the frontend without requests; only “换一集” samples again. This previews numbering arithmetic, not the existence of a matching Bangumi episode, and never writes configuration or metadata.

Plugin section changes use `history.pushState`, preserving Jellyfin route parameters and history metadata. Tool detail URLs add `tool=duplicates`, `tool=fix-metadata`, or `tool=missing-id`; reload restores the tool but does not rerun scans. Browser back/forward restores the matching section/tool. The tool back button uses its preceding catalogue entry when available; direct links return to the catalogue without leaving plugin settings.
