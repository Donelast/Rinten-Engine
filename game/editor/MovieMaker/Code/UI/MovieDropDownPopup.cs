using Rinten.UI;

namespace Editor.MovieMaker;

#nullable enable

//A class of its own because a popup is re-parented onto the window's overlay when it opens, so it is no longer under the panel whose stylesheet would have dressed it. The sheet has to travel with the popup itself.
/// <summary>The open list of an <see cref="IconDropDown{T}"/>.</summary>
[StyleSheet( "MovieDropDownPopup.cs.scss" )]
public sealed class MovieDropDownPopup : Popup
{
	/// <summary>A line between two runs of options.</summary>
	public void AddSeparator() => AddChild<Panel>( "separator" );
}
