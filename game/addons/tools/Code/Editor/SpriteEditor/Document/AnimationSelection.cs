namespace Editor.SpriteEditor;

//A restore replaces every animation object, and a delete can take the selected one away. Both are followed by name: the animation of that name is selected again where there is one, and the first otherwise.
/// <summary>The selected animation, kept on the sprite through everything the document does to it.</summary>
public sealed class AnimationSelection : IAnimationSelection, IDisposable
{
	private readonly ISpriteDocument _document;

	private Sprite.Animation _selected;
	private string _name;

	public event Action Changed;

	public AnimationSelection( ISpriteDocument document )
	{
		_document = document;

		_document.Loaded += OnLoaded;
		_document.Restored += OnRestored;
		_document.Changed += OnChanged;
	}

	public void Dispose()
	{
		_document.Loaded -= OnLoaded;
		_document.Restored -= OnRestored;
		_document.Changed -= OnChanged;
	}

	public Sprite.Animation Selected
	{
		get => _selected;
		set => Select( value, force: false );
	}

	private void Select( Sprite.Animation animation, bool force )
	{
		if ( !force && animation == _selected ) return;

		_selected = animation;
		_name = animation?.Name;

		Changed?.Invoke();
	}

	private void OnLoaded() => Select( _document.Sprite.Animations.FirstOrDefault(), force: true );

	//Every object is new, so the one that was selected is found by name and announced even where it is the same animation to the eye.
	private void OnRestored() => Select( Resolve(), force: true );

	private void OnChanged( SpriteChange change )
	{
		var animation = Resolve();

		//A rename keeps the object and changes the name it will be found by.
		if ( animation == _selected )
		{
			_name = animation?.Name;
			return;
		}

		Select( animation, force: false );
	}

	//The selected animation as it is on the sprite now: by reference, then by name, then the first.
	private Sprite.Animation Resolve()
	{
		var animations = _document.Sprite.Animations;

		if ( _selected is not null && animations.Contains( _selected ) ) return _selected;

		if ( !string.IsNullOrEmpty( _name ) )
		{
			var byName = animations.FirstOrDefault( a => a.Name == _name );
			if ( byName is not null ) return byName;
		}

		return animations.FirstOrDefault();
	}
}
