using Knit.Core.Domain;
using Knit.Core.Frontmatter;
using Knit.Cli.Output;

namespace Knit.Cli.Commands;

/// <summary>
/// The single implementation of "apply <c>field=value</c> to an item", shared by
/// <c>knit set</c> and <c>knit add</c>'s field flags. Both paths validate
/// identically because there is only one copy — a field the create path accepts
/// but the edit path rejects (or vice versa) can't happen.
/// </summary>
public static class FieldSet
{
    /// <param name="Item">The updated item; null when <paramref name="Error"/> is set.</param>
    /// <param name="Display">The normalized value to echo back — slugified slate,
    /// resolved priority wire name — which may differ from what was passed in.</param>
    public readonly record struct Result(Item? Item, string Display, string? Error, ErrorCode Code)
    {
        public static Result Ok(Item item, string display) => new(item, display, null, default);
        public static Result Fail(string error, ErrorCode code) => new(null, "", error, code);
    }

    public static Result Apply(CliContext ctx, Item item, string field, string value, bool force)
    {
        switch (field)
        {
            case "design":
            {
                // Empty clears the field. A non-empty slug must resolve to an
                // existing design doc (active or archived) — otherwise `set`
                // would silently bind a dangling reference that `knit context`
                // then reports as an "Active design" with no file behind it.
                if (!string.IsNullOrEmpty(value))
                {
                    var resolved = Knit.Core.Refs.RefResolver
                        .Resolve(new Knit.Core.Refs.Ref(Knit.Core.Refs.Bucket.Design, value), ctx.Root)
                        .GetValueOrThrow();
                    if (!resolved.Exists)
                        return Result.Fail(
                            $"no design '{value}' found (expected {Knit.Core.Designs.DesignsLayout.DesignDocPath(ctx.Root, value)}); create it with `knit design new {value}`",
                            ErrorCode.NotFound);
                }
                return Result.Ok(item with { Design = string.IsNullOrEmpty(value) ? null : value }, value);
            }

            case "project":
            {
                // Empty clears. The pointer is project-item-only
                // and must resolve to an existing doc folder — same
                // no-dangling-binding rule as `set design=`.
                if (!string.IsNullOrEmpty(value))
                {
                    if (item.Type != ItemType.Project)
                        return Result.Fail($"project is a project-only field; {Out.TitledPlain(ctx, item)} is a {item.Type.ToWire()}", ErrorCode.BadArgument);
                    var readme = Knit.Core.Projects.ProjectsLayout.ReadmePath(ctx.Root, value);
                    if (!File.Exists(readme))
                        return Result.Fail(
                            $"no project doc folder '{value}' found (expected {readme}); create it with `knit project new {value}`",
                            ErrorCode.NotFound);
                }
                return Result.Ok(item with { Project = string.IsNullOrEmpty(value) ? null : value }, value);
            }

            case "slate":
            {
                // Empty clears. A non-empty name is slugified and validated
                // against the slates.md registry — mirroring `set design=` —
                // so a typo doesn't silently mint a new slate. `--force`
                // allows a deliberate forward-ref (the union rule keeps it
                // visible; `knit slate add` can register it later).
                string? slate = null;
                if (!string.IsNullOrEmpty(value))
                {
                    slate = Knit.Core.Ids.Slug.From(value);
                    var known = Knit.Core.Slates.SlateRegistry.Load(ctx.Root)
                        .Any(r => r.Name.Equals(slate, StringComparison.OrdinalIgnoreCase));
                    if (!known && !force)
                        return Result.Fail(
                            $"no slate '{slate}' in the registry; add it with `knit slate add {slate}` or pass --force to forward-reference it",
                            ErrorCode.NotFound);
                }
                return Result.Ok(item with { Slate = slate }, slate ?? "");
            }

            case "due":
            {
                // Empty clears. A non-empty value must be a valid ISO date —
                // reject malformed CLI input loudly (unlike the lenient
                // on-disk read, which degrades to no-due).
                DateOnly? due = null;
                if (!string.IsNullOrEmpty(value))
                {
                    if (!DueDates.TryParse(value, out var parsed))
                        return Result.Fail($"invalid date '{value}' — expected YYYY-MM-DD", ErrorCode.BadArgument);
                    due = parsed;
                }
                return Result.Ok(item with { Due = due }, value);
            }

            case "priority":
            {
                // Empty or "normal" resets to the Normal default; any other
                // value must be a known level. Reject unknown input loudly
                // (the on-disk read is lenient and degrades to normal).
                if (!Priorities.TryParse(value, out var prio))
                    return Result.Fail($"unknown priority '{value}' — expected someday|normal|important|critical", ErrorCode.BadArgument);
                return Result.Ok(item with { Priority = prio }, prio.ToWire());
            }

            case "health":
            {
                // Project-only signal. Empty clears; reject unknown values
                // and non-project items loudly (the on-disk read is lenient,
                // and verify flags hand-edited drift).
                if (!Healths.TryParse(value, out var health))
                    return Result.Fail($"unknown health '{value}' — expected on-track|at-risk|off-track", ErrorCode.BadArgument);
                if (health is not null && item.Type != ItemType.Project)
                    return Result.Fail($"health is a project-only field; {Out.TitledPlain(ctx, item)} is a {item.Type.ToWire()}", ErrorCode.BadArgument);
                return Result.Ok(item with { Health = health }, health?.ToWire() ?? "");
            }

            case "parent":
                // Parent is a relation row in the body `## Links` table, not a
                // frontmatter scalar — `set` serializes scalars, so a
                // `parent=` here would drop on Save (reported success, no
                // persistence). Route users to the link/unlink verbs that own
                // it (with hierarchy + self-parent validation).
                return Result.Fail(
                    string.IsNullOrEmpty(value)
                        ? "'parent' is a link, not a settable field; clear it with `knit unlink <id> parent`"
                        : $"'parent' is a link, not a settable field; set it with `knit link {Out.Short(ctx, item.Id)} --parent {value}`",
                    ErrorCode.BadArgument);

            case "state":
            {
                var normalized = value.Trim().ToLowerInvariant();
                if (normalized == "blocked")
                    return Result.Fail(
                        "'blocked' is no longer a valid state; use `knit block` / `knit unblock` for the orthogonal flag",
                        ErrorCode.BadArgument);
                if (!EnumStrings.TryParseItemState(value, out var newState))
                    return Result.Fail($"unknown state: {value}", ErrorCode.BadArgument);
                return Result.Ok(item with { State = newState }, newState.ToWire());
            }

            // The title lives in two places — keep the leading body
            // H1 in lockstep so the next body save can't clobber it.
            case "title":
                return Result.Ok(item with { Title = value, Body = LeadingH1.SetOrInsert(item.Body, value) }, value);
            case "assignee":
                return Result.Ok(item with { Assignee = string.IsNullOrEmpty(value) ? null : value }, value);
            case "effort":
                return Result.Ok(item with { Effort = string.IsNullOrEmpty(value) ? null : value }, value);
            case "rank":
                return Result.Ok(item with { Rank = string.IsNullOrEmpty(value) ? null : value }, value);
            case "blocked":
                return Result.Ok(item with { BlockedReason = string.IsNullOrEmpty(value) ? null : value }, value);
            case "agent_skills":
                return Result.Ok(
                    item with
                    {
                        AgentSkills = string.IsNullOrEmpty(value)
                            ? []
                            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                    },
                    value);
            // Replace-only (the parser splits on the first `=` — no `+=`/`-=`).
            // Route through the KB Tags chokepoint so item + KB tag hygiene
            // can't drift. Incremental add/remove is a tracked follow-up.
            case "tags":
                return Result.Ok(
                    item with
                    {
                        Tags = string.IsNullOrEmpty(value)
                            ? []
                            : Knit.Core.Kb.Tags.Normalize(value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)),
                    },
                    value);

            default:
                return Result.Fail($"field '{field}' is not user-settable via 'set'", ErrorCode.BadArgument);
        }
    }
}
