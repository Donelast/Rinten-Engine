namespace Editor.Inspectors;

using Rinten.UI;

//How often a clutter entry is picked, relative to the others.
[StyleSheet( "ClutterEntriesGridControl.cs.scss" )]
sealed class ClutterWeightPopup : Popup
{
	public static ClutterWeightPopup Open( Panel source, float weight, Action<float> changed )
	{
		CloseAll();

		var popup = new ClutterWeightPopup();
		popup.AddClass( "clutter-weight-popup" );
		popup.SetPositioning( source, PositionMode.BelowLeft, 2 );
		popup.CloseWhenParentIsHidden = true;

		var slider = popup.AddChild( new SliderControl( 0.01f, 1f, 0.01f ) );
		slider.ShowTextEntry = true;
		slider.Value = weight;
		slider.OnValueChanged = changed;

		return popup;
	}
}
