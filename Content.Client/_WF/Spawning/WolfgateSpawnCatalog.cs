using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client._WF.Spawning;

/// <summary>One spawnable prototype, with everything the menu needs precomputed.</summary>
public sealed class WolfgateSpawnEntry
{
    public required EntityPrototype Prototype { get; init; }
    public required string Name { get; init; }
    public required string Suffix { get; init; }

    /// <summary>Lower-case name, id and suffix, matched against the search text.</summary>
    public required string Search { get; init; }

    public required WfSpawnCategory Category { get; init; }

    public string Id => Prototype.ID;
}

/// <summary>
/// The classified, sorted list of everything the spawn menu can place, cached until prototypes reload.
/// </summary>
public sealed class WolfgateSpawnCatalog
{
    private readonly IConfigurationManager _cfg;
    private readonly IPrototypeManager _proto;
    private readonly IComponentFactory _factory;

    private readonly List<WolfgateSpawnEntry> _all = new();
    private readonly Dictionary<WfSpawnCategory, List<WolfgateSpawnEntry>> _byCategory = new();
    private readonly Dictionary<WfSpawnGroup, List<WolfgateSpawnEntry>> _byGroup = new();
    private readonly Dictionary<string, WolfgateSpawnEntry> _byId = new();

    private bool _built;

    public WolfgateSpawnCatalog(IConfigurationManager cfg, IPrototypeManager proto, IComponentFactory factory)
    {
        _cfg = cfg;
        _proto = proto;
        _factory = factory;
    }

    public IReadOnlyList<WolfgateSpawnEntry> All
    {
        get
        {
            EnsureBuilt();
            return _all;
        }
    }

    public IReadOnlyList<WolfgateSpawnEntry> Category(WfSpawnCategory category)
    {
        EnsureBuilt();
        return _byCategory.TryGetValue(category, out var list) ? list : Array.Empty<WolfgateSpawnEntry>();
    }

    public IReadOnlyList<WolfgateSpawnEntry> Group(WfSpawnGroup group)
    {
        EnsureBuilt();
        return _byGroup.TryGetValue(group, out var list) ? list : Array.Empty<WolfgateSpawnEntry>();
    }

    public WolfgateSpawnEntry? Find(string id)
    {
        EnsureBuilt();
        return _byId.GetValueOrDefault(id);
    }

    public void Invalidate()
    {
        _built = false;
        _all.Clear();
        _byCategory.Clear();
        _byGroup.Clear();
        _byId.Clear();
    }

    private void EnsureBuilt()
    {
        if (_built)
            return;

        _built = true;

        var classifier = new WolfgateSpawnClassifier(_proto, _factory);
        var categoryFilter = _cfg.GetCVar(CVars.EntitiesCategoryFilter);
        _proto.TryIndex<EntityCategoryPrototype>(categoryFilter, out var filter);

        foreach (var prototype in _proto.EnumeratePrototypes<EntityPrototype>())
        {
            if (prototype.Abstract || prototype.HideSpawnMenu)
                continue;

            if (filter != null && !prototype.Categories.Contains(filter))
                continue;

            var name = string.IsNullOrEmpty(prototype.Name) ? prototype.ID : prototype.Name;
            var suffix = prototype.EditorSuffix ?? string.Empty;

            _all.Add(new WolfgateSpawnEntry
            {
                Prototype = prototype,
                Name = name,
                Suffix = suffix,
                Search = $"{name} {prototype.ID} {suffix}".ToLowerInvariant(),
                Category = classifier.Classify(prototype),
            });
        }

        _all.Sort(static (a, b) =>
        {
            var byName = string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
            return byName != 0 ? byName : string.Compare(a.Suffix, b.Suffix, StringComparison.Ordinal);
        });

        foreach (var entry in _all)
        {
            _byId[entry.Id] = entry;
            _byCategory.GetOrNew(entry.Category).Add(entry);
            _byGroup.GetOrNew(WfSpawnCategories.GroupFor(entry.Category)).Add(entry);
        }
    }
}
