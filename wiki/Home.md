# CamelWorks for Autodesk Navisworks

Coordination, data and delivery tools for **Navisworks 2024–2027**, Manage and Simulate.

CamelWorks adds one ribbon tab. On it are twelve **apps**; each opens in its own window with its
modes as **tabs** across the top, and each tab has **one main action**. Nothing has to be configured
before it will run: every screen reads what it needs from whatever is open, says what it worked out,
and lets you correct it.

## Start here

| | |
|---|---|
| [Installing](Installing) | The installer, the manual bundle, and which years are supported |
| [How the windows work](How-the-windows-work) | Apps, tabs, the action row, picking rows, the configuration window |
| [Common workflows](Common-workflows) | The clash round trip, a revision review, a data fix, a handover |
| [What it cannot do](What-it-cannot-do) | The honest limits, and why each one is there |
| [Troubleshooting](Troubleshooting) | When a screen says something you did not expect |

## The apps

| App | What it is for |
|---|---|
| [Clash Manager](Clash-Manager) | Find what fights, and decide it |
| [Issue Manager](Issue-Manager) | Everything raised on this job, in one register |
| [Issue Link](Issue-Link) | Issues out to the world, and answers back in |
| [Model Audit](Model-Audit) | Whether this federation is fit to coordinate against |
| [Compare Track](Compare-Track) | What changed, against what, since when |
| [Data Manager](Data-Manager) | What the elements carry, and the pen to fix it |
| [Quantify](Quantify) | Numbers out of the model, the way a bill wants them |
| [Sets Manager](Sets-Manager) | The selections everything else works on |
| [Scene Kit](Scene-Kit) | What is visible, and what colour it is |
| [Export Kit](Export-Kit) | The model as a file somebody else opens |
| [Site Pack](Site-Pack) | The model reaching the people building it |
| [Automate](Automate) | Doing all of it again next Friday, in one press |

Outside the twelve: **Review** (walk findings one at a time over the geometry), **Section Box**
(box the selection, or switch sectioning off), and **Help** (the guide, the limits, a sample
project and the diagnostics).

## Three things worth knowing before you start

**Every result is accounted for.** The clash bench never quietly shows a smaller number than the
engine found. Anything suppressed, collapsed or grouped is still counted and can be shown.

**Clearance is not clash.** A duct 1.9 m over a corridor floor touches nothing, so no clash test at
any tolerance will ever report it. [Clearance Check](Clash-Manager#clearance-check) is the tab that
finds it, and it works its findings the same way the clash bench does.

**Appearance is a stack, not a state.** Colours and hiding are named layers you can take back off,
so *why is this hidden* has an answer. Overrides CamelWorks did not author are reported and never
cleared without being asked.

## Licence

Free to use, including commercially. **Not free to sell.** Apache 2.0 with the Commons Clause —
source-available, not open source. The `LICENSE` file in the repository carries a plain-words
summary alongside the terms.
