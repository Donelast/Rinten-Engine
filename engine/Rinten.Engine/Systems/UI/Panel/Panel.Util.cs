
using System.Threading;

namespace Rinten.UI;

public partial class Panel
{
	internal double TimeNow => PanelRealTime.TimeNow;
	internal double TimeDelta => PanelRealTime.TimeDelta;

	/// <summary>Can be used to store random data without sub-classing the panel.</summary>
	[Hide]
	public object UserData { get; set; }


	CancellationTokenSource _deleteTokenSource;

	/// <summary>Get a token that is cancelled when the panel is deleted</summary>
	[Hide]
	public CancellationToken DeletionToken
	{
		get
		{
			if ( IsDeleting || !IsValid )
				return CancellationToken.None;

			_deleteTokenSource ??= new CancellationTokenSource();
			return _deleteTokenSource.Token;
		}
	}
}

//UI animation clock from the free-running timer, so panel-only windows animate without a game loop
static class PanelRealTime
{
	public static double TimeNow;
	public static double TimeDelta;

	private static double lastRead;

	public static void Update()
	{
		var now = RealTime.NowDouble;
		var delta = lastRead > 0 ? now - lastRead : 0.0;

		lastRead = now;

		//If we're running lower than 30fps, clamp it to avoid weirdness
		delta = delta.Clamp( 0.0, 1.0 / 30.0 );

		TimeDelta = delta;
		TimeNow += TimeDelta;
	}
}
