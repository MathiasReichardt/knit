# Conventions

A repo's and a user's own rules for agents, as plain markdown. When to read
them and how they rank: `knit` skill > Repo and user conventions.

- **Files:** the repo's `.knit/conventions.md` (committed, follows the knit
  root redirect) and the user's `conventions.md` beside the user-level
  `config.json` (`KNIT_CONFIG_HOME` moves both). `knit conventions --json`
  lists both with their paths, missing ones included.
- **Changing them:** `knit conventions set --scope repo|user < file.md`
  replaces the whole file; `knit conventions remove --scope repo|user`
  deletes it. Editing the file directly
  is fine once you know the path. Ask which scope when the user doesn't say:
  "for this repo / the team" is repo, "my rules" is user.
- **Enforceable beats prose.** A rule a machine can check belongs in a
  template, a config key or a `verify` check, not in conventions — say so when
  asked to add one.
- **Keep them short:** every agent run reads them. `knit verify` warns above
  200 lines.
