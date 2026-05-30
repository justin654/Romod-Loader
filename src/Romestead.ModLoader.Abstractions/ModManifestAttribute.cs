namespace Romestead.ModLoader;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ModManifestAttribute : Attribute
{
    public ModManifestAttribute(string id, string name, string version)
    {
        Id = id;
        Name = name;
        Version = version;
    }

    public string Id { get; }
    public string Name { get; }
    public string Version { get; }
    public MultiplayerSyncMode SyncMode { get; set; } = MultiplayerSyncMode.RequiredOnClient;
}
