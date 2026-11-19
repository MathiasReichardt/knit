<#
.SYNOPSIS
    Seed a Knit project with a Robot Factory demo: items, KB entries, a
    design doc, a project doc folder + milestones (one planned, one
    advanced to reached) + free-form docs (a flat doc and a folder-form
    entry), slates, comments, links, blockers, and a mermaid gallery
    (one diagram per common type).

.DESCRIPTION
    Drops a small but representative dataset so `knit tree`, `knit
    context`, and `knit kb show` immediately have
    something interesting to render. The data is light comedy
    (slightly dim robot pets and the people who assemble them) so the
    demo stays memorable.

    Composes the public `knit` CLI exclusively — no internal hooks.
    Every command is shown verbatim; the script doubles as a tour of
    the CLI surface.

    Suppresses stdout from `knit` calls and prints its own progress
    lines. Stderr is left visible: if anything goes wrong, you see it.

.NOTES
    Intended workflow (self-contained — no Knit source repo needed):

      1. Make an empty folder for the demo.
      2. Copy this script into it.
      3. Copy the `knit` binary (knit.exe on Windows) into the same
         folder, OR have `knit` on PATH.
      4. From inside the folder:
            knit init
            .\seed-robot-factory.ps1

    Safety: refuses to run unless `.knit/` exists, and prompts for
    confirmation before clearing existing items / KB / designs / projects.
    Never touches `config.json`, `.gitignore`, or installed skills.

    Writes one file outside `.knit/`: `docs/robot-spec.txt`, a real
    reference document the web file-viewer can open (linked by a file-kind
    `source:` entry and an item). It is overwritten on re-run, never
    deleted.

    Writes one file next to a doc without a knit command:
    `assembly-hall.svg` beside `doc:mermaid-gallery`, the gallery's SVG
    example. No command places a file beside a doc.
#>

[CmdletBinding()]
param(
    [switch] $Yes  # Skip the destructive-clear confirmation
)

$ErrorActionPreference = 'Stop'

# ---------- helpers ----------------------------------------------------

function Step($msg) { Write-Host "  $msg" -ForegroundColor DarkGray }
function Note($msg) { Write-Host $msg -ForegroundColor Cyan }
function Warn($msg) { Write-Host $msg -ForegroundColor Yellow }
function Die($msg) { Write-Host "error: $msg" -ForegroundColor Red; exit 1 }

# Run knit and discard stdout. Stderr leaks through naturally. Throws
# on non-zero exit so script halts on the first failure.
function K {
    param([Parameter(ValueFromRemainingArguments = $true)] [string[]] $a)
    & $script:KnitExe @a | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "knit $($a -join ' ') failed (exit $LASTEXITCODE)" }
}

# Run knit with --json appended; capture stdout, parse, return object.
function KJson {
    param([Parameter(ValueFromRemainingArguments = $true)] [string[]] $a)
    $out = & $script:KnitExe @a --json
    if ($LASTEXITCODE -ne 0) { throw "knit $($a -join ' ') --json failed (exit $LASTEXITCODE)" }
    return ($out -join "`n") | ConvertFrom-Json
}

# ---------- preflight --------------------------------------------------

# Resolution order:
#   1. `knit` on PATH (preferred — works for installed binaries).
#   2. `knit.exe` / `knit` next to this script ($PSScriptRoot).
#      Intended workflow: copy this script AND the knit binary into
#      the target folder, run from there. The binary lives next to
#      the script, not in some shared repo location.
$script:KnitExe = $null
$onPath = Get-Command knit -ErrorAction SilentlyContinue
if ($onPath) {
    $script:KnitExe = $onPath.Source
} else {
    foreach ($name in @('knit.exe', 'knit')) {
        $candidate = Join-Path $PSScriptRoot $name
        if (Test-Path $candidate -PathType Leaf) {
            $script:KnitExe = $candidate
            break
        }
    }
}

if (-not $script:KnitExe) {
    Die @"
knit not found.
Tried:
  * 'knit' on PATH
  * '$PSScriptRoot\knit.exe' (next to this script)
Either put 'knit' on PATH, or copy the knit binary into the same
folder as this script, then re-run.
"@
}

Step "using knit at: $script:KnitExe"

if (-not (Test-Path .knit)) {
    Die "no .knit/ folder here. Run ``knit init`` first, then re-run this script."
}

$cwdAbs      = (Resolve-Path '.').Path
$itemsDir    = Join-Path '.knit' 'items'
$kbDir       = Join-Path '.knit' 'kb'
$designsDir  = Join-Path '.knit' 'designs'
$projectsDir = Join-Path '.knit' 'projects'

# PowerShell's Remove-Item -Recurse follows directory junctions / symlinks
# and deletes their targets' contents. Refuse if any reparse point lives
# under the dirs we're about to wipe -- the only way this script could
# touch files outside .knit/.
$reparse = Get-ChildItem -Path $itemsDir, $kbDir, $designsDir, $projectsDir -Recurse -Force `
    -Attributes ReparsePoint -ErrorAction SilentlyContinue
if ($reparse) {
    Warn "Reparse points (junctions/symlinks) found under .knit/:"
    foreach ($r in $reparse) { Warn "  $($r.FullName)" }
    Die "refusing to recursively delete -- resolve these manually first."
}

$hasData = $false
foreach ($d in @($itemsDir, $kbDir, $designsDir, $projectsDir)) {
    if (Test-Path $d) {
        if ((Get-ChildItem -Force $d -ErrorAction SilentlyContinue | Measure-Object).Count -gt 0) {
            $hasData = $true; break
        }
    }
}

if ($hasData -and -not $Yes) {
    Warn "Existing items / KB / designs detected under:"
    Warn "  $cwdAbs"
    Warn "The seed script will DELETE:"
    Warn "  $(Join-Path $cwdAbs $itemsDir)"
    Warn "  $(Join-Path $cwdAbs $kbDir)"
    Warn "  $(Join-Path $cwdAbs $designsDir)"
    Warn "  $(Join-Path $cwdAbs $projectsDir)"
    Warn "  $(Join-Path $cwdAbs (Join-Path '.knit' 'slates.md'))"
    Warn "config.json, installed skills, and other files are untouched."
    $reply = Read-Host "Continue? (type 'yes' to clear and seed)"
    if ($reply -ne 'yes') { Die "aborted by user." }
}

if ($hasData) {
    Step "clearing items/ kb/ designs/ projects/ (+ slates.md)"
    foreach ($d in @($itemsDir, $kbDir, $designsDir, $projectsDir)) {
        if (Test-Path $d) { Remove-Item -Recurse -Force $d }
    }
    # The slate registry references the items we just cleared; a stale
    # copy would also make the `slate add` calls below fail as duplicates.
    $slatesFile = Join-Path '.knit' 'slates.md'
    if (Test-Path $slatesFile) { Remove-Item -Force $slatesFile }
}

# ---------- seed -------------------------------------------------------

Note ""
Note "[+] BotCo Robot Factory demo"

# --- A real reference document (lives in the repo, NOT under .knit/).
#     A file-kind `source:` entry points at it and an item links to it,
#     so the web file viewer (/api/file) opens a live target. This is the
#     intended use of a file source: linking an external document (here a
#     .txt spec; a PDF, image, or code file works the same way).
Note ""
Note "[+] Reference document (real -- for the web file viewer)"

Step "docs/robot-spec.txt"
New-Item -ItemType Directory -Force -Path 'docs' | Out-Null
Set-Content -Path (Join-Path 'docs' 'robot-spec.txt') -Encoding utf8 -Value @'
BotCo Robot Factory -- Standard Pet-Robot Specification
Revision: Champagne (Q1)          Maintained by: the floor, grudgingly

== Bobbins (round, chatty) ==
  Diameter ............ 38 cm (antenna not included)
  Antenna ............. 12 cm, wobble-rated to 15 degrees
  Fur ................. FluffcoMax FF-Long-Champagne (catalog p.14)
  Greeting loop ....... one cheerful "hello" per 4s, max 9 dB
  Head-pat tolerance .. 500 pats before recalibration
  Default mood ........ delighted

== Zoggers (square, stompy) ==
  Footprint ........... 30 x 30 cm
  Ankle joints ........ reinforced; rated for 200k stomps
  Dice luck stat ...... must roll >= 7 to graduate
  Oiling .............. squeak must stay <= 12 dB
  Default mood ........ determined

== Tinies (pocket scouts) ==
  Height .............. 9 cm
  Chassis ............. one stock frame, minimal fur
  Colorways ........... 1 (the custom rainbow line was discontinued)
  Default mood ........ curious

== Hard rules (do not negotiate with the glue) ==
  * Hair glue (FluffcoMax) must cure in <= 90s at 22 C.
  * Never ship a unit before the glue sets -- ask the spring glue incident.
  * QA reroutes. QA never scraps.
'@

# --- KB first: ADRs, docs, features, etc. Items will reference them.
Note ""
Note "[+] KB entries"

Step "adr: Fluffco supplier choice"
$adr1 = KJson kb add adr "Choose Fluffco as primary fur supplier" `
    --summary "Small-order supplier with hypoallergenic synthetic fur"

# Bodies via `kb edit --set-section` -- the same verb an agent has, so the
# seed never hand-edits a KB file. The ref comes from the create call rather
# than a guessed slug (adr slugs carry a generated number prefix).
K kb edit $adr1.ref --set-section "Context" --value @'
Three models ship with fur or hair, and the spring prototype run shed
badly. We need a supplier that takes small orders and certifies the fur
hypoallergenic for the petting-zoo pilot.
'@
K kb edit $adr1.ref --set-section "Decision" --value @'
Fluffco is the primary fur supplier for all three models.
'@
K kb edit $adr1.ref --set-section "Consequences" --value @'
- Single-supplier risk on a Q1-critical material.
- Their standard cure time sets the floor for the skin station.
'@

Step "adr: switch to FluffcoMax (will supersede #0001)"
$adr2 = KJson kb add adr "Switch to FluffcoMax premium line" `
    --summary "Better cure time after the spring glue incident"
K kb edit $adr2.ref --set-section "Context" --value @'
The spring glue incident traced back to standard-line fur soaking up
glue and pushing cure past 90s.
'@
K kb edit $adr2.ref --set-section "Decision" --value @'
Move to the FluffcoMax premium line for every hair install. Supersedes 0001.
'@
K kb edit $adr2.ref --set-section "Consequences" --value @'
- Cure lands at 70-80s, inside the 90s ceiling.
- Unit cost up ~12%; absorbed for Q1.
'@

Step "supersede: 0002 over 0001"
K kb supersede 0002 0001

Step "doc: happy-robot-checklist"
K kb add doc happy-robot-checklist `
    --title "Happy robot checklist" `
    --summary "Daily checks so assembled robots stay cheerful"

Step "feature: hair-installation"
K kb add feature hair-installation `
    --title "Hair installation" `
    --summary "Attach fur or hair to robot scalp plates without shedding"

Step "feature: assembly-line (will split to folder form)"
K kb add feature assembly-line `
    --title "Assembly line" `
    --summary "Five-station floor: frame, skin, walking, paint, oil"

Step "split feature:assembly-line into folder form"
K kb split feature:assembly-line

Step "log: pet-protocol incident (with a sequence diagram)"
$incident = KJson kb add log "Bobbin antenna pat incident" `
    --summary "Visitor patted antenna instead of head; Bobbin objected"
K kb edit $incident.ref --set-section "What happened" --value @'
```mermaid
sequenceDiagram
    actor V as Visitor
    participant A as Antenna sensor
    participant B as Bobbin
    V->>A: pat
    A->>B: touch event (zone: antenna)
    B-->>V: indignant beep
    Note over A,B: head-pat response never fired
    V->>B: pat on head
    B-->>V: happy chirp
```
'@

Step "idea: rolling-dice tournament"
K kb add idea "Monthly Zogger dice-rolling tournament"

Step "source: Fluffco 2026 catalog (link)"
K kb add source fluffco-2026 `
    --title "Fluffco 2026 fur catalog" `
    --url "https://fluffco.example/2026-catalog.pdf" `
    --summary "Fur reference; standard Bobbins use FF-Long-Champagne (p.14)"

Step "source: robot-spec.txt (file -- points at the real docs/ file)"
K kb add source robot-spec --kind file --path docs/robot-spec.txt `
    --title "Standard pet-robot specification" `
    --summary "Dimensions, tolerances, and the hard rules for all three models"

# --- Mermaid gallery: one fence per common diagram type, so click-to-pointer
#     in knit web can be tried on each (incl. the label-only types).
Note ""
Note "[+] Mermaid gallery"

Step "doc: mermaid-gallery"
K kb add doc mermaid-gallery `
    --title "Mermaid gallery" `
    --summary "Common mermaid diagram types, for testing click-to-pointer"
K kb edit doc:mermaid-gallery --set-section "Overview" --value "One fence per common diagram type, and one SVG file. Click an element in knit web to copy a pointer to it; click empty space for the whole diagram. A linked node has a badge: a Knit ref follows, any other target is copied."
K kb edit doc:mermaid-gallery --delete-section "Details" --delete-section "Known limitations"

Step "gallery: flowchart"
K kb edit doc:mermaid-gallery --set-section "Flowchart" --value @'
Flowchart with subgraphs, edge labels and mixed arrow styles.

```mermaid
flowchart LR
  order[Order received] --> check{In stock?}
  check -->|yes| pick[Pick parts]
  check -->|no| reorder[Reorder from supplier]
  reorder -.-> pick
  subgraph line [Assembly line]
    pick --> assemble[Assemble robot]
    assemble ==> qa[QA check]
  end
  qa -->|pass| ship([Ship])
  qa -->|fail| assemble
  click ship href "doc:mermaid-gallery#gantt"
  click reorder href "https://mermaid.js.org/syntax/flowchart.html#interaction"
```
'@

Step "gallery: sequence"
K kb edit doc:mermaid-gallery --set-section "Sequence" --value @'
```mermaid
sequenceDiagram
  actor customer as Customer
  participant shop as Web shop
  participant factory as Factory API
  customer->>shop: Place order
  shop->>factory: Create build job
  factory-->>shop: Job id
  Note over shop,factory: Async from here
  loop Every minute
    shop->>factory: Poll status
  end
  factory-->>customer: Robot shipped
```
'@

Step "gallery: class"
K kb edit doc:mermaid-gallery --set-section "Class" --value @'
```mermaid
classDiagram
  class Robot {
    +String serial
    +int happiness
    +pat() void
  }
  class Part {
    +String sku
  }
  class Antenna
  class FurCoat
  Robot "1" *-- "many" Part : built from
  Part <|-- Antenna
  Part <|-- FurCoat
  link Robot "doc:mermaid-gallery#state"
  click Antenna href "https://mermaid.js.org/syntax/classDiagram.html#interaction"
```
'@

Step "gallery: state"
K kb edit doc:mermaid-gallery --set-section "State" --value @'
```mermaid
stateDiagram-v2
  [*] --> queued
  queued --> building : start
  building --> testing : assembled
  testing --> building : failed QA
  testing --> shipped : passed QA
  shipped --> [*]
  click testing href "doc:mermaid-gallery#gantt"
```
'@

Step "gallery: entity relationship"
K kb edit doc:mermaid-gallery --set-section "Entity relationship" --value @'
```mermaid
erDiagram
  CUSTOMER ||--o{ ORDER : places
  ORDER ||--|{ ORDER_LINE : contains
  ROBOT_MODEL ||--o{ ORDER_LINE : "ordered as"
  CUSTOMER {
    string id PK
    string name
  }
  ORDER {
    string id PK
    date placed
  }
```
'@

Step "gallery: gantt"
K kb edit doc:mermaid-gallery --set-section "Gantt" --value @'
```mermaid
gantt
  title Production ramp-up
  dateFormat YYYY-MM-DD
  section Prep
    Source fur        :fur, 2026-10-05, 7d
    Calibrate line    :cal, after fur, 3d
  section Build
    Pilot batch       :pilot, after cal, 5d
    Full production   :after pilot, 14d
  click pilot href "doc:mermaid-gallery#flowchart"
```
'@

Step "gallery: pie"
K kb edit doc:mermaid-gallery --set-section "Pie" --value @'
```mermaid
pie title Defects by cause
  "Antenna wobble" : 42
  "Fur shedding" : 30
  "Grumpy firmware" : 18
  "Other" : 10
```
'@

Step "gallery: mindmap"
K kb edit doc:mermaid-gallery --set-section "Mindmap" --value @'
```mermaid
mindmap
  root((Happy robot))
    Hardware
      Soft fur
      Bouncy antenna
    Software
      Mood engine
      Pat sensor
    Care
      Daily pats
```
'@

Step "gallery: user journey"
K kb edit doc:mermaid-gallery --set-section "User journey" --value @'
```mermaid
journey
  title Unboxing a robot
  section Delivery
    Open box: 4: Customer
    Read manual: 2: Customer
  section First run
    Power on: 5: Customer, Robot
    First pat: 5: Customer
```
'@

Step "gallery: git graph"
K kb edit doc:mermaid-gallery --set-section "Git graph" --value @'
```mermaid
gitGraph
  commit id: "init"
  branch firmware
  commit id: "mood engine"
  checkout main
  commit id: "chassis"
  merge firmware
  commit id: "v1.0"
```
'@

Step "gallery: timeline"
K kb edit doc:mermaid-gallery --set-section "Timeline" --value @'
```mermaid
timeline
  title Factory history
  2024 : Founded : First prototype
  2025 : FluffCo deal
  2026 : Assembly line : 1000th robot
```
'@

Step "gallery: quadrant"
K kb edit doc:mermaid-gallery --set-section "Quadrant" --value @'
```mermaid
quadrantChart
  title Feature priority
  x-axis Low effort --> High effort
  y-axis Low joy --> High joy
  quadrant-1 Plan
  quadrant-2 Do now
  quadrant-3 Skip
  quadrant-4 Maybe
  Pat sensor: [0.3, 0.9]
  Self-grooming: [0.8, 0.7]
  Extra LEDs: [0.2, 0.3]
```
'@

Step "gallery: block -- layer stack"
K kb edit doc:mermaid-gallery --set-section "Block: layer stack" --value @'
The factory's software as a layer stack: apps on top, the controller firmware lib as the foundation.

```mermaid
block-beta
  columns 4
  lineui["Line UI"]:2 dash["Floor dashboard"]:2
  block:svc:4
    columns 3
    assembly["Assembly"] qa["QA"] shipping["Shipping"]
  end
  block:shared:4
    columns 4
    auth["Auth"] msg["Messaging"] store[("Build records")] cfg["Config"]
  end
  fw["Controller firmware lib<br>motors · sensors · safety"]:4
  classDef app stroke:#2563eb,stroke-width:2px
  class lineui,dash app
```

Blue frame: used on the floor.
'@

Step "gallery: block -- software stack"
K kb edit doc:mermaid-gallery --set-section "Block: software stack" --value @'
The robot's runtime, hardware at the bottom; a borderless label block names each layer.

```mermaid
block-beta
  columns 5
  l5["Apps"] block:apps:4
    columns 2
    pet["Pet behaviour"] greet["Greeting loop"]
  end
  l4["Middleware"] block:mid:4
    columns 2
    bus["Messaging"] motion["Motion control"]
  end
  l3["Runtime"] block:rt:4
    columns 2
    drivers["Drivers"] vm["Script runtime"]
  end
  l2["OS"] rtos["RTOS"]:4
  l1["Hardware"] block:hw:4
    columns 3
    board["Board"] sensors["Sensors"] motors["Motors"]
  end
  style l1 fill:none,stroke:none
  style l2 fill:none,stroke:none
  style l3 fill:none,stroke:none
  style l4 fill:none,stroke:none
  style l5 fill:none,stroke:none
```
'@

Step "gallery: block -- 3-tier services"
K kb edit doc:mermaid-gallery --set-section "Block: 3-tier services" --value @'
The order system in three tiers -- presentation, logic, data -- one nested block each.

```mermaid
block-beta
  columns 1
  block:presentation
    columns 2
    shop["Web shop"] dash["Line dashboard"]
  end
  block:logic
    columns 3
    order["Order service"] sched["Scheduling"] qa["QA service"]
  end
  block:data
    columns 2
    orders[("Orders DB")] parts[("Parts DB")]
  end
```
'@

Step "gallery: architecture"
K kb edit doc:mermaid-gallery --set-section "Architecture" --value @'
The plant's infrastructure: the line nested in the plant, a junction splitting the API's edge, side hints on every edge.

```mermaid
architecture-beta
  group plant(cloud)[Plant]
  group line(server)[Assembly line] in plant
  service api(server)[Factory API] in plant
  service db(database)[Build records] in plant
  service plc(disk)[Station PLCs] in line
  service cell(server)[Robot cell] in line
  service net(internet)[Internet]
  junction hub in plant
  net:R --> L:api
  api:B -- T:db
  api:R -- L:hub
  hub:B -- T:plc
  plc:R -- L:cell
  align row db plc
```
'@

# The one write next to a doc that isn't a knit command: no command places a
# file beside a doc (a human or agent puts it there by hand), so the seed does
# the same. It sits next to the gallery's own file, wherever knit put that.
Step "gallery: SVG floor plan (written next to the gallery doc)"
$galleryDir = Split-Path (& $script:KnitExe ref show doc:mermaid-gallery --path-only)
Set-Content -Path (Join-Path $galleryDir 'assembly-hall.svg') -Encoding utf8 -Value @'
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 640 400" font-family="sans-serif" font-size="13">
  <desc>Floor plan of the Q1 assembly hall: each station stands where it is on the floor, the conveyor runs in a U from Frame to the shipping dock, and QA sends failed units back to Skin.</desc>
  <defs>
    <marker id="arrow" class="k-arrow" viewBox="0 0 8 8" refX="7" refY="4" markerWidth="8" markerHeight="8" orient="auto">
      <path d="M0,0 L8,4 L0,8 Z" fill="#52525b"/>
    </marker>
  </defs>
  <rect class="k-group" x="8" y="8" width="624" height="384" rx="8" fill="#fafafa" stroke="#a1a1aa"/>
  <g data-kind="group" id="body-zone" class="k-group">
    <title>Body zone</title>
    <rect x="24" y="32" width="592" height="112" rx="8" fill="#fafafa" stroke="#a1a1aa"/>
    <text x="36" y="52">Body zone</text>
    <g data-kind="node" id="frame" class="k-node" transform="translate(48,72)">
      <title>Frame</title>
      <rect width="120" height="48" rx="6" fill="#f4f4f5" stroke="#71717a"/>
      <text x="60" y="29" text-anchor="middle" fill="#18181b">Frame</text>
    </g>
    <g data-kind="node" id="skin" class="k-node" transform="translate(256,72)">
      <title>Skin</title>
      <rect width="120" height="48" rx="6" fill="#f4f4f5" stroke="#71717a"/>
      <text x="60" y="29" text-anchor="middle" fill="#18181b">Skin</text>
    </g>
    <g data-kind="node" id="walking" class="k-node" transform="translate(464,72)">
      <title>Walking</title>
      <rect width="120" height="48" rx="6" fill="#f4f4f5" stroke="#71717a"/>
      <text x="60" y="29" text-anchor="middle" fill="#18181b">Walking</text>
    </g>
  </g>
  <g data-kind="group" id="finish-zone" class="k-group">
    <title>Finish zone</title>
    <rect x="24" y="200" width="592" height="112" rx="8" fill="#fafafa" stroke="#a1a1aa"/>
    <text x="36" y="220">Finish zone</text>
    <g data-kind="node" id="paint" class="k-node" transform="translate(464,240)">
      <title>Paint</title>
      <rect width="120" height="48" rx="6" fill="#f4f4f5" stroke="#71717a"/>
      <text x="60" y="29" text-anchor="middle" fill="#18181b">Paint</text>
    </g>
    <g data-kind="node" id="oil" class="k-node" transform="translate(256,240)">
      <title>Oil</title>
      <rect width="120" height="48" rx="6" fill="#f4f4f5" stroke="#71717a"/>
      <text x="60" y="29" text-anchor="middle" fill="#18181b">Oil</text>
    </g>
    <g data-kind="node" id="qa" class="k-node k-accent" transform="translate(48,240)">
      <title>QA gate</title>
      <rect width="120" height="48" rx="6" fill="#dbeafe" stroke="#2563eb"/>
      <text x="60" y="29" text-anchor="middle" fill="#18181b">QA gate</text>
    </g>
  </g>
  <g data-kind="node" id="dock" class="k-node" transform="translate(48,336)">
    <title>Shipping dock</title>
    <rect width="120" height="48" rx="6" fill="#f4f4f5" stroke="#71717a"/>
    <text x="60" y="29" text-anchor="middle" fill="#18181b">Shipping dock</text>
  </g>
  <g fill="none" stroke="#52525b" stroke-width="2" marker-end="url(#arrow)">
    <path data-kind="edge" id="frame-skin" class="k-edge" data-from="frame" data-to="skin" d="M168,96 H254"><title>conveyor</title></path>
    <path data-kind="edge" id="skin-walking" class="k-edge" data-from="skin" data-to="walking" d="M376,96 H462"><title>conveyor</title></path>
    <path data-kind="edge" id="walking-paint" class="k-edge" data-from="walking" data-to="paint" d="M524,120 V238"><title>conveyor</title></path>
    <path data-kind="edge" id="paint-oil" class="k-edge" data-from="paint" data-to="oil" d="M464,264 H378"><title>conveyor</title></path>
    <path data-kind="edge" id="oil-qa" class="k-edge" data-from="oil" data-to="qa" d="M256,264 H170"><title>conveyor</title></path>
    <path data-kind="edge" id="qa-dock" class="k-edge" data-from="qa" data-to="dock" d="M108,288 V334"><title>passed QA</title></path>
    <path data-kind="edge" id="qa-skin" class="k-edge k-muted" data-from="qa" data-to="skin" d="M140,240 V176 H316 V122" stroke="#a1a1aa" stroke-dasharray="6 4"><title>failed QA: rework</title></path>
  </g>
  <text data-kind="label" id="rework-note" class="k-label k-muted" x="220" y="170" fill="#a1a1aa">rework</text>
  <text data-kind="label" id="legend" class="k-label k-muted" x="616" y="380" text-anchor="end" fill="#a1a1aa">blue: quality gate · dashed: rework loop</text>
</svg>
'@
K kb edit doc:mermaid-gallery --set-section "SVG file" --value @'
The assembly hall's floor plan is an SVG file next to this doc: stations stand where they are on the floor, which no mermaid type lays out.

![Assembly hall floor plan](assembly-hall.svg)
'@

# --- Tags: a few shared tags across buckets so the KB tree tag filter
#     (ALL/ANY) and tag search have something to demo. Tags overlap on
#     purpose: `fur` / `materials` / `qa` each span multiple entries.
Note ""
Note "[+] Tags"

Step "tag: feature:hair-installation"
K kb set feature:hair-installation tags "fur,materials,qa"

Step "tag: feature:assembly-line"
K kb set feature:assembly-line tags "process,qa,station"

Step "tag: doc:happy-robot-checklist"
K kb set doc:happy-robot-checklist tags "qa,pet-care"

Step "tag: adr:0002 (FluffcoMax)"
K kb set adr:0002 tags "supplier,materials,fur"

Step "tag: source:fluffco-2026"
K kb set source:fluffco-2026 tags "supplier,fur,materials"

Step "tag: source:robot-spec"
K kb set source:robot-spec tags "spec,qa,reference"

Step "tag: idea:monthly-zogger-dice-rolling-tournament"
K kb set idea:monthly-zogger-dice-rolling-tournament tags "events,pet-care"

# --- Design docs: one active (fleshed out, with a mermaid diagram) and
#     one archived, so the Designs view + landing Designs row show both
#     an active doc and a completed one -- not an empty stub.
Note ""
Note "[+] Design"

Step "design: q1-assembly-line (active, fleshed out)"
# Bodies go in through `design edit --set-section`, never Set-Content --
# the seed is the consumer-side dogfood, so it uses the same verb an agent
# has. Frontmatter written by `design new` survives untouched.
K design new q1-assembly-line --title "Q1 Assembly Line Design"
K design edit q1-assembly-line --set-section "Overview" --value @'
How the Q1 floor turns frames into shippable robots across five stations,
and where the quality gate loops bad units back.
'@
K design edit q1-assembly-line --set-section "Goals" --value @'
- One floor layout serving Bobbins, Zoggers, and Tinies.
- Hard 90s glue-cure ceiling enforced at the skin station (see adr:0002).
- A QA gate that reroutes, never scraps.
'@
K design edit q1-assembly-line --set-section "Non-goals" --value @'
- Custom paint colorways (de-scoped from Q1).
- Second shift / throughput scaling -- that is a Q2 concern.
'@
K design edit q1-assembly-line --set-section "Approach" --value @'
```mermaid
flowchart LR
    subgraph body [Body stations]
        F[Frame] --> S[Skin]
        S -- "cured under 90s" --> W[Walking]
    end
    subgraph finish [Finish]
        P[Paint] --> O[Oil]
    end
    W --> P
    O --> QA{QA pass?}
    QA -- yes --> SHIP[Ship]
    QA -- no --> S
```
'@
K design edit q1-assembly-line --set-section "Open questions" --value @'
- Does the cool bay need its own glue-cure timer, or can it share Skin's?
- Where does pet-response calibration sit -- after Walking or after Oil?
'@
K design edit q1-assembly-line --set-section "Status" --value "Active -- drives the Q1 epic."

Step "design: pet-protocol (will be archived)"
K design new pet-protocol --title "Robot Pet-Interaction Protocol"
K design edit pet-protocol --set-section "Overview" --value @'
The original spec for how robots respond to being patted, petted, and
greeted. Superseded by the per-model calibration baked into each feature.
'@
K design edit pet-protocol --set-section "Goals" --value "- One shared pat/pet/greet response across every model."
K design edit pet-protocol --set-section "Non-goals" --value "- Voice interaction -- the models have no speakers."
K design edit pet-protocol --set-section "Approach" --value "A single lookup table from touch zone to response, shared by all models."
K design edit pet-protocol --set-section "Open questions" --value "- None left; the per-model split settled them."
# Not in the template -- `--set-section` appends a section that isn't there.
K design edit pet-protocol --set-section "Decision" --value @'
Calibration moved into the model features (Bobbins owns head-pat
response) rather than a single shared protocol. This doc is kept for
history.
'@
K design edit pet-protocol --set-section "Status" --value "Completed -- folded into the model features; archived."

Step "archive: pet-protocol -> designs/completed/"
K design archive pet-protocol

# --- Items: epic -> 3 features -> tasks
Note ""
Note "[+] Items (epic, features, tasks, bug)"

Step "epic: BotCo Q1 production push"
$epic = KJson add epic "BotCo Q1 production push"

Step "feature: Bobbins (round chatty model)"
$bobbins = KJson add feature "Bobbins -- round chatty model" --parent $epic.id

Step "feature: Zoggers (square stompy model)"
$zoggers = KJson add feature "Zoggers -- square stompy model" --parent $epic.id

Step "feature: Tinies (pocket scouts)"
$tinies = KJson add feature "Tinies -- pocket-sized scouts" --parent $epic.id

# Bobbins subtree
Step "task: Frame assembly (Bobbins)"
$frame = KJson add task "Frame assembly: rivets + spine" --parent $bobbins.id

Step "task: Artificial hair install (Bobbins)"
$hair = KJson add task "Artificial hair install (long fluffy)" --parent $bobbins.id

Step "bug:  Glue dries too slow (blocks Bobbins hair install)"
$glueBug = KJson add bug "Hair-glue dries too slow" --parent $epic.id

Step "task: Teach walking (Bobbins)"
$walk = KJson add task "Teach walking -- wobble allowance plus/minus 15deg" --parent $bobbins.id

Step "task: Pet-response calibration (Bobbins)"
$pet = KJson add task "Pet-response calibration (head pats)" --parent $bobbins.id

# A task with subtask children -> 4-level tree (epic > feature > task > subtask).
Step "subtask: Gyro balance calibration (under Teach walking)"
$gyro = KJson add task "Gyro balance calibration" --parent $walk.id

Step "subtask: Wobble damping tune (under Teach walking)"
$wobble = KJson add task "Wobble damping tune (settle under 2s)" --parent $walk.id

# A draft: proposed, not yet specified or approved. Absent from next / ready /
# pick, present in every count.
Step "draft: Dance mode (unapproved)"
K add task "Dance mode -- proposed, scope not agreed" --parent $bobbins.id --draft

# External rows: where the work actually lives. The branch sits on the parent
# and inherits down; the PR hangs off the task under review.
Step "external: branch + PR on the walking work"
K link $walk.id --branch "feature/walk-wobble"
K link $gyro.id --pr "https://github.com/botco/robots/pull/312" --note "review pending"

# Zoggers subtree
Step "task: Reinforced ankle joints (Zoggers)"
$ankles = KJson add task "Reinforced ankle joints" --parent $zoggers.id

Step "task: Rolling-dice training (Zoggers)"
$dice = KJson add task "Rolling-dice training (luck stat >= 7)" --parent $zoggers.id

Step "task: Luxury oiling station (Zoggers)"
$oil = KJson add task "Luxury oiling station (squeak <= 12 dB)" --parent $zoggers.id

# Tinies subtree
Step "task: Custom paint colorways (Tinies)"
$paint = KJson add task "Custom paint: 4 stock colorways" --parent $tinies.id

# --- States: a couple done, one in-progress, one blocked (flag), one dismissed
Note ""
Note "[+] States"

Step "done: Frame assembly"
K done $frame.id

Step "start: Artificial hair install (will be blocked next)"
K start $hair.id

Step "block: hair install blocked by glue bug (orthogonal flag — state stays in-progress)"
K block $hair.id --by $glueBug.id --reason "Awaiting Fluffco glue v2 — current batch peels off after 48h"

Step "dismiss: Custom paint colorways (de-scoped from Q1)"
K dismiss $paint.id

Step "start: Gyro balance calibration (subtask in progress)"
K start $gyro.id

# --- Dependencies: structural `Blocks` links. These feed the timeline's
#     blocker graph (LinkGraph reads `Blocks` rows only -- the `block` flag
#     above is an orthogonal status whose blocked-by row is audit history).
Note ""
Note "[+] Dependencies (blocks links)"

Step "link: glue bug --blocks-> hair install (undated blocker -> at-risk)"
K link $glueBug.id --blocks $hair.id

Step "link: hair install --blocks-> pet-response calibration (transitive chain)"
K link $hair.id --blocks $pet.id

Step "link: gyro calibration --blocks-> teach walking (dated in time)"
K link $gyro.id --blocks $walk.id

Step "link: wobble damping --blocks-> teach walking (will be dated too late -> at-risk)"
K link $wobble.id --blocks $walk.id

Step "link: ankle joints --blocks-> rolling-dice training (overdue blocker, still in time)"
K link $ankles.id --blocks $dice.id

# --- Due dates: relative to run time so overdue / due-soon / future states
#     stay meaningful on every run (never hard-coded). Undated items (the
#     glue bug) stay undated on purpose -- an undated blocker endangers the
#     hair-install deadline in the timeline.
Note ""
Note "[+] Due dates (relative to today)"

$overdueDate = (Get-Date).AddDays(-3).ToString('yyyy-MM-dd')
$soonDate    = (Get-Date).AddDays(5).ToString('yyyy-MM-dd')
$petDate     = (Get-Date).AddDays(9).ToString('yyyy-MM-dd')
$gyroDate    = (Get-Date).AddDays(12).ToString('yyyy-MM-dd')
$nearDate    = (Get-Date).AddDays(20).ToString('yyyy-MM-dd')
$wobbleDate  = (Get-Date).AddDays(25).ToString('yyyy-MM-dd')
$oilDate     = (Get-Date).AddDays(30).ToString('yyyy-MM-dd')
$farDate     = (Get-Date).AddDays(45).ToString('yyyy-MM-dd')
$pastDate    = (Get-Date).AddDays(-10).ToString('yyyy-MM-dd')

Step "due: Reinforced ankle joints -> overdue (-3d, red + warning)"
K set $ankles.id "due=$overdueDate"

Step "due: Artificial hair install -> due soon (+5d, amber)"
K set $hair.id "due=$soonDate"

Step "due: Pet-response calibration -> +9d (downstream of hair install)"
K set $pet.id "due=$petDate"

Step "due: Gyro calibration -> +12d (finishes before walking's due -> in time)"
K set $gyro.id "due=$gyroDate"

Step "due: Teach walking -> upcoming (+20d)"
K set $walk.id "due=$nearDate"

# Dated LATER than the item it blocks -- the dated-late at-risk case (a red
# edge between two dated markers on the timeline).
Step "due: Wobble damping -> +25d (after walking's due -> at-risk)"
K set $wobble.id "due=$wobbleDate"

Step "due: Luxury oiling station -> +30d"
K set $oil.id "due=$oilDate"

Step "due: Rolling-dice training -> future (+45d)"
K set $dice.id "due=$farDate"

# Everything above sets one field per call so each step narrates one feature.
# `add` also takes the fields directly -- one command instead of five, and
# rejected input creates nothing at all.
Step "add with field flags: Antenna tuning (Tinies) in one command"
K add task "Antenna tuning: 3-band sweep" --parent $tinies.id `
    --assignee rusty --due $oilDate --priority important --effort S --tags qa,rf

Step "due: Frame assembly -> past date on a done item (never overdue)"
K set $frame.id "due=$pastDate"

# --- Effort: free-text sizes on a few dated/at-risk items so the timeline
#     selection panel has effort text to surface. Deliberately
#     mixed formats -- effort is an authored string, not a scale.
Note ""
Note "[+] Effort"

Step "effort: Artificial hair install -> ~2d"
K set $hair.id "effort=~2d"

Step "effort: Hair-glue bug -> M"
K set $glueBug.id "effort=M"

Step "effort: Reinforced ankle joints -> L"
K set $ankles.id "effort=L"

Step "effort: Teach walking -> 1w"
K set $walk.id "effort=1w"

# --- Priority: one critical blocker, one important, one backlog (someday);
#     everything else stays at the normal default. Exercises --sort priority,
#     --min-priority, and the color-by-priority toggle. Priority is a
#     triage lens only -- it deliberately does NOT reorder `ready` / `next`.
Note ""
Note "[+] Priority"

Step "priority: Hair-glue bug -> critical (drop everything)"
K set $glueBug.id "priority=critical"

Step "priority: Artificial hair install -> important"
K set $hair.id "priority=important"

Step "priority: Custom paint colorways -> someday (de-scoped to backlog)"
K set $paint.id "priority=someday"

# A small team -- spread ownership so the owner / `mine` filters and
# list/tree assignee grouping have several people to show, not just one.
Note ""
Note "[+] Assignments (cogsworth / sparky / rusty)"

Step "assign: Artificial hair install -> cogsworth"
K assign $hair.id cogsworth

Step "assign: Hair-glue bug -> cogsworth"
K assign $glueBug.id cogsworth

Step "assign: Teach walking -> sparky"
K assign $walk.id sparky

Step "assign: Reinforced ankle joints -> sparky"
K assign $ankles.id sparky

Step "assign: Pet-response calibration -> rusty"
K assign $pet.id rusty

Step "assign: Gyro balance calibration -> rusty"
K assign $gyro.id rusty

# --- Slates: the named "work on this next" selection. The whole Q1 epic
#     inherits q1-push from the epic's own slate: (read-time inheritance);
#     the de-scoped paint task explicitly overrides to backlog, re-rooting
#     it out of the q1-push filter -- the override demo.
Note ""
Note "[+] Slates (q1-push active, backlog planned)"

Step "slate: q1-push (active)"
K slate add q1-push --status active --note "current floor bring-up"

Step "slate: backlog (planned)"
K slate add backlog --status planned --note "after the Q1 push"

Step "slate: epic -> q1-push (features + tasks inherit)"
K set $epic.id "slate=q1-push"

Step "slate: Custom paint colorways -> backlog (explicit override re-roots)"
K set $paint.id "slate=backlog"

# --- Agent context: skills (inherited), acceptance + code pointers (leaf)
Note ""
Note "[+] Agent context bits"

Step "agent_skills on epic (inherited down to features and tasks)"
K set $epic.id "agent_skills=robot-safety-checklist,fluffco-glue-handbook"

Step "agent_skills on Bobbins feature (head-pat domain knowledge)"
K set $bobbins.id "agent_skills=pet-response-calibration"

# --- Item tags: free-form cross-cutting labels, same mechanism as KB tags.
# A shared vocabulary ("qa", "materials") so the tree/list tag filter has a
# couple of multi-tagged items to demonstrate AND filtering.
Step "tags on hair-install (qa + materials)"
K set $hair.id "tags=qa,materials"

Step "tags on Gyro balance calibration (qa)"
K set $gyro.id "tags=qa"

# Mixed - [x] / - [ ] so the web UI shows interactive checkboxes with
# real on-load state, not an all-empty list.
Step "acceptance criteria on hair-install (partially checked)"
K edit $hair.id --set-section "Acceptance criteria" --value @"
- [x] FluffcoMax glue cures under 90s at 22 C
- [x] No shedding after 500 head-pats
- [ ] Pet-response calibration still passes
- [ ] Sign-off from the pet-protocol review
"@

# A markdown link to the real docs/robot-spec.txt -- the web file viewer
# opens it on click. The `../../../` climb is deliberate: it resolves to
# the repo root for both the SPA's link resolver and `knit verify`.
Step "code pointers on hair-install (links to real spec doc)"
K edit $hair.id --set-section "Code pointers" --value @"
- ``HairInstall.Cure`` -- glue-cure routine; respect the 90s timer
- ``PetResponse.Calibrate`` -- shares the head-plate model
- [robot-spec.txt](../../../docs/robot-spec.txt) -- dims + tolerances (also source:robot-spec)
"@

Step "code pointers on glue bug"
K edit $glueBug.id --set-section "Code pointers" --value @"
- ``GlueBatch.CureTimer`` -- where the 90s ceiling is enforced
- ``Supplier.FluffcoMax`` -- new supplier wiring
"@

Step "process diagram on epic (mermaid)"
K edit $epic.id --set-section "Process" --value @'
The Q1 line runs five stations in sequence; the QA gate loops bad
units back to the relevant station.

```mermaid
flowchart LR
    F[Frame] --> S[Skin]
    S --> W[Walking]
    W --> P[Paint]
    P --> O[Oil]
    O --> QA{QA pass?}
    QA -- yes --> SHIP[Ship]
    QA -- no --> S
```
'@

# --- Prose: real paragraph-length descriptions so the details pane and
#     the search corpus have something to chew on (not just titles).
Note ""
Note "[+] Descriptions (prose bodies)"

Step "description on epic"
K edit $epic.id --set-section "Description" --value @"
The Q1 push ships three robot lines off one floor: Bobbins (round and
chatty), Zoggers (square and stompy), and Tinies (pocket scouts). The
shared constraint is the FluffcoMax glue cure -- it gates the skin
station, so anything touching hair or pet-response waits on it.
"@

Step "description on Bobbins feature"
K edit $bobbins.id --set-section "Description" --value @"
Bobbins are the friendly face of the line: long fluffy fur, a chatty
greeting loop, and a head-plate tuned for pats. Most of the open risk
lives here -- the hair-install glue and the pet-response calibration
share the same head-plate model, so a change to one ripples into the
other.
"@

Step "description on Teach walking"
K edit $walk.id --set-section "Description" --value @"
Walking is allowed a plus/minus 15-degree wobble; anything past that
reads as 'drunk robot' on the showroom floor. Balance is split into two
subtasks -- gyro calibration and wobble damping -- so they can be tuned
independently before the combined gait test.
"@

# This prose deliberately references a KB feature that does not exist.
# `knit verify` flags it as a dangling prose ref (warning) and the web
# health view surfaces it -- a used system always has a little cruft.
Step "description on Tinies (leaves one dangling ref for verify)"
K edit $tinies.id --set-section "Description" --value @"
Tinies are pocket-sized scouts -- one stock chassis, minimal fur. The
custom colorways were de-scoped from Q1; they were going to reuse the
discontinued ``feature:rainbow-paint`` rig, which never shipped.
"@

# --- Links: KB ref, related
Note ""
Note "[+] Links"

Step "link: hair-install -> KB feature:hair-installation"
K link $hair.id --kb-ref feature:hair-installation --note "process and pet-test spec"

Step "link: hair-install -> KB adr:0002 (supplier choice)"
K link $hair.id --kb-ref adr:0002 --note "why we switched glue lines"

Step "link: hair-install -> KB source:robot-spec (the spec document)"
K link $hair.id --kb-ref source:robot-spec --note "physical spec + tolerances"

Step "link: pet-response -> related to walking"
K link $pet.id --related $walk.id

# --- Comments: a short thread on one item, not a single note.
Note ""
Note "[+] Comments"

Step "comment 1 on hair-install"
K comment $hair.id "Epoxy cure time tracked in the glue bug -- switching to FluffcoMax next batch."

Step "comment 2 on hair-install"
K comment $hair.id "FluffcoMax sample arrived. Cured in 72s at 22C on the bench -- under the 90s ceiling."

Step "comment 3 on hair-install"
K comment $hair.id "Blocked until the glue bug closes, but the acceptance checklist is two-thirds green already."

# --- Project + milestone: a project doc folder (readme + roadmap +
#     milestones/) plus the tracker-side `project` item that points at it.
#     The Q1 epic is parented UNDER the program below, so the project
#     structurally contains its work and the timeline's Project-depth rollup
#     folds the whole epic (features + tasks) into one program bar. Separately
#     the Q1 epic and the Bobbins feature aim at the q1-launch milestone via a
#     `Milestone` link -- parent = containment, Milestone = which target an
#     item aims at; the two axes are orthogonal. A couple of `todo`-type items
#     hang under the project (project can parent a todo; a todo can't sit at
#     root).
Note ""
Note "[+] Project + milestone (doc folder, roadmap, Milestone links, todos)"

$q1Date = (Get-Date).AddDays(60).ToString('yyyy-MM-dd')
$pilotDate = (Get-Date).AddDays(-30).ToString('yyyy-MM-dd')

Step "project item: BotCo 2026 program"
$program = KJson add project "BotCo 2026 program"

# One-shot co-create: scaffold the doc folder (readme + roadmap) AND link it to
# the existing program item in one action (--item), so the two halves join
# atomically and can't drift. Do NOT hand-assemble with `project new` +
# `set project=` -- the bare `project new` would mint a SECOND project item and
# leave an orphan.
# --roadmap is REQUIRED here: milestones are roadmap-gated, so the roadmap must
# exist before the `milestone new` calls below (don't reorder these steps).
Step "doc folder + join: botco-2026 (readme + roadmap.md), linked to the program item"
K project new botco-2026 --title "BotCo 2026 program" --roadmap --item $program.id

# health is a project-only signal -- at-risk fits the seeded risk (an overdue
# ankle-joints task and a critical glue bug blocking the Bobbins hair install).
Step "health: BotCo 2026 program -> at-risk"
K set $program.id "health=at-risk"

# Parent the whole Q1 production tree under the program so the project
# structurally contains its work. This is what makes the timeline's
# Project-depth rollup meaningful: the epic (and its features/tasks) fold into
# the program bar instead of surfacing as scattered leaves. Containment via
# the parent link is orthogonal to the `Milestone` links wired below.
# (Re-parent via `link --parent`, not `set parent=` -- parent is a Links-table
# row, not a plain frontmatter field, so `set` would silently no-op.)
Step "parent: Q1 push epic under the BotCo 2026 program (project contains the epic)"
K link $epic.id --parent $program.id

Step "milestone: q1-launch (target +60d, state planned)"
K project milestone new botco-2026 q1-launch --title "Q1 launch" --target $q1Date

# A delivered milestone so state advancement is visible in the seed: created
# planned, then advanced with `milestone set --state reached` (the same edit
# path the CLI and SPA use). Its past target reads as "already shipped".
Step "milestone: pilot (delivered) -> set state reached"
K project milestone new botco-2026 pilot --title "Prototype pilot" --target $pilotDate
K project milestone set botco-2026/pilot --state reached

# Roadmap prose links to each milestone with an ordinary relative link --
# roadmap.md and milestones/ are siblings in the project folder. This
# overwrites the scaffold stub with real narrative.
Step "roadmap.md -> narrative linking the milestones"
$roadmapPath = Join-Path '.knit' (Join-Path 'projects' (Join-Path 'botco-2026' 'roadmap.md'))
Set-Content -Path $roadmapPath -Encoding utf8 -Value @'
# BotCo 2026 program — roadmap

The 2026 floor plan, quarter by quarter. Each milestone links to its
sub-doc; items aim at a milestone via a `Milestone` link.

## Delivered → [Prototype pilot](milestones/pilot.md)
The pilot line proved the pipeline end to end — reached, and now the
baseline the 2026 program builds on.

## Q1 — First shippable pets → [Q1 launch](milestones/q1-launch.md)
Bring up the floor and ship the first Bobbins line. The Q1 production
push epic and the Bobbins feature both aim at this milestone.

## Q2 — Scale the line
Second shift, throughput, and the Zogger / Tinies lines. Milestones TBD.
'@

# Free-form docs: a project folder is "KB-like" -- any .md (or subfolder) you
# drop under it is browsable + searchable and inert to the link machinery. Seed
# one flat doc and one folder-form entry (readme.md + a sibling file) so the
# folder-entry path is exercised. These need no command -- just write the files.
Step "free-form doc: vendor-notes.md (flat)"
$botcoDir = Join-Path '.knit' (Join-Path 'projects' 'botco-2026')
Set-Content -Path (Join-Path $botcoDir 'vendor-notes.md') -Encoding utf8 -Value @'
# Vendor notes

Loose working notes on parts suppliers — not a milestone, not tracked work.
Free-form docs like this are searchable but inert to the link graph.

- Servo motors: two-week lead time, order Q1 stock early.
- Googly eyes: single-source; qualify a backup vendor before Q2.
'@

Step "free-form folder-form doc: specs/ (readme.md + a sibling)"
$specsDir = Join-Path $botcoDir 'specs'
New-Item -ItemType Directory -Force -Path $specsDir | Out-Null
Set-Content -Path (Join-Path $specsDir 'readme.md') -Encoding utf8 -Value @'
# Line specs

Shared specifications for the 2026 production lines. A folder-form doc: the
folder is the entry (readme.md), sibling files hang beside it.
'@
Set-Content -Path (Join-Path $specsDir 'paint-shop.md') -Encoding utf8 -Value @'
# Paint shop spec

Booth throughput, cure times, and the two-coat finish the Bobbins line needs.
'@

Step "milestone link: Q1 push epic -> botco-2026/q1-launch"
K link $epic.id --milestone botco-2026/q1-launch

Step "milestone link: Bobbins feature -> botco-2026/q1-launch"
K link $bobbins.id --milestone botco-2026/q1-launch

Step "todo: register the BotCo trademark (under the program)"
K add todo "Register the BotCo trademark" --parent $program.id

Step "todo: book the Q1 launch showcase venue (under the program)"
K add todo "Book the Q1 launch showcase venue" --parent $program.id

# --- Disconnected work: a small tree with NO project above it. The Q1 tree
#     folds into the program at Project depth; this one has no project
#     ancestor, so it surfaces as its own chain root (the orphan epic, with the
#     dated task rolled into it) and the timeline's `--hide-disconnected`
#     filter (SPA "Hide disconnected" toggle) drops it — a clean "real project
#     only" view. Deliberately left unfiled, the way real backlog often is.
Note ""
Note "[+] Disconnected work (exercises the timeline Hide-disconnected toggle)"

$boothDate = (Get-Date).AddDays(18).ToString('yyyy-MM-dd')

Step "epic: Showroom refresh (unscheduled -- not under any program)"
$showroom = KJson add epic "Showroom refresh (unscheduled)"

Step "task: Repaint the demo booth (dated, under the orphan epic)"
$booth = KJson add task "Repaint the demo booth" --parent $showroom.id
K set $booth.id "due=$boothDate"

# ---------- summary ----------------------------------------------------

Note ""
Note "[+] Seed complete."
Note ""
Note "Try these (CLI):"
Write-Host "  knit tree"           -ForegroundColor White
Write-Host "  knit ready"          -ForegroundColor White
Write-Host "  knit next"           -ForegroundColor White
Write-Host "  knit list --sort due       # soonest deadline first, undated last" -ForegroundColor White
Write-Host "  knit list --overdue        # the overdue ankle-joints task" -ForegroundColor White
Write-Host "  knit timeline              # deadline agenda + at-risk blockers (glue bug, wobble tune)" -ForegroundColor White
Write-Host "  knit timeline --depth project --range all  # program bar (epic rolled in) + the unfiled 'Showroom refresh' tree" -ForegroundColor White
Write-Host "  knit timeline --depth project --range all --hide-disconnected  # drops 'Showroom refresh' -> real projects only" -ForegroundColor White
Write-Host "  knit status                # completion %, per-assignee load, blocked + overdue" -ForegroundColor White
Write-Host "  knit status --project botco-2026  # the same rollup for one project (echoes its health signal)" -ForegroundColor White
Write-Host "  knit context $($hair.id.Substring(0,8))" -ForegroundColor White
Write-Host "  knit slate list --all      # q1-push (active) + backlog (planned)" -ForegroundColor White
Write-Host "  knit tree --slate q1-push  # the epic's subtree minus the backlogged paint task" -ForegroundColor White
Write-Host "  knit project list          # the BotCo 2026 doc folder + milestone count" -ForegroundColor White
Write-Host "  knit project milestone list botco-2026  # pilot (reached) + q1-launch (planned)" -ForegroundColor White
Write-Host "  knit project milestone show q1-launch   # the milestone + its backlinked items" -ForegroundColor White
Write-Host "  knit tree --milestone botco-2026/q1-launch   # items aiming at Q1 launch" -ForegroundColor White
Write-Host "  knit kb list"        -ForegroundColor White
Write-Host "  knit kb show feature:hair-installation" -ForegroundColor White
Write-Host "  knit design list"    -ForegroundColor White
Write-Host "  knit design list --completed" -ForegroundColor White
Write-Host "  knit verify          # finds the one dangling prose ref left on purpose" -ForegroundColor White
Note ""
Note "Or open the local web UI:"
Write-Host "  knit web             # tree/list/KB/designs + the file viewer, in the browser" -ForegroundColor White
Write-Host "                       # the summary strip above the tree shows the same rollup as 'knit status'" -ForegroundColor DarkGray
Note ""
