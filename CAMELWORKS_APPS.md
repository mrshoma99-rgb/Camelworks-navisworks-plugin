# The apps, as products

The suite's shape is in `CAMELWORKS_DESIGN.md`. This document is the other half: each app
treated as a product, not a binder of tools — audited tab by tab against one checklist, with
what was found wrong and what was changed written down. The per-app audits live in
[`docs/apps/`](docs/apps/), one file per app, and are part of the definition of done: an app
without its audit has not been through the pass.

## The checklist every app is held to

Asked of every tab, in the voice of the person who opens it — a BIM coordinator or BIM manager,
mid-week, on a real federation:

1. **The job.** Who opens this, at what moment of their week, to do what? A tab that cannot
   answer in one sentence is two tabs or none.
2. **Missing functionality.** What does that person need *next* that this tab dead-ends on?
   Where the next step exists elsewhere in the suite, there is a hand-off — a button that
   carries the context and jumps there — not a sentence telling them where to walk.
3. **Hostile inputs.** No property, tab or set name is typed from memory: the model already
   knows every name it carries, and `Views/Pickers.cs` reads them once, bounded, and offers
   them. Paths are suggested. Derivable defaults are derived and state themselves.
4. **Useful data.** The landing data answers the persona's first question: counts where they
   would count by hand, ages where staleness matters, names where ids would be shown. Anything
   that ends up in an email is pickable and copyable.
5. **Integration.** Where another app's output is this tab's input, the dependency is visible —
   the status strip or the surface says where that input stands, with the jump — never implicit.

## The two mechanisms that make a window an app

- **The status strip** (`Surfaces.Status`, drawn by `AppWindow` under the tab strip): where the
  app's workflow stands, in clickable figures, on every tab, refreshed after every run. Clash
  Manager says when tests last ran and what the board holds; Issue Link says what was last
  issued; Model Audit says what is loaded and what is broken; Compare Track names the baseline.
  Everything on it is a cheap read — store, index, host registers — never a traversal.
- **Hand-offs** (`AppWindow.Show(app, tab)` carrying context): Run tests → the board opens and
  loads itself; freeze → the change notice; a missing source → Broken Links; an issue going out
  → the board it issues. The week is a route, and the apps pass the baton instead of describing
  the next runner.

## The audits

| App | Audit |
|---|---|
| Clash Manager | [docs/apps/clash.md](docs/apps/clash.md) |
| Issue Link | [docs/apps/issues.md](docs/apps/issues.md) |
| Model Audit | [docs/apps/audit.md](docs/apps/audit.md) |
| Compare Track | [docs/apps/track.md](docs/apps/track.md) |
| Data Manager | [docs/apps/data.md](docs/apps/data.md) |
| Quantify | [docs/apps/quantify.md](docs/apps/quantify.md) |
| Sets Manager | [docs/apps/sets.md](docs/apps/sets.md) |
| Scene Kit | [docs/apps/scenes.md](docs/apps/scenes.md) |
| Export Kit | [docs/apps/exports.md](docs/apps/exports.md) |
| Site Pack | [docs/apps/site.md](docs/apps/site.md) |
| Automate | [docs/apps/automate.md](docs/apps/automate.md) |
