namespace Romestead.ModLoader;

internal sealed class ModOverlayRegistry : IModOverlayRegistry
{
    private readonly object _sync = new();
    private readonly List<ModOverlayInstance> _overlays = new();
    private static readonly IModOverlayHandle UnavailableHandle = new InertHandle();

    public event Action? OverlaysChanged;

    public IReadOnlyList<ModOverlayInstance> ActiveOverlays
    {
        get
        {
            if (!ModRegistries.Capabilities.IsAvailable(ModCapabilityId.Overlays))
            {
                return [];
            }

            lock (_sync)
            {
                return _overlays.ToArray();
            }
        }
    }

    public IModOverlayHandle Show(ModOverlayDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (string.IsNullOrWhiteSpace(definition.Id))
        {
            throw new ArgumentException("Overlay id is required.", nameof(definition));
        }

        if (!ModRegistries.Capabilities.IsAvailable(ModCapabilityId.Overlays))
        {
            return UnavailableHandle;
        }

        lock (_sync)
        {
            var existing = _overlays.Find(o => string.Equals(o.Id, definition.Id, StringComparison.Ordinal));
            if (existing is not null)
            {
                existing.Title = definition.Title;
                existing.Sections = definition.Sections;
                RaiseChanged();
                return new Handle(this, existing.Id);
            }

            var instance = new ModOverlayInstance
            {
                Id = definition.Id,
                Title = definition.Title,
                Placement = definition.Placement,
                Sections = definition.Sections,
            };
            _overlays.Add(instance);
            RaiseChanged();
            return new Handle(this, instance.Id);
        }
    }

    private bool TryUpdate(string id, string? title, bool replaceTitle, IReadOnlyList<ModSection> sections)
    {
        lock (_sync)
        {
            var instance = _overlays.Find(o => string.Equals(o.Id, id, StringComparison.Ordinal));
            if (instance is null)
            {
                return false;
            }

            if (replaceTitle)
            {
                instance.Title = title;
            }

            instance.Sections = sections;
            RaiseChanged();
            return true;
        }
    }

    private void Hide(string id)
    {
        lock (_sync)
        {
            var removed = _overlays.RemoveAll(o => string.Equals(o.Id, id, StringComparison.Ordinal));
            if (removed > 0)
            {
                RaiseChanged();
            }
        }
    }

    private bool IsVisible(string id)
    {
        lock (_sync)
        {
            return _overlays.Exists(o => string.Equals(o.Id, id, StringComparison.Ordinal));
        }
    }

    // Invoked while holding _sync. Subscribers (the render host) marshal back onto the UI thread,
    // so firing under the lock keeps notification order consistent with the mutation order.
    private void RaiseChanged() => OverlaysChanged?.Invoke();

    private sealed class Handle(ModOverlayRegistry owner, string id) : IModOverlayHandle
    {
        public string Id => id;
        public bool IsVisible => owner.IsVisible(id);

        public void Update(string? title, IReadOnlyList<ModSection> sections)
        {
            ArgumentNullException.ThrowIfNull(sections);
            owner.TryUpdate(id, title, replaceTitle: true, sections);
        }

        public void Update(IReadOnlyList<ModSection> sections)
        {
            ArgumentNullException.ThrowIfNull(sections);
            owner.TryUpdate(id, title: null, replaceTitle: false, sections);
        }

        public void Hide() => owner.Hide(id);
    }

    private sealed class InertHandle : IModOverlayHandle
    {
        public string Id => "";
        public bool IsVisible => false;
        public void Update(string? title, IReadOnlyList<ModSection> sections) { }
        public void Update(IReadOnlyList<ModSection> sections) { }
        public void Hide() { }
    }
}
