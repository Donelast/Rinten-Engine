using Rinten.Internal;

namespace Editor.Inspectors;

//Flat property list for one feature tab (no nested razor ControlSheet).
sealed class FeaturePropertyPanel : Panel, IControlSheet
{
	public Func<SerializedProperty, bool> PropertyFilter { get; set; }

	public FeaturePropertyPanel()
	{
		AddClass( "feature-property-panel" );
	}

	public void Build( IControlSheet.Feature feature )
	{
		DeleteChildren();

		var props = feature.Properties.Where( x => x != feature.EnabledProperty ).ToList();
		if ( props.Count == 0 ) return;

		IControlSheet.AddProperties( this, props, allowFeatures: false );
	}

	public void AddFeature( IControlSheet.Feature feature ) { }

	public void AddGroup( IControlSheet.Group group )
	{
		var g = AddChild<ControlSheetGroup>();
		g.QueueGroup( group );
	}

	public bool TestFilter( SerializedProperty prop ) => PropertyFilter?.Invoke( prop ) ?? true;
}
