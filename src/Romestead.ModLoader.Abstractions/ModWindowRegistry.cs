namespace Romestead.ModLoader;

internal sealed class ModWindowRegistry : IModWindowRegistry
{
    private readonly object _sync = new();
    private readonly List<ModWindowInstance> _windows = new();
    private static readonly IModWindowHandle UnavailableHandle = new InertHandle();

    public event Action? WindowsChanged;

    public IReadOnlyList<ModWindowInstance> ActiveWindows
    {
        get
        {
            if (!ModRegistries.Capabilities.IsAvailable(ModCapabilityId.Windows))
            {
                return [];
            }

            lock (_sync)
            {
                return _windows.ToArray();
            }
        }
    }

    public IModWindowHandle Open(ModWindowDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (string.IsNullOrWhiteSpace(definition.Id))
        {
            throw new ArgumentException("Window id is required.", nameof(definition));
        }

        if (!ModRegistries.Capabilities.IsAvailable(ModCapabilityId.Windows))
        {
            return UnavailableHandle;
        }

        lock (_sync)
        {
            var existing = _windows.Find(w => string.Equals(w.Id, definition.Id, StringComparison.Ordinal));
            if (existing is not null)
            {
                existing.Title = definition.Title;
                existing.Sections = definition.Sections;
                RaiseChanged();
                return new Handle(this, existing.Id);
            }

            var instance = new ModWindowInstance
            {
                Id = definition.Id,
                Title = definition.Title,
                Style = definition.Style,
                Width = definition.Width,
                X = definition.X,
                Y = definition.Y,
                ShowCloseButton = definition.ShowCloseButton,
                Sections = definition.Sections,
            };
            _windows.Add(instance);
            RaiseChanged();
            return new Handle(this, instance.Id);
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

    private bool TryUpdate(string id, string? title, bool replaceTitle, IReadOnlyList<ModSection> sections)
    {
        lock (_sync)
        {
            var instance = _windows.Find(w => string.Equals(w.Id, id, StringComparison.Ordinal));
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

    private bool IsOpen(string id)
    {
        lock (_sync)
        {
            return _windows.Exists(w => string.Equals(w.Id, id, StringComparison.Ordinal));
        }
    }

    // Fired while holding _sync; the render host marshals back onto the UI thread, so firing under
    // the lock keeps notification order consistent with mutation order.
    private void RaiseChanged() => WindowsChanged?.Invoke();

    private sealed class Handle(ModWindowRegistry owner, string id) : IModWindowHandle
    {
        public string Id => id;
        public bool IsOpen => owner.IsOpen(id);

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

        public void Close() => owner.Close(id);
    }

    private sealed class InertHandle : IModWindowHandle
    {
        public string Id => "";
        public bool IsOpen => false;
        public void Update(string? title, IReadOnlyList<ModSection> sections) { }
        public void Update(IReadOnlyList<ModSection> sections) { }
        public void Close() { }
    }
}
