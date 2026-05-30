using Romestead.RomodFormat;
using Romestead.RomodFormat.Package;
using Romestead.RomodFormat.Validation;
using System.Globalization;

namespace Romestead.ModLoader.RomodTool;

internal static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0)
            {
                PrintUsage();
                return 1;
            }

            return args[0].ToLowerInvariant() switch
            {
                "init" => RunInit(args[1..]),
                "override" => RunOverride(args[1..]),
                "validate" => RunValidate(args[1..]),
                "pack" => RunPack(args[1..]),
                "-h" or "--help" or "help" => RunHelp(),
                _ => UsageError($"Unknown command '{args[0]}'.")
            };
        }
        catch (RomodFormatException ex)
        {
            Console.Error.WriteLine($"ERROR: {ex.Message}");
            return 10;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"FATAL: {ex}");
            return 99;
        }
    }

    private static int RunHelp()
    {
        PrintUsage();
        return 0;
    }

    private static int UsageError(string message)
    {
        Console.Error.WriteLine(message);
        Console.Error.WriteLine();
        PrintUsage();
        return 2;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("romestead-mod  --  package and validate Romestead .romod packages");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  romestead-mod init     <ModId> [destinationFolder]");
        Console.WriteLine("      Scaffolds a new .romod source folder with a starter manifest and content.");
        Console.WriteLine();
        Console.WriteLine("  romestead-mod override init <ModId> [destinationFolder]");
        Console.WriteLine("      Scaffolds a value-override mod folder for editing existing content.");
        Console.WriteLine();
        Console.WriteLine("  romestead-mod override entity-health <folder> <baseGuid> <maxHealth>");
        Console.WriteLine("      Adds an entity definition max-health override for future spawns.");
        Console.WriteLine();
        Console.WriteLine("  romestead-mod override item <folder> <itemId> [--max-stack N] [--tier N]");
        Console.WriteLine("      Adds an existing item stack/tier override.");
        Console.WriteLine();
        Console.WriteLine("  romestead-mod override weapon <folder> <itemId> [--damage Type Min Max]");
        Console.WriteLine("                                  [--swing-timer N] [--range N] [--knockback N]");
        Console.WriteLine("                                  [--energy-cost N] [--special-energy-cost N]");
        Console.WriteLine("                                  [--stun-power N] [--movement-factor N]");
        Console.WriteLine("      Adds existing weapon value overrides. Repeat --damage for multiple channels.");
        Console.WriteLine();
        Console.WriteLine("  romestead-mod validate <folderOrFile>");
        Console.WriteLine("      Validates either a source folder (will pack to a temp .romod) or an");
        Console.WriteLine("      existing .romod archive. Prints warnings and errors; exit code is non-zero");
        Console.WriteLine("      if any errors are reported.");
        Console.WriteLine();
        Console.WriteLine("  romestead-mod pack     <folder> -o <output.romod>");
        Console.WriteLine("      Packs a source folder into a .romod archive. Refuses to write the output");
        Console.WriteLine("      if validation fails.");
    }

    private static int RunInit(string[] args)
    {
        if (args.Length == 0)
        {
            return UsageError("init: missing required <ModId> argument.");
        }

        var modId = args[0];
        if (string.IsNullOrWhiteSpace(modId))
        {
            return UsageError("init: <ModId> must be a non-empty string.");
        }

        var destination = args.Length >= 2 && !string.IsNullOrWhiteSpace(args[1])
            ? Path.GetFullPath(args[1])
            : Path.GetFullPath(modId);

        if (Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any())
        {
            Console.Error.WriteLine($"init: destination folder is not empty: {destination}");
            return 3;
        }

        Directory.CreateDirectory(destination);
        Directory.CreateDirectory(Path.Combine(destination, "content"));
        Directory.CreateDirectory(Path.Combine(destination, "assets", "icons"));

        File.WriteAllText(Path.Combine(destination, "romestead.mod.toml"),
            "id = \"" + modId + "\"\n" +
            "name = \"" + modId + "\"\n" +
            "version = \"0.1.0\"\n" +
            "schemaVersion = 1\n" +
            "syncMode = \"RequiredOnClient\"\n" +
            "author = \"\"\n" +
            "description = \"\"\n");

        File.WriteAllText(Path.Combine(destination, "content", "example.item.toml"),
            "id = \"material:" + modId.Replace('.', '_') + ":example\"\n" +
            "name = \"Example Item\"\n" +
            "description = \"Replace me with a real item.\"\n" +
            "icon = \"icon:example\"\n" +
            "maxStackSize = 99\n" +
            "tier = 1\n");

        Console.WriteLine($"Scaffolded new .romod source at {destination}");
        Console.WriteLine($"Next steps:");
        Console.WriteLine($"  1. Drop a 32x32 PNG at assets/icons/example.png");
        Console.WriteLine($"  2. Add an icon TOML at content/example.icon.toml that points to it");
        Console.WriteLine($"  3. Run: romestead-mod pack {destination} -o {modId}.romod");
        return 0;
    }

    private static int RunOverride(string[] args)
    {
        if (args.Length == 0)
        {
            return UsageError("override: missing subcommand.");
        }

        return args[0].ToLowerInvariant() switch
        {
            "init" => RunOverrideInit(args[1..]),
            "entity-health" => RunOverrideEntityHealth(args[1..]),
            "item" => RunOverrideItem(args[1..]),
            "weapon" => RunOverrideWeapon(args[1..]),
            _ => UsageError($"override: unknown subcommand '{args[0]}'.")
        };
    }

    private static int RunOverrideInit(string[] args)
    {
        if (args.Length == 0)
        {
            return UsageError("override init: missing required <ModId> argument.");
        }

        var modId = args[0];
        var destination = args.Length >= 2 && !string.IsNullOrWhiteSpace(args[1])
            ? Path.GetFullPath(args[1])
            : Path.GetFullPath(modId);

        if (Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any())
        {
            Console.Error.WriteLine($"override init: destination folder is not empty: {destination}");
            return 3;
        }

        Directory.CreateDirectory(Path.Combine(destination, "content"));
        File.WriteAllText(Path.Combine(destination, "romestead.mod.toml"),
            "id = \"" + EscapeToml(modId) + "\"\n" +
            "name = \"" + EscapeToml(modId) + "\"\n" +
            "version = \"0.1.0\"\n" +
            "schemaVersion = 1\n" +
            "syncMode = \"RequiredOnClient\"\n" +
            "author = \"\"\n" +
            "description = \"Existing content value overrides.\"\n");

        File.WriteAllText(GetOverrideContentPath(destination),
            "# Generated by romestead-mod override.\n" +
            "# Add [[entityHealth]] and [[items]] entries here, or use the override subcommands.\n");

        Console.WriteLine($"Scaffolded value-override mod at {destination}");
        return 0;
    }

    private static int RunOverrideEntityHealth(string[] args)
    {
        if (args.Length < 3)
        {
            return UsageError("override entity-health: expected <folder> <baseGuid> <maxHealth>.");
        }

        var folder = Path.GetFullPath(args[0]);
        if (!Guid.TryParse(args[1], out var baseGuid) || baseGuid == Guid.Empty)
        {
            return UsageError("override entity-health: <baseGuid> must be a non-empty GUID.");
        }

        if (!TryParseFloat(args[2], out var maxHealth) || maxHealth <= 0f)
        {
            return UsageError("override entity-health: <maxHealth> must be greater than zero.");
        }

        EnsureOverrideFolder(folder);
        AppendOverride(folder,
            "\n[[entityHealth]]\n" +
            $"baseId = \"{baseGuid}\"\n" +
            $"maxHealth = {F(maxHealth)}\n");
        Console.WriteLine($"Added entity health override: {baseGuid} maxHealth={F(maxHealth)}");
        return 0;
    }

    private static int RunOverrideItem(string[] args)
    {
        if (args.Length < 2)
        {
            return UsageError("override item: expected <folder> <itemId> [--max-stack N] [--tier N].");
        }

        var folder = Path.GetFullPath(args[0]);
        var itemId = args[1];
        int? maxStack = null;
        int? tier = null;

        for (var i = 2; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--max-stack":
                    if (++i >= args.Length || !int.TryParse(args[i], out var stack) || stack <= 0)
                    {
                        return UsageError("override item: --max-stack requires a positive integer.");
                    }
                    maxStack = stack;
                    break;
                case "--tier":
                    if (++i >= args.Length || !int.TryParse(args[i], out var parsedTier))
                    {
                        return UsageError("override item: --tier requires an integer.");
                    }
                    tier = parsedTier;
                    break;
                default:
                    return UsageError($"override item: unknown option '{args[i]}'.");
            }
        }

        if (maxStack is null && tier is null)
        {
            return UsageError("override item: provide at least one value to override.");
        }

        EnsureOverrideFolder(folder);
        var text = "\n[[items]]\n" +
            $"id = \"{EscapeToml(itemId)}\"\n" +
            (maxStack is { } stackValue ? $"maxStackSize = {stackValue}\n" : "") +
            (tier is { } tierValue ? $"tier = {tierValue}\n" : "");
        AppendOverride(folder, text);
        Console.WriteLine($"Added item override: {itemId}");
        return 0;
    }

    private static int RunOverrideWeapon(string[] args)
    {
        if (args.Length < 2)
        {
            return UsageError("override weapon: expected <folder> <itemId> [weapon options].");
        }

        var folder = Path.GetFullPath(args[0]);
        var itemId = args[1];
        var values = new Dictionary<string, float>(StringComparer.Ordinal);
        var damages = new List<(string Type, float Min, float Max)>();

        for (var i = 2; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--damage":
                    if (i + 3 >= args.Length || !TryParseFloat(args[i + 2], out var min) || !TryParseFloat(args[i + 3], out var max))
                    {
                        return UsageError("override weapon: --damage requires <Type> <Min> <Max>.");
                    }
                    damages.Add((args[i + 1], min, max));
                    i += 3;
                    break;
                case "--swing-timer":
                case "--range":
                case "--knockback":
                case "--energy-cost":
                case "--special-energy-cost":
                case "--stun-power":
                case "--movement-factor":
                    if (++i >= args.Length || !TryParseFloat(args[i], out var value))
                    {
                        return UsageError($"override weapon: {args[i - 1]} requires a number.");
                    }
                    values[args[i - 1]] = value;
                    break;
                default:
                    return UsageError($"override weapon: unknown option '{args[i]}'.");
            }
        }

        if (values.Count == 0 && damages.Count == 0)
        {
            return UsageError("override weapon: provide at least one weapon value to override.");
        }

        EnsureOverrideFolder(folder);
        var text = "\n[[items]]\n" +
            $"id = \"{EscapeToml(itemId)}\"\n\n" +
            "[items.weapon]\n" +
            FormatFloatOption(values, "--swing-timer", "swingTimer") +
            FormatFloatOption(values, "--range", "baseAttackRange") +
            FormatFloatOption(values, "--knockback", "baseKnockback") +
            FormatFloatOption(values, "--energy-cost", "energyCost") +
            FormatFloatOption(values, "--special-energy-cost", "specialEnergyCost") +
            FormatFloatOption(values, "--stun-power", "stunPower") +
            FormatFloatOption(values, "--movement-factor", "movementFactor");

        foreach (var damage in damages)
        {
            text += "\n[[items.weapon.damage]]\n" +
                $"type = \"{EscapeToml(damage.Type)}\"\n" +
                $"min = {F(damage.Min)}\n" +
                $"max = {F(damage.Max)}\n";
        }

        AppendOverride(folder, text);
        Console.WriteLine($"Added weapon override: {itemId}");
        return 0;
    }

    private static int RunValidate(string[] args)
    {
        if (args.Length == 0)
        {
            return UsageError("validate: missing required <folderOrFile> argument.");
        }

        var target = Path.GetFullPath(args[0]);
        var log = new ConsoleRomodLog();
        var pipeline = new RomodPackagePipeline();

        if (Directory.Exists(target))
        {
            // Pack in validate-only mode: it runs the same read+validate pipeline,
            // logs each diagnostic through `log`, and throws on errors. A clean
            // return means the package is valid (it never writes an output file
            // in validate-only mode, so there is nothing to inspect on disk).
            var tempPath = Path.Combine(Path.GetTempPath(), $"romod-validate-{Guid.NewGuid():N}.romod");
            try
            {
                var packResult = RomodPackager.Pack(target, tempPath, log, validateOnly: true);
                Console.WriteLine($"validate: OK ({packResult.Validation.Warnings.Count()} warning(s)).");
                return 0;
            }
            catch (RomodFormatException ex)
            {
                log.Error(ex.Message);
                return 11;
            }
            finally
            {
                try { File.Delete(tempPath); } catch { }
            }
        }

        if (File.Exists(target))
        {
            return PrintValidationResult(pipeline.Run(target, log).Validation);
        }

        return UsageError($"validate: {target} is not a folder or file.");
    }

    private static int PrintValidationResult(RomodValidationResult validation)
    {
        foreach (var diag in validation.Diagnostics)
        {
            switch (diag.Severity)
            {
                case RomodValidationSeverity.Error:
                    Console.Error.WriteLine(diag.ToString());
                    break;
                default:
                    Console.WriteLine(diag.ToString());
                    break;
            }
        }

        if (validation.HasErrors)
        {
            Console.Error.WriteLine($"validate: FAILED with {validation.Errors.Count()} error(s), {validation.Warnings.Count()} warning(s).");
            return 11;
        }

        Console.WriteLine($"validate: OK ({validation.Warnings.Count()} warning(s)).");
        return 0;
    }

    private static int RunPack(string[] args)
    {
        if (args.Length == 0)
        {
            return UsageError("pack: missing required <folder> argument.");
        }

        var folder = Path.GetFullPath(args[0]);
        string? output = null;

        for (var i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-o" or "--output":
                    if (i + 1 >= args.Length)
                    {
                        return UsageError("pack: -o requires a path argument.");
                    }
                    output = Path.GetFullPath(args[++i]);
                    break;
                default:
                    return UsageError($"pack: unknown option '{args[i]}'.");
            }
        }

        if (string.IsNullOrWhiteSpace(output))
        {
            // default: foldername.romod next to the source folder's parent
            var baseName = new DirectoryInfo(folder).Name;
            output = Path.Combine(Path.GetDirectoryName(folder.TrimEnd(Path.DirectorySeparatorChar)) ?? ".",
                                  $"{baseName}.romod");
        }

        var log = new ConsoleRomodLog();
        var result = RomodPackager.Pack(folder, output, log);
        Console.WriteLine($"pack: OK -> {result.OutputPath} ({result.FilesIncluded} files, " +
            $"{result.Validation.Warnings.Count()} warning(s)).");
        return 0;
    }

    private static void EnsureOverrideFolder(string folder)
    {
        if (!File.Exists(Path.Combine(folder, "romestead.mod.toml")))
        {
            throw new RomodFormatException(
                $"Override mod folder is missing romestead.mod.toml: {folder}. Run 'romestead-mod override init <ModId> {folder}' first.");
        }

        Directory.CreateDirectory(Path.Combine(folder, "content"));
        var contentPath = GetOverrideContentPath(folder);
        if (!File.Exists(contentPath))
        {
            File.WriteAllText(contentPath, "# Generated by romestead-mod override.\n");
        }
    }

    private static void AppendOverride(string folder, string text) =>
        File.AppendAllText(GetOverrideContentPath(folder), text);

    private static string GetOverrideContentPath(string folder) =>
        Path.Combine(folder, "content", "value-overrides.value-override.toml");

    private static bool TryParseFloat(string value, out float result) =>
        float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result);

    private static string F(float value) => value.ToString("0.####", CultureInfo.InvariantCulture);

    private static string FormatFloatOption(Dictionary<string, float> values, string option, string tomlKey) =>
        values.TryGetValue(option, out var value) ? $"{tomlKey} = {F(value)}\n" : "";

    private static string EscapeToml(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);

    private sealed class ConsoleRomodLog : IRomodLog
    {
        public void Info(string message) => Console.WriteLine(message);
        public void Warn(string message) => Console.WriteLine($"WARN: {message}");
        public void Error(string message) => Console.Error.WriteLine($"ERROR: {message}");
    }
}
