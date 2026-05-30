using System.Collections;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;

namespace Romestead.ModValueEditor;

internal sealed class GameItemDefinition
{
    public string Id { get; init; } = "";
    public string Icon { get; init; } = "";
    public int MaxStackSize { get; init; }
    public bool Unique { get; init; }
    public int? Tier { get; init; }
    public string Flags { get; init; } = "";
    public string EquipmentType { get; init; } = "";
    public string Material { get; init; } = "";
    public GameWeaponStats? Weapon { get; init; }

    public string Category =>
        Weapon is not null ? "Weapons" :
        !string.IsNullOrWhiteSpace(EquipmentType) ? "Equipment" :
        MaxStackSize > 1 ? "Stackable Items" :
        Unique ? "Unique Items" :
        "Other Items";

    public string DisplayText
    {
        get
        {
            var tier = Tier.HasValue ? $" T{Tier.Value}" : "";
            var stack = MaxStackSize > 1 ? $" x{MaxStackSize}" : "";
            return $"{Id}{tier}{stack}";
        }
    }

    public override string ToString() => DisplayText;

    public string ToDetailsText()
    {
        var sb = new StringBuilder();
        sb.AppendLine(Id);
        sb.AppendLine();
        sb.AppendLine($"Icon: {Empty(Icon)}");
        sb.AppendLine($"Max stack: {MaxStackSize}");
        sb.AppendLine($"Tier: {(Tier.HasValue ? Tier.Value.ToString() : "none")}");
        sb.AppendLine($"Unique: {Unique}");
        sb.AppendLine($"Flags: {Empty(Flags)}");

        if (!string.IsNullOrWhiteSpace(EquipmentType))
        {
            sb.AppendLine($"Equipment: {EquipmentType}");
            sb.AppendLine($"Material: {Empty(Material)}");
        }

        if (Weapon is { } weapon)
        {
            sb.AppendLine();
            sb.AppendLine("Weapon");
            sb.AppendLine($"Class: {Empty(weapon.Class)}");
            sb.AppendLine($"Swing timer: {F(weapon.SwingTimer)}");
            sb.AppendLine($"Range: {F(weapon.BaseAttackRange)}");
            sb.AppendLine($"Knockback: {F(weapon.BaseKnockback)}");
            sb.AppendLine($"Energy: {F(weapon.EnergyCost)}");
            sb.AppendLine($"Special energy: {F(weapon.SpecialEnergyCost)}");
            sb.AppendLine($"Stun: {F(weapon.StunPower)}");
            sb.AppendLine($"Movement: {F(weapon.MovementFactor)}");
            sb.AppendLine("Damage:");
            foreach (var damage in weapon.Damage.Where(d => d.Min != 0 || d.Max != 0))
            {
                sb.AppendLine($"  {damage.Type}: {F(damage.Min)}-{F(damage.Max)}");
            }
        }

        return sb.ToString();
    }

    private static string Empty(string value) => string.IsNullOrWhiteSpace(value) ? "none" : value;
    private static string F(float value) => OverrideDocument.F(value);
}

internal sealed class GameWeaponStats
{
    public string Class { get; init; } = "";
    public float SwingTimer { get; init; }
    public float BaseKnockback { get; init; }
    public float BaseAttackRange { get; init; }
    public float EnergyCost { get; init; }
    public float SpecialEnergyCost { get; init; }
    public float StunPower { get; init; }
    public float MovementFactor { get; init; }
    public List<GameDamageRange> Damage { get; init; } = [];
}

internal sealed class GameDamageRange
{
    public string Type { get; init; } = "";
    public float Min { get; init; }
    public float Max { get; init; }
}

internal static class GameItemCatalogLoader
{
    private static readonly object Sync = new();
    private static readonly HashSet<string> SetupRoots = new(StringComparer.OrdinalIgnoreCase);
    private static string? _resolverRoot;

    public static IReadOnlyList<GameItemDefinition> Load(string gameRoot)
    {
        lock (Sync)
        {
            gameRoot = Path.GetFullPath(gameRoot);
            InstallResolver(gameRoot);

            var shared = LoadAssembly(gameRoot, "Shared.dll");
            LoadAssembly(gameRoot, "CandideCreator.Shared.dll");
            LoadAssembly(gameRoot, "CandideServer.dll");

            if (SetupRoots.Add(gameRoot))
            {
                var setup = shared.GetType("Shared.Data.SharedDataSetup", throwOnError: true)!;
                var setupMethod = setup.GetMethod("Setup", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                    ?? throw new MissingMethodException("Shared.Data.SharedDataSetup", "Setup");
                setupMethod.Invoke(null, null);
            }

            var db = shared.GetType("Shared.Data.ItemDataBase", throwOnError: true)!;
            var getAllItems = db.GetMethod("GetAllItems", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new MissingMethodException("Shared.Data.ItemDataBase", "GetAllItems");
            if (getAllItems.Invoke(null, null) is not IEnumerable rawItems)
            {
                throw new InvalidOperationException("ItemDataBase.GetAllItems returned no enumerable item collection.");
            }

            return rawItems
                .Cast<object>()
                .Select(ReadItem)
                .OrderBy(i => i.Category, StringComparer.OrdinalIgnoreCase)
                .ThenBy(i => i.Id, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    private static GameItemDefinition ReadItem(object item)
    {
        var equippable = Field(item, "Equippable");
        var weapon = Field(equippable, "WeaponStats");

        return new GameItemDefinition
        {
            Id = Field(item, "Id")?.ToString() ?? "",
            Icon = Field(item, "Icon")?.ToString() ?? "",
            MaxStackSize = AsInt(Field(item, "MaxStackSize")),
            Unique = AsBool(Field(item, "Unique")),
            Tier = AsNullableInt(Field(item, "Tier")),
            Flags = Field(item, "Flags")?.ToString() ?? "",
            EquipmentType = Field(equippable, "EquipmentType")?.ToString() ?? "",
            Material = Field(equippable, "Material")?.ToString() ?? "",
            Weapon = weapon is null ? null : ReadWeapon(weapon)
        };
    }

    private static GameWeaponStats ReadWeapon(object weapon)
    {
        return new GameWeaponStats
        {
            Class = Field(weapon, "Class")?.ToString() ?? "",
            SwingTimer = AsFloat(Field(weapon, "SwingTimer")),
            BaseKnockback = AsFloat(Field(weapon, "BaseKnockback")),
            BaseAttackRange = AsFloat(Field(weapon, "BaseAttackRange")),
            EnergyCost = AsFloat(Field(weapon, "EnergyCost")),
            SpecialEnergyCost = AsFloat(Field(weapon, "SpecialEnergyCost")),
            StunPower = AsFloat(Field(weapon, "StunPower")),
            MovementFactor = AsFloat(Field(weapon, "MovementFactor")),
            Damage = ReadDamage(Field(weapon, "DamageRanges"))
        };
    }

    private static List<GameDamageRange> ReadDamage(object? damageRanges)
    {
        var min = ReadDamageArray(Field(damageRanges, "MinDamage"));
        var max = ReadDamageArray(Field(damageRanges, "MaxDamage"));
        var names = new[] { "True", "Slashing", "Piercing", "Bludgeoning", "Pyro", "Chloro", "Aqua", "Cosmo", "Necro" };
        var count = Math.Min(names.Length, Math.Max(min.Length, max.Length));
        var result = new List<GameDamageRange>();
        for (var i = 0; i < count; i++)
        {
            result.Add(new GameDamageRange
            {
                Type = names[i],
                Min = i < min.Length ? min[i] : 0,
                Max = i < max.Length ? max[i] : 0
            });
        }

        return result;
    }

    private static float[] ReadDamageArray(object? damageArray)
    {
        if (Field(damageArray, "Types") is float[] values)
        {
            return values;
        }

        return [];
    }

    private static object? Field(object? instance, string name)
    {
        if (instance is null)
        {
            return null;
        }

        return instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(instance);
    }

    private static int AsInt(object? value) => value is null ? 0 : Convert.ToInt32(value);
    private static int? AsNullableInt(object? value) => value is null ? null : Convert.ToInt32(value);
    private static bool AsBool(object? value) => value is bool b && b;
    private static float AsFloat(object? value) => value is null ? 0 : Convert.ToSingle(value);

    private static void InstallResolver(string gameRoot)
    {
        if (string.Equals(_resolverRoot, gameRoot, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _resolverRoot = gameRoot;
        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            var candidate = Path.Combine(gameRoot, name.Name + ".dll");
            return File.Exists(candidate) ? context.LoadFromAssemblyPath(candidate) : null;
        };
    }

    private static Assembly LoadAssembly(string gameRoot, string fileName)
    {
        var path = Path.Combine(gameRoot, fileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Game assembly not found: {path}", path);
        }

        var loaded = AssemblyLoadContext.Default.Assemblies.FirstOrDefault(a =>
            string.Equals(a.GetName().Name, Path.GetFileNameWithoutExtension(fileName), StringComparison.OrdinalIgnoreCase));
        return loaded ?? AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
    }
}
