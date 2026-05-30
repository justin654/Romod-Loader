using Candide.CandideUI;
using Candide.CandideUI.Components;
using Candide.CandideUI.Components.Buttons;
using Candide.CandideUI.Containers;
using Candide.CandideUI.PauseMenuUi;
using HarmonyLib;
using Romestead.ModLoader;
using Romestead.StartupHook;

namespace Romestead.ModLoader.ClientCore;

internal static class ModUiSettingsHost
{
    internal const string RootPageId = "romestead.modloader.ui.root";
    internal const string RootSidebarEntryId = "romestead.modloader.ui.sidebar.root";
    private const string PreviewPageId = "romestead.modloader.ui.preview";
    private const string RegistryPageId = "romestead.modloader.ui.registry";
    internal const string LogPageId = "romestead.modloader.ui.log";
    private const string ModDetailPagePrefix = "romestead.modloader.ui.moddetail:";
    private static bool _builtInPagesRegistered;
    private static bool _demoToggleEnabled = true;

    public static void EnsureBuiltInPagesRegistered()
    {
        if (_builtInPagesRegistered)
        {
            return;
        }

        _builtInPagesRegistered = true;

        ModRegistries.Ui.RegisterSettingsPage(new ModSettingsPageDefinition
        {
            Id = RootPageId,
            Title = "Mod Loader",
            Icon = "scroll:red",
            Order = -300,
            Build = _ => BuildRootPage()
        });
        ModRegistries.Ui.RegisterSidebarEntry(new ModSidebarEntryDefinition
        {
            Id = RootSidebarEntryId,
            Title = "Mod Loader",
            Icon = "scroll:red",
            Order = -300,
            TargetPageId = RootPageId
        });

        ModRegistries.Ui.RegisterSettingsPage(new ModSettingsPageDefinition
        {
            Id = PreviewPageId,
            Title = "UI API Preview",
            Icon = "scroll:red",
            Order = -200,
            Build = _ => BuildPreviewPage()
        });

        ModRegistries.Ui.RegisterSettingsPage(new ModSettingsPageDefinition
        {
            Id = RegistryPageId,
            Title = "UI Registry Summary",
            Icon = "scroll:red",
            Order = -190,
            Build = _ => BuildRegistryPage()
        });

        ModRegistries.Ui.RegisterSettingsPage(new ModSettingsPageDefinition
        {
            Id = LogPageId,
            Title = "Mod Loader Log",
            Icon = "scroll:red",
            Order = -180,
            Build = _ => BuildLogPage()
        });
    }

    public static IReadOnlyList<ModSettingsPageDefinition> GetRegisteredPages() =>
        ModRegistries.Ui.Pages;

    public static IReadOnlyList<ModSidebarEntryDefinition> GetRegisteredSidebarEntries() =>
        ModRegistries.Ui.SidebarEntries;

    internal static string GetModDetailPageId(string modId) => $"{ModDetailPagePrefix}{modId}";

    public static void ShowPage(SettingsMainPanel settingsPanel, string pageId)
    {
        var page = ResolvePageDefinition(pageId);

        AccessTools.Method(typeof(SettingsMainPanel), "SetCurrentSetting")
            .Invoke(settingsPanel, [new ModLoaderRegisteredPagePanel(settingsPanel, pageId)]);

        if (AccessTools.Field(typeof(SettingsMainPanel), "_titleIconLabel")
                .GetValue(settingsPanel) is CandideIconLabel title)
        {
            title.Text = page?.Title ?? "Mod Page";
            title.Icon = page?.Icon ?? "scroll:red";
        }
    }

    internal static ModSettingsPageDefinition? ResolvePageDefinition(string pageId)
    {
        var registered = GetRegisteredPages()
            .FirstOrDefault(candidate => string.Equals(candidate.Id, pageId, StringComparison.Ordinal));
        if (registered is not null)
        {
            return registered;
        }

        if (!pageId.StartsWith(ModDetailPagePrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var modId = pageId[ModDetailPagePrefix.Length..];
        var mod = ModLoaderUiState.GetKnownMods()
            .FirstOrDefault(candidate => string.Equals(candidate.Id, modId, StringComparison.OrdinalIgnoreCase));
        if (mod is null)
        {
            return new ModSettingsPageDefinition
            {
                Id = pageId,
                Title = "Mod Details",
                Icon = "scroll:red",
                Order = 0,
                Build = _ => BuildMissingModPage(modId)
            };
        }

        return new ModSettingsPageDefinition
        {
            Id = pageId,
            Title = mod.Name,
            Icon = "scroll:red",
            Order = 0,
            Build = _ => BuildModDetailPage(mod.Id)
        };
    }

    private static ModSettingsPage BuildRootPage()
    {
        var page = new ModSettingsPage();

        page.Sections.Add(new ModSection
        {
            Title = "Overview",
            Rows =
            [
                new ModLabelRow
                {
                    Text = "Manage installed mods, open registered mod pages, and review loader diagnostics.",
                    Style = ModUiTextStyle.Body
                },
                new ModInfoRow { Label = "Loaded mods", Value = ModRegistries.LoadedMods.Mods.Count.ToString(), Style = ModUiTextStyle.BodyStrong },
                new ModInfoRow { Label = "Failed mods", Value = ModRegistries.Diagnostics.FailedMods.Count.ToString() },
                new ModInfoRow { Label = "Registered settings pages", Value = GetRegisteredPages().Count.ToString() },
                new ModInfoRow { Label = "Compatibility entries", Value = (ModRegistries.Diagnostics.LocalCompatibilityReport?.Entries.Count ?? 0).ToString() },
                new ModInfoRow { Label = "Patch groups", Value = ModRegistries.Diagnostics.PatchGroups.Count.ToString() },
                new ModInfoRow { Label = "Unavailable capabilities", Value = ModRegistries.Diagnostics.CapabilityStates.Count(state => state.State == ModCapabilityState.Unavailable).ToString() },
                new ModInfoRow { Label = "Config path", Value = ModRegistries.Diagnostics.ConfigPath }
            ]
        });

        page.Sections.Add(new ModSection
        {
            Title = "Actions",
            Rows =
            [
                new ModToggleRow
                {
                    Label = "Enforce multiplayer compatibility",
                    Description = "Disconnect multiplayer joins after the compatibility report if required mods do not match.",
                    Value = ModRegistries.Diagnostics.EnforceMultiplayerCompatibility,
                    OnChanged = (context, enabled) =>
                    {
                        ModLoaderUiState.WriteLoaderConfig(ModRegistries.Diagnostics.DisabledModIds, enabled);
                        context.RefreshCurrentPage();
                    }
                },
                new ModButtonRow
                {
                    Label = "Open Mod Loader Log",
                    OnClick = context => context.NavigateToPage(LogPageId)
                }
            ]
        });

        var registeredPages = GetRegisteredPages()
            .Where(pageDefinition => !string.Equals(pageDefinition.Id, RootPageId, StringComparison.Ordinal))
            .ToList();
        if (registeredPages.Count > 0)
        {
            var section = new ModSection { Title = "Registered Mod Pages" };
            foreach (var registeredPage in registeredPages)
            {
                section.Rows.Add(new ModNavigateRow
                {
                    Label = registeredPage.Title,
                    TargetPageId = registeredPage.Id
                });
            }

            page.Sections.Add(section);
        }

        page.Sections.Add(BuildCompatibilitySection());
        page.Sections.Add(BuildCapabilitySection());
        page.Sections.Add(BuildRegisteredContentSection());
        page.Sections.Add(BuildInstalledModsSection());

        if (ModRegistries.Diagnostics.DisabledModIds.Count > 0)
        {
            var knownModIds = ModLoaderUiState.GetKnownMods()
                .Select(mod => mod.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var unknownDisabledIds = ModRegistries.Diagnostics.DisabledModIds
                .Where(modId => !knownModIds.Contains(modId))
                .ToList();
            if (unknownDisabledIds.Count > 0)
            {
                page.Sections.Add(new ModSection
                {
                    Title = "Disabled IDs Not Found On Disk",
                    Rows =
                    [
                        new ModListRow
                        {
                            Label = "IDs",
                            Values = unknownDisabledIds
                        }
                    ]
                });
            }
        }

        if (ModRegistries.Diagnostics.SkippedMods.Count > 0)
        {
            var section = new ModSection { Title = "Skipped Mods" };
            foreach (var mod in ModRegistries.Diagnostics.SkippedMods)
            {
                section.Rows.Add(new ModLabelRow { Text = $"{mod.Name} v{mod.Version} ({mod.Id}) SyncMode={mod.SyncMode}", Style = ModUiTextStyle.BodyStrong });
                section.Rows.Add(new ModLabelRow { Text = $"Reason: {mod.Reason}" });
            }

            page.Sections.Add(section);
        }

        if (ModRegistries.Diagnostics.FailedMods.Count > 0)
        {
            var section = new ModSection { Title = "Failed Mods" };
            foreach (var mod in ModRegistries.Diagnostics.FailedMods)
            {
                section.Rows.Add(new ModLabelRow { Text = $"{mod.Name} v{mod.Version} ({mod.Id}) SyncMode={mod.SyncMode}", Style = ModUiTextStyle.BodyStrong });
                section.Rows.Add(new ModLabelRow { Text = $"Reason: {mod.Reason}" });
            }

            page.Sections.Add(section);
        }

        if (ModRegistries.Diagnostics.Errors.Count > 0)
        {
            var section = new ModSection { Title = "Load Errors" };
            foreach (var error in ModRegistries.Diagnostics.Errors)
            {
                section.Rows.Add(new ModLabelRow { Text = error.Source, Style = ModUiTextStyle.BodyStrong });
                section.Rows.Add(new ModLabelRow { Text = error.Message });
            }

            page.Sections.Add(section);
        }

        return page;
    }

    private static ModSection BuildCompatibilitySection()
    {
        var snapshot = ModRegistries.Diagnostics.LatestRemoteCompatibilitySnapshot;
        var comparison = ModRegistries.Diagnostics.LatestRemoteCompatibilityComparison;
        var section = new ModSection { Title = "Latest Multiplayer Compatibility" };

        if (snapshot is null || comparison is null)
        {
            section.Rows.Add(new ModLabelRow
            {
                Text = "No remote host snapshot received yet.",
                Style = ModUiTextStyle.Body
            });
            return section;
        }

        section.Rows.Add(new ModInfoRow { Label = "Remote source", Value = snapshot.Source });
        section.Rows.Add(new ModInfoRow { Label = "Remote entries", Value = snapshot.Entries.Count.ToString() });
        section.Rows.Add(new ModInfoRow { Label = "Result", Value = comparison.Compatible ? "Compatible" : "Incompatible", Style = ModUiTextStyle.BodyStrong });

        if (comparison.Issues.Count == 0)
        {
            section.Rows.Add(new ModLabelRow { Text = "No mismatch issues were reported." });
            return section;
        }

        foreach (var issue in comparison.Issues)
        {
            section.Rows.Add(new ModLabelRow { Text = issue.Message });
        }

        return section;
    }

    private static ModSection BuildRegisteredContentSection()
    {
        return new ModSection
        {
            Title = "Registered Content",
            Rows =
            [
                new ModInfoRow { Label = "Items", Value = ModRegistries.Items.Pending.Count.ToString() },
                new ModInfoRow { Label = "Recipes", Value = ModRegistries.Recipes.Pending.Count.ToString() },
                new ModInfoRow { Label = "Text entries", Value = ModRegistries.Text.Pending.Count.ToString() },
                new ModInfoRow { Label = "Icons", Value = ModRegistries.Icons.Pending.Count.ToString() },
                new ModInfoRow { Label = "Skills", Value = ModRegistries.Skills.Pending.Count.ToString() },
                new ModInfoRow { Label = "Skill effects", Value = ModRegistries.SkillEffects.Pending.Count.ToString() },
                new ModInfoRow { Label = "Aggro tuning rules", Value = ModRegistries.AggroTuning.Pending.Count.ToString() },
                new ModInfoRow { Label = "Player classes", Value = ModRegistries.PlayerClasses.Pending.Count.ToString() },
                new ModInfoRow { Label = "Content diagnostic rows", Value = ModRegistries.Diagnostics.ContentDiagnostics.Count.ToString() }
            ]
        };
    }

    private static ModSection BuildCapabilitySection()
    {
        var section = new ModSection { Title = "Capabilities And Patch Health" };
        if (ModRegistries.Diagnostics.CapabilityStates.Count == 0)
        {
            section.Rows.Add(new ModLabelRow
            {
                Text = "No capability diagnostics have been recorded yet."
            });
            return section;
        }

        foreach (var capability in ModRegistries.Diagnostics.CapabilityStates)
        {
            section.Rows.Add(new ModLabelRow
            {
                Text = $"{capability.Id}: {capability.State}",
                Style = capability.State == ModCapabilityState.Available
                    ? ModUiTextStyle.Body
                    : ModUiTextStyle.BodyStrong
            });
            section.Rows.Add(new ModLabelRow { Text = capability.Summary });
        }

        var failedGroups = ModRegistries.Diagnostics.PatchGroups
            .Where(group => !group.Success)
            .ToList();
        if (failedGroups.Count == 0)
        {
            section.Rows.Add(new ModLabelRow { Text = "All registered patch groups installed cleanly." });
            return section;
        }

        foreach (var group in failedGroups)
        {
            section.Rows.Add(new ModLabelRow
            {
                Text = $"Patch group {group.Id}",
                Style = ModUiTextStyle.BodyStrong
            });
            section.Rows.Add(new ModLabelRow { Text = group.Message });
        }

        return section;
    }

    private static ModSection BuildInstalledModsSection()
    {
        var section = new ModSection { Title = "Installed Mods" };

        foreach (var mod in ModLoaderUiState.GetKnownMods())
        {
            var isEnabled = !ModRegistries.Diagnostics.DisabledModIds.Contains(mod.Id, StringComparer.OrdinalIgnoreCase);
            var reportEntry = ModRegistries.Diagnostics.LocalCompatibilityReport?.Entries
                .FirstOrDefault(entry => string.Equals(entry.Id, mod.Id, StringComparison.OrdinalIgnoreCase));
            var loadState = reportEntry?.LoadState.ToString() ?? "Unknown";

            section.Rows.Add(new ModToggleRow
            {
                Label = $"{mod.Name} v{mod.Version}",
                Description = "Toggle whether this mod loads on the next game launch.",
                Value = isEnabled,
                OnChanged = (context, enabled) =>
                {
                    var nextDisabledIds = ModRegistries.Diagnostics.DisabledModIds
                        .Where(id => !string.Equals(id, mod.Id, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    if (!enabled)
                    {
                        nextDisabledIds.Add(mod.Id);
                    }

                    ModLoaderUiState.WriteLoaderConfig(nextDisabledIds, ModRegistries.Diagnostics.EnforceMultiplayerCompatibility);
                    context.RefreshCurrentPage();
                }
            });
            section.Rows.Add(new ModButtonRow
            {
                Label = $"Open details for {mod.Name}",
                OnClick = context => context.NavigateToPage(GetModDetailPageId(mod.Id))
            });
            section.Rows.Add(new ModLabelRow
            {
                Text = $"{mod.Id} | SyncMode={mod.SyncMode} | LoadState={loadState} | {mod.AssemblyPath}"
            });
        }

        if (section.Rows.Count == 0)
        {
            section.Rows.Add(new ModLabelRow { Text = "No mods discovered yet." });
        }

        return section;
    }

    private static ModSettingsPage BuildMissingModPage(string modId)
    {
        return new ModSettingsPage
        {
            Sections =
            [
                new ModSection
                {
                    Title = "Mod Details",
                    Rows =
                    [
                        new ModLabelRow { Text = "Mod not found", Style = ModUiTextStyle.Title },
                        new ModLabelRow { Text = modId }
                    ]
                }
            ]
        };
    }

    private static ModSettingsPage BuildModDetailPage(string modId)
    {
        var mod = ModLoaderUiState.GetKnownMods()
            .FirstOrDefault(candidate => string.Equals(candidate.Id, modId, StringComparison.OrdinalIgnoreCase));
        if (mod is null)
        {
            return BuildMissingModPage(modId);
        }

        var page = new ModSettingsPage();
        var isDisabled = ModRegistries.Diagnostics.DisabledModIds.Contains(mod.Id, StringComparer.OrdinalIgnoreCase);
        var skipped = ModRegistries.Diagnostics.SkippedMods
            .FirstOrDefault(candidate => string.Equals(candidate.Id, mod.Id, StringComparison.OrdinalIgnoreCase));
        var failed = ModRegistries.Diagnostics.FailedMods
            .FirstOrDefault(candidate => string.Equals(candidate.Id, mod.Id, StringComparison.OrdinalIgnoreCase));
        var content = ModRegistries.Diagnostics.Content
            .FirstOrDefault(candidate => string.Equals(candidate.ModId, mod.Id, StringComparison.OrdinalIgnoreCase));
        var metadata = ModRegistries.Diagnostics.Metadata
            .FirstOrDefault(candidate => string.Equals(candidate.ModId, mod.Id, StringComparison.OrdinalIgnoreCase));
        var compatibility = ModRegistries.Diagnostics.LocalCompatibilityReport?.Entries
            .FirstOrDefault(candidate => string.Equals(candidate.Id, mod.Id, StringComparison.OrdinalIgnoreCase));
        var remoteSnapshotEntry = ModRegistries.Diagnostics.LatestRemoteCompatibilitySnapshot?.Entries
            .FirstOrDefault(candidate => string.Equals(candidate.Id, mod.Id, StringComparison.OrdinalIgnoreCase));
        var remoteIssues = ModRegistries.Diagnostics.LatestRemoteCompatibilityComparison?.Issues
            .Where(issue => string.Equals(issue.ModId, mod.Id, StringComparison.OrdinalIgnoreCase))
            .ToList() ?? [];
        var errors = ModRegistries.Diagnostics.Errors
            .Where(error => error.Source.Contains(mod.Id, StringComparison.OrdinalIgnoreCase) ||
                error.Source.Contains(mod.Name, StringComparison.OrdinalIgnoreCase) ||
                error.Source.Contains(mod.AssemblyPath, StringComparison.OrdinalIgnoreCase))
            .Select(error => error.Message)
            .ToList();
        var diagnostics = ModRegistries.Diagnostics.ContentDiagnostics
            .Where(diagnostic => string.Equals(diagnostic.ModId, mod.Id, StringComparison.OrdinalIgnoreCase))
            .ToList();

        page.Sections.Add(new ModSection
        {
            Title = "Overview",
            Rows =
            [
                new ModInfoRow { Label = "Name", Value = $"{mod.Name} v{mod.Version}", Style = ModUiTextStyle.BodyStrong },
                new ModInfoRow { Label = "ID", Value = mod.Id },
                new ModInfoRow { Label = "Enabled next launch", Value = (!isDisabled).ToString() },
                new ModInfoRow { Label = "Sync mode", Value = mod.SyncMode.ToString() },
                new ModInfoRow { Label = "DLL", Value = mod.AssemblyPath }
            ]
        });

        if (compatibility is not null)
        {
            var section = new ModSection
            {
                Title = "Local Compatibility",
                Rows =
                [
                    new ModInfoRow { Label = "Load state", Value = compatibility.LoadState.ToString() },
                    new ModInfoRow { Label = "Present locally", Value = compatibility.Present.ToString() }
                ]
            };
            if (!string.IsNullOrWhiteSpace(compatibility.Detail))
            {
                section.Rows.Add(new ModInfoRow { Label = "Detail", Value = compatibility.Detail });
            }

            page.Sections.Add(section);
        }

        if (remoteSnapshotEntry is not null || remoteIssues.Count > 0)
        {
            var section = new ModSection { Title = "Remote Compatibility" };
            if (remoteSnapshotEntry is not null)
            {
                section.Rows.Add(new ModInfoRow { Label = "Remote version", Value = remoteSnapshotEntry.Version });
                section.Rows.Add(new ModInfoRow { Label = "Remote load state", Value = remoteSnapshotEntry.LoadState.ToString() });
                section.Rows.Add(new ModInfoRow { Label = "Present on remote", Value = remoteSnapshotEntry.Present.ToString() });
                if (!string.IsNullOrWhiteSpace(remoteSnapshotEntry.Detail))
                {
                    section.Rows.Add(new ModInfoRow { Label = "Remote detail", Value = remoteSnapshotEntry.Detail });
                }
            }

            foreach (var issue in remoteIssues)
            {
                section.Rows.Add(new ModLabelRow { Text = issue.Message });
            }

            page.Sections.Add(section);
        }

        if (metadata is not null)
        {
            var section = new ModSection { Title = "Metadata" };
            if (!string.IsNullOrWhiteSpace(metadata.Author))
            {
                section.Rows.Add(new ModInfoRow { Label = "Author", Value = metadata.Author });
            }
            if (!string.IsNullOrWhiteSpace(metadata.Description))
            {
                section.Rows.Add(new ModInfoRow { Label = "Description", Value = metadata.Description });
            }
            if (!string.IsNullOrWhiteSpace(metadata.Homepage))
            {
                section.Rows.Add(new ModInfoRow { Label = "Homepage", Value = metadata.Homepage });
            }
            section.Rows.Add(new ModListRow
            {
                Label = "Dependencies",
                Values = metadata.Dependencies,
                EmptyText = "None"
            });
            page.Sections.Add(section);
        }

        if (skipped is not null)
        {
            page.Sections.Add(new ModSection
            {
                Title = "Skipped This Launch",
                Rows = [new ModLabelRow { Text = skipped.Reason }]
            });
        }

        if (failed is not null)
        {
            page.Sections.Add(new ModSection
            {
                Title = "Failed This Launch",
                Rows = [new ModLabelRow { Text = failed.Reason }]
            });
        }

        page.Sections.Add(new ModSection
        {
            Title = "Registered Content",
            Rows =
            [
                new ModListRow { Label = "Items", Values = content?.ItemIds ?? [], EmptyText = "None" },
                new ModListRow { Label = "Recipes", Values = content?.RecipeIds ?? [], EmptyText = "None" },
                new ModListRow { Label = "Text", Values = content?.TextIds ?? [], EmptyText = "None" },
                new ModListRow { Label = "Icons", Values = content?.IconIds ?? [], EmptyText = "None" },
                new ModListRow { Label = "Skills", Values = content?.SkillIds ?? [], EmptyText = "None" },
                new ModListRow { Label = "Skill effects", Values = content?.SkillEffectIds ?? [], EmptyText = "None" },
                new ModListRow { Label = "Aggro tuning", Values = content?.AggroTuningIds ?? [], EmptyText = "None" },
                new ModListRow { Label = "Player classes", Values = content?.PlayerClassIds ?? [], EmptyText = "None" }
            ]
        });

        var diagnosticsSection = new ModSection { Title = "Content Diagnostics" };
        if (diagnostics.Count == 0)
        {
            diagnosticsSection.Rows.Add(new ModLabelRow
            {
                Text = "No runtime content diagnostics yet. Open a save or crafting station to trigger more checks."
            });
        }
        else
        {
            foreach (var diagnostic in diagnostics)
            {
                diagnosticsSection.Rows.Add(new ModLabelRow
                {
                    Text = $"{diagnostic.ContentType} {diagnostic.ContentId}: {diagnostic.Status}",
                    Style = ModUiTextStyle.BodyStrong
                });
                diagnosticsSection.Rows.Add(new ModLabelRow { Text = diagnostic.Detail });
            }
        }
        page.Sections.Add(diagnosticsSection);

        if (errors.Count > 0)
        {
            page.Sections.Add(new ModSection
            {
                Title = "Errors",
                Rows = errors.Select(message => new ModLabelRow { Text = message }).Cast<ModUiRow>().ToList()
            });
        }

        return page;
    }

    private static ModSettingsPage BuildPreviewPage()
    {
        return new ModSettingsPage
        {
            Sections =
            [
                new ModSection
                {
                    Title = "Overview",
                    Rows =
                    [
                        new ModLabelRow
                        {
                            Text = "This page is rendered through the declarative mod UI registry.",
                            Style = ModUiTextStyle.Title
                        },
                        new ModInfoRow
                        {
                            Label = "Registered settings pages",
                            Value = ModRegistries.Ui.Pages.Count.ToString()
                        },
                        new ModInfoRow
                        {
                            Label = "Loaded mods",
                            Value = ModRegistries.LoadedMods.Mods.Count.ToString()
                        },
                        new ModInfoRow
                        {
                            Label = "Failed mods",
                            Value = ModRegistries.Diagnostics.FailedMods.Count.ToString()
                        }
                    ]
                },
                new ModSection
                {
                    Title = "Interactive Rows",
                    Rows =
                    [
                        new ModToggleRow
                        {
                            Label = "Preview toggle",
                            Description = "Demonstrates a registry-backed toggle row with safe callback execution.",
                            Value = _demoToggleEnabled,
                            OnChanged = (context, enabled) =>
                            {
                                _demoToggleEnabled = enabled;
                                context.Logger.Info($"[modui] Preview toggle changed: {enabled}");
                                context.RefreshCurrentPage();
                            }
                        },
                        new ModButtonRow
                        {
                            Label = "Write test line to mod log",
                            OnClick = context =>
                            {
                                context.Logger.Info("[modui] Preview button clicked.");
                                context.RefreshCurrentPage();
                            }
                        },
                        new ModButtonRow
                        {
                            Label = "Refresh this page",
                            OnClick = context => context.RefreshCurrentPage()
                        }
                    ]
                },
                new ModSection
                {
                    Title = "Navigation",
                    Rows =
                    [
                        new ModNavigateRow
                        {
                            Label = "Open UI registry summary",
                            TargetPageId = RegistryPageId
                        }
                    ]
                }
            ]
        };
    }

    private static ModSettingsPage BuildRegistryPage()
    {
        var pageIds = ModRegistries.Ui.Pages
            .Select(page => $"{page.Title} ({page.Id})")
            .ToArray();

        return new ModSettingsPage
        {
            Sections =
            [
                new ModSection
                {
                    Title = "Registered Pages",
                    Rows =
                    [
                        new ModListRow
                        {
                            Label = "Pages",
                            Values = pageIds,
                            EmptyText = "No settings pages registered."
                        }
                    ]
                },
                new ModSection
                {
                    Title = "Diagnostics",
                    Rows =
                    [
                        new ModInfoRow
                        {
                            Label = "Content diagnostics",
                            Value = ModRegistries.Diagnostics.ContentDiagnostics.Count.ToString()
                        },
                        new ModInfoRow
                        {
                            Label = "Patch groups",
                            Value = ModRegistries.Diagnostics.PatchGroups.Count.ToString()
                        },
                        new ModInfoRow
                        {
                            Label = "Capabilities",
                            Value = ModRegistries.Diagnostics.CapabilityStates.Count.ToString()
                        },
                        new ModInfoRow
                        {
                            Label = "Compatibility entries",
                            Value = (ModRegistries.Diagnostics.LocalCompatibilityReport?.Entries.Count ?? 0).ToString()
                        },
                        new ModInfoRow
                        {
                            Label = "Log path",
                            Value = ModRegistries.Diagnostics.LogPath
                        }
                    ]
                }
            ]
        };
    }

    private static ModSettingsPage BuildLogPage()
    {
        var rows = new List<ModUiRow>
        {
            new ModInfoRow
            {
                Label = "Log",
                Value = ModRegistries.Diagnostics.LogPath
            }
        };

        rows.AddRange(ReadLogTail(100).Select(line => new ModLabelRow
        {
            Text = line,
            Style = GetLogRowStyle(line)
        }));

        return new ModSettingsPage
        {
            Sections =
            [
                new ModSection
                {
                    Title = "Recent Log Lines",
                    Rows = rows
                }
            ]
        };
    }

    private static IReadOnlyList<string> ReadLogTail(int maxLines)
    {
        var logPath = ModRegistries.Diagnostics.LogPath;
        if (string.IsNullOrWhiteSpace(logPath) || !File.Exists(logPath))
        {
            return ["Log file not found yet."];
        }

        try
        {
            using var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            var lines = new Queue<string>();

            while (reader.ReadLine() is { } line)
            {
                lines.Enqueue(line);
                while (lines.Count > maxLines)
                {
                    lines.Dequeue();
                }
            }

            return lines.Count == 0 ? ["Log file is empty."] : lines.ToArray();
        }
        catch (Exception ex)
        {
            return [$"Failed to read log: {ex.Message}"];
        }
    }

    private static ModUiTextStyle GetLogRowStyle(string line)
    {
        if (line.Contains("[ERROR]", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("[WARN]", StringComparison.OrdinalIgnoreCase))
        {
            return ModUiTextStyle.BodyStrong;
        }

        return ModUiTextStyle.Body;
    }
}

internal sealed class ModLoaderRegisteredPagePanel : CandideUiContainerSingleItem, ISettingsCategoryUi
{
    private readonly SettingsMainPanel _settingsPanel;
    private readonly string _pageId;

    public ModLoaderRegisteredPagePanel(SettingsMainPanel settingsPanel, string pageId)
    {
        _settingsPanel = settingsPanel;
        _pageId = pageId;
        Rebuild();
    }

    public bool HasChanges() => false;
    public void Undo() { }
    public void UserSave() { }

    private void Rebuild()
    {
        var scrollViewer = new CandideVerticalScrollViewer();
        var panel = new CandideVerticalStackPanel
        {
            Margin = new CandideThickness { Left = 20, Top = 12, Right = 20, Bottom = 12 }
        };

        if (!string.Equals(_pageId, ModUiSettingsHost.RootPageId, StringComparison.Ordinal))
        {
            panel.AddChild(CreateBackButton(), false);
        }
        panel.AddChild(CreateRefreshButton(), false);

        var definition = ModUiSettingsHost.ResolvePageDefinition(_pageId);

        if (definition is null)
        {
            panel.AddChild(CreateBodyLabel("Settings page not found.", CandideTextStyle.TitleBold, 8), false);
            panel.AddChild(CreateBodyLabel(_pageId, CandideTextStyle.MainSmall, 8), false);
            scrollViewer.SetChild(panel);
            SetChild(scrollViewer);
            return;
        }

        ModSettingsPage page;
        try
        {
            page = definition.Build(CreateBuildContext());
        }
        catch (Exception ex)
        {
            CoreState.Logger?.Error($"Failed to build settings page '{definition.Id}'.", ex);
            panel.AddChild(CreateBodyLabel($"Failed to build page: {definition.Title}", CandideTextStyle.TitleBold, 8), false);
            panel.AddChild(CreateBodyLabel(ex.Message, CandideTextStyle.MainSmall, 8), false);
            scrollViewer.SetChild(panel);
            SetChild(scrollViewer);
            return;
        }

        var isFirstSection = true;
        foreach (var section in page.Sections)
        {
            RenderSection(panel, section, isFirstSection);
            isFirstSection = false;
        }

        scrollViewer.SetChild(panel);
        SetChild(scrollViewer);
    }

    private void RenderSection(CandideVerticalStackPanel panel, ModSection section, bool isFirstSection)
    {
        if (!isFirstSection)
        {
            panel.AddChild(CreateSectionSeparator(), false);
        }

        if (!string.IsNullOrWhiteSpace(section.Title))
        {
            panel.AddChild(CreateSectionLabel(section.Title), false);
        }

        foreach (var row in section.Rows)
        {
            RenderRow(panel, row);
        }
    }

    private void RenderRow(CandideVerticalStackPanel panel, ModUiRow row)
    {
        switch (row)
        {
            case ModLabelRow label:
                panel.AddChild(CreateBodyLabel(label.Text, MapStyle(label.Style), GetBottomMargin(label.Style)), false);
                break;

            case ModInfoRow info:
                panel.AddChild(CreateBodyLabel(info.Label, CandideTextStyle.MainBold, 2), false);
                panel.AddChild(CreateIndentedLabel(info.Value, MapStyle(info.Style), 10), false);
                break;

            case ModListRow list:
                panel.AddChild(CreateBodyLabel(list.Label, CandideTextStyle.MainBold, 4), false);
                if (list.Values.Count == 0)
                {
                    panel.AddChild(CreateIndentedLabel(list.EmptyText, CandideTextStyle.MainSmall, 10), false);
                }
                else
                {
                    foreach (var value in list.Values)
                    {
                        panel.AddChild(CreateIndentedLabel(value, CandideTextStyle.MainSmall, 6), false);
                    }
                }
                break;

            case ModToggleRow toggle:
                panel.AddChild(CreateToggle(toggle), false);
                break;

            case ModButtonRow button:
                panel.AddChild(CreateButton(button.Label, () => InvokeSafe(button.Label, () => button.OnClick(CreateActionContext()))), false);
                break;

            case ModNavigateRow navigation:
                panel.AddChild(CreateButton(navigation.Label, () => ModUiSettingsHost.ShowPage(_settingsPanel, navigation.TargetPageId)), false);
                break;

            default:
                panel.AddChild(CreateBodyLabel($"Unsupported row type: {row.GetType().Name}", CandideTextStyle.MainSmall, 8), false);
                break;
        }
    }

    private CandideUiElement CreateToggle(ModToggleRow row)
    {
        return CandideUiSettingsHelper.CreateCheckboxSetting(
            row.Value,
            row.Label,
            row.Description ?? "",
            enabled => InvokeSafe(row.Label, () => row.OnChanged(CreateActionContext(), enabled)));
    }

    private NoIconLabelButton CreateBackButton() =>
        CreateButton("Back to Mod Loader", () => ModLoaderSettingsUi.Show(_settingsPanel), 180, 12);

    private NoIconLabelButton CreateRefreshButton() =>
        CreateButton("Refresh Page", Rebuild, 180, 12);

    private static NoIconLabelButton CreateButton(string text, Action onClick, int width = 220, int bottomMargin = 8)
    {
        return new NoIconLabelButton(width)
        {
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new CandideThickness { Bottom = bottomMargin },
            OnClickAction = onClick
        };
    }

    private static CandideTextLabel CreateSectionLabel(string text)
    {
        return new CandideTextLabel
        {
            Text = text,
            TextStyle = CandideTextStyle.MainBold,
            Wrap = true,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new CandideThickness { Top = 4, Bottom = 8 }
        };
    }

    private static CandideHorizontalSeparator CreateSectionSeparator()
    {
        return new CandideHorizontalSeparator
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new CandideThickness { Top = 4, Bottom = 10 }
        };
    }

    private static CandideTextLabel CreateBodyLabel(string text, CandideTextStyle style, int bottomMargin)
    {
        return new CandideTextLabel
        {
            Text = text,
            TextStyle = style,
            Wrap = true,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new CandideThickness { Bottom = bottomMargin }
        };
    }

    private static CandideTextLabel CreateIndentedLabel(string text, CandideTextStyle style, int bottomMargin)
    {
        return new CandideTextLabel
        {
            Text = text,
            TextStyle = style,
            Wrap = true,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new CandideThickness { Left = 12, Bottom = bottomMargin }
        };
    }

    private ModSettingsBuildContext CreateBuildContext()
    {
        var gameRoot = AppContext.BaseDirectory;
        var modRoot = Path.Combine(gameRoot, "romestead_modding", "mods");
        return new ModSettingsBuildContext
        {
            GameRoot = gameRoot,
            ModRoot = modRoot,
            ModDirectory = modRoot,
            Logger = CoreState.Logger ?? throw new InvalidOperationException("Logger is not initialized."),
            Apis = new RegistryBackedApiResolver()
        };
    }

    private ModUiActionContext CreateActionContext()
    {
        return new ModUiActionContext
        {
            Logger = CoreState.Logger ?? throw new InvalidOperationException("Logger is not initialized."),
            NavigateToPage = pageId => ModUiSettingsHost.ShowPage(_settingsPanel, pageId),
            RefreshCurrentPage = Rebuild
        };
    }

    private static CandideTextStyle MapStyle(ModUiTextStyle style) =>
        style switch
        {
            ModUiTextStyle.Title => CandideTextStyle.TitleBold,
            ModUiTextStyle.BodyStrong => CandideTextStyle.MainBold,
            _ => CandideTextStyle.MainSmall
        };

    private static int GetBottomMargin(ModUiTextStyle style) =>
        style switch
        {
            ModUiTextStyle.Title => 8,
            ModUiTextStyle.BodyStrong => 6,
            _ => 4
        };

    private void InvokeSafe(string actionName, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            CoreState.Logger?.Error($"A mod UI callback threw for page '{_pageId}' and action '{actionName}'.", ex);
            Rebuild();
        }
    }

    private sealed class RegistryBackedApiResolver : IModApiResolver
    {
        public bool TryGet<TApi>(out TApi? api) where TApi : class
        {
            api = typeof(TApi) switch
            {
                var type when type == typeof(IItemRegistry) => ModRegistries.Items as TApi,
                var type when type == typeof(IRecipeRegistry) => ModRegistries.Recipes as TApi,
                var type when type == typeof(ITextRegistry) => ModRegistries.Text as TApi,
                var type when type == typeof(IIconRegistry) => ModRegistries.Icons as TApi,
                var type when type == typeof(ISkillRegistry) => ModRegistries.Skills as TApi,
                var type when type == typeof(ISkillEffectRegistry) => ModRegistries.SkillEffects as TApi,
                var type when type == typeof(IPlayerClassRegistry) => ModRegistries.PlayerClasses as TApi,
                var type when type == typeof(IAggroTuningRegistry) => ModRegistries.AggroTuning as TApi,
                var type when type == typeof(IStatRegistry) => ModRegistries.Stats as TApi,
                var type when type == typeof(IContentRegistry) => ModRegistries.Content as TApi,
                var type when type == typeof(IModCapabilityApi) => ModRegistries.Capabilities as TApi,
                var type when type == typeof(IMultiplayerApi) => new MultiplayerApi() as TApi,
                var type when type == typeof(IModUiRegistry) => ModRegistries.Ui as TApi,
                var type when type == typeof(IModOverlayRegistry) => ModRegistries.Overlays as TApi,
                var type when type == typeof(IModWindowRegistry) => ModRegistries.Windows as TApi,
                var type when type == typeof(IModCraftingRegistry) => ModRegistries.Crafting as TApi,
                var type when type == typeof(IModLifecycle) => ModRegistries.Lifecycle as TApi,
                var type when type == typeof(ISceneApi) => ModRegistries.Lifecycle as TApi,
                var type when type == typeof(IWorldMapApi) => CoreState.WorldMap as TApi,
                _ => null
            };

            return api is not null;
        }
    }
}
