# Security

Knit is a local tool: a CLI plus a cheap browser UI for **your own machine**.
It is not built to be exposed to a network, let alone the internet.

Knit is MIT-licensed and provided "as is", without warranty (see [LICENSE](LICENSE)).
There is no guaranteed support or fix timeline.

## How it works

- **CLI:** runs as you and reads/writes the repo with your file-system rights.
  It makes no network calls.
- **`knit web`:** a local server for the browser UI. Its API has **no
  authentication** and includes every write the CLI has.
  - Binds `127.0.0.1` by default: only your own machine can reach it.
  - Answers only requests addressed to `localhost` / a loopback IP (blocks DNS
    rebinding), and refuses writes coming from other websites' pages: a write
    with a foreign `Origin` gets a 403, and every write must send
    `Content-Type: application/json` (even without a body), which a page on
    another site can't send without a CORS preflight the API never grants.
  - `--host <ip>` widens the bind for a container or dev box. Then **anyone who
    can reach the port can edit the repo**. Knit warns when you do this.
- **Repo content is treated as untrusted:** rendered markdown is sanitized,
  Mermaid diagrams render in strict mode, a Content-Security-Policy stops
  injected script from running, and file endpoints can't reach outside the
  Knit root.

## What Knit doesn't do

- No users, logins, roles or encryption.
- No hardening for multi-user, shared or internet-facing use. Don't run
  `knit web` that way.
- No sandboxing of AI agents. Skills that `knit skill install` writes are plain
  instructions; review them like any change you commit.

## In scope

Ways to break the guarantees above without the user opting out of them:

- another website reading or changing a repo through a default `knit web`
- script injection (XSS) through repo content shown in the web UI
- reading or writing files outside the Knit root
- secrets leaking into output (e.g. a remote URL with a token)

## Out of scope

Things that follow from the design and are documented:

- anything reachable because `knit web` was started with `--host` (that's the
  documented trade-off)
- attacks that already need code running as your user on your machine
- vulnerabilities in dependencies that Knit's usage doesn't trigger (report
  those upstream)
