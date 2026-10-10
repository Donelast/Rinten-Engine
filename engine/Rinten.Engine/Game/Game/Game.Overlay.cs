using Rinten.Engine;
using Rinten.Modals;

namespace Rinten;

public static partial class Game
{
	/// <summary>Opens modal overlays: packages, maps, news, reviews, settings, bindings, player list</summary>
	public partial class Overlay
	{
		/// <summary>Opens a modal for the specified game package</summary>
		public static void ShowGameModal( string packageIdent )
		{
			using var scope = GlobalContext.MenuScope();

			IModalSystem.Current?.Game( packageIdent );
		}

		/// <summary>Opens a modal for the specified map package</summary>
		public static void ShowMapModal( string packageIdent )
		{
			using var scope = GlobalContext.MenuScope();

			IModalSystem.Current?.Map( packageIdent );
		}

		/// <summary>Opens a modal for the specified package</summary>
		public static void ShowPackageModal( string ident )
		{
			using var scope = GlobalContext.MenuScope();

			IModalSystem.Current?.Package( ident, "" );
		}

		/// <summary>Opens a modal for the specified package on the specified page</summary>
		public static void ShowPackageModal( string ident, string page )
		{
			using var scope = GlobalContext.MenuScope();

			IModalSystem.Current?.Package( ident, page );
		}

		/// <summary>Opens a modal for the specified organization. This is most likely called from a Package - so get the organization from there.</summary>
		public static void ShowOrganizationModal( Package.Organization org )
		{
			using var scope = GlobalContext.MenuScope();

			IModalSystem.Current?.Organization( org );
		}

		/// <summary>Opens a modal to review the specified package</summary>
		public static void ShowReviewModal( Package package )
		{
			using var scope = GlobalContext.MenuScope();

			IModalSystem.Current?.Review( package );
		}

		/// <summary>Opens a modal to report the specified package</summary>
		public static void ShowReportModal( string packageIdent )
		{
			using var scope = GlobalContext.MenuScope();

			IModalSystem.Current?.Report( packageIdent );
		}

		/// <summary>Opens a modal for selecting a package</summary>
		public static void ShowPackageSelector( string query, Action<Package> onSelect, Action<string> onFilterChanged = null )
		{
			using var scope = GlobalContext.MenuScope();

			IModalSystem.Current?.PackageSelect( query, onSelect, onFilterChanged );
		}

		/// <summary>Opens a modal for selecting a map. This can be either a map package from the workshop (an ident), or a scene from a mount (a path)</summary>
		public static void ShowMapSelector( Action<string> onSelect, string selected = null )
		{
			using var scope = GlobalContext.MenuScope();

			IModalSystem.Current?.MapSelect( onSelect, selected );
		}

		/// <summary>Opens a modal that lets you modify your settings Optionally, you can specify a page to open directly to: "keybinds", "video", "input", "audio", "game", "storage", "developer"</summary>
		public static void ShowSettingsModal( string page = "" )
		{
			using var scope = GlobalContext.MenuScope();

			IModalSystem.Current?.Settings( page );
		}

		/// <summary>Shows modal to edit the streaming settings</summary>
		public static void ShowServiceConnector()
		{
			using var scope = GlobalContext.MenuScope();

			IModalSystem.Current?.ServiceConnector();
		}

		/// <summary>Opens a modal that lets you view and rebind game input actions.</summary>
		public static void ShowBinds()
		{
			using var scope = GlobalContext.MenuScope();

			IModalSystem.Current?.Settings( "keybinds" );
		}

		/// <summary>Opens a modal to create a game with a bunch of settings. We use this in the menu when you click "Create Game" and the game has options.</summary>
		public static void CreateGame( in CreateGameOptions options )
		{
			using var scope = GlobalContext.MenuScope();

			IModalSystem.Current?.CreateGame( options );
		}

		/// <summary>Open a modal that shows a list of players currently in the game</summary>
		public static void ShowPlayerList()
		{
			using var scope = GlobalContext.MenuScope();

			IModalSystem.Current?.PlayerList();
		}

		/// <summary>Opens the pause menu overlay. This is the same menu that appears when pressing ESC.</summary>
		public static void ShowPauseMenu()
		{
			if ( IModalSystem.Current?.IsModalOpen == true )
				return;

			using var scope = GlobalContext.MenuScope();

			IModalSystem.Current?.PauseMenu();
		}

		/// <summary>Closes the top overlay if one exists</summary>
		public static void Close()
		{
			if ( IModalSystem.Current?.IsModalOpen != true )
				return;

			IModalSystem.Current?.PauseMenu();
		}

		/// <summary>Close all open overlays</summary>
		public static void CloseAll( bool immediate = false )
		{
			IModalSystem.Current?.CloseAll( immediate );
		}

		/// <summary>Returns true if any overlay is open</summary>
		public bool IsOpen => IModalSystem.Current?.IsModalOpen ?? false;

		/// <summary>Returns true if the pause menu overlay is open</summary>
		public bool IsPauseMenuOpen => IModalSystem.Current?.IsPauseMenuOpen ?? false;
	}
}
