# Status report automation — idea

Status: **idea / not scheduled.** Open question: is automation knit's job
(regenerate inside `knit web`), or a forge's (a scheduled CI job knit only
documents)?

`knit status report` writes the page on request. A
page nobody regenerates goes stale quietly, and its readers are the people
least likely to notice. Automation must stay off by default and must not run
on every write.

## The constraint every trigger meets

The page is a function of the data **and today**: "100 days ago", overdue and
at risk all move daily. A regenerated page differs from yesterday's even when
no item changed. Any trigger therefore costs about one diff per day it fires,
and the only real noise control is cadence.

## Options

| Option | Trigger | For | Against |
|---|---|---|---|
| **A. Scheduled CI job** | Weekly / nightly forge action runs `knit status report`, commits or publishes it | Readers are on the forge; no local churn; cadence controls noise | Needs the knit binary in CI (Phase 1); the commit is the bot's |
| **B. `knit web`, debounced** | `status_report.auto`: N minutes after the last write, at most once a day | Rides the existing watcher; zero setup | Only while the server runs; writes a tracked file in the background, which can surprise a commit in progress |
| **C. Event-based** | Milestone `reached` / `dropped` / retargeted, a project or epic closed | Changes only when the story does | Dates go stale between events; a hook in the mutators |
| **D. Git pre-commit hook** | Regenerate and stage | Always current in commits | Hooks are per checkout; staging into someone's commit fights "knit never commits"; lefthook / pre-commit already do it with no knit code |
| **E. Staleness hint** | `knit status` / `verify` and the SPA menu note a page older than `status_report.stale_days` | Never writes; cheap; safe default | Not automation |

## Leaning

E + A. E as a small config key (unset = no hint) keeps a manual workflow
honest. A as a documented sample workflow in `/knit-projects`, not knit code.
B only if in-tool automation is wanted after all: opt-in, at most once a day,
and skipped when only the `Generated` line would change.
