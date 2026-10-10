namespace Rinten;

public partial class BaseCombatWeapon
{
	//NPC usage data: ranges, bursts, spread. The engine stores it, game AI reads it

	/// <summary>How an NPC fights with a weapon - engagement band, burst cadence and accuracy.</summary>
	public record struct NpcUsage()
	{
		/// <summary>Closer than this and the NPC shouldn't attack - back off first.</summary>
		public float MinRange { get; set; } = 0f;

		/// <summary>Furthest distance the NPC will attack from.</summary>
		public float MaxRange { get; set; } = 1400f;

		/// <summary>Fewest shots in a burst.</summary>
		public int BurstMin { get; set; } = 2;

		/// <summary>Most shots in a burst.</summary>
		public int BurstMax { get; set; } = 5;

		/// <summary>Shortest rest between bursts, seconds.</summary>
		public float RestMin { get; set; } = 0.3f;

		/// <summary>Longest rest between bursts, seconds.</summary>
		public float RestMax { get; set; } = 0.6f;

		/// <summary>Spread multiplier when an NPC fires this - how forgiving the weapon is in untrained hands. The NPC multiplies in its own skill and applies the result via <see cref="SpreadScale"/>.</summary>
		public float SpreadScale { get; set; } = 3f;

		/// <summary>Can NPCs pick this up off the ground to arm themselves?</summary>
		public bool CanBePickedUp { get; set; } = true;
	}

	/// <summary>Can NPCs fight with this weapon at all?</summary>
	[Property, FeatureEnabled( "NPC" )] public bool UsableByNpcs { get; set; } = true;

	/// <summary>How an NPC fights with this weapon.</summary>
	[Property, Feature( "NPC" )] public NpcUsage Npc { get; set; } = new();
}
