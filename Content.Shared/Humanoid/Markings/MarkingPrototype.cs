using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared.Humanoid.Markings
{
    [Prototype]
    public sealed partial class MarkingPrototype : IPrototype
    {
        [IdDataField]
        public string ID { get; private set; } = "uwu";

        public string Name { get; private set; } = default!;

        [DataField("bodyPart", required: true)]
        public HumanoidVisualLayers BodyPart { get; private set; } = default!;

        [DataField("markingCategory", required: true)]
        public MarkingCategories MarkingCategory { get; private set; } = default!;

        [DataField("speciesRestriction")]
        public List<string>? SpeciesRestrictions { get; private set; }

        [DataField("sexRestriction")]
        public Sex? SexRestriction { get; private set; }

        [DataField("followSkinColor")]
        public bool FollowSkinColor { get; private set; } = false;

        [DataField("forcedColoring")]
        public bool ForcedColoring { get; private set; } = false;

        [DataField("coloring")]
        public MarkingColors Coloring { get; private set; } = new();

        [DataField("sprites", required: true)]
        public List<SpriteSpecifier> Sprites { get; private set; } = default!;

        // WOLFGATE - ported from HardLight/Floof: multi-layer markings.
        /// <summary>
        /// Places individual sprites of this marking into arbitrary humanoid layers rather than all
        /// into <see cref="BodyPart"/>. Lets a tail sit behind the mob from most angles and over the
        /// suit when facing north. Maps RSI state name -> <see cref="HumanoidVisualLayers"/> name.
        /// </summary>
        [DataField("layering")]
        public Dictionary<string, string>? Layering { get; private set; }

        /// <summary>
        /// Ties one sprite's colour to another's so a multi-sprite marking is coloured as a single
        /// unit. Maps the state that inherits -> the state it inherits from; the inheriting state is
        /// hidden from the colour picker.
        /// </summary>
        [DataField("colorLinks")]
        public Dictionary<string, string>? ColorLinks { get; private set; }
        // End WOLFGATE

        // impstation edit - allow markings to support shaders
		[DataField("shader")]
		public string? Shader { get; private set; } = null;
        // end impstation edit
        public Marking AsMarking()
        {
            return new Marking(ID, Sprites.Count);
        }
    }
}
