using System;

namespace Editor
{
	public partial class ComboBox : Widget
	{
		/// <summary>The field the words are typed into, where this box is editable.</summary>
		public LineEdit LineEdit { get; init; }


		public ComboBox( Widget parent = null ) : base()
		{
			MakeSurface( parent );

			LineEdit = new LineEdit( this );
			Editable = false;

			Size = new Vector2( 120, Theme.ControlHeight );
		}

		public string CurrentText
		{
			get
			{
				if ( editable ) return LineEdit?.Text ?? editText ?? string.Empty;

				return currentIndex >= 0 && currentIndex < items.Count ? items[currentIndex].Text : string.Empty;
			}
			set
			{
				editText = value;

				if ( LineEdit is not null ) LineEdit.Text = value;

				if ( !changedProgramatically && FindIndex( value ) is int found ) CurrentIndex = found;

				Update();
			}
		}

		public int CurrentIndex
		{
			get => currentIndex;
			set
			{
				if ( CurrentIndex == value )
					return;

				currentIndex = value;

				if ( editable && LineEdit is not null ) LineEdit.Text = CurrentText;

				Update();
			}
		}

		public int Count
		{
			get => items.Count;
		}

		bool changedProgramatically = false;

		public int? FindIndex( string text )
		{
			var found = items.FindIndex( x => x.Text == text );

			return found < 0 ? null : found;
		}

		public bool TrySelectNamed( string name )
		{
			var i = FindIndex( name );
			if ( i == null ) return false;

			var changed = CurrentIndex != i.Value;
			CurrentIndex = i.Value;
			if ( changed ) InternalIndexChanged();
			return true;
		}

		public void ClearText()
		{
			editText = string.Empty;

			if ( LineEdit is not null ) LineEdit.Text = string.Empty;

			Update();
		}
		public void Clear()
		{
			items.Clear();
			currentIndex = -1;

			Update();
		}

		public bool AllowDuplicates
		{
			get => allowDuplicates;
			set
			{
				allowDuplicates = value;
			}
		}

		public int MaxVisibleItems
		{
			get => maxVisibleItems;
			set
			{
				maxVisibleItems = value;
			}
		}

		private bool allowDuplicates;
		private int maxVisibleItems = 16;
		public bool Editable
		{
			get => editable;
			set
			{
				editable = value;

				MarkNeedsArrange();
				Update();
			}
		}
		/// <summary>Where a typed-in word is added to the list.</summary>
		public InsertMode Insertion { get; set; } = InsertMode.AtBottom;

		public Action OnReturn { get; internal set; }


		public enum InsertMode
		{
			Skip,
			AtTop,
			AtCurrent,
			AtBottom,
			AfterCurrent,
			BeforeCurrent,
			Alphabetically
		}

		public event Action TextChanged;
		public event Action ItemChanged;

		protected virtual void OnTextChanged()
		{
			var txt = CurrentText;

			AutoComplete?.OnAutoComplete( txt, ScreenPosition );
			TextChanged?.Invoke();

			InvokeSelected();
		}

		internal void InternalTextChanged() => OnTextChanged();

		Dictionary<string, Action> ItemActions = new();

		public void AddItem( string text, string icon = null, Action onSelected = null, string description = null, bool selected = false, bool enabled = true )
		{
			if ( !allowDuplicates && FindIndex( text ) is not null ) return;

			items.Add( new Item { Text = text, Icon = icon, Description = description, Enabled = enabled } );

			if ( selected ) CurrentIndex = items.Count - 1;
			if ( onSelected != null ) ItemActions[text] = onSelected;

			AdjustSize();
			Update();
		}

		internal void InternalIndexChanged() => OnItemChanged();

		public void InvokeSelected()
		{
			if ( ItemActions.TryGetValue( CurrentText, out var action ) )
			{
				action.Invoke();
			}
		}

		protected virtual void OnItemChanged()
		{
			SaveToStateCookie();

			var txt = CurrentText;

			ItemChanged?.Invoke();

			InvokeSelected();

			if ( !changedProgramatically )
				SignalValuesChanged();
		}

		public void SetAutoComplete( Action<Menu, string> func )
		{
			AutoComplete = new AutoComplete( this );
			AutoComplete.OnOptionSelected = ( o ) => CurrentText = o;
			AutoComplete.OnBuildOptions = func;
		}

		public AutoComplete AutoComplete { get; set; }

		protected override void OnBlur( FocusChangeReason reason )
		{
			AutoComplete?.OnParentBlur();

			base.OnBlur( reason );
		}

		protected override void OnKeyPress( KeyEvent e )
		{
			AutoComplete?.OnParentKeyPress( e );

			base.OnKeyPress( e );
		}
	}
}
