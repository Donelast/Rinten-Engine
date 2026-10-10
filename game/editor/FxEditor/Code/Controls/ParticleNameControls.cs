namespace Editor.FxEditor;

using Editor.Inspectors;

/// <summary>A name picked out of a list the effect declares - one of its shapes, one of its slots. The same shape as <see cref="ParticleParameterControl"/>, and found the same way: whichever open editor owns the object.</summary>
public abstract class ParticleNameControl : StringDropdownControl
{
	/// <summary>Something the definition holds, to find the definition from.</summary>
	public object Owner { get; set; }

	/// <summary>The effect a name is picked from when the owner is in no open editor - a player's anchor bindings in a scene, whose effect is the player's. Set by whoever builds such a sheet, for as long as it is up.</summary>
	public static ParticleDefinition Context { get; set; }

	protected abstract string Icon { get; }

	protected abstract IEnumerable<string> Names( ParticleDefinition definition );

	protected override List<Rinten.UI.Option> BuildOptions()
	{
		var options = new List<Rinten.UI.Option>
		{
			new( "None", "block", "" )
		};

		var current = Read() ?? "";
		var found = string.IsNullOrWhiteSpace( current );

		var owner = Owner ?? Property?.Parent?.Targets?.FirstOrDefault();

		var definition = ( owner is ParticleAnchorBinding ? Context : null ) ?? ParticleEditorWindow.DefinitionOf( owner ) ?? Context;

		if ( definition is not null )
		{
			foreach ( var name in Names( definition ) )
			{
				if ( string.IsNullOrWhiteSpace( name ) ) continue;

				found |= string.Equals( current, name, StringComparison.OrdinalIgnoreCase );

				options.Add( new Rinten.UI.Option( name, Icon, name ) );
			}
		}

		if ( !found )
			options.Add( new Rinten.UI.Option( $"{current} (missing)", "error", current ) );

		return options;
	}
}

/// <summary>One of the effect's shapes, by name.</summary>
[CustomEditor( typeof( string ), WithAllAttributes = [typeof( ParticleShapeNameAttribute )] )]
public sealed class ParticleShapeNameControl : ParticleNameControl
{
	protected override string Icon => "category";

	protected override IEnumerable<string> Names( ParticleDefinition definition )
		=> definition.Shapes.Select( shape => shape.Name );
}

/// <summary>One of the effect's anchors, by name.</summary>
[CustomEditor( typeof( string ), WithAllAttributes = [typeof( ParticleAnchorNameAttribute )] )]
public sealed class ParticleAnchorNameControl : ParticleNameControl
{
	protected override string Icon => "anchor";

	protected override IEnumerable<string> Names( ParticleDefinition definition )
		=> definition.Anchors.Select( anchor => anchor.Name );
}

/// <summary>One of the effect's slots, by name.</summary>
[CustomEditor( typeof( string ), WithAllAttributes = [typeof( ParticleSlotNameAttribute )] )]
public sealed class ParticleSlotNameControl : ParticleNameControl
{
	protected override string Icon => "bookmark";

	protected override IEnumerable<string> Names( ParticleDefinition definition )
		=> definition.Slots;
}
