using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Serialization;
using AgentStandards.Core;
using AgentStandards.Core.Adapters;
using AgentStandards.Core.Diagnostics;
using AgentStandards.Core.Installation;
using AgentStandards.Core.IO;
using AgentStandards.Core.Rendering;
using AgentStandards.Core.Templates;

namespace AgentStandards.Tool;

/// <summary>Command-line front end. All I/O goes through the injected writers so tests can run it in-process.</summary>
public sealed class Cli(ToolEnvironment environment, TextWriter stdout, TextWriter stderr, TextReader stdin)
{
    public const int ExitOk = 0;
    public const int ExitError = 1;
    public const int ExitConflicts = 2;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly StandardsEngine _engine = new(environment);

    // Options shared by every command.
    private readonly Option<string?> _project = new("--project", "-p") { Description = "Repository root to operate on (default: current directory).", Recursive = true };
    private readonly Option<bool> _global = new("--global", "-g") { Description = "Operate on global (per-user) standards instead of a project.", Recursive = true };
    private readonly Option<bool> _json = new("--json") { Description = "Write machine-readable JSON to standard output.", Recursive = true };
    private readonly Option<bool> _verbose = new("--verbose", "-v") { Description = "Show unchanged files and extra detail.", Recursive = true };

    // Install/update options.
    private readonly Option<string[]> _profile = new("--profile") { Description = "Profile(s) to install, e.g. generic, dotnet, maui, webapi. Default: detected from repository files.", AllowMultipleArgumentsPerToken = true };
    private readonly Option<string[]> _assistants = new("--assistants") { Description = "Assistant adapters: claude, copilot (default: all).", AllowMultipleArgumentsPerToken = true };
    private readonly Option<string[]> _exclude = new("--exclude") { Description = "Exclude items, e.g. skill:review-performance, agent:migration-specialist, standard:accessibility, rule:NAME.", AllowMultipleArgumentsPerToken = true };
    private readonly Option<bool> _mcp = new("--mcp") { Description = "Add MCP servers declared by the selected profiles to .mcp.json (existing entries are never changed)." };
    private readonly Option<string?> _templatesVersion = new("--version") { Description = "Templates package version to install, or 'latest'. An explicit version is pinned." };
    private readonly Option<string[]> _source = new("--source", "-s") { Description = "Package source (folder or feed URL) for templates. Default: nuget.config sources.", AllowMultipleArgumentsPerToken = true };
    private readonly Option<bool> _offline = new("--offline") { Description = "Never contact package sources; use bundled, cached or --source folder packages only." };
    private readonly Option<string?> _templates = new("--templates") { Description = "Use a local template directory instead of a package (for custom or forked standards)." };
    private readonly Option<string?> _packageId = new("--package-id") { Description = "Templates package ID (default: AgentStandards.Templates)." };
    private readonly Option<bool> _prerelease = new("--prerelease") { Description = "Allow prerelease versions when resolving 'latest'." };
    private readonly Option<bool> _dryRun = new("--dry-run") { Description = "Show what would change without writing anything." };
    private readonly Option<bool> _force = new("--force") { Description = "Replace locally modified or unmanaged files (a backup is always kept)." };
    private readonly Option<bool> _yes = new("--yes", "-y") { Description = "Do not prompt for confirmation." };
    private readonly Option<bool> _prune = new("--prune") { Description = "Remove managed files the new templates no longer provide (unmodified files only)." };

    public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        var root = new RootCommand("Install, update and validate shared AI coding-assistant standards (Claude Code, GitHub Copilot).");
        root.Options.Add(_project);
        root.Options.Add(_global);
        root.Options.Add(_json);
        root.Options.Add(_verbose);

        root.Subcommands.Add(BuildInstallCommand("init", "Install standards into a repository (or globally with --global).", isUpdate: false));
        root.Subcommands.Add(BuildInstallCommand("update", "Safely update installed standards, preserving local changes.", isUpdate: true));
        root.Subcommands.Add(BuildDiffCommand());
        root.Subcommands.Add(BuildStatusCommand());
        root.Subcommands.Add(BuildValidateCommand());
        root.Subcommands.Add(BuildListCommand());
        root.Subcommands.Add(BuildDoctorCommand());
        root.Subcommands.Add(BuildRollbackCommand());
        root.Subcommands.Add(BuildResolveCommand());
        root.Subcommands.Add(BuildCompleteCommand());

        var parse = root.Parse(args);
        return await parse.InvokeAsync(new InvocationConfiguration { Output = stdout, Error = stderr }, cancellationToken);
    }

    private Command BuildInstallCommand(string name, string description, bool isUpdate)
    {
        var command = new Command(name, description);
        foreach (var option in new Option[] { _profile, _assistants, _exclude, _mcp, _templatesVersion, _source, _offline, _templates, _packageId, _prerelease, _dryRun, _force, _yes })
            command.Options.Add(option);
        if (isUpdate)
            command.Options.Add(_prune);

        command.SetAction((parse, ct) => Guard(parse, async () =>
        {
            var request = BuildRequest(parse, isUpdate);
            var prepared = await _engine.PrepareAsync(request, ct);
            var json = parse.GetValue(_json);
            if (!json)
                WritePlan(prepared, parse.GetValue(_verbose));

            if (request.DryRun || !prepared.Plan.HasChanges)
            {
                if (json)
                    WriteJson(PlanJson(prepared, null, request.DryRun));
                else
                    stdout.WriteLine(request.DryRun ? "Dry run: no files were changed." : "Nothing to do; already up to date.");
                if (!json && prepared.Plan.HasConflicts)
                    stdout.WriteLine(ConflictHint(prepared.Plan));
                return prepared.Plan.HasConflicts ? ExitConflicts : ExitOk;
            }

            if (environment.IsInteractive && !json && !parse.GetValue(_yes) && !Confirm("Apply these changes?"))
            {
                stdout.WriteLine("Cancelled; no files were changed.");
                return ExitOk;
            }

            var execution = StandardsEngine.Apply(prepared, isUpdate);
            if (json)
            {
                WriteJson(PlanJson(prepared, execution, false));
            }
            else
            {
                stdout.WriteLine($"Done: {execution.Written.Count} written, {execution.Deleted.Count} deleted.");
                if (execution.BackupId is not null)
                    stdout.WriteLine($"Backup {execution.BackupId} saved; undo with 'agent-standards rollback'.");
                if (prepared.Plan.HasConflicts)
                    stdout.WriteLine(ConflictHint(prepared.Plan));
            }
            return prepared.Plan.HasConflicts ? ExitConflicts : ExitOk;
        }));
        return command;
    }

    private Command BuildDiffCommand()
    {
        var command = new Command("diff", "Preview the changes 'update' (or 'init' when not installed) would make, as unified diffs.");
        foreach (var option in new Option[] { _profile, _assistants, _exclude, _mcp, _templatesVersion, _source, _offline, _templates, _packageId, _prerelease, _force, _prune })
            command.Options.Add(option);
        command.SetAction((parse, ct) => Guard(parse, async () =>
        {
            var root = Root(parse);
            var installed = ManifestStore.Exists(root);
            var request = BuildRequest(parse, isUpdate: installed, dryRunOverride: true);
            var prepared = await _engine.PrepareAsync(request, ct);
            var diffs = new List<object>();
            foreach (var action in prepared.Plan.Actions)
            {
                string? diff = null;
                if (action.Writes || action.Deletes)
                    diff = TextDiff.Unified(action.Before, action.Deletes ? null : action.After, action.Path);
                else if (action.Kind == PlanActionKind.Conflict && action.Incoming is not null)
                    diff = TextDiff.Unified(action.Before, action.Incoming, action.Path);
                if (string.IsNullOrEmpty(diff))
                    continue;
                diffs.Add(new { path = action.Path, action = action.Kind, diff });
                if (!parse.GetValue(_json))
                {
                    stdout.WriteLine($"# {Label(action.Kind)}: {action.Path} — {action.Reason}");
                    stdout.Write(diff);
                    stdout.WriteLine();
                }
            }

            if (parse.GetValue(_json))
                WriteJson(new { templates = TemplatesJson(prepared.Templates), changes = diffs });
            else if (diffs.Count == 0)
                stdout.WriteLine("No differences.");
            return ExitOk;
        }));
        return command;
    }

    private Command BuildStatusCommand()
    {
        var command = new Command("status", "Show installed versions, profiles and locally modified files.");
        command.SetAction((parse, _) => Guard(parse, () =>
        {
            var report = StatusService.Get(Root(parse), _engine.Resolver.BundledVersion);
            var m = report.Manifest;
            if (parse.GetValue(_json))
            {
                WriteJson(new
                {
                    root = report.Root,
                    scope = m.Scope,
                    toolVersion = environment.ToolVersion,
                    installedWithToolVersion = m.ToolVersion,
                    templates = m.Templates,
                    bundledTemplatesVersion = report.BundledVersion,
                    updateAvailable = report.NewerBundledTemplates,
                    profiles = m.Profiles,
                    resolvedProfiles = m.ResolvedProfiles,
                    assistants = m.Assistants,
                    exclude = m.Exclude,
                    mcp = m.Mcp,
                    files = report.Files,
                    conflictCopies = report.PendingConflicts,
                    unresolvedConflicts = report.Files.Where(f => f.State == FileState.Conflict).Select(f => f.Path),
                    interruptedOperation = report.OperationRunning ? null : report.InterruptedOperation,
                    operationInProgress = report.OperationRunning,
                    unmanagedFiles = report.UnmanagedFiles,
                    backups = report.Backups.Select(b => new { b.Id, b.Operation, b.CreatedAt, b.TemplatesVersionBefore, b.TemplatesVersionAfter })
                });
                return Task.FromResult(report.InterruptedOperation is null || report.OperationRunning ? ExitOk : ExitError);
            }

            stdout.WriteLine($"Root:       {report.Root} ({m.Scope.ToString().ToLowerInvariant()} scope)");
            stdout.WriteLine($"Templates:  {m.Templates.PackageId} {m.Templates.Version} ({m.Templates.Origin}{(m.Templates.Pinned ? ", pinned" : "")})");
            stdout.WriteLine($"Tool:       running {environment.ToolVersion}; installed with {m.ToolVersion}; bundled templates {report.BundledVersion}");
            stdout.WriteLine($"Profiles:   {string.Join(", ", m.Profiles)} (resolved: {string.Join(", ", m.ResolvedProfiles)})");
            stdout.WriteLine($"Assistants: {string.Join(", ", m.Assistants)}");
            if (m.Exclude.Count > 0)
                stdout.WriteLine($"Excluded:   {string.Join(", ", m.Exclude)}");
            var groups = report.Files.GroupBy(f => f.State).ToDictionary(g => g.Key, g => g.Count());
            stdout.WriteLine($"Files:      {report.Files.Count} managed — {string.Join(", ", groups.Select(g => $"{g.Value} {StateLabel(g.Key)}"))}");
            foreach (var file in report.Files.Where(f => f.State != FileState.Unmodified || parse.GetValue(_verbose)))
                stdout.WriteLine($"  {StateLabel(file.State),-14} {file.Path}");
            foreach (var conflict in report.PendingConflicts.Where(c => parse.GetValue(_verbose)))
                stdout.WriteLine($"  {"conflict copy",-14} {conflict}");
            if (report.Files.Any(f => f.State == FileState.Conflict))
                stdout.WriteLine($"Conflicts:  merge the copies under {InstallPlanner.ConflictsFolder}/, then run 'agent-standards resolve <path>' (or --all).");
            if (report.UnmanagedFiles.Count > 0)
            {
                stdout.WriteLine($"Unmanaged:  {report.UnmanagedFiles.Count} assistant configuration file(s) not managed by agent-standards{(parse.GetValue(_verbose) ? ":" : " (--verbose to list)")}");
                foreach (var file in report.UnmanagedFiles.Where(_ => parse.GetValue(_verbose)))
                    stdout.WriteLine($"  {"unmanaged",-14} {file}");
            }
            if (report.OperationRunning)
                stdout.WriteLine("IN PROGRESS: another agent-standards operation is changing this installation; run status again when it finishes.");
            else if (report.InterruptedOperation is { } interrupted)
                stdout.WriteLine($"INTERRUPTED: '{interrupted.Operation}' (backup {interrupted.BackupId}) did not finish. Run 'agent-standards rollback' to restore the previous state, or 'agent-standards complete' to keep it if every change was written.");
            if (report.NewerBundledTemplates)
                stdout.WriteLine($"Update available: templates {report.BundledVersion} (run 'agent-standards update --dry-run').");
            stdout.WriteLine(report.Backups.Count > 0 ? $"Backups:    {report.Backups.Count} (latest {report.Backups[0].Id})" : "Backups:    none");
            return Task.FromResult(report.InterruptedOperation is null || report.OperationRunning ? ExitOk : ExitError);
        }));
        return command;
    }

    private Command BuildValidateCommand()
    {
        var command = new Command("validate", "Validate installed configuration and references, or a template directory with --templates.");
        command.Options.Add(_templates);
        var bundled = new Option<bool>("--bundled") { Description = "Validate the templates bundled with this tool." };
        command.Options.Add(bundled);
        command.SetAction((parse, _) => Guard(parse, () =>
        {
            IReadOnlyList<ValidationIssue> issues;
            string target;
            var templatesDir = parse.GetValue(_templates);
            if (templatesDir is not null || parse.GetValue(bundled))
            {
                var set = templatesDir is not null
                    ? TemplateSet.Create(TemplateLoader.ReadDirectory(templatesDir), TemplateSet.DefaultPackageId, null, templatesDir)
                    : _engine.Resolver.LoadBundled();
                issues = TemplateValidator.Validate(set);
                target = $"templates {set.Source} ({set.Profiles.Count} profiles, {set.Standards.Count} standards, {set.Skills.Count} skills, {set.Agents.Count} agents, {set.Rules.Count} rules)";
            }
            else
            {
                var root = Root(parse);
                issues = InstallationValidator.Validate(root, environment.HomeDirectory);
                target = root;
            }

            var errors = issues.Count(i => i.Severity == Severity.Error);
            if (parse.GetValue(_json))
                WriteJson(new { target, valid = errors == 0, errors, warnings = issues.Count - errors, issues });
            else
            {
                foreach (var issue in issues)
                    (issue.Severity == Severity.Error ? stderr : stdout).WriteLine(issue.ToString());
                stdout.WriteLine(errors == 0
                    ? $"Valid: {target} ({issues.Count} warning(s))."
                    : $"Invalid: {target} — {errors} error(s), {issues.Count - errors} warning(s).");
            }
            return Task.FromResult(errors == 0 ? ExitOk : ExitError);
        }));
        return command;
    }

    private Command BuildListCommand()
    {
        var command = new Command("list", "List available profiles, skills, agents, rules, standards or assistants.");
        var kind = new Argument<string>("kind")
        {
            Description = "profiles | skills | agents | rules | standards | assistants | all",
            Arity = ArgumentArity.ZeroOrOne,
            DefaultValueFactory = _ => "all"
        };
        kind.AcceptOnlyFromAmong("profiles", "skills", "agents", "rules", "standards", "assistants", "all");
        command.Arguments.Add(kind);
        foreach (var option in new Option[] { _templatesVersion, _source, _offline, _templates, _packageId, _prerelease })
            command.Options.Add(option);
        command.SetAction((parse, ct) => Guard(parse, async () =>
        {
            var set = await _engine.Resolver.ResolveAsync(new TemplateRequest
            {
                PackageId = parse.GetValue(_packageId) ?? TemplateSet.DefaultPackageId,
                Version = parse.GetValue(_templatesVersion),
                TemplatesDirectory = parse.GetValue(_templates),
                Sources = parse.GetValue(_source) ?? [],
                Offline = parse.GetValue(_offline),
                IncludePrerelease = parse.GetValue(_prerelease),
                ConfigRoot = Root(parse),
                AllowCacheWrite = false
            }, ct);
            var which = parse.GetValue(kind)!;
            bool Show(string k) => which == "all" || which == k;

            var profiles = set.Profiles.Values.OrderBy(p => p.Id).Select(p => new { p.Id, p.DisplayName, p.Description, p.Extends, detects = p.Detect is not null }).ToList();
            var skills = set.Skills.Values.OrderBy(s => s.Name).Select(s => new { s.Name, s.Description }).ToList();
            var agents = set.Agents.Values.OrderBy(a => a.Id).Select(a => new { a.Id, access = a.Access == AgentAccess.ReadOnly ? "read-only" : "read-write", a.Description }).ToList();
            var rules = set.Rules.Values.OrderBy(r => r.Id).Select(r => new { r.Id, r.Globs, r.Description }).ToList();
            var standards = set.Standards.Select(s => new { id = s, title = ContentBuilder.TitleOf(set.Files[$"standards/{s}.md"], s) }).ToList();
            var assistants = AdapterRegistry.All.Select(a => new { a.Id, a.DisplayName, a.Summary }).ToList();

            if (parse.GetValue(_json))
            {
                WriteJson(new
                {
                    templates = TemplatesJson(set),
                    profiles = Show("profiles") ? profiles : null,
                    skills = Show("skills") ? skills : null,
                    agents = Show("agents") ? agents : null,
                    rules = Show("rules") ? rules : null,
                    standards = Show("standards") ? standards : null,
                    assistants = Show("assistants") ? assistants : null
                });
                return ExitOk;
            }

            stdout.WriteLine($"Templates: {set.PackageId} {set.Version} ({set.Source})");
            if (Show("profiles"))
            {
                stdout.WriteLine("\nProfiles:");
                foreach (var p in profiles)
                    stdout.WriteLine($"  {p.Id,-12} {p.Description}{(p.Extends.Count > 0 ? $" [extends {string.Join(", ", p.Extends)}]" : "")}");
            }
            if (Show("skills"))
            {
                stdout.WriteLine("\nSkills:");
                foreach (var s in skills)
                    stdout.WriteLine($"  {s.Name,-24} {FirstSentence(s.Description)}");
            }
            if (Show("agents"))
            {
                stdout.WriteLine("\nAgents:");
                foreach (var a in agents)
                    stdout.WriteLine($"  {a.Id,-24} ({a.access}) {FirstSentence(a.Description)}");
            }
            if (Show("rules"))
            {
                stdout.WriteLine("\nPath-scoped rules:");
                foreach (var r in rules)
                    stdout.WriteLine($"  {r.Id,-24} {string.Join(", ", r.Globs)}");
            }
            if (Show("standards"))
            {
                stdout.WriteLine("\nStandards:");
                foreach (var s in standards)
                    stdout.WriteLine($"  {s.id,-24} {s.title}");
            }
            if (Show("assistants"))
            {
                stdout.WriteLine("\nAssistants:");
                foreach (var a in assistants)
                    stdout.WriteLine($"  {a.Id,-10} {a.DisplayName}: {a.Summary}");
            }
            return ExitOk;
        }));
        return command;
    }

    private Command BuildDoctorCommand()
    {
        var command = new Command("doctor", "Diagnose installation and environment problems (read-only, no network).");
        command.SetAction((parse, _) => Guard(parse, () =>
        {
            var scope = parse.GetValue(_global) ? InstallScope.Global : InstallScope.Project;
            var checks = Doctor.Run(environment, _engine.Resolver, Root(parse), scope);
            if (parse.GetValue(_json))
                WriteJson(new { healthy = checks.All(c => c.Status != CheckStatus.Error), checks });
            else
                foreach (var check in checks)
                    stdout.WriteLine($"[{check.Status.ToString().ToLowerInvariant(),-7}] {check.Name}: {check.Detail}");
            return Task.FromResult(checks.Any(c => c.Status == CheckStatus.Error) ? ExitError : ExitOk);
        }));
        return command;
    }

    private Command BuildRollbackCommand()
    {
        var command = new Command("rollback", "Undo the most recent init/update (or a specific backup) using its backup snapshot.");
        var to = new Option<string?>("--to") { Description = "Backup ID to roll back (default: most recent)." };
        var list = new Option<bool>("--list") { Description = "List available backups." };
        command.Options.Add(to);
        command.Options.Add(list);
        command.Options.Add(_dryRun);
        command.Options.Add(_force);
        command.Options.Add(_yes);
        command.SetAction((parse, _) => Guard(parse, () =>
        {
            var root = Root(parse);
            var json = parse.GetValue(_json);
            if (parse.GetValue(list))
            {
                var backups = BackupStore.List(root);
                if (json)
                    WriteJson(backups);
                else if (backups.Count == 0)
                    stdout.WriteLine("No backups.");
                else
                    foreach (var b in backups)
                        stdout.WriteLine($"{b.Id}  {b.Operation,-7} templates {b.TemplatesVersionBefore ?? "(none)"} → {b.TemplatesVersionAfter}  {b.Files.Count} file(s)");
                return Task.FromResult(ExitOk);
            }

            var preview = RollbackService.Rollback(root, parse.GetValue(to), dryRun: true, force: parse.GetValue(_force));
            if (!json)
            {
                stdout.WriteLine(preview.RecoversInterruptedOperation
                    ? $"Recovering interrupted {preview.Backup.Operation} {preview.Backup.Id}:"
                    : $"Rolling back {preview.Backup.Operation} {preview.Backup.Id} (templates {preview.Backup.TemplatesVersionAfter} → {preview.Backup.TemplatesVersionBefore ?? "not installed"}):");
                foreach (var action in preview.Actions)
                    stdout.WriteLine($"  {RollbackLabel(action.Kind),-16} {action.Path} — {action.Reason}");
                if (preview.Partial)
                    stdout.WriteLine("Partial rollback: files changed after the operation are kept (see skip-modified); use --force to revert them too.");
            }

            if (parse.GetValue(_dryRun))
            {
                if (json) WriteJson(RollbackJson(preview));
                else stdout.WriteLine("Dry run: no files were changed.");
                return Task.FromResult(preview.Partial ? ExitConflicts : ExitOk);
            }

            if (!parse.GetValue(_yes))
            {
                if (!environment.IsInteractive || json)
                    throw new InvalidOperationException("Rollback changes files; re-run with --yes to confirm (or --dry-run to preview).");
                if (!Confirm("Roll back these files?"))
                {
                    stdout.WriteLine("Cancelled; no files were changed.");
                    return Task.FromResult(ExitOk);
                }
            }

            var result = RollbackService.Rollback(root, preview.Backup.Id, dryRun: false, force: parse.GetValue(_force));
            if (json) WriteJson(RollbackJson(result));
            else
            {
                stdout.WriteLine(result.Partial ? "Rollback complete (partial: some files were kept)." : "Rollback complete.");
                foreach (var warning in result.Warnings)
                    stdout.WriteLine($"note: {warning}");
            }
            return Task.FromResult(result.Partial ? ExitConflicts : ExitOk);
        }));
        return command;
    }

    private Command BuildResolveCommand()
    {
        var command = new Command("resolve",
            "Accept your current file as the merge result of a conflict: the incoming version becomes the new baseline and the conflict copies are removed.");
        var paths = new Argument<string[]>("paths") { Description = "Managed file path(s) with a pending conflict.", Arity = ArgumentArity.ZeroOrMore };
        var all = new Option<bool>("--all") { Description = "Resolve every pending conflict." };
        var acceptLocal = new Option<bool>("--accept-local") { Description = "Also accept files you have not changed since the conflict, discarding the incoming changes." };
        command.Arguments.Add(paths);
        command.Options.Add(all);
        command.Options.Add(acceptLocal);
        command.Options.Add(_dryRun);
        command.Options.Add(_force);
        command.Options.Add(_yes);
        command.SetAction((parse, _) => Guard(parse, () =>
        {
            var root = Root(parse);
            var json = parse.GetValue(_json);
            var selected = parse.GetValue(paths) ?? [];
            var preview = ConflictService.Resolve(root, selected, parse.GetValue(all), dryRun: true, parse.GetValue(_force), parse.GetValue(acceptLocal));

            if (!json)
            {
                foreach (var item in preview.Items)
                    stdout.WriteLine($"  {ResolveLabel(item.State),-10} {item.Path} — {ResolveExplanation(item.State)}");
                if (preview.Items.Count > 0)
                    stdout.WriteLine("Resolving accepts each file exactly as it is now; incoming changes that are not in it are dropped.");
            }

            if (preview.Items.Count == 0)
            {
                if (json) WriteJson(ResolveJson(preview));
                else stdout.WriteLine("No pending conflicts.");
                return Task.FromResult(ExitOk);
            }

            if (preview.Blocked.Count > 0)
            {
                var message = $"Not resolved: {string.Join(", ", preview.Blocked)} unchanged since the conflict was reported. " +
                              $"Merge from {InstallPlanner.ConflictsFolder}/<path>.incoming first, or pass --accept-local to keep your version and discard the incoming changes.";
                if (json) WriteJson(ResolveJson(preview) with { error = message });
                stderr.WriteLine($"error: {message}");
                return Task.FromResult(ExitError);
            }

            if (parse.GetValue(_dryRun))
            {
                if (json) WriteJson(ResolveJson(preview));
                else stdout.WriteLine("Dry run: no files were changed.");
                return Task.FromResult(ExitOk);
            }

            if (!parse.GetValue(_yes))
            {
                if (!environment.IsInteractive || json)
                    throw new InvalidOperationException("resolve accepts your current files as the merge result; re-run with --yes to confirm (or --dry-run to preview).");
                if (!Confirm("Accept these files as resolved?"))
                {
                    stdout.WriteLine("Cancelled; no files were changed.");
                    return Task.FromResult(ExitOk);
                }
            }

            var result = ConflictService.Resolve(root, selected, parse.GetValue(all), dryRun: false, parse.GetValue(_force), parse.GetValue(acceptLocal));
            if (json)
                WriteJson(ResolveJson(result));
            else
            {
                stdout.WriteLine($"Resolved {result.Resolved.Count} conflict(s); these files are now the baseline for future updates.");
                foreach (var copy in result.KeptCopies)
                    stdout.WriteLine($"  kept       {copy} — edited by you; delete it when finished (or re-run with --force)");
                if (result.RemainingConflicts.Count > 0)
                    stdout.WriteLine($"Still unresolved: {string.Join(", ", result.RemainingConflicts)}");
            }
            return Task.FromResult(ExitOk);
        }));
        return command;
    }

    private sealed record ResolveOutput(object resolved, object remaining, object items, object blocked, object keptCopies, bool applied, string? backupId, string? error = null);

    private static ResolveOutput ResolveJson(ResolveResult r) => new(
        r.Resolved, r.RemainingConflicts,
        r.Items.Select(i => new { path = i.Path, state = ResolveLabel(i.State) }), r.Blocked, r.KeptCopies, r.Applied, r.BackupId);

    private static string ResolveLabel(ResolveState state) => state.ToString().ToLowerInvariant();

    private static string ResolveExplanation(ResolveState state) => state switch
    {
        ResolveState.Merged => "changed since the conflict was reported; it will be accepted as your merge",
        ResolveState.Unchanged => "NOT changed since the conflict was reported; accepting it discards the incoming changes (needs --accept-local)",
        ResolveState.Missing => "file or managed block is missing (needs --accept-local)",
        _ => "recorded by an older version; cannot tell whether you merged it"
    };

    private Command BuildCompleteCommand()
    {
        var command = new Command("complete", "Finish an interrupted operation whose changes were all written: verify every file, then clear the journal.");
        command.Options.Add(_dryRun);
        command.SetAction((parse, _) => Guard(parse, () =>
        {
            var result = CompletionService.Complete(Root(parse), parse.GetValue(_dryRun));
            if (parse.GetValue(_json))
            {
                WriteJson(new
                {
                    operation = result.Journal.Operation,
                    backupId = result.Journal.BackupId,
                    verified = result.Verified,
                    applied = result.Applied,
                    checks = result.Checks.Select(c => new { path = c.Path, matches = c.Matches, detail = c.Detail })
                });
            }
            else
            {
                stdout.WriteLine($"Checking interrupted '{result.Journal.Operation}' (backup {result.Journal.BackupId}):");
                foreach (var check in result.Checks.Where(c => !c.Matches || parse.GetValue(_verbose)))
                    stdout.WriteLine($"  {(check.Matches ? "ok" : "MISMATCH"),-9} {check.Path} — {check.Detail}");
                stdout.WriteLine(!result.Verified
                    ? "Not completed: the operation did not finish writing. Run 'agent-standards rollback' to restore the previous state."
                    : result.Applied
                        ? $"Completed: all {result.Checks.Count} change(s) verified; the operation is now finished."
                        : $"Dry run: all {result.Checks.Count} change(s) verified; 'complete' would finish the operation.");
            }
            return Task.FromResult(result.Verified ? ExitOk : ExitError);
        }));
        return command;
    }

    private static object RollbackJson(RollbackResult r) => new
    {
        backupId = r.Backup.Id,
        operation = r.Backup.Operation,
        templatesVersionBefore = r.Backup.TemplatesVersionBefore,
        templatesVersionAfter = r.Backup.TemplatesVersionAfter,
        applied = r.Applied,
        partial = r.Partial,
        recoversInterruptedOperation = r.RecoversInterruptedOperation,
        warnings = r.Warnings,
        actions = r.Actions.Select(a => new { kind = RollbackLabel(a.Kind), path = a.Path, reason = a.Reason, forced = a.Forced })
    };

    private static string RollbackLabel(RollbackActionKind kind) => kind switch
    {
        RollbackActionKind.AlreadyRestored => "already-restored",
        RollbackActionKind.SkipModified => "skip-modified",
        _ => kind.ToString().ToLowerInvariant()
    };

    private InstallRequest BuildRequest(ParseResult parse, bool isUpdate, bool? dryRunOverride = null)
    {
        var global = parse.GetValue(_global);
        var scope = global ? InstallScope.Global : InstallScope.Project;
        return new InstallRequest
        {
            Root = _engine.ResolveRoot(scope, parse.GetValue(_project)),
            Scope = scope,
            IsUpdate = isUpdate,
            Profiles = Split(parse.GetValue(_profile)),
            Assistants = Split(parse.GetValue(_assistants)),
            Exclude = Split(parse.GetValue(_exclude)),
            Mcp = parse.GetResult(_mcp) is not null ? parse.GetValue(_mcp) : null,
            Version = parse.GetValue(_templatesVersion),
            PackageId = parse.GetValue(_packageId),
            TemplatesDirectory = parse.GetValue(_templates) is { } t ? Path.GetFullPath(t) : null,
            Sources = parse.GetValue(_source) ?? [],
            Offline = parse.GetValue(_offline),
            Prerelease = parse.GetValue(_prerelease),
            Force = parse.GetValue(_force),
            Prune = parse.GetResult(_prune) is not null && parse.GetValue(_prune),
            DryRun = dryRunOverride ?? (parse.GetResult(_dryRun) is not null && parse.GetValue(_dryRun))
        };
    }

    private string Root(ParseResult parse) =>
        _engine.ResolveRoot(parse.GetValue(_global) ? InstallScope.Global : InstallScope.Project, parse.GetValue(_project));

    private static IReadOnlyList<string>? Split(string[]? values)
    {
        if (values is null || values.Length == 0)
            return null;
        return values.SelectMany(v => v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).ToList();
    }

    private async Task<int> Guard(ParseResult parse, Func<Task<int>> action)
    {
        try
        {
            return await action();
        }
        catch (OperationCanceledException)
        {
            return Fail(parse, "cancelled.", null);
        }
        catch (Exception ex) when (ex is TemplateException or ManifestException or UnsafePathException or DirectoryNotFoundException
                                       or InvalidOperationException or ArgumentException or IOException or UnauthorizedAccessException
                                       or OperationFailedException or InterruptedOperationException or OperationInProgressException)
        {
            return Fail(parse, ex.Message, ex);
        }
        catch (Exception ex)
        {
            return Fail(parse, $"unexpected error ({ex.GetType().Name}): {ex.Message}. No partial changes are left behind: writes are transactional. Re-run with --verbose for details.", ex);
        }
    }

    private int Fail(ParseResult parse, string message, Exception? ex)
    {
        if (parse.GetValue(_json))
            WriteJson(new { error = message });
        stderr.WriteLine($"error: {message}");
        if (ex is not null && parse.GetValue(_verbose))
            stderr.WriteLine(ex);
        return ExitError;
    }

    private void WritePlan(PreparedOperation prepared, bool verbose)
    {
        var plan = prepared.Plan;
        var t = prepared.Templates;
        stdout.WriteLine($"Target:     {plan.Root} ({plan.Scope.ToString().ToLowerInvariant()} scope)");
        stdout.WriteLine($"Templates:  {t.PackageId} {t.Version} ({t.Source}){(plan.Manifest.Templates.Pinned ? " — pinned" : "")}");
        var detected = prepared.Detection is { } d
            ? d.FellBackToGeneric ? " — no framework detected, using generic" : $" — detected from {string.Join("; ", d.Evidence.Where(e => d.Profiles.Contains(e.Key)).SelectMany(e => e.Value).Take(3))}"
            : "";
        stdout.WriteLine($"Profiles:   {string.Join(", ", prepared.Profiles.Requested)} (resolved: {string.Join(", ", prepared.Profiles.Ordered.Select(p => p.Id))}){detected}");
        stdout.WriteLine($"Assistants: {string.Join(", ", plan.Manifest.Assistants)}");
        if (prepared.Detection is { } scan && (verbose || scan.Truncated))
            stdout.WriteLine($"Detection:  {scan.FilesScanned} files scanned{(scan.Truncated ? " — file limit reached, results may be incomplete; use --profile" : "")}. Detection {scan.Limits}.");
        foreach (var message in prepared.Messages)
            stdout.WriteLine($"note: {message}");
        stdout.WriteLine();

        // JSON merges are always shown: their reason reports MCP servers that were kept as the user defined them.
        foreach (var action in plan.Actions.Where(a => verbose || a.Mode == ManagedMode.JsonMerge || a.Kind is not (PlanActionKind.Unchanged or PlanActionKind.Adopt)))
            stdout.WriteLine($"  {Label(action.Kind),-12} {action.Path}{(action.Kind is PlanActionKind.Create or PlanActionKind.Update || action.Reason == "up to date" ? "" : " — " + action.Reason)}");

        var counts = plan.Actions.GroupBy(a => a.Kind).OrderBy(g => g.Key).Select(g => $"{g.Count()} {Label(g.Key)}");
        stdout.WriteLine();
        stdout.WriteLine($"Summary: {string.Join(", ", counts)}.");
    }

    private object PlanJson(PreparedOperation prepared, ExecutionResult? execution, bool dryRun) => new
    {
        root = prepared.Plan.Root,
        scope = prepared.Plan.Scope,
        dryRun,
        applied = execution is not null,
        templates = TemplatesJson(prepared.Templates),
        pinned = prepared.Plan.Manifest.Templates.Pinned,
        profiles = prepared.Profiles.Requested,
        resolvedProfiles = prepared.Profiles.Ordered.Select(p => p.Id),
        detection = prepared.Detection,
        assistants = prepared.Plan.Manifest.Assistants,
        messages = prepared.Messages,
        hasConflicts = prepared.Plan.HasConflicts,
        actions = prepared.Plan.Actions.Select(a => new { path = a.Path, action = a.Kind, mode = a.Mode, reason = a.Reason, source = a.Source }),
        backupId = execution?.BackupId,
        conflictCopies = execution?.ConflictCopies
    };

    private static string ConflictHint(InstallPlan plan)
    {
        var conflicts = plan.Actions.Where(a => a.Kind == PlanActionKind.Conflict).ToList();
        var managed = conflicts.Count(a => a.Entry is not null);
        var unmanaged = conflicts.Count - managed;
        var parts = new List<string>();
        if (managed > 0)
            parts.Add($"{managed} unresolved conflict(s): local versions kept; merge the copies under {InstallPlanner.ConflictsFolder}/ (*.incoming, *.base) and run 'agent-standards resolve <path>'");
        if (unmanaged > 0)
            parts.Add($"{unmanaged} existing unmanaged file(s) left untouched; rename or remove them, or re-run with --force (a backup is kept)");
        return string.Join(". ", parts) + ".";
    }

    private static object TemplatesJson(TemplateSet set) => new { packageId = set.PackageId, version = set.Version, source = set.Source };

    private void WriteJson(object value) => stdout.WriteLine(JsonSerializer.Serialize(value, JsonOptions));

    private bool Confirm(string question)
    {
        stdout.Write($"{question} [y/N] ");
        var answer = stdin.ReadLine()?.Trim();
        return answer is not null && (answer.Equals("y", StringComparison.OrdinalIgnoreCase) || answer.Equals("yes", StringComparison.OrdinalIgnoreCase));
    }

    private static string Label(PlanActionKind kind) => kind switch
    {
        PlanActionKind.SkipDeleted => "skip-deleted",
        PlanActionKind.KeepLocal => "keep-local",
        PlanActionKind.RemoveBlock => "remove-block",
        _ => kind.ToString().ToLowerInvariant()
    };

    private static string StateLabel(FileState state) => state switch
    {
        FileState.BlockMissing => "block-removed",
        _ => state.ToString().ToLowerInvariant()
    };

    private static string FirstSentence(string text)
    {
        var line = string.Join(' ', text.Split('\n', StringSplitOptions.TrimEntries));
        var dot = line.IndexOf(". ", StringComparison.Ordinal);
        return dot > 0 ? line[..(dot + 1)] : line;
    }
}
