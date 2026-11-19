# Third-party notices

Knit ships as a single self-contained binary. That binary statically links its
NuGet dependencies and embeds the built web SPA, so the components listed here
are **redistributed inside the executable** even though they are not visible as
separate files. Their licenses require attribution; this file provides it.

Knit itself is MIT-licensed — see [LICENSE](LICENSE). Nothing here changes that;
these notices cover other people's code.

Scope:

- **Build-time and test-only dependencies are excluded** (xunit,
  Microsoft.NET.Test.Sdk, Vite, ESLint, TypeScript, …) — they are not
  redistributed.
- The npm list is the full production dependency tree from
  `src/Knit.Web/package-lock.json`. Tree-shaking means the bundle contains a
  subset; listing the whole tree over-attributes rather than under-attributes.
  Type-only packages (`@types/*`, `@chevrotain/types`) contribute no runtime
  code but are listed for completeness.
- No dependency in either tree ships a `NOTICE` file, so there is nothing to
  reproduce under Apache-2.0 §4(d).

The npm section is generated — **don't hand-edit it**. After any dependency
change:

```bash
node tools/gen-npm-notices.mjs           # rewrite the section
node tools/gen-npm-notices.mjs --check   # fail if stale (CI)
```

It reads each package's own license file for the copyright line, and refuses to
run if a package's license can't be resolved. Dual-licensed packages and
metadata gaps are pinned in the script's `OVERRIDES`, with the reasoning in
[design/decisions.md](design/decisions.md) > Build & packaging. The .NET table is short and
hand-maintained from `dotnet list package --include-transitive` plus each
package's `.nuspec`.

`knit licenses` prints this file from inside the binary, so the notices travel
with the executable rather than depending on this repo.

---

## .NET (NuGet)

All MIT. Licenses and copyright lines taken from each package's `.nuspec`.

| Package | Version | License | Copyright | Project |
|---|---|---|---|---|
| Spectre.Console | 0.57.1 | MIT | Patrik Svensson, Phil Scott, Nils Andresen, Cédric Luthi | https://github.com/spectreconsole/spectre.console |
| Spectre.Console.Ansi | 0.57.1 | MIT | Patrik Svensson, Phil Scott, Nils Andresen, Cédric Luthi | https://github.com/spectreconsole/spectre.console |
| System.CommandLine | 2.0.9 | MIT | © Microsoft Corporation | https://github.com/dotnet/command-line-api |
| FunicularSwitch | 6.5.0 | MIT | bluehands GmbH & Co.mmunication KG | https://github.com/bluehands/Funicular-Switch |
| FunicularSwitch.Analyzers | 1.0.0 | MIT | bluehands GmbH & Co.mmunication KG | https://github.com/bluehands/Funicular-Switch |
| System.IO.Hashing | 10.0.9 | MIT | © Microsoft Corporation | https://dot.net/ |
| Microsoft.Extensions.FileProviders.Embedded | 10.0.9 | MIT | © Microsoft Corporation | https://asp.net/ |
| Microsoft.Extensions.FileProviders.Abstractions | 10.0.9 | MIT | © Microsoft Corporation | https://dot.net/ |
| Microsoft.Extensions.Primitives | 10.0.9 | MIT | © Microsoft Corporation | https://dot.net/ |

The .NET runtime and base class libraries linked in by the Native AOT publish
(`Microsoft.DotNet.ILCompiler`, `Microsoft.NET.ILLink.Tasks`, and the runtime
itself) are © Microsoft Corporation, MIT — https://github.com/dotnet/runtime.
The ASP.NET Core shared framework behind `knit web` (`Microsoft.AspNetCore.App`:
Kestrel, minimal APIs) is © Microsoft Corporation, MIT —
https://github.com/dotnet/aspnetcore. Both repos keep their own third-party
notices for code they include.

---

## npm (embedded SPA)

<!-- npm-list:start -->

### MIT (143)

- **@antfu/install-pkg** 1.1.0 — Copyright (c) 2021 Anthony Fu <https://github.com/antfu>
- **@braintree/sanitize-url** 7.1.2 — Copyright (c) 2017 Braintree
- **@codemirror/autocomplete** 6.20.3 — Copyright (C) 2018-2021 by Marijn Haverbeke <marijn@haverbeke.berlin> and others
- **@codemirror/commands** 6.10.4 — Copyright (C) 2018-2021 by Marijn Haverbeke <marijn@haverbeke.berlin> and others
- **@codemirror/lang-cpp** 6.0.3 — Copyright (C) 2018-2021 by Marijn Haverbeke <marijn@haverbeke.berlin> and others
- **@codemirror/lang-css** 6.3.1 — Copyright (C) 2018-2021 by Marijn Haverbeke <marijn@haverbeke.berlin> and others
- **@codemirror/lang-go** 6.0.1 — Copyright (C) 2024 by Marijn Haverbeke <marijn@haverbeke.berlin> and others
- **@codemirror/lang-html** 6.4.11 — Copyright (C) 2018-2021 by Marijn Haverbeke <marijn@haverbeke.berlin> and others
- **@codemirror/lang-javascript** 6.2.5 — Copyright (C) 2018-2021 by Marijn Haverbeke <marijn@haverbeke.berlin> and others
- **@codemirror/lang-json** 6.0.2 — Copyright (C) 2018-2021 by Marijn Haverbeke <marijn@haverbeke.berlin> and others
- **@codemirror/lang-less** 6.0.2 — Copyright (C) 2018-2021 by Marijn Haverbeke <marijn@haverbeke.berlin> and others
- **@codemirror/lang-markdown** 6.5.0 — Copyright (C) 2018-2021 by Marijn Haverbeke <marijn@haverbeke.berlin> and others
- **@codemirror/lang-python** 6.2.1 — Copyright (C) 2018-2021 by Marijn Haverbeke <marijn@haverbeke.berlin> and others
- **@codemirror/lang-rust** 6.0.2 — Copyright (C) 2018-2021 by Marijn Haverbeke <marijn@haverbeke.berlin> and others
- **@codemirror/lang-sass** 6.0.2 — Copyright (C) 2018-2021 by Marijn Haverbeke <marijn@haverbeke.berlin> and others
- **@codemirror/lang-sql** 6.10.0 — Copyright (C) 2018-2021 by Marijn Haverbeke <marijn@haverbeke.berlin> and others
- **@codemirror/lang-xml** 6.1.0 — Copyright (C) 2018-2021 by Marijn Haverbeke <marijn@haverbeke.berlin> and others
- **@codemirror/lang-yaml** 6.1.3 — Copyright (C) 2024 by Marijn Haverbeke <marijn@haverbeke.berlin> and others
- **@codemirror/language** 6.12.3 — Copyright (C) 2018-2021 by Marijn Haverbeke <marijn@haverbeke.berlin> and others
- **@codemirror/legacy-modes** 6.5.3 — Copyright (C) 2018-2021 by Marijn Haverbeke <marijn@haverbeke.berlin> and others
- **@codemirror/lint** 6.9.7 — Copyright (C) 2018-2021 by Marijn Haverbeke <marijn@haverbeke.berlin> and others
- **@codemirror/search** 6.7.1 — Copyright (C) 2018-2021 by Marijn Haverbeke <marijn@haverbeke.berlin> and others
- **@codemirror/state** 6.7.0 — Copyright (C) 2018-2021 by Marijn Haverbeke <marijn@haverbeke.berlin> and others
- **@codemirror/theme-one-dark** 6.1.3 — Copyright (C) 2018-2021 by Marijn Haverbeke <marijn@haverbeke.berlin> and others
- **@codemirror/view** 6.43.2 — Copyright (C) 2018-2021 by Marijn Haverbeke <marijn@haverbeke.berlin> and others
- **@dnd-kit/accessibility** 3.1.1 — Copyright (c) 2021, Claudéric Demers
- **@dnd-kit/core** 6.3.1 — Copyright (c) 2021, Claudéric Demers
- **@dnd-kit/sortable** 10.0.0 — Copyright (c) 2021, Claudéric Demers
- **@dnd-kit/utilities** 3.2.2 — Copyright (c) 2021, Claudéric Demers
- **@iconify/types** 2.0.0 — Copyright (c) 2021 - 2022 Vjacheslav Trushkin / Iconify OÜ
- **@iconify/utils** 3.1.3 — Copyright (c) 2021-PRESENT Vjacheslav Trushkin
- **@lezer/common** 1.5.2 — Copyright (C) 2018 by Marijn Haverbeke <marijn@haverbeke.berlin> and others
- **@lezer/cpp** 1.1.6 — Copyright (C) 2020 by Marijn Haverbeke <marijn@haverbeke.berlin> and others
- **@lezer/css** 1.3.3 — Copyright (C) 2018 by Marijn Haverbeke <marijn@haverbeke.berlin> and others
- **@lezer/go** 1.0.1 — Copyright (C) 2020 by Marijn Haverbeke <marijn@haverbeke.berlin> and others
- **@lezer/highlight** 1.2.3 — Copyright (C) 2018 by Marijn Haverbeke <marijn@haverbeke.berlin> and others
- **@lezer/html** 1.3.13 — Copyright (C) 2018 by Marijn Haverbeke <marijn@haverbeke.berlin> and others
- **@lezer/javascript** 1.5.4 — Copyright (C) 2018 by Marijn Haverbeke <marijn@haverbeke.berlin> and others
- **@lezer/json** 1.0.3 — Copyright (C) 2020 by Marijn Haverbeke <marijn@haverbeke.berlin>, Arun Srinivasan <rulfzid@gmail.com>, and others
- **@lezer/lr** 1.4.10 — Copyright (C) 2018 by Marijn Haverbeke <marijn@haverbeke.berlin> and others
- **@lezer/markdown** 1.6.4 — Copyright (C) 2020 by Marijn Haverbeke <marijn@haverbeke.berlin> and others
- **@lezer/python** 1.1.19 — Copyright (C) 2020 by Marijn Haverbeke <marijn@haverbeke.berlin> and others
- **@lezer/rust** 1.0.2 — Copyright (C) 2018 by Marijn Haverbeke <marijn@haverbeke.berlin> and others
- **@lezer/sass** 1.1.0 — Copyright (C) 2018 by Marijn Haverbeke <marijn@haverbeke.berlin> and others
- **@lezer/xml** 1.0.6 — Copyright (C) 2018 by Marijn Haverbeke <marijn@haverbeke.berlin> and others
- **@lezer/yaml** 1.0.4 — Copyright (C) 2024 by Marijn Haverbeke <marijnh@gmail.com> and others
- **@marijn/find-cluster-break** 1.0.2 — Copyright (C) 2024 by Marijn Haverbeke <marijn@haverbeke.berlin>
- **@mermaid-js/parser** 1.2.1 — Copyright (c) 2023 Yokozuna59
- **@tailwindcss/typography** 0.5.20 — Copyright (c) Tailwind Labs, Inc.
- **@tanstack/history** 1.162.0 — Copyright (c) 2021-present Tanner Linsley
- **@tanstack/query-core** 5.101.1 — Copyright (c) 2021-present Tanner Linsley
- **@tanstack/react-query** 5.101.1 — Copyright (c) 2021-present Tanner Linsley
- **@tanstack/react-router** 1.170.16 — Copyright (c) 2021-present Tanner Linsley
- **@tanstack/react-store** 0.9.3 — Copyright (c) 2021 Tanner Linsley
- **@tanstack/router-core** 1.171.13 — Copyright (c) 2021-present Tanner Linsley
- **@tanstack/store** 0.9.3 — Copyright (c) 2021 Tanner Linsley
- **@types/d3** 7.4.3 — Copyright (c) Microsoft Corporation.
- **@types/d3-array** 3.2.2 — Copyright (c) Microsoft Corporation.
- **@types/d3-axis** 3.0.6 — Copyright (c) Microsoft Corporation.
- **@types/d3-brush** 3.0.6 — Copyright (c) Microsoft Corporation.
- **@types/d3-chord** 3.0.6 — Copyright (c) Microsoft Corporation.
- **@types/d3-color** 3.1.3 — Copyright (c) Microsoft Corporation.
- **@types/d3-contour** 3.0.6 — Copyright (c) Microsoft Corporation.
- **@types/d3-delaunay** 6.0.4 — Copyright (c) Microsoft Corporation.
- **@types/d3-dispatch** 3.0.7 — Copyright (c) Microsoft Corporation.
- **@types/d3-drag** 3.0.7 — Copyright (c) Microsoft Corporation.
- **@types/d3-dsv** 3.0.7 — Copyright (c) Microsoft Corporation.
- **@types/d3-ease** 3.0.2 — Copyright (c) Microsoft Corporation.
- **@types/d3-fetch** 3.0.7 — Copyright (c) Microsoft Corporation.
- **@types/d3-force** 3.0.10 — Copyright (c) Microsoft Corporation.
- **@types/d3-format** 3.0.4 — Copyright (c) Microsoft Corporation.
- **@types/d3-geo** 3.1.0 — Copyright (c) Microsoft Corporation.
- **@types/d3-hierarchy** 3.1.7 — Copyright (c) Microsoft Corporation.
- **@types/d3-interpolate** 3.0.4 — Copyright (c) Microsoft Corporation.
- **@types/d3-path** 3.1.1 — Copyright (c) Microsoft Corporation.
- **@types/d3-polygon** 3.0.2 — Copyright (c) Microsoft Corporation.
- **@types/d3-quadtree** 3.0.6 — Copyright (c) Microsoft Corporation.
- **@types/d3-random** 3.0.3 — Copyright (c) Microsoft Corporation.
- **@types/d3-scale** 4.0.9 — Copyright (c) Microsoft Corporation.
- **@types/d3-scale-chromatic** 3.1.0 — Copyright (c) Microsoft Corporation.
- **@types/d3-selection** 3.0.11 — Copyright (c) Microsoft Corporation.
- **@types/d3-shape** 3.1.8 — Copyright (c) Microsoft Corporation.
- **@types/d3-time** 3.0.4 — Copyright (c) Microsoft Corporation.
- **@types/d3-time-format** 4.0.3 — Copyright (c) Microsoft Corporation.
- **@types/d3-timer** 3.0.2 — Copyright (c) Microsoft Corporation.
- **@types/d3-transition** 3.0.9 — Copyright (c) Microsoft Corporation.
- **@types/d3-zoom** 3.0.8 — Copyright (c) Microsoft Corporation.
- **@types/dompurify** 3.0.5 — Copyright (c) Microsoft Corporation.
- **@types/geojson** 7946.0.16 — Copyright (c) Microsoft Corporation.
- **@types/react** 19.2.17 — Copyright (c) Microsoft Corporation.
- **@types/trusted-types** 2.0.7 — Copyright (c) Microsoft Corporation.
- **@upsetjs/venn.js** 2.0.0 — Copyright (c) 2013 Ben Frederickson
- **codemirror** 6.0.2 — Copyright (C) 2018-2021 by Marijn Haverbeke <marijn@haverbeke.berlin> and others
- **commander** 7.2.0 — Copyright (c) 2011 TJ Holowaychuk <tj@vision-media.ca>
- **commander** 8.3.0 — Copyright (c) 2011 TJ Holowaychuk <tj@vision-media.ca>
- **cookie-es** 3.1.1 — Cookie-es copyright (c) Pooya Parsa <pooya@pi0.io>
- **cose-base** 1.0.3 — Copyright (c) 2019 - present, iVis@Bilkent.
- **cose-base** 2.2.0 — Copyright (c) 2019 - present, iVis@Bilkent.
- **crelt** 1.0.6 — Copyright (C) 2020 by Marijn Haverbeke <marijn@haverbeke.berlin>
- **cssesc** 3.0.0 — Mathias Bynens
- **csstype** 3.2.3 — Copyright (c) 2017-2018 Fredrik Nicol
- **cytoscape** 3.34.0 — Copyright (c) 2016-2026, The Cytoscape Consortium.
- **cytoscape-cose-bilkent** 4.1.0 — Copyright (c) 2016-2018, The Cytoscape Consortium.
- **cytoscape-fcose** 2.2.0 — Copyright (c) 2018 - present, iVis-at-Bilkent.
- **dagre-d3-es** 7.0.14 — Original dagre-d3 copyright: Copyright (c) 2013 Chris Pettitt
- **dayjs** 1.11.21 — Copyright (c) 2018-present, iamkun
- **es-toolkit** 1.48.1 — Copyright (c) 2024 Viva Republica, Inc.
- **fastdom** 1.0.12 — Wilson Page
- **hachure-fill** 0.5.2 — Copyright (c) 2023 Preet Shihn
- **iconv-lite** 0.6.3 — Copyright (c) 2011 Alexander Shtuchkin
- **import-meta-resolve** 4.2.0 — Copyright (c) Titus Wormer <mailto:tituswormer@gmail.com>
- **katex** 0.16.47 — Copyright (c) 2013-2020 Khan Academy and other contributors
- **khroma** 2.1.0 — Copyright (c) 2019-present Fabio Spampinato, Andrew Maney *(no `license` field in the lockfile; MIT per the license file it ships)*
- **layout-base** 2.0.1 — Copyright (c) 2019 iVis@Bilkent
- **layout-base** 1.0.2 — Copyright (c) 2019 iVis@Bilkent
- **lodash-es** 4.18.1 — John-David Dalton <john.david.dalton@gmail.com>
- **marked** 18.0.5 — Copyright (c) 2018+, MarkedJS (https://github.com/markedjs/)
- **marked** 16.4.2 — Copyright (c) 2018+, MarkedJS (https://github.com/markedjs/)
- **mermaid** 11.17.2 — Copyright (c) 2014 - 2022 Knut Sveidqvist
- **package-manager-detector** 1.6.0 — Copyright (c) 2020-PRESENT Anthony Fu <https://github.com/antfu>
- **path-data-parser** 0.1.0 — Copyright (c) 2020 Preet Shihn
- **points-on-curve** 0.2.0 — Copyright (c) 2020 Preet Shihn
- **points-on-path** 0.2.1 — Copyright (c) 2020 Preet
- **postcss-selector-parser** 6.0.10 — Copyright (c) Ben Briggs <beneb.info@gmail.com> (http://beneb.info)
- **react** 19.2.7 — Copyright (c) Meta Platforms, Inc. and affiliates.
- **react-dom** 19.2.7 — Copyright (c) Meta Platforms, Inc. and affiliates.
- **roughjs** 4.6.6 — Copyright (c) 2019 Preet Shihn
- **safer-buffer** 2.1.2 — Copyright (c) 2018 Nikita Skovoroda <chalkerx@gmail.com>
- **scheduler** 0.27.0 — Copyright (c) Meta Platforms, Inc. and affiliates.
- **seroval** 1.5.4 — MIT License Copyright (c) 2025 Alexis Munsayac <alexis.munsayac@gmail.com>
- **seroval-plugins** 1.5.4 — MIT License Copyright (c) 2025 Alexis Munsayac <alexis.munsayac@gmail.com>
- **strictdom** 1.0.1 — Wilson Page
- **style-mod** 4.1.3 — Copyright (C) 2018 by Marijn Haverbeke <marijn@haverbeke.berlin> and others
- **stylis** 4.4.0 — Copyright (c) 2016-present Sultan Tarimo
- **tailwindcss** 4.3.1 — Copyright (c) Tailwind Labs, Inc.
- **tinyexec** 1.2.4 — Copyright (c) 2024 Tinylibs
- **ts-dedent** 2.3.0 — Copyright (c) 2018 Tamino Martinius
- **use-sync-external-store** 1.6.0 — Copyright (c) Meta Platforms, Inc. and affiliates.
- **util-deprecate** 1.0.2 — Copyright (c) 2014 Nathan Rajlich <nathan@tootallnate.net>
- **uuid** 14.0.1 — Copyright (c) 2010-2020 Robert Kieffer and other contributors
- **w3c-keyname** 2.2.8 — Copyright (C) 2016 by Marijn Haverbeke <marijn@haverbeke.berlin> and others
- **zod** 4.4.3 — Copyright (c) 2025 Colin McDonnell
- **zustand** 5.0.14 — Copyright (c) 2019 Paul Henschel

### ISC (34)

- **d3** 7.9.0 — Copyright 2010-2023 Mike Bostock
- **d3-array** 3.2.4 — Copyright 2010-2023 Mike Bostock
- **d3-axis** 3.0.0 — Copyright 2010-2021 Mike Bostock
- **d3-brush** 3.0.0 — Copyright 2010-2021 Mike Bostock
- **d3-chord** 3.0.1 — Copyright 2010-2021 Mike Bostock
- **d3-color** 3.1.0 — Copyright 2010-2022 Mike Bostock
- **d3-contour** 4.0.2 — Copyright 2012-2023 Mike Bostock
- **d3-delaunay** 6.0.4 — Copyright 2018-2021 Observable, Inc.
- **d3-dispatch** 3.0.1 — Copyright 2010-2021 Mike Bostock
- **d3-drag** 3.0.0 — Copyright 2010-2021 Mike Bostock
- **d3-dsv** 3.0.1 — Copyright 2013-2021 Mike Bostock
- **d3-fetch** 3.0.1 — Copyright 2016-2021 Mike Bostock
- **d3-force** 3.0.0 — Copyright 2010-2021 Mike Bostock
- **d3-format** 3.1.2 — Copyright 2010-2026 Mike Bostock
- **d3-geo** 3.1.1 — Copyright 2010-2024 Mike Bostock
- **d3-hierarchy** 3.1.2 — Copyright 2010-2021 Mike Bostock
- **d3-interpolate** 3.0.1 — Copyright 2010-2021 Mike Bostock
- **d3-path** 3.1.0 — Copyright 2015-2022 Mike Bostock
- **d3-polygon** 3.0.1 — Copyright 2010-2021 Mike Bostock
- **d3-quadtree** 3.0.1 — Copyright 2010-2021 Mike Bostock
- **d3-random** 3.0.1 — Copyright 2010-2021 Mike Bostock
- **d3-scale** 4.0.2 — Copyright 2010-2021 Mike Bostock
- **d3-scale-chromatic** 3.1.0 — Copyright 2010-2024 Mike Bostock
- **d3-selection** 3.0.0 — Copyright 2010-2021 Mike Bostock
- **d3-shape** 3.2.0 — Copyright 2010-2022 Mike Bostock
- **d3-time** 3.1.0 — Copyright 2010-2022 Mike Bostock
- **d3-time-format** 4.1.0 — Copyright 2010-2021 Mike Bostock
- **d3-timer** 3.0.1 — Copyright 2010-2021 Mike Bostock
- **d3-transition** 3.0.1 — Copyright 2010-2021 Mike Bostock
- **d3-zoom** 3.0.0 — Copyright 2010-2021 Mike Bostock
- **delaunator** 5.1.0 — Copyright (c) 2026, Mapbox
- **internmap** 1.0.1 — Copyright 2021 Mike Bostock
- **internmap** 2.0.3 — Copyright 2021 Mike Bostock
- **lucide-react** 1.21.0 — Copyright (c) 2026 Lucide Icons and Contributors

### BSD-3-Clause (6)

- **d3-array** 2.12.1 — Copyright 2010-2020 Mike Bostock
- **d3-ease** 3.0.1 — Copyright 2010-2021 Mike Bostock
- **d3-path** 1.0.9 — Copyright 2015-2016 Mike Bostock
- **d3-sankey** 0.12.3 — Copyright 2015, Mike Bostock
- **d3-shape** 1.3.7 — Copyright 2010-2015 Mike Bostock
- **rw** 1.3.3 — Copyright (c) 2014-2016, Michael Bostock

### Apache-2.0 (2)

- **@chevrotain/types** 11.1.2 — Shahar Soel
- **dompurify** 3.4.16 — Dr.-Ing. Mario Heiderich, Cure53 <mario@cure53.de> (https://cure53.de/) *(dual-licensed `MPL-2.0 OR Apache-2.0`; Apache-2.0 elected)*

### 0BSD (1)

_0BSD imposes no attribution condition; listed for completeness._

- **tslib** 2.8.1 — Copyright (c) Microsoft Corporation.

### Unlicense (2)

_Unlicense imposes no attribution condition; listed for completeness._

- **isbot** 5.1.44
- **robust-predicates** 3.0.3 — Vladimir Agafonkin

Total: 188 packages.

<!-- npm-list:end -->

---

## License texts

### MIT

Applies to every package marked MIT above, each under the copyright notice
listed with it.

```
Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

### ISC

Applies to the d3 family, `lucide-react`, `delaunator`, and `internmap`.

```
Permission to use, copy, modify, and/or distribute this software for any
purpose with or without fee is hereby granted, provided that the above
copyright notice and this permission notice appear in all copies.

THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHOR DISCLAIMS ALL WARRANTIES WITH
REGARD TO THIS SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF MERCHANTABILITY
AND FITNESS. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR ANY SPECIAL, DIRECT,
INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES WHATSOEVER RESULTING FROM
LOSS OF USE, DATA OR PROFITS, WHETHER IN AN ACTION OF CONTRACT, NEGLIGENCE OR
OTHER TORTIOUS ACTION, ARISING OUT OF OR IN CONNECTION WITH THE USE OR
PERFORMANCE OF THIS SOFTWARE.
```

### BSD-3-Clause

Applies to the d3 v1/v2 packages listed above and `rw` (© Mike Bostock and
contributors).

```
Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

* Redistributions of source code must retain the above copyright notice, this
  list of conditions and the following disclaimer.

* Redistributions in binary form must reproduce the above copyright notice,
  this list of conditions and the following disclaimer in the documentation
  and/or other materials provided with the distribution.

* Neither the name of the copyright holder nor the names of its contributors
  may be used to endorse or promote products derived from this software
  without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE
FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
```

### 0BSD

Applies to `tslib` (© Microsoft Corporation).

```
Permission to use, copy, modify, and/or distribute this software for any
purpose with or without fee is hereby granted.

THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHOR DISCLAIMS ALL WARRANTIES WITH
REGARD TO THIS SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF MERCHANTABILITY
AND FITNESS. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR ANY SPECIAL, DIRECT,
INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES WHATSOEVER RESULTING FROM
LOSS OF USE, DATA OR PROFITS, WHETHER IN AN ACTION OF CONTRACT, NEGLIGENCE OR
OTHER TORTIOUS ACTION, ARISING OUT OF OR IN CONNECTION WITH THE USE OR
PERFORMANCE OF THIS SOFTWARE.
```

### The Unlicense

Applies to `isbot` and `robust-predicates`.

```
This is free and unencumbered software released into the public domain.

Anyone is free to copy, modify, publish, use, compile, sell, or distribute
this software, either in source code form or as a compiled binary, for any
purpose, commercial or non-commercial, and by any means.

In jurisdictions that recognize copyright laws, the author or authors of this
software dedicate any and all copyright interest in the software to the public
domain. We make this dedication for the benefit of the public at large and to
the detriment of our heirs and successors. We intend this dedication to be an
overt act of relinquishment in perpetuity of all present and future rights to
this software under copyright law.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN
ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

For more information, please refer to <https://unlicense.org/>
```

### Apache License 2.0

Applies to `@chevrotain/types`, and to `dompurify` under the Apache-2.0 option
elected above.

```
                                 Apache License
                           Version 2.0, January 2004
                        http://www.apache.org/licenses/

   TERMS AND CONDITIONS FOR USE, REPRODUCTION, AND DISTRIBUTION

   1. Definitions.

      "License" shall mean the terms and conditions for use, reproduction,
      and distribution as defined by Sections 1 through 9 of this document.

      "Licensor" shall mean the copyright owner or entity authorized by
      the copyright owner that is granting the License.

      "Legal Entity" shall mean the union of the acting entity and all
      other entities that control, are controlled by, or are under common
      control with that entity. For the purposes of this definition,
      "control" means (i) the power, direct or indirect, to cause the
      direction or management of such entity, whether by contract or
      otherwise, or (ii) ownership of fifty percent (50%) or more of the
      outstanding shares, or (iii) beneficial ownership of such entity.

      "You" (or "Your") shall mean an individual or Legal Entity
      exercising permissions granted by this License.

      "Source" form shall mean the preferred form for making modifications,
      including but not limited to software source code, documentation
      source, and configuration files.

      "Object" form shall mean any form resulting from mechanical
      transformation or translation of a Source form, including but
      not limited to compiled object code, generated documentation,
      and conversions to other media types.

      "Work" shall mean the work of authorship, whether in Source or
      Object form, made available under the License, as indicated by a
      copyright notice that is included in or attached to the work
      (an example is provided in the Appendix below).

      "Derivative Works" shall mean any work, whether in Source or Object
      form, that is based on (or derived from) the Work and for which the
      editorial revisions, annotations, elaborations, or other modifications
      represent, as a whole, an original work of authorship. For the purposes
      of this License, Derivative Works shall not include works that remain
      separable from, or merely link (or bind by name) to the interfaces of,
      the Work and Derivative Works thereof.

      "Contribution" shall mean any work of authorship, including
      the original version of the Work and any modifications or additions
      to that Work or Derivative Works thereof, that is intentionally
      submitted to Licensor for inclusion in the Work by the copyright owner
      or by an individual or Legal Entity authorized to submit on behalf of
      the copyright owner. For the purposes of this definition, "submitted"
      means any form of electronic, verbal, or written communication sent
      to the Licensor or its representatives, including but not limited to
      communication on electronic mailing lists, source code control systems,
      and issue tracking systems that are managed by, or on behalf of, the
      Licensor for the purpose of discussing and improving the Work, but
      excluding communication that is conspicuously marked or otherwise
      designated in writing by the copyright owner as "Not a Contribution."

      "Contributor" shall mean Licensor and any individual or Legal Entity
      on behalf of whom a Contribution has been received by Licensor and
      subsequently incorporated within the Work.

   2. Grant of Copyright License. Subject to the terms and conditions of
      this License, each Contributor hereby grants to You a perpetual,
      worldwide, non-exclusive, no-charge, royalty-free, irrevocable
      copyright license to reproduce, prepare Derivative Works of,
      publicly display, publicly perform, sublicense, and distribute the
      Work and such Derivative Works in Source or Object form.

   3. Grant of Patent License. Subject to the terms and conditions of
      this License, each Contributor hereby grants to You a perpetual,
      worldwide, non-exclusive, no-charge, royalty-free, irrevocable
      (except as stated in this section) patent license to make, have made,
      use, offer to sell, sell, import, and otherwise transfer the Work,
      where such license applies only to those patent claims licensable
      by such Contributor that are necessarily infringed by their
      Contribution(s) alone or by combination of their Contribution(s)
      with the Work to which such Contribution(s) was submitted. If You
      institute patent litigation against any entity (including a
      cross-claim or counterclaim in a lawsuit) alleging that the Work
      or a Contribution incorporated within the Work constitutes direct
      or contributory patent infringement, then any patent licenses
      granted to You under this License for that Work shall terminate
      as of the date such litigation is filed.

   4. Redistribution. You may reproduce and distribute copies of the
      Work or Derivative Works thereof in any medium, with or without
      modifications, and in Source or Object form, provided that You
      meet the following conditions:

      (a) You must give any other recipients of the Work or
          Derivative Works a copy of this License; and

      (b) You must cause any modified files to carry prominent notices
          stating that You changed the files; and

      (c) You must retain, in the Source form of any Derivative Works
          that You distribute, all copyright, patent, trademark, and
          attribution notices from the Source form of the Work,
          excluding those notices that do not pertain to any part of
          the Derivative Works; and

      (d) If the Work includes a "NOTICE" text file as part of its
          distribution, then any Derivative Works that You distribute must
          include a readable copy of the attribution notices contained
          within such NOTICE file, excluding those notices that do not
          pertain to any part of the Derivative Works, in at least one
          of the following places: within a NOTICE text file distributed
          as part of the Derivative Works; within the Source form or
          documentation, if provided along with the Derivative Works; or,
          within a display generated by the Derivative Works, if and
          wherever such third-party notices normally appear. The contents
          of the NOTICE file are for informational purposes only and
          do not modify the License. You may add Your own attribution
          notices within Derivative Works that You distribute, alongside
          or as an addendum to the NOTICE text from the Work, provided
          that such additional attribution notices cannot be construed
          as modifying the License.

      You may add Your own copyright statement to Your modifications and
      may provide additional or different license terms and conditions
      for use, reproduction, or distribution of Your modifications, or
      for any such Derivative Works as a whole, provided Your use,
      reproduction, and distribution of the Work otherwise complies with
      the conditions stated in this License.

   5. Submission of Contributions. Unless You explicitly state otherwise,
      any Contribution intentionally submitted for inclusion in the Work
      by You to the Licensor shall be under the terms and conditions of
      this License, without any additional terms or conditions.
      Notwithstanding the above, nothing herein shall supersede or modify
      the terms of any separate license agreement you may have executed
      with Licensor regarding such Contributions.

   6. Trademarks. This License does not grant permission to use the trade
      names, trademarks, service marks, or product names of the Licensor,
      except as required for reasonable and customary use in describing the
      origin of the Work and reproducing the content of the NOTICE file.

   7. Disclaimer of Warranty. Unless required by applicable law or
      agreed to in writing, Licensor provides the Work (and each
      Contributor provides its Contributions) on an "AS IS" BASIS,
      WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or
      implied, including, without limitation, any warranties or conditions
      of TITLE, NON-INFRINGEMENT, MERCHANTABILITY, or FITNESS FOR A
      PARTICULAR PURPOSE. You are solely responsible for determining the
      appropriateness of using or redistributing the Work and assume any
      risks associated with Your exercise of permissions under this License.

   8. Limitation of Liability. In no event and under no legal theory,
      whether in tort (including negligence), contract, or otherwise,
      unless required by applicable law (such as deliberate and grossly
      negligent acts) or agreed to in writing, shall any Contributor be
      liable to You for damages, including any direct, indirect, special,
      incidental, or consequential damages of any character arising as a
      result of this License or out of the use or inability to use the
      Work (including but not limited to damages for loss of goodwill,
      work stoppage, computer failure or malfunction, or any and all
      other commercial damages or losses), even if such Contributor
      has been advised of the possibility of such damages.

   9. Accepting Warranty or Additional Liability. While redistributing
      the Work or Derivative Works thereof, You may choose to offer,
      and charge a fee for, acceptance of support, warranty, indemnity,
      or other liability obligations and/or rights consistent with this
      License. However, in accepting such obligations, You may act only
      on Your own behalf and on Your sole responsibility, not on behalf
      of any other Contributor, and only if You agree to indemnify,
      defend, and hold each Contributor harmless for any liability
      incurred by, or claims asserted against, such Contributor by reason
      of your accepting any such warranty or additional liability.

   END OF TERMS AND CONDITIONS
```
