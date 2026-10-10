using System.Reflection;
using Rinten.Helpers;

namespace Editor.FxEditor;

/// <summary>The effect's undo stack, its file, and everything that changes its shape.</summary>
public sealed partial class ParticleEditorWindow
{
	readonly UndoSystem _undo = new();

	string _committed;
	string _pendingName;
	bool _restoring;
	bool _writing;
	bool _dirty;

	//Undo

	internal bool CanUndo => _undo.Back.Count > 0;
	internal bool CanRedo => _undo.Forward.Count > 0;

	//The undo stack, for the window's own history dock to read.
	internal UndoSystem UndoSystem => _undo;

	void ClearUndo()
	{
		_undo.Initialize();

		_pendingName = null;
		_committed = Snapshot();
		_dirty = false;
	}

	//Say what the change about to happen is called. Named before rather than after, because the thing that knows what a change is
	//is whatever is about to make it, and the thing that notices it happened is a property sheet that only knows a value moved.
	internal void PushUndo( string name )
	{
		if ( _pendingName is not null && _pendingName != name )
			CommitUndo();

		_pendingName = name;
	}

	//Close off a change, where the definition actually came out different.
	internal void CommitUndo( string name = null )
	{
		if ( _restoring ) return;

		var title = name ?? _pendingName ?? "Edit Effect";

		_pendingName = null;

		var before = _committed;
		var after = Snapshot();

		if ( before is null || after is null || before == after )
		{
			_committed = after ?? before;
			return;
		}

		_committed = after;

		_undo.Insert( title, () => Restore( before ), () => Restore( after ) );
	}

	[Shortcut( "editor.undo", "CTRL+Z" )]
	internal bool Undo() => _undo.Undo();

	[Shortcut( "editor.redo", "CTRL+Y" )]
	internal bool Redo() => _undo.Redo();

	string Snapshot()
	{
		if ( _definition is null )
			return null;

		try
		{
			return _definition.Serialize().ToJsonString();
		}
		catch ( Exception e )
		{
			Log.Warning( e, "Particles: could not snapshot the effect" );

			return null;
		}
	}

	void Restore( string state )
	{
		if ( _definition is null || string.IsNullOrEmpty( state ) )
			return;

		if ( Json.ParseToJsonObject( state ) is not { } json )
			return;

		_restoring = true;

		try
		{
			json.Remove( "__version" );

			_definition.Deserialize( json );
			_committed = state;

			//Everything on screen was pointing at objects that no longer exist - a deserialize builds the emitters and modules again from scratch.
			Select( _definition );

			OnEdited( "Undo" );
		}
		finally
		{
			_restoring = false;
		}
	}

	//The file

	//Something was edited: the preview is rebuilt from it, and the file catches up a moment later.
	//A delay or a duration moved on the timeline: the players follow, the tree and sheet stay put.
	internal void NoteTimingChanged()
	{
		if ( _definition is null ) return;

		_definition.NoteChanged();
		_dirty = true;
	}

	internal void OnEdited( string name = null )
	{
		if ( _definition is null ) return;
		_preview?.RebuildTimeline();

		if ( !_restoring )
			CommitUndo( name );

		//What tells every player of this effect - the preview's included - that what they built is out of date. See ParticleDefinition.Revision.
		_definition.NoteChanged();

		PushToScenes();

		_dirty = true;
		_writeAt = 0.15f;

		_tree?.Refresh();
		_parameters?.Refresh();
		UpdateTitle();
	}

	//Rebuilds every player of this effect in every open scene, since background scenes may not tick
	void PushToScenes()
	{
		if ( _definition is null ) return;

		var path = _definition.ResourcePath;

		foreach ( var session in SceneEditorSession.All )
		{
			if ( session?.Scene is not { } scene || !scene.IsValid() ) continue;

			foreach ( var player in scene.GetAllComponents<ParticlePlayer>() )
			{
				if ( !player.IsValid() || player.Definition is null ) continue;

				var same = ReferenceEquals( player.Definition, _definition )
					|| ( !string.IsNullOrEmpty( path ) && player.Definition.ResourcePath == path );

				if ( !same ) continue;

				if ( !ReferenceEquals( player.Definition, _definition ) )
					player.Definition = _definition;

				player.Rebuild();
			}
		}
	}

	//Write what was edited, now, where there is anything to write.
	internal void Checkpoint()
	{
		if ( _writing || !_dirty ) return;
		if ( _asset is null || _definition is null ) return;

		_writing = true;

		try
		{
			if ( !_asset.SaveToDisk( _definition, compile: false ) )
				Log.Warning( $"Particles: could not write {_asset.Path}" );

			_dirty = false;
		}
		catch ( Exception e )
		{
			Log.Warning( e, "Particles: could not write what is open" );
		}
		finally
		{
			_writing = false;
		}
	}

	//The file catches up with the edits a beat after the last of them. A beat rather than at
	//once, because a drag on a slider is a change per frame and each of them would be a write.
	[EditorEvent.Frame]
	void WriteWhenSettled()
	{
		if ( !_dirty ) return;
		if ( _writeAt > 0.0f ) return;

		Checkpoint();
	}

	RealTimeUntil _writeAt;

	[Shortcut( "editor.save", "CTRL+S" )]
	void Save() => Checkpoint();

	//Emitters

	//A new emitter with the modules a plain effect is made of.
	internal static ParticleDefinition.Emitter NewEmitter( string name )
	{
		var emitter = new ParticleDefinition.Emitter { Name = name };

		emitter.Add( Named( new SpawnRateModule() ) );
		emitter.Add( Named( new InitializePositionModule() ) );
		emitter.Add( Named( new InitializeVelocityModule() ) );
		emitter.Add( Named( new InitializeLifetimeModule() ) );
		emitter.Add( Named( new InitializeSizeModule() ) );
		emitter.Add( Named( new InitializeColorModule() ) );
		emitter.Add( Named( new GravityModule() ) );
		emitter.Add( Named( new SpriteRenderModule() ) );

		return emitter;
	}

	//A module wearing the name its type is listed under.
	static ParticleModule Named( ParticleModule module )
	{
		module.Name = DisplayInfo.ForType( module.GetType() ).Name;

		return module;
	}

	internal void AddEmitter()
	{
		if ( _definition is null ) return;

		PushUndo( "Add Emitter" );

		var emitter = NewEmitter( $"Emitter {_definition.Emitters.Count + 1}" );

		_definition.Emitters.Add( emitter );

		OnEdited();
		Select( emitter );
	}

	internal void DeleteEmitter( ParticleDefinition.Emitter emitter )
	{
		if ( _definition is null || emitter is null ) return;

		PushUndo( "Delete Emitter" );

		_definition.Emitters.Remove( emitter );

		OnEdited();
		Select( _definition );
	}

	internal void DuplicateEmitter( ParticleDefinition.Emitter emitter )
	{
		if ( _definition is null || emitter is null ) return;

		var index = _definition.Emitters.IndexOf( emitter );

		if ( index < 0 ) return;

		if ( Clone( emitter ) is not { } clone ) return;

		PushUndo( "Duplicate Emitter" );

		clone.Identifier = Guid.NewGuid().ToString();
		clone.Name = $"{emitter.Name} Copy";

		_definition.Emitters.Insert( index + 1, clone );

		OnEdited();
		Select( clone );
	}

	//Shapes

	internal ParticleShape AddShape( ParticleShape.ShapeKind kind )
	{
		if ( _definition is null ) return null;

		PushUndo( "Add Shape" );

		var shape = new ParticleShape { Kind = kind, Name = UniqueShapeName( kind.ToString() ) };

		_definition.Shapes.Add( shape );

		OnEdited();
		Select( shape );

		return shape;
	}

	internal void DeleteShape( ParticleShape shape )
	{
		if ( _definition is null || shape is null ) return;

		PushUndo( "Delete Shape" );

		_definition.Shapes.Remove( shape );

		OnEdited();
		Select( _definition );
	}

	internal void DuplicateShape( ParticleShape shape )
	{
		if ( _definition is null || shape is null ) return;

		var index = _definition.Shapes.IndexOf( shape );

		if ( index < 0 || Clone( shape ) is not { } clone ) return;

		PushUndo( "Duplicate Shape" );

		clone.Identifier = Guid.NewGuid().ToString();
		clone.Name = UniqueShapeName( shape.Name );

		_definition.Shapes.Insert( index + 1, clone );

		OnEdited();
		Select( clone );
	}

	//Every module naming a shape reads it by name, so a rename carries them.
	internal void NoteShapeRenamed( ParticleShape shape, string was ) => CarryShapeRename( _definition, shape, was );

	//The same, for an effect edited where there is no window - the inspector's page.
	internal static void CarryShapeRename( ParticleDefinition definition, ParticleShape shape, string was )
	{
		if ( definition is null || string.IsNullOrEmpty( was ) || was == shape.Name ) return;

		Rename<ParticleShapeNameAttribute>( definition.Emitters.SelectMany( emitter => emitter.AllModules() ), was, shape.Name );
	}

	string UniqueShapeName( string stem )
	{
		var name = stem;
		var n = 1;

		while ( _definition.Shape( name ) is not null )
			name = $"{stem} {++n}";

		return name;
	}

	//Anchors

	internal ParticleAnchor AddAnchor()
	{
		if ( _definition is null ) return null;

		PushUndo( "Add Anchor" );

		var anchor = new ParticleAnchor { Name = UniqueName( "Anchor", name => _definition.Anchor( name ) is not null ) };

		_definition.Anchors.Add( anchor );

		OnEdited();
		Select( anchor );

		return anchor;
	}

	internal void DeleteAnchor( ParticleAnchor anchor )
	{
		if ( _definition is null || anchor is null ) return;

		PushUndo( "Delete Anchor" );

		_definition.Anchors.Remove( anchor );

		OnEdited();
		Select( _definition );
	}

	internal void DuplicateAnchor( ParticleAnchor anchor )
	{
		if ( _definition is null || anchor is null ) return;

		var index = _definition.Anchors.IndexOf( anchor );

		if ( index < 0 || Clone( anchor ) is not { } clone ) return;

		PushUndo( "Duplicate Anchor" );

		clone.Identifier = Guid.NewGuid().ToString();
		clone.Name = UniqueName( anchor.Name, name => _definition.Anchor( name ) is not null );

		_definition.Anchors.Insert( index + 1, clone );

		OnEdited();
		Select( clone );
	}

	//Everything naming an anchor - modules, shapes, emitters - reads it by name, so a rename carries them.
	internal void NoteAnchorRenamed( ParticleAnchor anchor, string was ) => CarryAnchorRename( _definition, anchor, was );

	//The same, for an effect edited where there is no window - the inspector's page.
	internal static void CarryAnchorRename( ParticleDefinition definition, ParticleAnchor anchor, string was )
	{
		if ( definition is null || string.IsNullOrEmpty( was ) || was == anchor.Name ) return;

		var owners = definition.Emitters.SelectMany( emitter => emitter.AllModules() ).Cast<object>()
			.Concat( definition.Shapes ).Concat( definition.Emitters );

		Rename<ParticleAnchorNameAttribute>( owners, was, anchor.Name );
	}

	static void Rename<TAttribute>( IEnumerable<object> owners, string was, string now ) where TAttribute : Attribute
	{
		foreach ( var owner in owners )
		{
			foreach ( var property in owner.GetType().GetProperties() )
			{
				if ( property.PropertyType != typeof( string ) || property.GetCustomAttribute<TAttribute>() is null ) continue;
				if ( !string.Equals( property.GetValue( owner ) as string, was, StringComparison.OrdinalIgnoreCase ) ) continue;

				property.SetValue( owner, now );
			}
		}
	}

	static string UniqueName( string stem, Func<string, bool> taken )
	{
		var name = stem;
		var n = 1;

		while ( taken( name ) )
			name = $"{stem} {++n}";

		return name;
	}

	//Modules

	//Every module type that belongs in this stage, in the order a menu should list them.
	internal static IEnumerable<TypeDescription> ModuleTypes( ParticleModuleStage stage )
	{
		foreach ( var type in EditorTypeLibrary.GetTypes<ParticleModule>() )
		{
			if ( type.IsAbstract || type.TargetType == typeof( UnknownParticleModule ) )
				continue;

			if ( type.Create<ParticleModule>()?.Stage != stage )
				continue;

			yield return type;
		}
	}

	internal void AddModule( ParticleDefinition.Emitter emitter, TypeDescription type )
	{
		if ( emitter is null || type is null ) return;

		if ( type.Create<ParticleModule>() is not { } module ) return;

		PushUndo( $"Add {type.Title}" );

		module.Name = type.Title;
		emitter.Add( module );

		OnEdited();
		Select( module );
	}

	internal void DeleteModule( ParticleDefinition.Emitter emitter, ParticleModule module )
	{
		if ( emitter is null || module is null ) return;

		PushUndo( "Delete Module" );

		emitter.Remove( module );

		OnEdited();
		Select( emitter );
	}

	internal void DuplicateModule( ParticleDefinition.Emitter emitter, ParticleModule module )
	{
		if ( emitter?.Modules( module?.Stage ?? ParticleModuleStage.Spawn ) is not { } list ) return;

		var index = list.IndexOf( module );

		if ( index < 0 ) return;

		if ( Clone( module, module.GetType() ) is not ParticleModule clone ) return;

		PushUndo( "Duplicate Module" );

		clone.Identifier = Guid.NewGuid().ToString();
		clone.Name = $"{module.Name} Copy";

		list.Insert( index + 1, clone );

		OnEdited();
		Select( clone );
	}

	internal void SetModuleEnabled( ParticleModule module, bool enabled )
	{
		if ( module is null || module.Enabled == enabled ) return;

		PushUndo( enabled ? "Enable Module" : "Disable Module" );

		module.Enabled = enabled;

		OnEdited();
	}

	//Move a module up or down inside its own stage. Inside its own stage and nowhere else: the four lists are four
	//moments, and a module written to run on a particle being born has nothing sensible to do in the middle of one's life.
	internal void MoveModule( ParticleDefinition.Emitter emitter, ParticleModule module, int direction )
	{
		if ( emitter is null || module is null ) return;
		if ( emitter.Modules( module.Stage ) is not { } list ) return;

		var index = list.IndexOf( module );
		var target = index + direction;

		if ( index < 0 || target < 0 || target >= list.Count ) return;

		PushUndo( "Reorder Modules" );

		list.RemoveAt( index );
		list.Insert( target, module );

		OnEdited();
	}

	//The clipboard

	//A module or an emitter, copied as the JSON it is saved as, so it pastes into any effect in any window - or into a text editor, for that matter.
	const string ModuleClip = "rinten-fx-module:";
	const string EmitterClip = "rinten-fx-emitter:";

	internal void CopyModule( ParticleModule module )
	{
		if ( module is null ) return;

		var list = new List<ParticleModule> { module };

		EditorUtility.Clipboard.Copy( ModuleClip + Json.ToNode( list, typeof( List<ParticleModule> ) ).ToJsonString() );
	}

	internal void CopyEmitter( ParticleDefinition.Emitter emitter )
	{
		if ( emitter is null ) return;

		EditorUtility.Clipboard.Copy( EmitterClip + Json.ToNode( emitter, typeof( ParticleDefinition.Emitter ) ).ToJsonString() );
	}

	//Whether what is on the clipboard is something this can take.
	internal bool CanPaste
	{
		get
		{
			var text = EditorUtility.Clipboard.Paste();

			return text is not null && ( text.StartsWith( ModuleClip ) || text.StartsWith( EmitterClip ) );
		}
	}

	//Whatever is on the clipboard, into the effect: a module goes into the emitter given (or the selected one), an emitter goes in beside the rest.
	internal void Paste( ParticleDefinition.Emitter into = null )
	{
		if ( _definition is null ) return;

		var text = EditorUtility.Clipboard.Paste();
		if ( string.IsNullOrEmpty( text ) ) return;

		try
		{
			if ( text.StartsWith( EmitterClip ) )
			{
				var emitter = (ParticleDefinition.Emitter)Json.FromNode( Json.ParseToJsonObject( text[EmitterClip.Length..] ), typeof( ParticleDefinition.Emitter ) );
				if ( emitter is null ) return;

				emitter.Identifier = Guid.NewGuid().ToString();
				emitter.Name = UniqueEmitterName( emitter.Name );

				foreach ( var module in emitter.AllModules() )
					module.Identifier = Guid.NewGuid().ToString();

				PushUndo( "Paste Emitter" );
				_definition.Emitters.Add( emitter );
				OnEdited();
				Select( emitter );

				return;
			}

			if ( text.StartsWith( ModuleClip ) )
			{
				into ??= EmitterOf( _selection ) ?? _definition.Emitters.LastOrDefault();
				if ( into is null ) return;

				var node = System.Text.Json.Nodes.JsonNode.Parse( text[ModuleClip.Length..] );
				var modules = (List<ParticleModule>)Json.FromNode( node, typeof( List<ParticleModule> ) );
				if ( modules is null || modules.Count == 0 ) return;

				PushUndo( "Paste Module" );

				ParticleModule last = null;

				foreach ( var module in modules )
				{
					module.Identifier = Guid.NewGuid().ToString();
					into.Add( module );
					last = module;
				}

				OnEdited();
				Select( last );
			}
		}
		catch ( Exception e )
		{
			Log.Warning( e, "Particles: could not paste that" );
		}
	}

	//The emitter something selected belongs to - itself, or the one holding the module.
	internal ParticleDefinition.Emitter EmitterOf( object target )
	{
		if ( target is ParticleDefinition.Emitter emitter ) return emitter;

		if ( target is ParticleModule module && _definition is not null )
		{
			foreach ( var each in _definition.Emitters )
			{
				if ( each.AllModules().Contains( module ) ) return each;
			}
		}

		return null;
	}

	string UniqueEmitterName( string name )
	{
		name = string.IsNullOrWhiteSpace( name ) ? "Emitter" : name;

		if ( !_definition.Emitters.Any( e => e.Name == name ) ) return name;

		for ( var i = 2; ; i++ )
		{
			var candidate = $"{name} {i}";
			if ( !_definition.Emitters.Any( e => e.Name == candidate ) ) return candidate;
		}
	}

	//Solo

	//The one emitter the preview plays, by identifier, or null for all of them. By identifier because an undo builds the emitters again.
	internal string SoloEmitter { get; private set; }

	internal bool IsSolo( ParticleDefinition.Emitter emitter ) => emitter is not null && SoloEmitter == emitter.Identifier;

	internal void ToggleSolo( ParticleDefinition.Emitter emitter )
	{
		SoloEmitter = IsSolo( emitter ) ? null : emitter?.Identifier;

		_tree?.NoteSelection();
	}

	//Templates

	//An emitter built from one of the stock recipes, added and selected.
	internal void AddEmitterFromTemplate( string template )
	{
		if ( _definition is null ) return;

		var emitter = EmitterTemplates.Build( template );
		if ( emitter is null ) return;

		emitter.Name = UniqueEmitterName( emitter.Name );

		PushUndo( "Add Emitter" );
		_definition.Emitters.Add( emitter );
		OnEdited();
		Select( emitter );
	}

	//Parameters

	internal ParticleDefinition.Parameter AddParameter( ParticleParameterKind kind )
	{
		if ( _definition is null ) return null;

		PushUndo( "Add Parameter" );

		ParticleDefinition.Parameter added;

		switch ( kind )
		{
			case ParticleParameterKind.Vector:
				var vector = new ParticleDefinition.VectorParameter { Name = Unused( "Vector", _definition.VectorParameters ) };
				_definition.VectorParameters.Add( vector );
				added = vector;
				break;

			case ParticleParameterKind.Color:
				var colour = new ParticleDefinition.ColorParameter { Name = Unused( "Color", _definition.ColorParameters ) };
				_definition.ColorParameters.Add( colour );
				added = colour;
				break;

			default:
				var number = new ParticleDefinition.FloatParameter { Name = Unused( "Float", _definition.FloatParameters ) };
				_definition.FloatParameters.Add( number );
				added = number;
				break;
		}

		OnEdited();
		Select( added );

		return added;
	}

	//Renames a parameter and every module binding to it, since modules bind parameters by name
	internal void RenameParameter( ParticleDefinition.Parameter parameter, string name )
	{
		if ( _definition is null ) return;

		name = name?.Trim();

		if ( string.IsNullOrEmpty( name ) || name == parameter.Name ) return;

		var kind = ParameterList.KindOf( parameter );

		if ( ParameterList.ParametersOf( _definition, kind ).Any( other => other != parameter && string.Equals( other.Name, name, StringComparison.OrdinalIgnoreCase ) ) )
			return;

		PushUndo( "Rename Parameter" );

		Rebind( kind, parameter.Name, name );

		parameter.Name = name;

		OnEdited();
		Select( parameter );
	}

	//The name was typed straight into the property sheet, which knows nothing of bindings: the modules follow it here.
	internal void NoteParameterRenamed( ParticleDefinition.Parameter parameter, string previous )
	{
		if ( string.IsNullOrEmpty( previous ) || previous == parameter.Name ) return;

		Rebind( ParameterList.KindOf( parameter ), previous, parameter.Name );
	}

	void Rebind( ParticleParameterKind kind, string from, string to )
	{
		foreach ( var (_, binding) in Bindings( kind ) )
		{
			if ( string.Equals( binding.ParameterName, from, StringComparison.OrdinalIgnoreCase ) )
				binding.ParameterName = to;
		}
	}

	internal ParticleDefinition.Parameter DuplicateParameter( ParticleDefinition.Parameter parameter )
	{
		if ( _definition is null ) return null;

		var copy = (ParticleDefinition.Parameter)Clone( parameter, parameter.GetType() );
		if ( copy is null ) return null;

		var kind = ParameterList.KindOf( parameter );
		var stem = System.Text.RegularExpressions.Regex.Replace( parameter.Name ?? "", @"\d+$", "" );

		copy.Identifier = Guid.NewGuid().ToString();

		PushUndo( "Duplicate Parameter" );

		switch ( copy )
		{
			case ParticleDefinition.VectorParameter vector:
				vector.Name = Unused( stem, _definition.VectorParameters );
				_definition.VectorParameters.Insert( _definition.VectorParameters.IndexOf( (ParticleDefinition.VectorParameter)parameter ) + 1, vector );
				break;

			case ParticleDefinition.ColorParameter colour:
				colour.Name = Unused( stem, _definition.ColorParameters );
				_definition.ColorParameters.Insert( _definition.ColorParameters.IndexOf( (ParticleDefinition.ColorParameter)parameter ) + 1, colour );
				break;

			case ParticleDefinition.FloatParameter number:
				number.Name = Unused( stem, _definition.FloatParameters );
				_definition.FloatParameters.Insert( _definition.FloatParameters.IndexOf( (ParticleDefinition.FloatParameter)parameter ) + 1, number );
				break;
		}

		OnEdited();
		Select( copy );

		return copy;
	}

	//Take a parameter away, and unbind every module that was reading it.
	internal void RemoveParameter( ParticleDefinition.Parameter parameter )
	{
		if ( _definition is null ) return;

		PushUndo( "Remove Parameter" );

		foreach ( var (_, binding) in Bindings( ParameterList.KindOf( parameter ) ) )
		{
			if ( !string.Equals( binding.ParameterName, parameter.Name, StringComparison.OrdinalIgnoreCase ) ) continue;

			binding.ParameterName = null;
			binding.UseParameter = false;
		}

		_definition.FloatParameters.Remove( parameter as ParticleDefinition.FloatParameter );
		_definition.VectorParameters.Remove( parameter as ParticleDefinition.VectorParameter );
		_definition.ColorParameters.Remove( parameter as ParticleDefinition.ColorParameter );

		OnEdited();

		if ( IsSelected( parameter ) )
			Select( _definition );
	}

	//Every module with a binding that reads this parameter.
	internal IEnumerable<ParticleModule> ParameterUses( ParticleDefinition.Parameter parameter )
	{
		if ( _definition is null || parameter is null ) yield break;

		foreach ( var (module, binding) in Bindings( ParameterList.KindOf( parameter ) ) )
		{
			if ( binding.UseParameter && string.Equals( binding.ParameterName, parameter.Name, StringComparison.OrdinalIgnoreCase ) )
				yield return module;
		}
	}

	//Default edited: refresh the preview live, then one undo entry and a rebuild once editing stops
	internal void OnParameterValueEdited( ParticleDefinition.Parameter parameter, bool finished )
	{
		if ( _definition is null ) return;

		_dirty = true;
		_writeAt = 0.15f;

		if ( !finished )
		{
			_preview?.RefreshParameters();
			return;
		}

		PushUndo( $"Edit {parameter.Name}" );

		_definition.NoteChanged();
		_preview?.RefreshParameters();
		PushToScenes();

		CommitUndo();
		UpdateTitle();
	}

	//Every binding of one kind on every module, with the module it is on. By reflection over the module's properties rather than by a list the module keeps:
	//a module says what it reads by the type of the property, and that is the one place it says so. An addon's module is walked the same as the engine's.
	IEnumerable<(ParticleModule Module, IParticleBinding Binding)> Bindings( ParticleParameterKind kind )
	{
		if ( _definition is null ) yield break;

		var wanted = kind switch
		{
			ParticleParameterKind.Vector => typeof( ParticleVectorBinding ),
			ParticleParameterKind.Color => typeof( ParticleColorBinding ),
			_ => typeof( ParticleFloatBinding ),
		};

		foreach ( var emitter in _definition.Emitters )
		{
			foreach ( var module in emitter.AllModules() )
			{
				foreach ( var property in module.GetType().GetProperties() )
				{
					if ( property.PropertyType != wanted || !property.CanRead ) continue;

					if ( property.GetValue( module ) is { } value )
						yield return (module, new BindingView( value ));
				}
			}
		}
	}

	//The two things every binding has, whichever of the three it is.
	internal interface IParticleBinding
	{
		string ParameterName { get; set; }
		bool UseParameter { get; set; }
	}

	//One of the three bindings seen as the two properties they share. The bindings are
	//sealed classes with no common base, so this is a view over whichever one it was handed.
	sealed class BindingView : IParticleBinding
	{
		readonly object _binding;

		public BindingView( object binding ) => _binding = binding;

		public string ParameterName
		{
			get => _binding switch
			{
				ParticleFloatBinding f => f.ParameterName,
				ParticleVectorBinding v => v.ParameterName,
				ParticleColorBinding c => c.ParameterName,
				_ => null,
			};
			set
			{
				switch ( _binding )
				{
					case ParticleFloatBinding f: f.ParameterName = value; break;
					case ParticleVectorBinding v: v.ParameterName = value; break;
					case ParticleColorBinding c: c.ParameterName = value; break;
				}
			}
		}

		public bool UseParameter
		{
			get => _binding switch
			{
				ParticleFloatBinding f => f.UseParameter,
				ParticleVectorBinding v => v.UseParameter,
				ParticleColorBinding c => c.UseParameter,
				_ => false,
			};
			set
			{
				switch ( _binding )
				{
					case ParticleFloatBinding f: f.UseParameter = value; break;
					case ParticleVectorBinding v: v.UseParameter = value; break;
					case ParticleColorBinding c: c.UseParameter = value; break;
				}
			}
		}
	}

	//A name nothing in the list is already called.
	static string Unused<T>( string stem, List<T> parameters ) where T : ParticleDefinition.Parameter
	{
		for ( var index = parameters.Count + 1; ; index++ )
		{
			var name = $"{stem}{index}";

			if ( !parameters.Any( parameter => string.Equals( parameter.Name, name, StringComparison.OrdinalIgnoreCase ) ) )
				return name;
		}
	}

	//Copies

	//A copy of something in the definition that shares nothing with it. Through JSON, because a module is polymorphic and its list
	//carries its own converter - a hand-written clone would have to know every module there is, including ones an addon added.
	static T Clone<T>( T source ) where T : class => (T)Clone( source, typeof( T ) );

	static object Clone( object source, Type type )
	{
		if ( source is null ) return null;

		try
		{
			return Json.FromNode( Json.ToNode( source, type ), type );
		}
		catch ( Exception e )
		{
			Log.Warning( e, "Particles: could not copy that" );

			return null;
		}
	}
}
