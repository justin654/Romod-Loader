namespace Romestead.ModLoader;

internal sealed class ModCraftingRegistry : IModCraftingRegistry
{
    private readonly object _sync = new();
    private readonly List<ModStationCraftingInstance> _windows = new();
    private static readonly IModCraftingWindowHandle UnavailableHandle = new InertHandle();

    public event Action? Changed;

    public IReadOnlyList<ModStationCraftingInstance> ActiveStationWindows
    {
        get
        {
            if (!ModRegistries.Capabilities.IsAvailable(ModCapabilityId.CraftingUi))
            {
                return [];
            }

            lock (_sync)
            {
                return _windows.ToArray();
            }
        }
    }

    public IModCraftingWindowHandle OpenStation(ModStationCraftingDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (string.IsNullOrWhiteSpace(definition.Id))
        {
            throw new ArgumentException("Crafting window id is required.", nameof(definition));
        }

        if (definition.StationIds is null || definition.StationIds.Count == 0)
        {
            throw new ArgumentException("At least one crafting station id is required.", nameof(definition));
        }

        if (!ModRegistries.Capabilities.IsAvailable(ModCapabilityId.CraftingUi))
        {
            return UnavailableHandle;
        }

        lock (_sync)
        {
            var existing = _windows.FindIndex(w => string.Equals(w.Id, definition.Id, StringComparison.Ordinal));
            var instance = new ModStationCraftingInstance
            {
                Id = definition.Id,
                StationIds = definition.StationIds,
                X = definition.X,
                Y = definition.Y,
            };

            if (existing >= 0)
            {
                _windows[existing] = instance;
            }
            else
            {
                _windows.Add(instance);
            }

            RaiseChanged();
            return new Handle(this, definition.Id);
        }
    }

    public void Close(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return;
        }

        lock (_sync)
        {
            if (_windows.RemoveAll(w => string.Equals(w.Id, id, StringComparison.Ordinal)) > 0)
            {
                RaiseChanged();
            }
        }
    }

    private bool IsOpen(string id)
    {
        lock (_sync)
        {
            return _windows.Exists(w => string.Equals(w.Id, id, StringComparison.Ordinal));
        }
    }

    private void RaiseChanged() => Changed?.Invoke();

    private sealed class Handle(ModCraftingRegistry owner, string id) : IModCraftingWindowHandle
    {
        public string Id => id;
        public bool IsOpen => owner.IsOpen(id);
        public void Close() => owner.Close(id);
    }

    private sealed class InertHandle : IModCraftingWindowHandle
    {
        public string Id => "";
        public bool IsOpen => false;
        public void Close() { }
    }
}
