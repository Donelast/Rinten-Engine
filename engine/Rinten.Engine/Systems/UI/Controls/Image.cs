using Rinten.Rendering;

namespace Rinten.UI
{
	/// <summary>A generic box that displays a given texture within itself.</summary>
	[Library( "image" ), Alias( "img" ), Expose]
	public partial class Image : Panel
	{
		/// <summary>The texture being displayed by this panel.</summary>
		public Texture Texture
		{
			get => texture;
			set
			{
				if ( texture == value ) return;

				texture = value;
				_textureSize = value.IsValid() ? value.Size : default;
				_textureVersion = value?.DirtyVersion ?? 0;

				IsRenderDirty = true;
				LayoutTree?.MarkDirty();
				SetNeedsPreLayout();
			}
		}

		private Texture texture;

		Vector2 _textureSize;
		int _textureVersion;

		//Loading can replace the contents of the same cached Texture wrapper - a placeholder that takes the real picture when
		//it arrives - so the version is watched as the hash, the way a panel's background image is watched by its paint.
		protected override int BuildHash() => texture?.DirtyVersion ?? 0;

		protected override void OnBuildHashChanged()
		{
			base.OnBuildHashChanged();

			if ( texture is null || texture.DirtyVersion == _textureVersion ) return;
			_textureVersion = texture.DirtyVersion;

			var size = texture.IsValid() ? texture.Size : default;
			if ( size == _textureSize ) return;
			_textureSize = size;

			IsRenderDirty = true;
			LayoutTree?.MarkDirty();
			SetNeedsPreLayout();
		}

		public Image()
		{
			LayoutTree.SetMeasureFunction( MeasureTexture );
		}

		/// <summary>Set <see cref="Texture"/> from a file path. URLs supported.</summary>
		public async void SetTexture( string name )
		{
			if ( string.IsNullOrWhiteSpace( name ) ) return;
			if ( !IsValid ) return;

			Texture = await Texture.LoadAsync( name );

			if ( !IsValid ) return;

			//The setter above says it for us now - both that there is drawing to do and that the panel measures differently.

		}

		float oldScaleToScreen = 1.0f;
		internal override void PreLayout( LayoutCascade cascade )
		{
			base.PreLayout( cascade );

			if ( ScaleToScreen != oldScaleToScreen )
			{
				LayoutTree.MarkDirty();
			}
		}

		//The editor walk draws this itself.
		internal override bool DrawnByWalk => true;

		public override void OnDraw()
		{
			if ( Texture == null )
				return;

			var length = ComputedStyle.ObjectFit switch
			{
				ObjectFit.Contain => Length.Contain,
				ObjectFit.Cover => Length.Cover,
				ObjectFit.Fill => Length.Percent( 100 ).Value,
				_ => Length.Auto,
			};

			DrawBackgroundTexture( Texture, length );
		}

		public override void SetProperty( string name, string value )
		{
			base.SetProperty( name, value );

			if ( name == "src" ) SetTexture( value );
		}

		internal Vector2 MeasureTexture( float width, Rinten.Layout.MeasureMode widthMode, float height, Rinten.Layout.MeasureMode heightMode )
		{
			if ( !Texture.IsValid() ) return default;

			try
			{
				var (w, h) = (Texture.Width, Texture.Height);
				var exact = Rinten.Layout.MeasureMode.Exactly;
				var atMost = Rinten.Layout.MeasureMode.AtMost;

				oldScaleToScreen = ScaleToScreen;
				var ideal = new Vector2( w * ScaleToScreen, h * ScaleToScreen );

				if ( widthMode == exact ) return new Vector2( width, h * width / w );
				if ( heightMode == exact ) return new Vector2( w * height / h, height );

				if ( widthMode == atMost && heightMode == atMost && (width < ideal.x || height < ideal.y) )
				{
					float scale = Math.Min( width / w, height / h );
					return new Vector2( w * scale, h * scale );
				}

				if ( widthMode == atMost && width < ideal.x ) return new Vector2( width, h * width / w );
				if ( heightMode == atMost && height < ideal.y ) return new Vector2( w * height / h, height );

				return ideal;
			}
			catch ( System.Exception e )
			{
				Log.Warning( e );
				return default;
			}
		}

	}

	namespace Construct
	{
		public static class ImageConstructor
		{
			/// <summary>Create an image with given texture and CSS classname.</summary>
			public static Image Image( this PanelCreator self, string image = null, string classname = null )
			{
				var control = self.panel.AddChild<Image>();

				if ( image != null )
					control.SetTexture( image );

				if ( classname != null )
					control.AddClass( classname );

				return control;
			}
		}
	}
}
