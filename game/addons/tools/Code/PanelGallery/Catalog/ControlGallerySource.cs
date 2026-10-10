using Editor.Inspectors;
using Rinten.UI;

namespace Editor.Gallery;

/// <summary>A page written by nobody: one example per property editor the type library knows.</summary>
public sealed class ControlGallerySource : IGalleryEntrySource
{
	public const string Group = "Property Editors";

	//After the hand-written pages, before the legacy one.
	const int GroupOrder = 5000;

	readonly ISampleValueProvider samples;

	public ControlGallerySource( ISampleValueProvider samples )
	{
		this.samples = samples ?? throw new ArgumentNullException( nameof( samples ) );
	}

	public IEnumerable<GalleryEntry> Entries()
	{
		foreach ( var type in EditorTypeLibrary.GetTypes<BaseControl>() )
		{
			if ( type.IsAbstract || type.IsInterface ) continue;

			foreach ( var editor in type.GetAttributes<CustomEditorAttribute>() )
			{
				var target = editor.TargetType;

				if ( target is null || target.IsGenericTypeDefinition || editor.ForMethod ) continue;

				yield return Describe( type, editor, target );
			}
		}
	}

	GalleryEntry Describe( TypeDescription control, CustomEditorAttribute editor, Type valueType )
	{
		var controlName = string.IsNullOrEmpty( control.Title ) ? control.Name : control.Title;
		var title = $"{controlName} · {ValueName( valueType )}";
		var description = $"{control.Name} editing a {ValueName( valueType )}.";

		if ( editor.WithAllAttributes is { Length: > 0 } attributes )
		{
			title += " [attr]";
			description += " Meant for properties marked " + string.Join( ", ", attributes.Select( x => x.Name.Replace( "Attribute", "" ) ) ) + "; shown here without them.";
		}

		return new GalleryEntry( title, string.IsNullOrEmpty( control.Icon ) ? "tune" : control.Icon, Group, description, GroupOrder,
			() => Build( control, valueType ) );
	}

	Panel Build( TypeDescription control, Type valueType )
	{
		var value = samples.TryCreate( valueType, samples, out var sample ) ? sample : null;
		var property = SampleProperty.Create( valueType, value );

		var instance = control.Create<BaseControl>()
			?? throw new InvalidOperationException( $"{control.Name} could not be created" );

		instance.Property = property;

		return instance;
	}

	static string ValueName( Type type )
	{
		if ( Nullable.GetUnderlyingType( type ) is { } inner ) return ValueName( inner ) + "?";
		if ( !type.IsGenericType ) return type.Name;

		var name = type.Name;
		var tick = name.IndexOf( '`' );

		if ( tick >= 0 ) name = name[..tick];

		return name + "<" + string.Join( ", ", type.GetGenericArguments().Select( ValueName ) ) + ">";
	}
}
