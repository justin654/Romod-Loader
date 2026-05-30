using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Romestead.ModLoader.Installer;

internal static class Program
{
    private const string BackupSuffix = ".modloader-backup";
    private const string HookAssemblyName = "Romestead.StartupHook";
    private const string HookTypeFullName = "StartupHook";
    private const string HookMethodName = "Initialize";
    private const string HookPackageKey = "Romestead.StartupHook/1.0.0";
    private const string HookAssemblyVersion = "1.0.0.0";

    // The mod-facing contract now ships as its own assembly (was previously linked
    // into StartupHook.dll). It must be registered in deps.json too so the .NET host
    // adds it to the TPA / Default load context — otherwise the loader and the mods
    // would bind to different copies and the `mod is IRomesteadMod` check would fail.
    private const string AbstractionsAssemblyName = "Romestead.ModLoader.Abstractions";
    private const string AbstractionsPackageKey = "Romestead.ModLoader.Abstractions/1.0.0";
    private const string AbstractionsAssemblyVersion = "1.0.0.0";

    private static readonly InstallTarget ClientTarget = new(
        "Romestead.dll",
        "Romestead.deps.json",
        "Candide.Program",
        "Main",
        "Romestead client");

    public static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            PrintUsage();
            return 1;
        }

        try
        {
            var verb = args[0].ToLowerInvariant();
            var gameRoot = Path.GetFullPath(args[1]);
            if (!Directory.Exists(gameRoot))
            {
                Console.Error.WriteLine($"Game root does not exist: {gameRoot}");
                return 2;
            }

            return verb switch
            {
                "install" when args.Length >= 3 => Install(gameRoot, Path.GetFullPath(args[2]), ClientTarget),
                "install-target" when args.Length >= 7 => Install(
                    gameRoot,
                    Path.GetFullPath(args[2]),
                    new InstallTarget(args[3], args[4], args[5], args[6], args[3])),
                "uninstall" => Uninstall(gameRoot, ClientTarget),
                "uninstall-target" when args.Length >= 4 => Uninstall(
                    gameRoot,
                    new InstallTarget(args[2], args[3], "", "", args[2])),
                "status" => Status(gameRoot, ClientTarget),
                _ => UsageError()
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"FATAL: {ex}");
            return 99;
        }
    }

    private static int UsageError()
    {
        PrintUsage();
        return 1;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Romestead Mod Loader Installer");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  install         <gameRoot> <hookDir>");
        Console.WriteLine("  install-target  <gameRoot> <hookDir> <targetDll> <targetDeps> <entryType> <entryMethod>");
        Console.WriteLine("  uninstall       <gameRoot>");
        Console.WriteLine("  uninstall-target <gameRoot> <targetDll> <targetDeps>");
        Console.WriteLine("  status          <gameRoot>");
    }

    private static int Install(string gameRoot, string hookDir, InstallTarget target)
    {
        var targetDll = Path.Combine(gameRoot, target.TargetDllName);
        if (!File.Exists(targetDll))
        {
            Console.Error.WriteLine($"{target.TargetDllName} not found at {targetDll}");
            return 3;
        }

        if (!Directory.Exists(hookDir))
        {
            Console.Error.WriteLine($"Hook directory not found: {hookDir}");
            return 4;
        }

        var hookDll = Path.Combine(hookDir, HookAssemblyName + ".dll");
        if (!File.Exists(hookDll))
        {
            Console.Error.WriteLine($"{HookAssemblyName}.dll not found in {hookDir}");
            return 5;
        }

        var targetDeps = Path.Combine(gameRoot, target.TargetDepsName);
        if (!File.Exists(targetDeps))
        {
            Console.Error.WriteLine($"{target.TargetDepsName} not found at {targetDeps}");
            return 6;
        }

        RestoreOrBackup(targetDll, targetDll + BackupSuffix, target.TargetDllName, IsDllPatched(targetDll, target));
        RestoreOrBackup(targetDeps, targetDeps + BackupSuffix, target.TargetDepsName, IsDepsPatched(targetDeps));

        CopyHookFiles(hookDir, gameRoot, target.TargetDllName);
        PatchEntryMethod(targetDll, hookDll, gameRoot, target);
        PatchDepsJson(targetDeps, target);

        Console.WriteLine();
        Console.WriteLine($"Install complete for {target.DisplayName}.");
        return 0;
    }

    private static void RestoreOrBackup(string targetPath, string backupPath, string label, bool liveIsPatched)
    {
        if (!liveIsPatched)
        {
            // The live file is unpatched, so it is the authoritative vanilla copy. This covers the
            // first install AND the case where Steam updated the game after a previous install: the
            // backup may be stale, so we refresh it from the current file rather than restoring an
            // older vanilla over a freshly-updated one (which would crash with assembly skew).
            File.Copy(targetPath, backupPath, overwrite: true);
            Console.WriteLine(File.Exists(backupPath)
                ? $"Backed up vanilla {label} -> {Path.GetFileName(backupPath)} (refreshed from current unpatched file)."
                : $"Backed up original {label} -> {Path.GetFileName(backupPath)}");
            return;
        }

        // The live file is already patched. Recover the pristine vanilla from the backup before
        // re-patching. If no backup exists we cannot recover the original, so refuse rather than
        // patch an already-patched (or otherwise unknown) assembly.
        if (!File.Exists(backupPath))
        {
            throw new InvalidOperationException(
                $"{label} appears to be already patched but no backup ({Path.GetFileName(backupPath)}) exists. " +
                "Cannot recover the original. Use Steam's 'Verify integrity of game files' to restore it, then re-run install.");
        }

        File.Copy(backupPath, targetPath, overwrite: true);
        Console.WriteLine($"Restored {label} from existing backup before re-patching.");
    }

    private static bool IsDllPatched(string dllPath, InstallTarget target)
    {
        try
        {
            using var module = ModuleDefinition.ReadModule(dllPath);
            var entryType = module.Types.FirstOrDefault(type => type.FullName == target.EntryTypeName);
            var entryMethod = entryType?.Methods.FirstOrDefault(method => method.Name == target.EntryMethodName);
            return entryMethod is not null && IsAlreadyPatched(entryMethod);
        }
        catch
        {
            // If we cannot read the module we treat it as unpatched; the subsequent patch step will
            // surface any real problem with a clearer error.
            return false;
        }
    }

    private static bool IsDepsPatched(string depsPath)
    {
        try
        {
            var root = JsonNode.Parse(File.ReadAllText(depsPath)) as JsonObject;
            if (root?["libraries"] is JsonObject libraries && libraries.ContainsKey(HookPackageKey))
            {
                return true;
            }

            if (root?["targets"] is JsonObject targets)
            {
                foreach (var (_, targetNode) in targets)
                {
                    if (targetNode is JsonObject targetObj && targetObj.ContainsKey(HookPackageKey))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private static int Uninstall(string gameRoot, InstallTarget target)
    {
        var targetDll = Path.Combine(gameRoot, target.TargetDllName);
        var targetDeps = Path.Combine(gameRoot, target.TargetDepsName);
        var dllBackup = targetDll + BackupSuffix;
        var depsBackup = targetDeps + BackupSuffix;

        var anyRestored = false;
        if (File.Exists(dllBackup))
        {
            File.Copy(dllBackup, targetDll, overwrite: true);
            File.Delete(dllBackup);
            Console.WriteLine($"Restored {target.TargetDllName} and removed backup.");
            anyRestored = true;
        }

        if (File.Exists(depsBackup))
        {
            File.Copy(depsBackup, targetDeps, overwrite: true);
            File.Delete(depsBackup);
            Console.WriteLine($"Restored {target.TargetDepsName} and removed backup.");
            anyRestored = true;
        }

        if (!anyRestored)
        {
            Console.Error.WriteLine("No backups found - nothing to restore. (Was it ever installed?)");
            return 10;
        }

        Console.WriteLine("Note: Romestead.StartupHook.dll and its dependencies remain in the target folder. Delete them manually if you want a fully clean uninstall.");
        return 0;
    }

    private static int Status(string gameRoot, InstallTarget target)
    {
        var targetDll = Path.Combine(gameRoot, target.TargetDllName);
        var backup = targetDll + BackupSuffix;
        Console.WriteLine($"Game root: {gameRoot}");
        Console.WriteLine($"  {target.TargetDllName} exists: {File.Exists(targetDll)}");
        Console.WriteLine($"  Backup exists:         {File.Exists(backup)}");

        if (!File.Exists(targetDll))
        {
            return 0;
        }

        using var module = ModuleDefinition.ReadModule(targetDll);
        var entryType = module.Types.FirstOrDefault(type => type.FullName == target.EntryTypeName);
        var entryMethod = entryType?.Methods.FirstOrDefault(method => method.Name == target.EntryMethodName);
        if (entryMethod is null)
        {
            Console.WriteLine($"  Entry point {target.EntryTypeName}.{target.EntryMethodName}: NOT FOUND");
            return 0;
        }

        var patched = IsAlreadyPatched(entryMethod);
        Console.WriteLine($"  Entry point patched:   {(patched ? "YES" : "no")}");
        return 0;
    }

    private static void CopyHookFiles(string hookDir, string gameRoot, string targetDllName)
    {
        var patterns = new[] { "*.dll", "*.pdb", "*.deps.json" };
        var copied = 0;
        foreach (var pattern in patterns)
        {
            foreach (var src in Directory.GetFiles(hookDir, pattern, SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileName(src);
                if (string.Equals(name, targetDllName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var dest = Path.Combine(gameRoot, name);
                File.Copy(src, dest, overwrite: true);
                Console.WriteLine($"  copied: {name}");
                copied++;
            }
        }

        Console.WriteLine($"Copied {copied} hook file(s) into game root.");
    }

    private static void PatchEntryMethod(string targetDll, string hookDll, string gameRoot, InstallTarget target)
    {
        using var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(gameRoot);
        resolver.AddSearchDirectory(Path.GetDirectoryName(hookDll)!);

        var readerParams = new ReaderParameters { ReadWrite = true, InMemory = true, AssemblyResolver = resolver };
        using var module = ModuleDefinition.ReadModule(targetDll, readerParams);

        var entryType = module.Types.FirstOrDefault(type => type.FullName == target.EntryTypeName)
            ?? throw new InvalidOperationException($"Type {target.EntryTypeName} not found in {target.TargetDllName}.");
        var entryMethod = entryType.Methods.FirstOrDefault(method => method.Name == target.EntryMethodName)
            ?? throw new InvalidOperationException($"Method {target.EntryMethodName} not found on {target.EntryTypeName}.");

        using var hookAsm = AssemblyDefinition.ReadAssembly(hookDll);
        var hookType = hookAsm.MainModule.Types.FirstOrDefault(type => type.FullName == HookTypeFullName)
            ?? throw new InvalidOperationException($"Type {HookTypeFullName} not found in {Path.GetFileName(hookDll)}.");
        var hookMethod = hookType.Methods.FirstOrDefault(method => method.Name == HookMethodName && method.Parameters.Count == 0 && method.IsStatic)
            ?? throw new InvalidOperationException($"Static method {HookMethodName}() not found on {HookTypeFullName}.");

        var importedCall = module.ImportReference(hookMethod);

        if (IsAlreadyPatched(entryMethod))
        {
            Console.WriteLine($"Entry point already has a call to {HookTypeFullName}.{HookMethodName}; replacing it with refreshed reference.");
            entryMethod.Body.Instructions[0].Operand = importedCall;
        }
        else
        {
            var il = entryMethod.Body.GetILProcessor();
            var originalFirst = entryMethod.Body.Instructions[0];
            il.InsertBefore(originalFirst, il.Create(OpCodes.Call, importedCall));
            Console.WriteLine($"Injected call to {HookTypeFullName}.{HookMethodName}() at the top of {target.EntryTypeName}.{target.EntryMethodName}.");
        }

        module.Write(targetDll);
    }

    private static void PatchDepsJson(string targetDeps, InstallTarget target)
    {
        var json = File.ReadAllText(targetDeps);
        var root = JsonNode.Parse(json) as JsonObject
            ?? throw new InvalidOperationException($"{target.TargetDepsName} root is not a JSON object.");

        var targets = root["targets"] as JsonObject
            ?? throw new InvalidOperationException($"{target.TargetDepsName} is missing 'targets'.");
        var libraries = root["libraries"] as JsonObject
            ?? throw new InvalidOperationException($"{target.TargetDepsName} is missing 'libraries'.");

        // Both loader assemblies ship into the game root and must be on the TPA.
        var loaderPackages = new[]
        {
            (Key: HookPackageKey, Dll: HookAssemblyName + ".dll", Version: HookAssemblyVersion),
            (Key: AbstractionsPackageKey, Dll: AbstractionsAssemblyName + ".dll", Version: AbstractionsAssemblyVersion),
        };

        var targetsTouched = 0;
        foreach (var (_, targetNode) in targets)
        {
            if (targetNode is not JsonObject targetObj)
            {
                continue;
            }

            foreach (var package in loaderPackages)
            {
                if (targetObj.ContainsKey(package.Key))
                {
                    targetObj.Remove(package.Key);
                }

                targetObj[package.Key] = new JsonObject
                {
                    ["runtime"] = new JsonObject
                    {
                        [package.Dll] = new JsonObject
                        {
                            ["assemblyVersion"] = package.Version,
                            ["fileVersion"] = package.Version
                        }
                    }
                };
            }

            targetsTouched++;
        }

        foreach (var package in loaderPackages)
        {
            if (libraries.ContainsKey(package.Key))
            {
                libraries.Remove(package.Key);
            }

            libraries[package.Key] = new JsonObject
            {
                ["type"] = "project",
                ["serviceable"] = false,
                ["sha512"] = ""
            };
        }

        var writeOptions = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(targetDeps, root.ToJsonString(writeOptions));
        Console.WriteLine($"Registered {HookPackageKey} and {AbstractionsPackageKey} in {target.TargetDepsName} ({targetsTouched} target section(s) + libraries).");
    }

    private static bool IsAlreadyPatched(MethodDefinition method)
    {
        if (method.Body is null || method.Body.Instructions.Count == 0)
        {
            return false;
        }

        var first = method.Body.Instructions[0];
        if (first.OpCode != OpCodes.Call || first.Operand is not MethodReference methodReference)
        {
            return false;
        }

        return methodReference.Name == HookMethodName &&
            methodReference.DeclaringType?.FullName == HookTypeFullName &&
            (methodReference.DeclaringType?.Scope?.Name?.StartsWith(HookAssemblyName, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private sealed record InstallTarget(
        string TargetDllName,
        string TargetDepsName,
        string EntryTypeName,
        string EntryMethodName,
        string DisplayName);
}
