using Rinten.Bind;

namespace Editor
{
	public partial class Widget : EditorObject
	{


	}
}

namespace Rinten
{
	public static partial class RintenToolExtensions
	{
		/// <summary>Bind the Left hand side to the value of the given console variable.</summary>
		public static Link FromConsoleVariable( this Builder self, string name )
		{
			var c = self;
			return c.From( () => ConsoleSystem.GetValue( name ), x => ConsoleSystem.SetValue( name, x ) );
		}

		/// <summary>Bind the Left hand side to the value of the given console variable as an integer.</summary>
		public static Link FromConsoleVariableInt( this Builder self, string name )
		{
			var c = self;
			return c.From( () => Editor.ConsoleSystem.GetValueInt( name, 0 ), x => ConsoleSystem.SetValue( name, x ) );
		}
	}
}
