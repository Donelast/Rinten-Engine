using System.Text.Json.Nodes;

namespace Rinten;

[Expose]
public class RenderOptions : IJsonPopulator, IEquatable<RenderOptions>, ICloneable
{
	Action onDirty;

	internal RenderOptions( Action onDirty )
	{
		this.onDirty = onDirty;
	}

	/// <summary>Regular game rendering layers</summary>
	public bool Game
	{
		get;
		set { field = value; onDirty?.InvokeWithWarning(); }
	} = true;

	/// <summary>Drawn over the world with depth of its own, so it is never behind anything else - a weapon held up to the eye, which would otherwise go into the wall it is pushed against. Casts no shadow.</summary>
	public bool Overlay
	{
		get;
		set { field = value; onDirty?.InvokeWithWarning(); }
	}

	/// <summary>Drawn over the game's interface, where it is not also in <see cref="Game"/>.</summary>
	public bool AfterUI
	{
		get;
		set { field = value; onDirty?.InvokeWithWarning(); }
	}

	/// <summary>Apply these options to a SceneObject</summary>
	public void Apply( SceneObject obj )
	{
		Assert.IsValid( obj );

		obj.Flags.SetFlag( Rendering.SceneObjectFlags.ExcludeGameLayer, !Game && !Overlay );
		obj.Flags.SetFlag( Rendering.SceneObjectFlags.GameOverlayLayer, Overlay );
		obj.Flags.SetFlag( Rendering.SceneObjectFlags.UIOverlayLayer, AfterUI );
	}

	internal RenderOptions Clone()
	{
		RenderOptions cloned = new( null )
		{
			Game = Game,
			Overlay = Overlay,
			AfterUI = AfterUI
		};
		return cloned;
	}

	object ICloneable.Clone() => Clone();

	public override bool Equals( object obj ) => Equals( obj as RenderOptions );
	public virtual bool Equals( RenderOptions obj )
	{
		return Game == obj.Game && AfterUI == obj.AfterUI && Overlay == obj.Overlay;
	}

	public override int GetHashCode() => HashCode.Combine( Game, AfterUI, Overlay );

	JsonNode IJsonPopulator.Serialize()
	{
		var jso = new JsonObject();
		jso["GameLayer"] = Game;
		jso["OverlayLayer"] = Overlay;
		jso["AfterUILayer"] = AfterUI;

		return jso;
	}

	void IJsonPopulator.Deserialize( JsonNode node )
	{
		if ( node is not JsonObject jso )
			return;

		Game = jso["GameLayer"]?.GetValue<bool>() ?? true;
		Overlay = jso["OverlayLayer"]?.GetValue<bool>() ?? false;
		AfterUI = jso["AfterUILayer"]?.GetValue<bool>() ?? false;
	}
}
