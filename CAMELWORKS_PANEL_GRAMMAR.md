# The panel grammar

Three independent design reviews of the nineteen panels reached the same diagnosis, so it is
written down here rather than left in a pull request nobody will find again.

The panels are six now and the rest are windows, and every rule below applies to a window exactly
as it applies to a panel. A window has more room, which makes rule 4 easier to break, not harder:
space is what let six tools grow two scrolling columns with the irreversible button in the corner
of one of them.

## What went wrong

Every panel was `Scroll(Stack(Card, Card, Card, Section, Section))`. A tool and an option got
identical chrome — glyph, heading, subtitle sentence, rule, body, buttons — so rank was invisible.
Cards and collapsible sections were being used as **spacing**, not as **meaning**. Once a card no
longer says "this is a tool" and a section no longer says "this is separate", a two-button colour
toggle and a full-federation scan look exactly alike.

On six of eight panels in one workspace, the first card existed only to hold a button — so the
panel opened on furniture and pushed its own answer to card two or three.

The client put it plainly: *"you don't know what is part of a tool and what is its own tool… does
colouring the model button need its own expandable tab, or is it just an option?"*

## The five rules

**1. The Runner is the hinge.** Controls above it, results below it. Neither the Runner nor its
controls get a card. A panel opens on the tool, not on a box containing the tool.

**2. The result is the card.** The biggest, most decorated element is the answer, never the query.
A panel with no verdict grows one — a figure row, or a card subtitle stating the whole outcome in
one phrase.

**3. One primary per panel.** A second card is earned only by a second act with a different
consequence — reading a response back mutates the board; exporting one does not. Rank by frequency
first and badness second: separate tools go below a rule, findings sort worst-first, and everything
clean collapses to one row each.

**4. An option never gets a card, and rarely a section.** If it takes no input and produces nothing
outside the current view, it is an option: a field, a toggle, or a button in the same action row.
Two options that answer one question become one control. Fold only an *input* that is off or
default for most people, is not needed to trust the button, and can be summarised in about
thirty-four characters at 470 pixels — `"Off — values read exactly as they are"` is the model. Fold
it **below** the primary action, never between the user and it. Never fold a result, a failure, or
a setting that can destroy somebody's data.

**5. Prose above a control is deleted on sight.** Its meaning is carried by, in order of
preference: the control's own label; a field hint under ten words; a section summary stating the
current value; a pill on the row it describes; or a line printed **after** the run instead of a
warning printed before it. If a sentence explains why a default is right, the default states it. If
it explains what a button will do, the caption states it. If it promises nothing is applied yet, a
preview and a commit button state it.

## The test

Point at any element on any screen and answer in one word: **tool**, **option**, or **result**.
If the answer is "option" and it has a card or a section of its own, it is wrong. If the answer is
"result" and it is above the button that produces it, it is wrong. If nobody can answer, delete it.
