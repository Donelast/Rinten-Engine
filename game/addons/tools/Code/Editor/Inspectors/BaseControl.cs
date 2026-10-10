using Editor;
using Facepunch.ActionGraphs;

namespace Editor.Inspectors;

public class BaseControl : Panel, IEditorDropTarget
{
	SerializedProperty _property;
	bool _needsRebuild;
	bool _readOnly;

	public BaseControl()
	{
		AddClass( "control" );
	}

	int widthTiers;

	/// <summary>Says how wide this control came out, as classes its sheet can take parts away by - see <see cref="WidthTiers"/>.</summary>
	public override void FinalLayout( Vector2 offset )
	{
		base.FinalLayout( offset );

		WidthTiers.Apply( this, ref widthTiers );
	}

	[Parameter]
	public SerializedProperty Property
	{
		get => _property;
		set
		{
			if ( _property == value ) return;

			_property = value;
			_needsRebuild = true;
			seenVersion = -1;

			//The short one. The row this sits in carries the full account of the property - name, type, summary, default - and the
			//pointer finds this one first, so hovering the field explains the field and hovering its label explains the property.
			Tooltip = value?.Description ?? value?.DisplayName;

			ApplyReadOnlyState();
		}
	}

	/// <summary>Forces the control read-only regardless of <see cref="Property"/>.</summary>
	[Parameter]
	public bool ReadOnly
	{
		get => _readOnly;
		set
		{
			if ( _readOnly == value ) return;

			_readOnly = value;
			ApplyReadOnlyState();
		}
	}

	[Parameter]
	public bool MultipleRows
	{
		get => HasClass( "multiple-rows" );
		set => SetClass( "multiple-rows", value );
	}

	[Parameter]
	public bool ShowBackground
	{
		get => !HasClass( "no-background" );
		set => SetClass( "no-background", !value );
	}

	/// <summary>True when editing is blocked by <see cref="ReadOnly"/> or the bound property.</summary>
	public bool IsReadOnly => _readOnly || IsPropertyReadOnly();

	protected bool IsPropertyReadOnly()
	{
		if ( Property is null )
			return false;

		return !Property.IsEditable || Property.HasAttribute<ReadOnlyAttribute>();
	}

	void ApplyReadOnlyState()
	{
		var readOnly = IsReadOnly;
		SetClass( "readonly", readOnly );
		AcceptsFocus = !readOnly;
		OnReadOnlyChanged();
		StateHasChanged();
	}

	//Sync interactive children when read-only state changes.
	protected virtual void OnReadOnlyChanged()
	{
	}

	/// <summary>Options this control puts at the top of the property menu.</summary>
	public virtual void OnPropertyContextMenu( Menu menu )
	{
	}

	//Property menu on the control itself, since not every control sits in a row; innermost answers
	protected override void OnRightClick( MousePanelEvent e )
	{
		base.OnRightClick( e );

		if ( Property is null ) return;

		if ( PropertyContextMenu.Open( this, Property, this ) )
			e.StopPropagation();
	}

	/// <summary>The value as text, for the row's copy option.</summary>
	public virtual string ToClipboardString()
	{
		if ( Property is null ) return null;

		using var options = PushSerializationOptions();

		var str = Json.Serialize( Property.GetValue<object>() );
		if ( str?.StartsWith( '"' ) ?? false ) str = str.Substring( 1, str.Length - 2 );

		return str;
	}

	/// <summary>Takes what the row's paste option found on the clipboard.</summary>
	public virtual void FromClipboardString( string clipboard )
	{
		if ( Property is null || IsReadOnly || string.IsNullOrWhiteSpace( clipboard ) ) return;

		clipboard = clipboard.Trim();

		if ( !clipboard.StartsWith( '{' ) && !clipboard.StartsWith( '[' ) && !clipboard.StartsWith( '"' ) )
			clipboard = $"\"{clipboard}\"";

		using var options = PushSerializationOptions();
		using var uniqueGuids = ActionGraph.PushMakeGuidsUnique( true );

		if ( !Json.TryDeserialize( clipboard, Property.PropertyType, out var value ) || value is null )
			return;

		Property.Parent?.NoteStartEdit( Property );
		Property.SetValue( value );
		Property.Parent?.NoteFinishEdit( Property );
	}

	//The action graph serializer wants to know which GameObject the property is on.
	IDisposable PushSerializationOptions()
	{
		if ( Property.GetContainingGameObject() is not { } gameObject ) return null;

		return ActionGraph.PushTarget( InputDefinition.Target( typeof( GameObject ), gameObject ) );
	}

	public virtual bool AcceptEditorDrops => false;

	public virtual DropAction OnEditorDragHover( Widget.DragEvent ev ) => DropAction.Ignore;

	public virtual void OnEditorDragDrop( Widget.DragEvent ev )
	{
	}

	public virtual void OnEditorDragLeave()
	{
	}

	/// <summary>Called whenever SerializedProperty changes to another property and we think we probably need to rebuild</summary>
	public virtual void Rebuild()
	{

	}

	void RebuildIfNeeded()
	{
		if ( !_needsRebuild ) return;
		_needsRebuild = false;
		Rebuild();
		ApplyReadOnlyState();
	}

	protected override void OnAfterTreeRender( bool firstTime )
	{
		base.OnAfterTreeRender( firstTime );
		RebuildIfNeeded();
	}

	int valueHash = 0;
	int seenVersion = -1;

	//Controls poll on different frames rather than all on the same one. Seeded per instance so a sheet built in one go does not line them up.
	static int pollSeed;
	int pollPhase = pollSeed++;

	const int PollInterval = 4;

	int ValueHash() => HashCode.Combine( _property.GetValue<object>( default ) );

	public override void Tick()
	{
		RebuildIfNeeded();

		base.Tick();

		if ( HasFocus ) return;
		if ( _property is null ) return;

		//An edit made through the editor announces itself all the way up, so the ordinary frame costs one int compared against what this control drew.
		var version = _property.ChangeVersion;
		if ( version != seenVersion )
		{
			seenVersion = version;
			valueHash = ValueHash();
			StateHasChanged();
			OnPropertyValueChanged();
			return;
		}

		//Nothing announced. A value can still move without telling anyone - game code writing a field while the scene runs - so it is read back anyway,
		//but every few frames instead of every one. Reading it means boxing it, and that is the whole cost of an inspector that is only ever watching.
		if ( ++pollPhase % PollInterval != 0 ) return;

		var hash = ValueHash();
		if ( hash == valueHash ) return;

		valueHash = hash;
		StateHasChanged();
		OnPropertyValueChanged();
	}

	//The property's value moved; a control built by hand refreshes what shows it here.
	protected virtual void OnPropertyValueChanged()
	{
	}

	public virtual bool SupportsMultiEdit => false;

	/// <summary>True when the editor should sit below the property title instead of beside it.</summary>
	public virtual bool IsWideMode => false;

	/// <summary>False when the row should not show a property title (e.g. inline editors).</summary>
	public virtual bool IncludeLabel => true;

	/// <summary>True for placeholder controls (e.g. missing editor).</summary>
	public virtual bool IsFallbackEditor => false;

	/// <summary>Create a BaseControl for a given SerializedProperty. We'll look at BaseControls with [CustomEditor] attributes and see if any of them can handle this property.</summary>
	public static BaseControl CreateFor( SerializedProperty property )
	{
		ArgumentNullException.ThrowIfNull( property, nameof( property ) );

		var scored = ControlEditors.ForGame( property );

		//Use the first editor we can successfully create. Indexed rather than enumerated because the list is scratch shared with the next caller.
		for ( var i = 0; i < scored.Count; i++ )
		{
			var c = scored[i].Type.Create<BaseControl>();
			if ( c is not null )
			{
				if ( property.IsMultipleValues && !c.SupportsMultiEdit )
				{
					c.Delete( true );
					return new MultiEditNotSupported( property );
				}

				c.Property = property;
				return c;
			}
		}

		//Nope - sorry, nothing for you
		if ( property.IsMethod )
			return null;

		return new MissingEditor( property );
	}
}

file class InfoControl : BaseControl
{
	public InfoControl( SerializedProperty property, string info )
	{
		AddClass( "info-control" );
		AddClass( "no-background" );
		var label = AddChild<Rinten.UI.Label>();
		label.Text = info;

		Property = property;
	}

	protected override void OnReadOnlyChanged()
	{
		AcceptsFocus = true;
	}
}

file class MultiEditNotSupported( SerializedProperty property ) : InfoControl( property, "MultiEditNotSupported" )
{
}

file class MissingEditor( SerializedProperty property ) : InfoControl( property, $"No Editor Found For {property.PropertyType.Name}" )
{
	public override bool IsFallbackEditor => true;
}
