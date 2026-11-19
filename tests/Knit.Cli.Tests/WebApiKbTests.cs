using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Knit.Core.Config;
using Knit.Core.Discovery;
using Knit.Core.Domain;
using Knit.Core.Frontmatter;
using Knit.Data;
using Knit.WebApi;
using Knit.WebApi.Contracts;
using Xunit;

namespace Knit.Cli.Tests;

public class WebApiKbTests
{
    [Fact]
    public async Task Kb_endpoints_cover_browse_create_patch_rename_index()
    {
        var root = CreateRepoWithKb();
        try
        {
            var port = GetFreePort();
            var app = WebApiBuilder.Build([], new WebApiOptions
            {
                RootPath = root,
                Port = port,
                OpenBrowser = false,
                EnableStaticFiles = false,
                EnableWatch = false,
                EnableSearch = false,
            });
            await app.StartAsync();
            try
            {
                using var http = TestApi.Client(port);
                // This test covers the KB lifecycle, not optimistic concurrency
                // (WebApiMutationTests owns the 428/412 cases). Force-match every PATCH
                // so the If-Match guard doesn't reject them.
                http.DefaultRequestHeaders.Add("If-Match", "*");
                var opts = new JsonSerializerOptions(JsonSerializerDefaults.Web);

                // Tree includes docs/widget.
                var tree = await http.GetFromJsonAsync<KbTreeNodeDto[]>("/api/kb/tree", opts);
                Assert.NotNull(tree);
                var docsFolder = Array.Find(tree!, n => n.IsFolder && n.Name == "docs");
                Assert.NotNull(docsFolder);
                Assert.Contains(docsFolder!.Children, c => c.Ref == "doc:widget");

                // List returns the same widget summary.
                var list = await http.GetFromJsonAsync<KbSummaryDto[]>("/api/kb/list?bucket=doc", opts);
                Assert.NotNull(list);
                Assert.Contains(list!, s => s.Ref == "doc:widget" && s.Title == "Widget overview");

                // Folder card resolves docs/ and returns indexBody=null (no index yet).
                var folder = await http.GetFromJsonAsync<KbFolderDto>("/api/kb/folder/docs", opts);
                Assert.NotNull(folder);
                Assert.Equal("docs", folder!.RelPath);

                // Entry GET round-trip.
                var entry = await http.GetFromJsonAsync<KbDto>("/api/kb/entry/doc/widget", opts);
                Assert.NotNull(entry);
                Assert.Equal("doc:widget", entry!.Ref);
                Assert.Equal("Widget overview", entry.Title);

                // Create a new ADR.
                var createReq = new KbCreateRequestDto(
                    Bucket: "adr",
                    Title: "Pick storage layer",
                    Slug: null, Summary: null, Feature: null, Date: null,
                    SourceKind: null, Url: null, Path: null);
                var createResp = await http.PostAsJsonAsync("/api/kb/entry", createReq, opts);
                Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);
                var adr = await createResp.Content.ReadFromJsonAsync<KbDto>(opts);
                Assert.NotNull(adr);
                Assert.Equal("adr", adr!.Bucket);
                Assert.StartsWith("0001-", adr.Slug);

                // PATCH updates body + frontmatter. Tags carry duplicates,
                // mixed case, and whitespace — the server normalizes them.
                var patch = new KbPatchRequestDto(
                    Frontmatter: entry.Frontmatter with
                    {
                        Summary = "Updated summary",
                        Title = "Widget, revised",
                        Feature = ["billing"],
                        Tags = ["  api ", "API", "", "db"],
                    },
                    Body: "Body rewritten.\n");
                var patchResp = await http.PatchAsJsonAsync("/api/kb/entry/doc/widget", patch, opts);
                Assert.Equal(HttpStatusCode.OK, patchResp.StatusCode);
                var patched = await patchResp.Content.ReadFromJsonAsync<KbDto>(opts);
                Assert.NotNull(patched);
                Assert.Equal("Updated summary", patched!.Frontmatter.Summary);
                Assert.Equal("Widget, revised", patched.Frontmatter.Title);
                // doc uses scalar feature; the list round-trips back as one element.
                Assert.Equal(["billing"], patched.Frontmatter.Feature);
                Assert.Equal(["api", "db"], patched.Frontmatter.Tags);
                Assert.Contains("Body rewritten", patched.Body);

                // A body-only edit whose leading H1 changed mirrors into the
                // frontmatter title. No frontmatter is sent, so the title
                // is sourced purely from the new H1.
                var h1Patch = new KbPatchRequestDto(
                    Frontmatter: null,
                    Body: "# Widget reworked\n\nReworked body.\n");
                var h1Resp = await http.PatchAsJsonAsync("/api/kb/entry/doc/widget", h1Patch, opts);
                Assert.Equal(HttpStatusCode.OK, h1Resp.StatusCode);
                var h1Patched = await h1Resp.Content.ReadFromJsonAsync<KbDto>(opts);
                Assert.Equal("Widget reworked", h1Patched!.Frontmatter.Title);

                // A frontmatter-title-only edit (the header) mirrors into the
                // leading body H1 (title→H1 direction).
                var titlePatch = new KbPatchRequestDto(
                    Frontmatter: h1Patched.Frontmatter with { Title = "Widget by title" },
                    Body: null);
                var titleResp = await http.PatchAsJsonAsync("/api/kb/entry/doc/widget", titlePatch, opts);
                Assert.Equal(HttpStatusCode.OK, titleResp.StatusCode);
                var titlePatched = await titleResp.Content.ReadFromJsonAsync<KbDto>(opts);
                Assert.Equal("Widget by title", titlePatched!.Frontmatter.Title);
                Assert.StartsWith("# Widget by title", titlePatched.Body);

                // Rename.
                var renameReq = new KbRenameRequestDto(Ref: "doc:widget", NewSlug: "widget-2");
                var renameResp = await http.PostAsJsonAsync("/api/kb/rename", renameReq, opts);
                Assert.Equal(HttpStatusCode.OK, renameResp.StatusCode);
                var renamed = await renameResp.Content.ReadFromJsonAsync<KbRenameResultDto>(opts);
                Assert.NotNull(renamed);
                Assert.Equal("doc:widget-2", renamed!.NewRef);

                // Old slug 404s; new slug fetches OK.
                var oldResp = await http.GetAsync("/api/kb/entry/doc/widget");
                Assert.Equal(HttpStatusCode.NotFound, oldResp.StatusCode);
                var newResp = await http.GetAsync("/api/kb/entry/doc/widget-2");
                Assert.Equal(HttpStatusCode.OK, newResp.StatusCode);

                // Rebuild index files.
                var rebuild = await http.PostAsJsonAsync(
                    "/api/kb/index/rebuild", new { }, opts);
                Assert.Equal(HttpStatusCode.OK, rebuild.StatusCode);
                var rebuiltBody = await rebuild.Content.ReadFromJsonAsync<KbIndexRebuildResultDto>(opts);
                Assert.NotNull(rebuiltBody);
                Assert.NotEmpty(rebuiltBody!.FilesWritten);
            }
            finally
            {
                await app.StopAsync();
                await app.DisposeAsync();
            }
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task Kb_subdoc_create_requires_folder_form_parent()
    {
        var root = CreateRepoWithKb();
        try
        {
            // Add a feature in flat form, and a second one we will split.
            var featuresDir = Path.Combine(root, ".knit", "kb", "features");
            Directory.CreateDirectory(featuresDir);
            File.WriteAllText(Path.Combine(featuresDir, "flat.md"),
                "---\ntitle: Flat feature\nslug: flat\n---\n\nBody.\n");
            var splitDir = Path.Combine(featuresDir, "assembly-line");
            Directory.CreateDirectory(splitDir);
            File.WriteAllText(Path.Combine(splitDir, "readme.md"),
                "---\ntitle: Assembly line\nslug: assembly-line\n---\n\nBody.\n");

            var port = GetFreePort();
            var app = WebApiBuilder.Build([], new WebApiOptions
            {
                RootPath = root,
                Port = port,
                OpenBrowser = false,
                EnableStaticFiles = false,
                EnableWatch = false,
                EnableSearch = false,
            });
            await app.StartAsync();
            try
            {
                using var http = TestApi.Client(port);
                var opts = new JsonSerializerOptions(JsonSerializerDefaults.Web);

                // Sub-doc create against folder-form parent → 201.
                var ok = new KbCreateRequestDto(
                    Bucket: "feature", Title: "Wiring", Slug: "assembly-line/wiring",
                    Summary: null, Feature: null, Date: null,
                    SourceKind: null, Url: null, Path: null);
                var okResp = await http.PostAsJsonAsync("/api/kb/entry", ok, opts);
                Assert.Equal(HttpStatusCode.Created, okResp.StatusCode);
                var created = await okResp.Content.ReadFromJsonAsync<KbDto>(opts);
                Assert.NotNull(created);
                Assert.Equal("feature:assembly-line/wiring", created!.Ref);
                Assert.True(File.Exists(Path.Combine(splitDir, "wiring.md")),
                    "sub-doc file should be at features/assembly-line/wiring.md");

                // Sub-doc create against flat-form parent → 400.
                var flat = new KbCreateRequestDto(
                    Bucket: "feature", Title: "Sub", Slug: "flat/sub",
                    Summary: null, Feature: null, Date: null,
                    SourceKind: null, Url: null, Path: null);
                var flatResp = await http.PostAsJsonAsync("/api/kb/entry", flat, opts);
                Assert.Equal(HttpStatusCode.BadRequest, flatResp.StatusCode);

                // adr ignores a user-supplied slug entirely (it derives
                // NNNN-title), so a nested-looking slug is simply not honored —
                // the created slug carries no slash, hence no path escape.
                var nope = new KbCreateRequestDto(
                    Bucket: "adr", Title: "Nope", Slug: "0001-foo/bar",
                    Summary: null, Feature: null, Date: null,
                    SourceKind: null, Url: null, Path: null);
                var nopeResp = await http.PostAsJsonAsync("/api/kb/entry", nope, opts);
                Assert.Equal(HttpStatusCode.Created, nopeResp.StatusCode);
                var nopeCreated = await nopeResp.Content.ReadFromJsonAsync<KbDto>(opts);
                Assert.DoesNotContain("/", nopeCreated!.Slug);
                Assert.EndsWith("-nope", nopeCreated.Slug);
            }
            finally
            {
                await app.StopAsync();
                await app.DisposeAsync();
            }
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task Kb_rename_supports_nested_slug_and_reverse()
    {
        var root = CreateRepoWithKb();
        try
        {
            var featuresDir = Path.Combine(root, ".knit", "kb", "features");
            Directory.CreateDirectory(featuresDir);
            // Flat sibling feature.
            File.WriteAllText(Path.Combine(featuresDir, "wiring.md"),
                "---\ntitle: Wiring\nslug: wiring\n---\n\nBody.\n");
            // Folder-form parent.
            var parentDir = Path.Combine(featuresDir, "assembly-line");
            Directory.CreateDirectory(parentDir);
            File.WriteAllText(Path.Combine(parentDir, "readme.md"),
                "---\ntitle: Assembly line\nslug: assembly-line\n---\n\nBody.\n");
            // Flat-form feature for the negative test.
            File.WriteAllText(Path.Combine(featuresDir, "flat.md"),
                "---\ntitle: Flat feature\nslug: flat\n---\n\nBody.\n");

            var port = GetFreePort();
            var app = WebApiBuilder.Build([], new WebApiOptions
            {
                RootPath = root,
                Port = port,
                OpenBrowser = false,
                EnableStaticFiles = false,
                EnableWatch = false,
                EnableSearch = false,
            });
            await app.StartAsync();
            try
            {
                using var http = TestApi.Client(port);
                var opts = new JsonSerializerOptions(JsonSerializerDefaults.Web);

                // 1. Nested rename: feature:wiring → feature:assembly-line/wiring.
                var nested = new KbRenameRequestDto(Ref: "feature:wiring",
                    NewSlug: "assembly-line/wiring");
                var nestedResp = await http.PostAsJsonAsync("/api/kb/rename", nested, opts);
                Assert.Equal(HttpStatusCode.OK, nestedResp.StatusCode);
                var nestedResult = await nestedResp.Content.ReadFromJsonAsync<KbRenameResultDto>(opts);
                Assert.NotNull(nestedResult);
                Assert.Equal("feature:assembly-line/wiring", nestedResult!.NewRef);
                Assert.True(File.Exists(Path.Combine(parentDir, "wiring.md")),
                    "renamed entry should land at features/assembly-line/wiring.md");
                Assert.False(File.Exists(Path.Combine(featuresDir, "wiring.md")),
                    "old top-level file should be gone");

                // 2. Reverse: nested → top-level.
                var reverse = new KbRenameRequestDto(
                    Ref: "feature:assembly-line/wiring",
                    NewSlug: "wiring");
                var reverseResp = await http.PostAsJsonAsync("/api/kb/rename", reverse, opts);
                Assert.Equal(HttpStatusCode.OK, reverseResp.StatusCode);
                Assert.True(File.Exists(Path.Combine(featuresDir, "wiring.md")),
                    "entry should be back at top-level");
                Assert.False(File.Exists(Path.Combine(parentDir, "wiring.md")),
                    "nested file should be gone");

                // 3. Negative: rename into flat-form parent → 400 (parent must
                //    be folder-form; flat.md has no readme.md sibling).
                var bad = new KbRenameRequestDto(Ref: "feature:wiring",
                    NewSlug: "flat/wiring");
                var badResp = await http.PostAsJsonAsync("/api/kb/rename", bad, opts);
                Assert.Equal(HttpStatusCode.BadRequest, badResp.StatusCode);

                // 4. Negative: nested target whose parent simply doesn't exist
                //    → 400 (doc bucket allows folder form, but no doc:foo here).
                var badParent = new KbRenameRequestDto(Ref: "doc:widget",
                    NewSlug: "foo/widget");
                var badParentResp = await http.PostAsJsonAsync("/api/kb/rename", badParent, opts);
                Assert.Equal(HttpStatusCode.BadRequest, badParentResp.StatusCode);
            }
            finally
            {
                await app.StopAsync();
                await app.DisposeAsync();
            }
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task Kb_subdoc_create_derives_slug_from_title()
    {
        var root = CreateRepoWithKb();
        try
        {
            // Folder-form feature parent.
            var featuresDir = Path.Combine(root, ".knit", "kb", "features");
            var featParent = Path.Combine(featuresDir, "assembly-line");
            Directory.CreateDirectory(featParent);
            File.WriteAllText(Path.Combine(featParent, "readme.md"),
                "---\ntitle: Assembly line\nslug: assembly-line\n---\n\nBody.\n");
            // Folder-form doc parent.
            var docsDir = Path.Combine(root, ".knit", "kb", "docs");
            var docParent = Path.Combine(docsDir, "manual");
            Directory.CreateDirectory(docParent);
            File.WriteAllText(Path.Combine(docParent, "readme.md"),
                "---\ntitle: Manual\n---\n\nBody.\n");

            var port = GetFreePort();
            var app = WebApiBuilder.Build([], new WebApiOptions
            {
                RootPath = root,
                Port = port,
                OpenBrowser = false,
                EnableStaticFiles = false,
                EnableWatch = false,
                EnableSearch = false,
            });
            await app.StartAsync();
            try
            {
                using var http = TestApi.Client(port);
                var opts = new JsonSerializerOptions(JsonSerializerDefaults.Web);

                // Feature sub-doc, title only. The web UI sends "<parent>/"
                // (prefix + empty leaf) when the slug field is left blank.
                var feat = new KbCreateRequestDto(
                    Bucket: "feature", Title: "Wiring AND Cabling", Slug: "assembly-line/",
                    Summary: null, Feature: null, Date: null,
                    SourceKind: null, Url: null, Path: null);
                var featResp = await http.PostAsJsonAsync("/api/kb/entry", feat, opts);
                Assert.Equal(HttpStatusCode.Created, featResp.StatusCode);
                var featCreated = await featResp.Content.ReadFromJsonAsync<KbDto>(opts);
                Assert.Equal("feature:assembly-line/wiring-and-cabling", featCreated!.Ref);
                Assert.True(File.Exists(Path.Combine(featParent, "wiring-and-cabling.md")));

                // Doc sub-doc, title only.
                var doc = new KbCreateRequestDto(
                    Bucket: "doc", Title: "Getting Started", Slug: "manual/",
                    Summary: null, Feature: null, Date: null,
                    SourceKind: null, Url: null, Path: null);
                var docResp = await http.PostAsJsonAsync("/api/kb/entry", doc, opts);
                Assert.Equal(HttpStatusCode.Created, docResp.StatusCode);
                var docCreated = await docResp.Content.ReadFromJsonAsync<KbDto>(opts);
                Assert.Equal("doc:manual/getting-started", docCreated!.Ref);
                Assert.True(File.Exists(Path.Combine(docParent, "getting-started.md")));

                // Typed leaf overrides the derived one (typed value wins).
                var override_ = new KbCreateRequestDto(
                    Bucket: "feature", Title: "Power Wiring Diagram", Slug: "assembly-line/wiring",
                    Summary: null, Feature: null, Date: null,
                    SourceKind: null, Url: null, Path: null);
                var overrideResp = await http.PostAsJsonAsync("/api/kb/entry", override_, opts);
                Assert.Equal(HttpStatusCode.Created, overrideResp.StatusCode);
                var overrideCreated = await overrideResp.Content.ReadFromJsonAsync<KbDto>(opts);
                Assert.Equal("feature:assembly-line/wiring", overrideCreated!.Ref);

                // Empty leaf with no title → 400 (nothing to derive from).
                var empty = new KbCreateRequestDto(
                    Bucket: "feature", Title: null, Slug: "assembly-line/",
                    Summary: null, Feature: null, Date: null,
                    SourceKind: null, Url: null, Path: null);
                var emptyResp = await http.PostAsJsonAsync("/api/kb/entry", empty, opts);
                Assert.Equal(HttpStatusCode.BadRequest, emptyResp.StatusCode);
            }
            finally
            {
                await app.StopAsync();
                await app.DisposeAsync();
            }
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task Kb_explicit_slugs_are_slugified()
    {
        var root = CreateRepoWithKb();
        try
        {
            var port = GetFreePort();
            var app = WebApiBuilder.Build([], new WebApiOptions
            {
                RootPath = root,
                Port = port,
                OpenBrowser = false,
                EnableStaticFiles = false,
                EnableWatch = false,
                EnableSearch = false,
            });
            await app.StartAsync();
            try
            {
                using var http = TestApi.Client(port);
                var opts = new JsonSerializerOptions(JsonSerializerDefaults.Web);

                // Feature slug is slugified too (no longer stored verbatim).
                var feat = new KbCreateRequestDto(
                    Bucket: "feature", Title: "User Management", Slug: "User Mgmt",
                    Summary: null, Feature: null, Date: null,
                    SourceKind: null, Url: null, Path: null);
                var featResp = await http.PostAsJsonAsync("/api/kb/entry", feat, opts);
                Assert.Equal(HttpStatusCode.Created, featResp.StatusCode);
                var featCreated = await featResp.Content.ReadFromJsonAsync<KbDto>(opts);
                Assert.Equal("feature:user-mgmt", featCreated!.Ref);

                // Title-only feature (inline tree create, no slug field) derives
                // the slug from the title — same fallback as doc.
                var featTitleOnly = new KbCreateRequestDto(
                    Bucket: "feature", Title: "Order Tracking", Slug: null,
                    Summary: null, Feature: null, Date: null,
                    SourceKind: null, Url: null, Path: null);
                var featTitleResp = await http.PostAsJsonAsync("/api/kb/entry", featTitleOnly, opts);
                Assert.Equal(HttpStatusCode.Created, featTitleResp.StatusCode);
                var featTitleCreated = await featTitleResp.Content.ReadFromJsonAsync<KbDto>(opts);
                Assert.Equal("feature:order-tracking", featTitleCreated!.Ref);

                // Typed idea slug with spaces / punctuation / case is slugified.
                var idea = new KbCreateRequestDto(
                    Bucket: "idea", Title: "Some idea", Slug: "My Great Idea!",
                    Summary: null, Feature: null, Date: null,
                    SourceKind: null, Url: null, Path: null);
                var ideaResp = await http.PostAsJsonAsync("/api/kb/entry", idea, opts);
                Assert.Equal(HttpStatusCode.Created, ideaResp.StatusCode);
                var ideaCreated = await ideaResp.Content.ReadFromJsonAsync<KbDto>(opts);
                Assert.Equal("idea:my-great-idea", ideaCreated!.Ref);

                // Idea with no slug derives + slugifies from the title text.
                var ideaDerived = new KbCreateRequestDto(
                    Bucket: "idea", Title: "Another Bright Spark", Slug: null,
                    Summary: null, Feature: null, Date: null,
                    SourceKind: null, Url: null, Path: null);
                var ideaDerivedResp = await http.PostAsJsonAsync("/api/kb/entry", ideaDerived, opts);
                Assert.Equal(HttpStatusCode.Created, ideaDerivedResp.StatusCode);
                var ideaDerivedCreated = await ideaDerivedResp.Content.ReadFromJsonAsync<KbDto>(opts);
                Assert.Equal("idea:another-bright-spark", ideaDerivedCreated!.Ref);

                // Source id is slugified too (and an overlong id is truncated,
                // not rejected, since it can no longer overflow the FS limit).
                var src = new KbCreateRequestDto(
                    Bucket: "source", Title: null, Slug: "RFC 9457",
                    Summary: null, Feature: null, Date: null,
                    SourceKind: "link", Url: "https://example.com", Path: null);
                var srcResp = await http.PostAsJsonAsync("/api/kb/entry", src, opts);
                Assert.Equal(HttpStatusCode.Created, srcResp.StatusCode);
                var srcCreated = await srcResp.Content.ReadFromJsonAsync<KbDto>(opts);
                Assert.Equal("source:rfc-9457", srcCreated!.Ref);
            }
            finally
            {
                await app.StopAsync();
                await app.DisposeAsync();
            }
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task Kb_delete_flat_folder_siblings_and_containment()
    {
        var root = CreateRepoWithKb();
        try
        {
            var featuresDir = Path.Combine(root, ".knit", "kb", "features");
            // Folder-form entry with no siblings.
            var soloDir = Path.Combine(featuresDir, "solo");
            Directory.CreateDirectory(soloDir);
            File.WriteAllText(Path.Combine(soloDir, "readme.md"),
                "---\ntitle: Solo\nslug: solo\n---\n\nBody.\n");
            // Folder-form entry with a sub-doc sibling.
            var comboDir = Path.Combine(featuresDir, "combo");
            Directory.CreateDirectory(comboDir);
            File.WriteAllText(Path.Combine(comboDir, "readme.md"),
                "---\ntitle: Combo\nslug: combo\n---\n\nBody.\n");
            File.WriteAllText(Path.Combine(comboDir, "wiring.md"),
                "---\ntitle: Wiring\nslug: combo/wiring\n---\n\nBody.\n");
            // A file outside .knit/kb that a traversal ref must never reach.
            var outside = Path.Combine(root, "outside-secret.md");
            File.WriteAllText(outside, "secret\n");

            var port = GetFreePort();
            var app = WebApiBuilder.Build([], new WebApiOptions
            {
                RootPath = root,
                Port = port,
                OpenBrowser = false,
                EnableStaticFiles = false,
                EnableWatch = false,
                EnableSearch = false,
            });
            await app.StartAsync();
            try
            {
                using var http = TestApi.Client(port);
                var opts = new JsonSerializerOptions(JsonSerializerDefaults.Web);

                // Dry run reports shape without deleting.
                var dry = await http.PostAsJsonAsync("/api/kb/delete",
                    new KbDeleteRequestDto("doc:widget", false, true), opts);
                Assert.Equal(HttpStatusCode.OK, dry.StatusCode);
                var dryResult = await dry.Content.ReadFromJsonAsync<KbDeleteResultDto>(opts);
                Assert.True(dryResult!.DryRun);
                Assert.False(dryResult.IsFolderForm);
                Assert.Empty(dryResult.BlockingFiles);
                Assert.True(File.Exists(Path.Combine(root, ".knit", "kb", "docs", "widget.md")),
                    "dry run must not delete");

                // Flat delete removes the file.
                var flat = await http.PostAsJsonAsync("/api/kb/delete",
                    new KbDeleteRequestDto("doc:widget", false, false), opts);
                Assert.Equal(HttpStatusCode.OK, flat.StatusCode);
                Assert.False(File.Exists(Path.Combine(root, ".knit", "kb", "docs", "widget.md")));

                // Folder-form with no siblings deletes the whole folder.
                var solo = await http.PostAsJsonAsync("/api/kb/delete",
                    new KbDeleteRequestDto("feature:solo", false, false), opts);
                Assert.Equal(HttpStatusCode.OK, solo.StatusCode);
                var soloResult = await solo.Content.ReadFromJsonAsync<KbDeleteResultDto>(opts);
                Assert.True(soloResult!.DeletedFolder);
                Assert.False(Directory.Exists(soloDir));

                // Folder-form with siblings, deleteFolder=false → 409 + blocker list.
                var blocked = await http.PostAsJsonAsync("/api/kb/delete",
                    new KbDeleteRequestDto("feature:combo", false, false), opts);
                Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
                var blockedResult = await blocked.Content.ReadFromJsonAsync<KbDeleteResultDto>(opts);
                Assert.Contains(blockedResult!.BlockingFiles, f => f.EndsWith("combo/wiring.md"));
                Assert.True(File.Exists(Path.Combine(comboDir, "readme.md")),
                    "blocked delete must leave the folder intact");

                // Folder-form with siblings, deleteFolder=true → recursive remove.
                var folder = await http.PostAsJsonAsync("/api/kb/delete",
                    new KbDeleteRequestDto("feature:combo", true, false), opts);
                Assert.Equal(HttpStatusCode.OK, folder.StatusCode);
                var folderResult = await folder.Content.ReadFromJsonAsync<KbDeleteResultDto>(opts);
                Assert.True(folderResult!.DeletedFolder);
                Assert.False(Directory.Exists(comboDir));

                // Containment: a traversal ref resolving outside .knit/kb is
                // refused without touching the filesystem.
                var escape = await http.PostAsJsonAsync("/api/kb/delete",
                    new KbDeleteRequestDto("feature:../../../outside-secret", false, false), opts);
                Assert.Equal(HttpStatusCode.BadRequest, escape.StatusCode);
                Assert.True(File.Exists(outside), "containment guard must not delete the outside file");
            }
            finally
            {
                await app.StopAsync();
                await app.DisposeAsync();
            }
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task Patch_adr_origin_validates_and_canonicalizes_refs()
    {
        var root = CreateRepoWithKb();
        try
        {
            // A resolvable design for the origin to point at.
            var designsDir = Path.Combine(root, ".knit", "designs");
            Directory.CreateDirectory(designsDir);
            File.WriteAllText(
                Path.Combine(designsDir, "storage-design.md"),
                "---\ntitle: Storage\n---\n\n# Storage\n\nbody\n");

            var port = GetFreePort();
            var app = WebApiBuilder.Build([], new WebApiOptions
            {
                RootPath = root,
                Port = port,
                OpenBrowser = false,
                EnableStaticFiles = false,
                EnableWatch = false,
                EnableSearch = false,
            });
            await app.StartAsync();
            try
            {
                using var http = TestApi.Client(port);
                http.DefaultRequestHeaders.Add("If-Match", "*");
                var opts = new JsonSerializerOptions(JsonSerializerDefaults.Web);

                var createReq = new KbCreateRequestDto(
                    Bucket: "adr", Title: "Pick storage layer",
                    Slug: null, Summary: null, Feature: null, Date: null,
                    SourceKind: null, Url: null, Path: null);
                var createResp = await http.PostAsJsonAsync("/api/kb/entry", createReq, opts);
                Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);
                var adr = (await createResp.Content.ReadFromJsonAsync<KbDto>(opts))!;
                var slug = adr.Slug;

                // Valid origin persists (canonicalized to design:storage).
                var okPatch = new KbPatchRequestDto(
                    Frontmatter: adr.Frontmatter with { Origin = ["design:storage"] },
                    Body: null);
                var okResp = await http.PatchAsJsonAsync($"/api/kb/entry/adr/{slug}", okPatch, opts);
                Assert.Equal(HttpStatusCode.OK, okResp.StatusCode);
                var okDto = (await okResp.Content.ReadFromJsonAsync<KbDto>(opts))!;
                Assert.Equal(["design:storage"], okDto.Frontmatter.Origin);

                // A doc: ref is neither design nor item — rejected.
                var wrongBucket = new KbPatchRequestDto(
                    Frontmatter: adr.Frontmatter with { Origin = ["doc:widget"] },
                    Body: null);
                var wrongResp = await http.PatchAsJsonAsync($"/api/kb/entry/adr/{slug}", wrongBucket, opts);
                Assert.Equal(HttpStatusCode.BadRequest, wrongResp.StatusCode);

                // An unresolvable design is rejected too.
                var dangling = new KbPatchRequestDto(
                    Frontmatter: adr.Frontmatter with { Origin = ["design:ghost"] },
                    Body: null);
                var danglingResp = await http.PatchAsJsonAsync($"/api/kb/entry/adr/{slug}", dangling, opts);
                Assert.Equal(HttpStatusCode.BadRequest, danglingResp.StatusCode);

                // The rejected patches left the good origin intact.
                var after = (await http.GetFromJsonAsync<KbDto>($"/api/kb/entry/adr/{slug}", opts))!;
                Assert.Equal(["design:storage"], after.Frontmatter.Origin);
            }
            finally
            {
                await app.StopAsync();
                await app.DisposeAsync();
            }
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static string CreateRepoWithKb()
    {
        var root = Path.Combine(Path.GetTempPath(), $"knit-kb-{Guid.NewGuid():N}");
        var knitDir = Path.Combine(root, ".knit");
        Directory.CreateDirectory(Path.Combine(knitDir, "items"));
        ConfigLoader.Save(knitDir, new KnitConfig { Handle = "alice", Display = "Alice" });

        var docDir = Path.Combine(knitDir, "kb", "docs");
        Directory.CreateDirectory(docDir);
        File.WriteAllText(Path.Combine(docDir, "widget.md"),
            "---\ntitle: Widget overview\n---\n\n# Widget overview\n\nDescribes the widget subsystem.\n");

        return root;
    }

    private static int GetFreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var p = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return p;
    }

    private static void TryDelete(string root)
    {
        try { Directory.Delete(root, recursive: true); } catch { }
    }
}
