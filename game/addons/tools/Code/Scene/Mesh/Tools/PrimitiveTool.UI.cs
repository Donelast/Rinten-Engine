namespace Editor.MeshEditor;

partial class PrimitiveTool
{
	public override Panel CreateToolSidebarPanel() => new PrimitiveToolPanel( this );

	/// <summary>Settings for the shape being drawn; shape choice itself is on the option strip</summary>
	public class PrimitiveToolPanel : ToolSettingsPanel
	{
		readonly PrimitiveTool _tool;
		readonly Panel _settings;
		readonly IconPanel _mark;
		readonly Rinten.UI.Label _title;

		//The editor the title and settings were built for. The strip beside the modes is what changes it, so this follows it rather than setting it.
		Type _shown;

		public PrimitiveToolPanel( PrimitiveTool tool )
		{
			_tool = tool;

			//Its own title rather than AddTitle's, because both halves of it change with the shape being built.
			var titleRow = AddChild<Panel>( "tool-title" );

			//The square and the glyph are not the same panel: the glyph centred in the square is what lines it up with the title. See ShowMark.
			_mark = titleRow.AddChild<Panel>( "mark" ).AddChild<IconPanel>( "glyph" );
			_title = titleRow.AddChild<Rinten.UI.Label>( "text" );

			_settings = AddChild<Panel>( "column" );

			Show();

			var row = AddRow();

			AddTextButton( row, "Create", "done", _tool.Create ).EnabledWhen = () => _tool.Editor is { CanBuild: true };
			AddTextButton( row, "Cancel", "close", _tool.Cancel ).EnabledWhen = () => _tool.Editor is { InProgress: true };

			AddStretch();
		}

		//The strip does not know this panel is open, so what it picked is checked rather than pushed.
		public override void Tick()
		{
			base.Tick();

			if ( _shown == _tool.Editor?.GetType() ) return;

			Show();
		}

		void Show()
		{
			_shown = _tool.Editor?.GetType();

			ShowTitle();
			BuildSettings();
		}

		void BuildSettings()
		{
			_settings.DeleteChildren( true );

			if ( _tool.Editor?.CreatePanel() is { } panel )
				_settings.AddChild( panel );
		}

		void ShowTitle()
		{
			//The tool clears its editor when it is put away, and this panel is still up for the frame that
			//happens in - so the type would be asked for as null, and the type library dereferences it.
			if ( _shown is null ) return;

			var type = EditorTypeLibrary.GetType( _shown );
			if ( type is null ) return;

			ToolSettingsPanel.ShowMark( _mark, type.Icon );
			_title.Text = $"Create {type.Title}";
		}

		[EditorEvent.Hotload]
		public void Hotload() => Show();

		//Enter and Escape are on the scene view's own bar - see ViewportToolsPanel.Shortcuts. Held here they
		//were live only while this panel was built, which is inside the mapping workspace and nowhere else.
	}
}
