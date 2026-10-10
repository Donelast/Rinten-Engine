namespace Rinten;

/// <summary>When applied to an attribute, which is them applied to a member.. This will make <see cref="MemberDescription"/> set on the attribute upon load. This provides a convenient way to know which member the attribute was attached to.</summary>
public interface IMemberAttribute
{
	/// <summary>Description of the member this attribute was attached to.</summary>
	MemberDescription MemberDescription { get; set; }
}
