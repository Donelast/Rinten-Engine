namespace Rinten;

/// <summary>Base interface for components that can be grouped for sprite rendering. Contains the fields needed for render group classification.</summary>
public interface ISpriteRenderGroup
{
	bool Opaque { get; }
	bool Additive { get; }
	bool Shadows { get; }
	bool IsSorted { get; }

	/// <summary>Material for its sprites, or null for the default; batches are grouped by material (one draw each)</summary>
	Material MaterialOverride => null;

	/// <summary>Where its sprites are drawn among the others - lower first. Part of what a batch is grouped by, because a batch is one draw and the order is between draws: every sprite of one order is one batch, drawn after every lower one. Nought by default. See SpriteRenderer.SortOrder.</summary>
	int SortOrder => 0;
}
