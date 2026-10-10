namespace Editor.MdlEditor;

using Editor.Inspectors;

/// <summary>The sequence a graph node plays, picked out of everything the editor can reach.</summary>
[CustomEditor( typeof( string ), WithAllAttributes = [typeof( ModelClipAttribute )] )]
public sealed class ModelClipControl : StringDropdownControl
{
	public override bool AcceptEditorDrops => !IsReadOnly;

	protected override List<Rinten.UI.Option> BuildOptions()
	{
		var options = new List<Rinten.UI.Option> { new( "None", "" ) };

		foreach ( var name in ModelClipCatalog.Names( FindGraph() ) )
			options.Add( new Rinten.UI.Option( name, "animation", name ) );

		return options;
	}

	//What the property holds, reduced to the name the list is keyed by.
	protected override string Read() => ModelClipCatalog.Key( Property?.As.String ?? "" );

	protected override void Assign( string value ) => base.Assign( ModelClipCatalog.Key( value ?? "" ) );

	public override DropAction OnEditorDragHover( Widget.DragEvent ev )
	{
		var accept = !IsReadOnly && ClipNameFrom( ev ) is not null;

		SetClass( "drop-target", accept );

		return accept ? DropAction.Link : DropAction.Ignore;
	}

	public override void OnEditorDragLeave() => SetClass( "drop-target", false );

	public override void OnEditorDragDrop( Widget.DragEvent ev )
	{
		SetClass( "drop-target", false );

		if ( IsReadOnly || ClipNameFrom( ev ) is not { } name ) return;

		Assign( name );
	}

	static string ClipNameFrom( Widget.DragEvent ev )
	{
		if ( ev.Data.Object is ClipDragData { AnimationNames.Count: > 0 } clips )
			return ModelClipCatalog.Key( clips.AnimationNames[0] );

		if ( ev.Data.Object is string text )
			return ModelClipCatalog.Key( text );

		return null;
	}

	Data FindGraph()
	{
		var targets = Property?.Parent?.Targets;
		var node = targets?.OfType<Node>().FirstOrDefault()
			?? targets?.OfType<VariationDataNode.VariationData>().FirstOrDefault()?.Owner;

		return node?.Root;
	}

	//The editor this property belongs to. There is no widget tree to walk up from on a panel, so the graph is what identifies
	//the window - and failing that, whichever editor is open, which is the fallback the widget this replaced already had.
	Window FindWindow() => Window.For( null, FindGraph() );
}
