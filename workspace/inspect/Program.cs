using Mono.Cecil;

var gameRoot = ResolveGameRoot();

var asms = new[] { "Romestead.dll", "CandideCreator.Shared.dll", "Shared.dll", "CandideServer.dll", "Server.dll" };
var modules = new List<ModuleDefinition>();
var resolver = new DefaultAssemblyResolver();
resolver.AddSearchDirectory(gameRoot);
foreach (var a in asms)
{
    try { modules.Add(ModuleDefinition.ReadModule(Path.Combine(gameRoot, a), new ReaderParameters { AssemblyResolver = resolver })); }
    catch (Exception ex) { Console.WriteLine($"!! could not read {a}: {ex.Message}"); }
}

string mode = args.Length > 0 ? args[0] : "loadingscreen";
string filter = args.Length > 1 ? args[1] : "";

IEnumerable<TypeDefinition> AllTypes() => modules.SelectMany(m => m.GetTypes());

void DumpType(TypeDefinition t)
{
    Console.WriteLine($"\n==== {t.FullName}  (module {t.Module.Name}) ====");
    Console.WriteLine($"  base: {t.BaseType?.FullName}");
    foreach (var f in t.Fields)
        Console.WriteLine($"  field {(f.IsStatic ? "static " : "")}{f.FieldType.Name} {f.Name}");
    foreach (var m in t.Methods)
    {
        var ps = string.Join(", ", m.Parameters.Select(p => $"{p.ParameterType.Name} {p.Name}"));
        Console.WriteLine($"  method {(m.IsStatic ? "static " : "")}{m.ReturnType.Name} {m.Name}({ps})");
    }
}

if (mode == "find")
{
    foreach (var t in AllTypes().Where(t => t.FullName.Contains(filter, StringComparison.OrdinalIgnoreCase)))
        Console.WriteLine($"{t.FullName}   [{t.Module.Name}]");
}
else if (mode == "type")
{
    foreach (var t in AllTypes().Where(t => t.FullName.Equals(filter, StringComparison.OrdinalIgnoreCase) || t.Name.Equals(filter, StringComparison.OrdinalIgnoreCase)))
        DumpType(t);
}
else if (mode == "loadingscreen")
{
    foreach (var t in AllTypes().Where(t => t.Name.Contains("Loading", StringComparison.OrdinalIgnoreCase) || t.Name.Contains("LoadingScreen", StringComparison.OrdinalIgnoreCase)))
        DumpType(t);
}
else if (mode == "il")
{
    // dump called methods + string literals for Type.Method (filter = "Type.Method" or "Type")
    var parts = filter.Split('.');
    var typeName = parts.Length > 1 ? string.Join('.', parts[..^1]) : filter;
    var methodName = parts.Length > 1 ? parts[^1] : null;
    foreach (var t in AllTypes().Where(t => t.FullName.EndsWith(typeName, StringComparison.OrdinalIgnoreCase) || t.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase)))
    {
        foreach (var m in t.Methods.Where(m => m.HasBody && (methodName == null || m.Name.Equals(methodName, StringComparison.OrdinalIgnoreCase))))
        {
            Console.WriteLine($"\n--- {t.Name}.{m.Name} ---");
            foreach (var ins in m.Body.Instructions)
            {
                var op = ins.Operand;
                if (op is MethodReference mr) Console.WriteLine($"  {ins.OpCode.Name} -> {mr.DeclaringType.Name}.{mr.Name}");
                else if (op is FieldReference fr) Console.WriteLine($"  {ins.OpCode.Name} -> {fr.DeclaringType.Name}.{fr.Name}");
                else if (op is string s) Console.WriteLine($"  {ins.OpCode.Name} \"{s}\"");
            }
        }
    }
}
else if (mode == "rawil")
{
    // dump full IL for Type.Method (filter = "Type.Method" or "Type")
    var parts = filter.Split('.');
    var typeName = parts.Length > 1 ? string.Join('.', parts[..^1]) : filter;
    var methodName = parts.Length > 1 ? parts[^1] : null;
    foreach (var t in AllTypes().Where(t => t.FullName.EndsWith(typeName, StringComparison.OrdinalIgnoreCase) || t.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase)))
    {
        foreach (var m in t.Methods.Where(m => m.HasBody && (methodName == null || m.Name.Equals(methodName, StringComparison.OrdinalIgnoreCase))))
        {
            Console.WriteLine($"\n--- {t.FullName}.{m.Name} ---");
            foreach (var ins in m.Body.Instructions)
            {
                Console.WriteLine($"  {ins.Offset:X4}: {ins.OpCode,-12} {FormatOperand(ins.Operand)}");
            }
        }
    }
}
else if (mode == "member")
{
    // find methods/fields whose name contains filter
    foreach (var t in AllTypes())
    {
        foreach (var m in t.Methods.Where(m => m.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)))
            Console.WriteLine($"{t.FullName}.{m.Name}({string.Join(",", m.Parameters.Select(p=>p.ParameterType.Name))})");
        foreach (var f in t.Fields.Where(f => f.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)))
            Console.WriteLine($"{t.FullName}.{f.Name} : {f.FieldType.Name}");
    }
}
else if (mode == "uses")
{
    foreach (var t in AllTypes())
    {
        foreach (var m in t.Methods.Where(m => m.HasBody))
        {
            var hit = false;
            foreach (var ins in m.Body.Instructions)
            {
                var text = ins.Operand switch
                {
                    MethodReference mr => $"{mr.DeclaringType.FullName}.{mr.Name}",
                    FieldReference fr => $"{fr.DeclaringType.FullName}.{fr.Name}",
                    TypeReference tr => tr.FullName,
                    _ => ins.Operand?.ToString() ?? string.Empty
                };

                if (text.Contains(filter, StringComparison.OrdinalIgnoreCase))
                {
                    hit = true;
                    break;
                }
            }

            if (hit)
            {
                Console.WriteLine($"{t.FullName}.{m.Name}({string.Join(",", m.Parameters.Select(p => p.ParameterType.Name))})");
            }
        }
    }
}

static string ResolveGameRoot()
{
    var environmentValue = Environment.GetEnvironmentVariable("ROMESTEAD_GAME_ROOT");
    if (!string.IsNullOrWhiteSpace(environmentValue))
    {
        return Path.GetFullPath(environmentValue);
    }

    var workspaceRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
    var configPath = Path.Combine(workspaceRoot, "Workspace.local.props");
    if (File.Exists(configPath))
    {
        try
        {
            var doc = System.Xml.Linq.XDocument.Load(configPath);
            var value = doc.Root?
                .Elements("PropertyGroup")
                .SelectMany(group => group.Elements("RomesteadGameRoot"))
                .Select(element => element.Value.Trim())
                .FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate));
            if (!string.IsNullOrWhiteSpace(value))
            {
                return Path.GetFullPath(value);
            }
        }
        catch
        {
            // Fall back to the conventional install location.
        }
    }

    return @"C:\Program Files (x86)\Steam\steamapps\common\romestead";
}

static string FormatOperand(object operand) => operand switch
{
    null => string.Empty,
    MethodReference mr => $"{mr.DeclaringType.FullName}.{mr.Name}",
    FieldReference fr => $"{fr.DeclaringType.FullName}.{fr.Name}",
    TypeReference tr => tr.FullName,
    _ => operand.ToString() ?? string.Empty
};
