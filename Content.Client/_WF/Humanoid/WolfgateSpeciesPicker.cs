using System.Linq;
using System.Numerics;
using Content.Client._WF.Stylesheets;
using Content.Client.Lobby;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Preferences;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client._WF.Humanoid;

/// <summary>
/// Species browser: one row per species, each with a full-body preview of a default character of that
/// species and a short summary. Subspecies are listed under the species they vary, and every row is a
/// toggle in one group, so picking a row is picking a species.
/// </summary>
public sealed class WolfgateSpeciesPicker : BoxContainer
{
    [Dependency] private IEntityManager _entManager = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;

    /// <summary>Raised with the species id when a row is clicked.</summary>
    public Action<string>? OnSpeciesSelected;

    private readonly LobbyUIController _controller;
    private readonly ButtonGroup _group = new();
    private readonly Dictionary<string, ContainerButton> _rows = new();
    private readonly List<EntityUid> _dummies = new();

    private string? _selected;

    public WolfgateSpeciesPicker()
    {
        IoCManager.InjectDependencies(this);
        _controller = UserInterfaceManager.GetUIController<LobbyUIController>();
        Orientation = LayoutOrientation.Vertical;
        SeparationOverride = 6;
    }

    protected override void EnteredTree()
    {
        base.EnteredTree();
        if (_rows.Count == 0)
            Populate();
    }

    protected override void ExitedTree()
    {
        base.ExitedTree();
        Clear();
    }

    private void Clear()
    {
        foreach (var dummy in _dummies)
            _entManager.DeleteEntity(dummy);

        _dummies.Clear();
        _rows.Clear();
        DisposeAllChildren();
    }

    /// <summary>Marks a row as chosen without raising <see cref="OnSpeciesSelected"/>.</summary>
    public void SetSelected(string species)
    {
        _selected = species;
        foreach (var (id, row) in _rows)
            row.Pressed = id == species;
    }

    /// <summary>
    /// Rebuilds every row, spawning one preview dummy per species. Not free, so it runs when the
    /// species list changes or the tab re-enters the tree, not on every selection.
    /// </summary>
    public void Populate()
    {
        Clear();

        var all = _prototypeManager.EnumeratePrototypes<SpeciesPrototype>().Where(s => s.RoundStart).ToList();

        // One group per species that has variants, keyed by the species they vary. Everything else is its
        // own group of one. The species a group is named after need not be playable itself: Protogen is
        // not round start here, only its subspecies are.
        var groups = all.GroupBy(s => s.SubspeciesOf?.Id ?? s.ID)
            .Select(g => (Name: GroupName(g.Key, g), Members: g.OrderBy(DisplayName).ToList()))
            .OrderBy(g => g.Name)
            .ToList();

        foreach (var (name, members) in groups)
        {
            // A lone species speaks for itself; a group of variants gets a heading naming what they vary.
            if (members.Count > 1 || members[0].SubspeciesOf != null)
            {
                AddChild(new Label
                {
                    Text = name,
                    StyleClasses = { StyleWolfgate.StyleClassCreatorHeading },
                    Margin = new Thickness(2, 6, 0, 0),
                });
            }

            foreach (var species in members)
                AddRow(species);
        }

        if (_selected != null)
            SetSelected(_selected);
    }

    /// <summary>Name a group is sorted and headed by: the species it varies, or its only member.</summary>
    private string GroupName(string id, IEnumerable<SpeciesPrototype> members)
    {
        if (_prototypeManager.TryIndex<SpeciesPrototype>(id, out var proto))
            return DisplayName(proto);

        return DisplayName(members.First());
    }

    private string DisplayName(SpeciesPrototype species) => Loc.GetString(species.Name);

    private void AddRow(SpeciesPrototype species)
    {
        var row = new ContainerButton
        {
            ToggleMode = true,
            Group = _group,
            HorizontalExpand = true,
            StyleClasses = { WolfgateMarkingTile.StyleClassTile },
        };
        row.OnPressed += _ =>
        {
            _selected = species.ID;
            OnSpeciesSelected?.Invoke(species.ID);
        };

        var text = new BoxContainer
        {
            Orientation = LayoutOrientation.Vertical,
            SeparationOverride = 2,
            HorizontalExpand = true,
            VerticalAlignment = VAlignment.Center,
        };
        text.AddChild(new Label
        {
            Text = DisplayName(species),
            StyleClasses = { StyleWolfgate.StyleClassCreatorHeading },
        });

        var summary = new RichTextLabel { HorizontalExpand = true, Margin = new Thickness(0, 2, 0, 0) };
        summary.SetMessage(Summary(species));
        text.AddChild(summary);

        row.AddChild(new BoxContainer
        {
            Orientation = LayoutOrientation.Horizontal,
            SeparationOverride = 12,
            Margin = new Thickness(6, 4),
            HorizontalExpand = true,
            Children = { Preview(species), text },
        });

        _rows[species.ID] = row;
        AddChild(row);
    }

    /// <summary>
    /// Full-body sprite of a default character of this species: species skin tone, no hair or facial hair,
    /// no clothing, and only the markings the species gives itself.
    /// </summary>
    private Control Preview(SpeciesPrototype species)
    {
        var view = new SpriteView
        {
            Scale = new Vector2(3, 3),
            OverrideDirection = Direction.South,
            // Fixed box so every row is the same height, and Fit shrinks the sprites that are taller
            // than a tile (Shadekin ears, Harpy wings) rather than clipping them.
            SetSize = new Vector2(104, 104),
            Stretch = SpriteView.StretchMode.Fit,
            VerticalAlignment = VAlignment.Center,
        };

        var profile = HumanoidCharacterProfile.DefaultWithSpecies(species.ID)
            .WithCharacterAppearance(HumanoidCharacterAppearance.DefaultWithSpecies(species.ID));

        // Species that do not offer the default sex would otherwise load a base sprite set they have no art for.
        if (species.Sexes.Count > 0 && !species.Sexes.Contains(profile.Sex))
            profile = profile.WithSex(species.Sexes[0]);

        var dummy = _controller.LoadProfileEntity(profile, null, false);
        _dummies.Add(dummy);
        view.SetEntity(dummy);
        return view;
    }

    private static FormattedMessage Summary(SpeciesPrototype species)
    {
        var key = $"species-summary-{species.ID.ToLowerInvariant()}";
        return FormattedMessage.FromMarkupPermissive(Loc.TryGetString(key, out var summary) ? summary : string.Empty);
    }
}
