# Working rules for this repository

## Writing style — applies to everything

Write in plain, professional, technical English. This is a standing rule and it
applies to commit messages, PR titles and bodies, code comments, XML docs,
documentation and chat replies.

Do:

- State what changed and why, in the fewest words that are still accurate.
- Use ordinary technical vocabulary: "the export did not include cell fills",
  not "the picture stopped at the edge of the window".
- Keep comments to what a maintainer needs: the constraint, the reason for a
  non-obvious choice, the failure mode being guarded against.
- Prefer bullet points and short paragraphs.

Do not:

- Write in a literary, essayistic or storytelling register.
- Open a comment or a commit with a bolded aphorism or a rhetorical setup.
- Use rhetorical questions, dramatic contrast ("X was not Y, it was Z"),
  or narrative framing about imagined users and their afternoons.
- Repeat the same point in three different phrasings for emphasis.
- Pad a commit message to paragraphs when two sentences cover it.

Example of the required register for a commit message:

    Add per-cell styling to XlsxWriter

    The writer only had plain and bold formats, so the matrix export lost
    folder colours, symbol fills and the diagonal. Adds XlsxStyle (fill, ink,
    bold, centred, rotated, boxed), merged ranges, explicit column widths and
    row heights. The stylesheet is generated from the styles a workbook
    actually uses. Sheets that set no styles produce the same output as before.

## Other standing rules

- No code, comment, commit message or document may mention KVI, Kanadevia or
  iConstruct.
- Be sparing with GitHub Actions minutes. The `ui-shots` workflow draws only
  the named pages, in the light palette.
- The matrix's default save target is the project database. Changes sync back
  into Navisworks only when the open model is an `.nwf` and the Navisworks
  edition is Manage.
- Do not update `wiki/` unless explicitly asked. `docs/` and `checks/` are
  normal.
