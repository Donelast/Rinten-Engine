using System;
using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Views;
using Android.Widget;

//Rinten has a Color of its own, and this file lives in a namespace under Rinten - so the one a view is painted with has to be named.
using AndroidColor = global::Android.Graphics.Color;

namespace Rinten.Android;

//Shown when the game fails to start: the error, where the report is, and a share button
internal static class CrashScreen
{
	//Views belong to the layout and activity, so they are not disposed here (CA2000 suppressed)
#pragma warning disable CA2000
	public static void Show( Activity activity, string text, string file )
	{
		if ( activity is null ) return;

		var page = new LinearLayout( activity ) { Orientation = Orientation.Vertical };
		page.SetBackgroundColor( AndroidColor.ParseColor( "#101012" ) );
		page.SetPadding( 32, 48, 32, 32 );

		var title = new TextView( activity )
		{
			Text = "The game stopped",
			TextSize = 20,
		};
		title.SetTextColor( AndroidColor.ParseColor( "#f5f5f5" ) );

		page.AddView( title );

		if ( !string.IsNullOrEmpty( file ) )
		{
			var where = new TextView( activity )
			{
				Text = $"A report is saved as\n{file}",
				TextSize = 11,
			};
			where.SetTextColor( AndroidColor.ParseColor( "#96969b" ) );
			where.SetPadding( 0, 16, 0, 16 );

			page.AddView( where );
		}

		var body = new TextView( activity )
		{
			Text = text,
			TextSize = 10,
		};
		body.SetTextColor( AndroidColor.ParseColor( "#dcdce1" ) );
		body.SetTypeface( Typeface.Monospace, TypefaceStyle.Normal );
		body.SetTextIsSelectable( true );

		var scroll = new ScrollView( activity );
		scroll.AddView( body );

		//The report takes the room that is going; the button stays on the screen under it however long the report is.
		page.AddView( scroll, new LinearLayout.LayoutParams(
			ViewGroup.LayoutParams.MatchParent, 0, 1.0f ) );

		var send = new Button( activity ) { Text = "Send the report" };
		send.Click += ( _, _ ) => Send( activity, text );

		page.AddView( send );

		activity.SetContentView( page );
	}
#pragma warning restore CA2000

	//Shares the report as text, which avoids a content provider; the file stays for attaching
	private static void Send( Activity activity, string text )
	{
		try
		{
			using var intent = new Intent( Intent.ActionSend );

			intent.SetType( "text/plain" );
			intent.PutExtra( Intent.ExtraSubject, $"{activity.PackageName} crash report" );
			intent.PutExtra( Intent.ExtraText, text );

			using var chooser = Intent.CreateChooser( intent, "Send the report" );

			activity.StartActivity( chooser );
		}
		catch ( Exception e )
		{
			global::Android.Util.Log.Error( "rinten", $"could not send the report: {e.Message}" );
		}
	}
}
