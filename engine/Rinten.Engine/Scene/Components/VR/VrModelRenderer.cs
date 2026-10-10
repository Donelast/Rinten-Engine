namespace Rinten.VR;

/// <summary>Renders a device-specific model for a VR device</summary>
[Title( "VR Model Renderer" )]
[Category( "VR" )]
[Icon( "view_in_ar" )]
public class VRModelRenderer : Component
{
	/// <summary>Represents a controller to use when fetching the model (which device)</summary>
	public enum ModelSources
	{
		/// <summary>The left controller</summary>
		LeftHand,

		/// <summary>The right controller</summary>
		RightHand
	}

	private ModelSources _modelSource = ModelSources.LeftHand;

	/// <summary>Which device should we use to fetch the model?</summary>
	[Property]
	public ModelSources ModelSource
	{
		get => _modelSource;
		set
		{
			_modelSource = value;
			UpdateModel();
		}
	}

	/// <summary>Which model renderer should we use as the target? A skinned one also has its buttons, trigger, grip and stick pushed by what the hand is doing.</summary>
	[Property]
	public ModelRenderer ModelRenderer { get; set; }

	protected override void OnStart()
	{
		UpdateModel();
	}

	//The headset says what is in a hand a moment after the session starts, and again whenever it changes
	//- so the model is asked for every frame. It is built once per controller and handed back after that.
	protected override void OnUpdate()
	{
		UpdateModel();
	}

	private void UpdateModel()
	{
		if ( !Enabled || Scene.IsEditor || !Game.IsRunningInVR )
			return;

		if ( ModelRenderer == null )
			return;

		if ( IsProxy )
			return;

		var hand = (ModelSource == ModelSources.LeftHand) ? Input.VR.LeftHand : Input.VR.RightHand;
		var model = hand.GetModel() ?? Rinten.Model.Cube;

		if ( ModelRenderer.Model != model ) ModelRenderer.Model = model;

		if ( ModelRenderer is SkinnedModelRenderer skinned ) Press( skinned, hand );
	}

	//The controller's moving parts where the hand has them - see XrControllerModels.Morphs. A model
	//without them sets nothing. With no blend: a button follows the finger, not a moment after it.
	private static void Press( SkinnedModelRenderer renderer, VRController hand )
	{
		var morphs = renderer.Morphs;
		var stick = hand.Joystick.Value;

		morphs.Set( XrControllerModels.Morphs.Trigger, hand.Trigger.Value, 0 );
		morphs.Set( XrControllerModels.Morphs.Grip, hand.Grip.Value, 0 );
		morphs.Set( XrControllerModels.Morphs.StickPress, hand.JoystickPress.IsPressed ? 1 : 0, 0 );
		morphs.Set( XrControllerModels.Morphs.StickLeft, MathF.Max( 0, -stick.x ), 0 );
		morphs.Set( XrControllerModels.Morphs.StickRight, MathF.Max( 0, stick.x ), 0 );
		morphs.Set( XrControllerModels.Morphs.StickUp, MathF.Max( 0, stick.y ), 0 );
		morphs.Set( XrControllerModels.Morphs.StickDown, MathF.Max( 0, -stick.y ), 0 );
		morphs.Set( XrControllerModels.Morphs.ButtonA, hand.ButtonA.IsPressed ? 1 : 0, 0 );
		morphs.Set( XrControllerModels.Morphs.ButtonB, hand.ButtonB.IsPressed ? 1 : 0, 0 );
	}
}
