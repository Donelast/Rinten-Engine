using System.Collections.Concurrent;
using ChatMessage = Rinten.Streamer.ChatMessage;
using Viewer = Rinten.Streamer.Viewer;

namespace Rinten.Engine;

internal static partial class Streamer
{
	//Active chatters keyed by username. Written from the websocket thread and read from the main thread, so it has to be concurrent.
	static readonly ConcurrentDictionary<string, Viewer> _viewers = new( StringComparer.OrdinalIgnoreCase );

	//Cached snapshot handed out by Viewers. Only rebuilt when the roster gains or loses someone - updating
	//an existing viewer's details mutates the shared Viewer instance, which the snapshot already references.
	static Viewer[] _viewersSnapshot = [];
	static volatile bool _viewersDirty;

	//Whether we've taken our first presence snapshot from Get Chatters. The first reconcile seeds
	//the roster silently - we don't raise joins for people who were already here when we started.
	static bool _seededFromPresence;

	/// <summary>Everyone we currently think is in chat. The returned list is a shared snapshot - don't hold onto it across frames if you care about joins/leaves, just read it again.</summary>
	public static IReadOnlyList<Viewer> Viewers
	{
		get
		{
			if ( _viewersDirty )
			{
				//Clear before snapshotting: a concurrent add/remove during the rebuild will re-set the flag and we'll rebuild again next read rather than miss it.
				_viewersDirty = false;
				_viewersSnapshot = _viewers.Values.ToArray();
			}

			return _viewersSnapshot;
		}
	}

	//A viewer joined chat. Adds them to the roster and notifies scene listeners.
	internal static void OnUserJoin( string username )
	{
		var viewer = GetOrAddViewer( username, out var added );
		if ( viewer is null )
			return;

		//Only announce a join the first time we see them. If they're already in the roster -
		//they've chatted, sent a duplicate JOIN, or were already counted by presence - stay quiet.
		if ( !added )
			return;

		Dispatch( x => x.OnStreamJoin( viewer ) );
	}

	//A viewer left chat. Removes them from the roster and notifies scene listeners.
	internal static void OnUserLeave( string username )
	{
		if ( string.IsNullOrEmpty( username ) )
			return;

		//They've been removed from the roster, but listeners still want to know who left. Fall
		//back to a minimal viewer if we never tracked them (Twitch sends leaves for unknowns too).
		var viewer = RemoveViewer( username ) ?? new Viewer { Username = username, DisplayName = username };

		Dispatch( x => x.OnStreamLeave( viewer ) );
	}

	//A chat message arrived. Updates (or creates) the sender in the roster - join events are unreliable so
	//a message also enrolls them - builds the message around that viewer, and notifies scene listeners.
	internal static void OnUserMessage( string channel, string username, string userId, string displayName, string color, string[] badges, string message, int bits, bool isFirstMessage )
	{
		var viewer = GetOrAddViewer( username );
		if ( viewer is null )
			return;

		if ( !string.IsNullOrEmpty( userId ) )
			viewer.StreamerId = userId;

		if ( !string.IsNullOrEmpty( displayName ) )
			viewer.DisplayName = displayName;

		viewer.Color = Color.TryParse( color, out var parsed ) ? parsed : null;
		viewer.Badges = badges;
		viewer.HasChatted = true;

		var chat = new ChatMessage
		{
			Viewer = viewer,
			Message = message,
			Channel = channel,
			Bits = bits,
			IsFirstMessage = isFirstMessage,
			Time = DateTimeOffset.UtcNow,
		};

		Dispatch( x => x.OnStreamMessage( chat ) );
	}

	//Adds a viewer from event tags without a join/chat; only overwrites fields the event carried
	static Viewer EnrollViewer( string username, string userId, string displayName, string color, string[] badges )
	{
		var viewer = GetOrAddViewer( username );
		if ( viewer is null )
			return null;

		if ( !string.IsNullOrEmpty( userId ) )
			viewer.StreamerId = userId;

		if ( !string.IsNullOrEmpty( displayName ) )
			viewer.DisplayName = displayName;

		if ( !string.IsNullOrEmpty( color ) && Color.TryParse( color, out var parsed ) )
			viewer.Color = parsed;

		if ( badges is not null )
			viewer.Badges = badges;

		return viewer;
	}

	//Dispatch a stream event to scene components implementing IEvents. These arrive on
	//the websocket thread, so we marshal to the main thread before touching the scene.
	static void Dispatch( Action<Rinten.Streamer.IEvents> action )
	{
		MainThread.Queue( () =>
		{
			using ( GlobalContext.GameScope() )
			{
				ISceneEvent<Rinten.Streamer.IEvents>.Post( action );
			}
		} );
	}

	static Viewer GetOrAddViewer( string username ) => GetOrAddViewer( username, out _ );

	static Viewer GetOrAddViewer( string username, out bool added )
	{
		added = false;

		if ( string.IsNullOrEmpty( username ) )
			return null;

		if ( _viewers.TryGetValue( username, out var existing ) )
			return existing;

		var viewer = new Viewer
		{
			Username = username,
			DisplayName = username,
		};

		if ( _viewers.TryAdd( username, viewer ) )
		{
			_viewersDirty = true;
			added = true;
			return viewer;
		}

		//Lost a race to another add - return whoever won.
		return _viewers.TryGetValue( username, out existing ) ? existing : viewer;
	}

	//Pre-seed the roster with usernames we already know are present - e.g. the IRC NAMES list sent when we join a channel. This
	//is silent: it fills Viewers without raising join events, since these people were already here and we didn't see them arrive.
	internal static void SeedViewers( IEnumerable<string> usernames )
	{
		if ( usernames is null )
			return;

		foreach ( var username in usernames )
			GetOrAddViewer( username );
	}

	//Syncs the roster to a full chatter snapshot: new = join, missing = leave. Pass null on failed poll
	internal static void ReconcileViewers( Dictionary<string, (string DisplayName, string UserId)> present )
	{
		if ( present is null )
			return;

		//Joins: in the snapshot but not yet in the roster.
		foreach ( var (username, info) in present )
		{
			var viewer = GetOrAddViewer( username, out var added );
			if ( viewer is null )
				continue;

			if ( !string.IsNullOrEmpty( info.UserId ) )
				viewer.StreamerId = info.UserId;

			if ( !string.IsNullOrEmpty( info.DisplayName ) )
				viewer.DisplayName = info.DisplayName;

			if ( added && _seededFromPresence )
				Dispatch( x => x.OnStreamJoin( viewer ) );
		}

		//Leaves: in the roster but no longer in the snapshot.
		foreach ( var username in _viewers.Keys.ToArray() )
		{
			if ( present.ContainsKey( username ) )
				continue;

			var viewer = RemoveViewer( username );
			if ( viewer is null )
				continue;

			if ( _seededFromPresence )
				Dispatch( x => x.OnStreamLeave( viewer ) );
		}

		_seededFromPresence = true;
	}

	static Viewer RemoveViewer( string username )
	{
		if ( string.IsNullOrEmpty( username ) )
			return null;

		if ( !_viewers.TryRemove( username, out var viewer ) )
			return null;

		_viewersDirty = true;
		return viewer;
	}

	//Drop the whole roster, raising a leave for everyone first. Called when the stream disconnects
	//so listeners can tear down per-viewer state instead of being left holding a stale roster.
	static void ClearViewers()
	{
		foreach ( var viewer in _viewers.Values )
			Dispatch( x => x.OnStreamLeave( viewer ) );

		_viewers.Clear();
		_viewersDirty = true;
		_seededFromPresence = false;
	}
}
