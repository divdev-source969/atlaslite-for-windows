# AtlasLite for Windows 10

A fast, small, dark, rounded web browser for Windows 10 (32-bit or 64-bit).

**Status: first version. It is compiled by GitHub (not tested on a real PC by the author), so expect to report small bugs.**

## What you get

- **Real web support: HTML, CSS and JavaScript.** Pages are drawn by Microsoft Edge WebView2 (Chromium), so sites work like in Edge/Chrome (YouTube, Gmail, GitHub, ...).
- **Rounded window**, custom title bar with tabs, dark and light mode, accent colours.
- **Built-in ad blocker**: blocks ad and tracker requests, hides common ad boxes, counts what it blocked (shield button). Add your own rules in Settings → *Edit list*.
- **New-tab page** with clock, search, shortcuts you can add/remove, and a **wallpaper** (9 presets, or your own image, with dimming and blur).
- **Settings panel** (`atlas://settings`, or menu → *Settings & wallpaper*).
- **Low-memory mode**: tabs you have not used for 90 seconds are frozen, background tabs run at low memory priority, Chromium starts in low-memory mode, and the app trims its own memory when minimised.
- 32-bit build (`x86`): runs on 32-bit and 64-bit Windows 10, with a smaller footprint than a 64-bit process.

## Honest limits

- **RAM:** the browser engine is Chromium. AtlasLite itself is tiny, but each open site costs what it costs in Edge. Expect roughly 150-300 MB for one simple tab (the WebView2 engine processes are shared with nothing else), more for heavy sites. It is *lighter than Chrome with many tabs* thanks to tab freezing, not lighter than a tiny engine. It cannot be tiny and run modern JavaScript sites at the same time.
- **No Chrome extensions.** The built-in ad blocker and settings replace them. (WebView2 does not run the Chrome Web Store extensions in this setup.)
- **Rounded corners on Windows 10** are made by clipping the window, so the edge is very slightly less smooth than on Windows 11, where Windows rounds the window itself.
- **No bookmarks manager, history page or password manager yet.** Shortcuts on the new-tab page are the quick-access feature.
- Needs the **WebView2 Runtime**. It is already on up-to-date Windows 10/11. If AtlasLite says it is missing: https://go.microsoft.com/fwlink/p/?LinkId=2124703
- Ad blocking works on requests (not full filter-list syntax). It is good against common ad/tracker domains, not as complete as uBlock Origin.

## Shortcuts

Ctrl+T new tab · Ctrl+W close tab · Ctrl+L or F6 address bar · F5 / Ctrl+R reload · Alt+Left/Right back/forward · Ctrl+Tab / Ctrl+Shift+Tab switch tabs · Ctrl+, settings · F11 full screen · F12 developer tools

## Build with GitHub (no Visual Studio, no installs)

1. Create a new GitHub repository and upload everything from this folder (drag-and-drop skips hidden folders).
2. In the repo click **Add file → Create new file**, name it `.github/workflows/build.yml` (typing the slashes makes the folders), paste the contents of the `build.yml` from this folder, and commit.
3. Open the **Actions** tab → **Build AtlasLite for Windows** → **Run workflow**.
4. When it is green, open the run and download **AtlasLite-windows** from *Artifacts*. Unzip it **fully** (right-click → Extract All) and double-click `AtlasLite.exe`. Keep the `Pages` and `Data` folders next to it.

The workflow runs the ad-blocker tests first, then builds.

## Where your data lives

- Settings, shortcuts, your wallpaper and your block list: `%APPDATA%\AtlasLite`
- Browser profile (cookies, cache): `%LOCALAPPDATA%\AtlasLite\WebView2Data`

Delete those folders to reset everything.

## Code map

| File | Job |
|---|---|
| `src/MainForm.cs` | window, rounded corners, tabs, toolbar, menu, settings messages |
| `src/BrowserTab.cs` | one WebView2 tab: ad blocking, dark mode, freezing |
| `src/Controls.cs` | tab strip / title bar, buttons, address bar (all drawn by us) |
| `src/AdBlock.cs` | rule engine (unit-tested in `tests/`) |
| `src/AppSettings.cs` | settings file + JSON for the pages |
| `Pages/` | new-tab page and settings page (plain HTML/CSS/JS) |
| `Data/blocklist.txt` | starter block list |

## Licence

BSD 3-clause, see `LICENSE`. Uses Microsoft Edge WebView2 (separate Microsoft licence, installed on your PC).
