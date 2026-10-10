namespace Editor.MeshEditor;

partial class PrimitiveTool
{
	/// <summary>Shape choice as buttons beside the modes: drag-out box shapes first, then other editors</summary>
	public override ToolGroup CreateOptionGroup()
	{
		var group = new ToolGroup( "Shape", ToolButtonSize.Option );

		foreach ( var type in Shown<PrimitiveBuilder>() )
		{
			var target = type.TargetType;

			group.Add( new ToolCommand( type.Icon, type.Title, () => DrawBoxed( target ) )
			{
				Description = type.Description,
				Active = () => Editor is BlockEditor block && block.Shape?.GetType() == target,
			} );
		}

		foreach ( var type in Shown<PrimitiveEditor>() )
		{
			if ( type.TargetType == typeof( BlockEditor ) ) continue;

			var target = type.TargetType;

			group.Add( new ToolCommand( type.Icon, type.Title, () => SelectEditor( target ) )
			{
				Description = type.Description,
				Active = () => Editor?.GetType() == target,
			} );
		}

		return ToolGroup.OrNull( group );
	}

	//Draws a shape the block editor pulls out of a box.
	private void DrawBoxed( Type builder )
	{
		SelectEditor( typeof( BlockEditor ) );

		if ( Editor is BlockEditor block && block.Shape?.GetType() != builder )
		{
			block.Shape = EditorTypeLibrary.Create<PrimitiveBuilder>( builder );
		}
	}

	//The types of one kind worth a button, in the order they get one.
	private static IEnumerable<TypeDescription> Shown<T>()
		=> EditorTypeLibrary.GetTypes<T>()
			.Where( x => !x.IsAbstract )
			.OrderBy( x => x.Order )
			.ThenBy( x => x.Title );
}
