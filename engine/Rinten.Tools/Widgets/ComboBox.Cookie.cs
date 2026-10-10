namespace Editor
{
	public partial class ComboBox : Widget
	{
		string _stateCookie;

		public string StateCookie
		{
			get => _stateCookie;
			set
			{
				if ( _stateCookie == value ) return;
				_stateCookie = value;

				RestoreFromStateCookie();
			}
		}

		public virtual void RestoreFromStateCookie()
		{
			if ( string.IsNullOrWhiteSpace( StateCookie ) )
				return;

			var state = EditorCookie.GetString( $"ComboBox.{StateCookie}.State", null );
			if ( state == null ) return;

			var index = FindIndex( state ) ?? -1;
			if ( index < 0 ) return;

			CurrentIndex = index;
		}

		public virtual void SaveToStateCookie()
		{
			if ( string.IsNullOrWhiteSpace( StateCookie ) )
				return;

			EditorCookie.SetString( $"ComboBox.{StateCookie}.State", CurrentText );
		}
	}
}
