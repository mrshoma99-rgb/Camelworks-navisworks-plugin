# Publishing the wiki

The pages in `wiki/` are the GitHub wiki, kept in this repository so they are reviewed like code.
GitHub serves a wiki from a **second git repository** (`<repo>.wiki.git`) that nothing but git can
write to — there is no REST API for it — so publishing is a copy and a push.

## Once, to enable it

Settings → Features → tick **Wikis**, then open the Wiki tab and create the first page. The
`.wiki.git` repository does not exist until a page has been saved.

## Every time

```bash
git clone https://github.com/mrshoma99-rgb/Camelworks-navisworks-plugin.wiki.git /tmp/cw-wiki
cp wiki/*.md /tmp/cw-wiki/
cd /tmp/cw-wiki && git add -A && git commit -m "Update the wiki" && git push
```

`Home.md` is the landing page, `_Sidebar.md` the navigation, `_Footer.md` the strip at the bottom.
Every other file is a page whose title is its filename with the dashes read as spaces, which is why
they are named `Clash-Manager.md` and not `clash_manager.md`.

Links between pages are by page name — `[Clash Manager](Clash-Manager)` — with `#anchors` matching
the headings. `checks/validate-wiki-links.py` proves every one of them resolves.
