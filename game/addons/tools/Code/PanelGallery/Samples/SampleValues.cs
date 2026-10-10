namespace Editor.Gallery;

//In order: what the control declared for itself, the table of engine and asset types, plain numbers and text, and finally a bare constructor. Adding a way to make samples is adding a provider here; the pages never learn about it.
/// <summary>The chain the gallery uses to find a value for a type.</summary>
public static class SampleValues
{
	public static ISampleValueProvider CreateDefault()
		=> new SampleValueChain( new AttributeSampleProvider(), CreateTable(), new PrimitiveSampleProvider(), new ConstructorSampleProvider() );

	//Engine maths, time, and the assets every project ships with.
	static TableSampleProvider CreateTable()
	{
		return new TableSampleProvider()
			.Add( () => new Color( 0.25f, 0.5f, 0.9f, 1f ) )
			.Add( () => new Vector2( 3, 4 ) )
			.Add( () => new Vector3( 1, 2, 3 ) )
			.Add( () => new Vector4( 1, 2, 3, 4 ) )
			.Add( () => Rotation.From( 30, 45, 0 ) )
			.Add( () => new Angles( 30, 45, 0 ) )
			.Add( () => new Transform( new Vector3( 100, 50, 10 ), Rotation.From( 0, 90, 0 ), 1.5f ) )
			.Add( () => new Margin( 10, 10, 10, 20 ) )
			.Add( () => new Rect( 20, 20, 200, 100 ) )
			.Add( () => BBox.FromPositionAndSize( Vector3.Zero, 64 ) )
			.Add( () => new Sphere( Vector3.Zero, 32 ) )
			.Add( () => new RangedFloat( 2, 8 ) )
			.Add( () => new Curve( new Curve.Frame( 0.0f, 0.5f ), new Curve.Frame( 0.5f, 1.0f ), new Curve.Frame( 1.0f, 0.8f ) ) )
			.Add( () => new Gradient( new Gradient.ColorFrame( 0.0f, Color.Cyan ), new Gradient.ColorFrame( 0.4f, Color.Red ), new Gradient.ColorFrame( 1.0f, Color.Yellow ) ) )
			.Add( () => new DateTime( 2006, 11, 29, 14, 30, 0 ) )
			.Add( () => TimeSpan.FromMinutes( 90 ) )
			.Add( () => Guid.NewGuid() )
			.Add( () => typeof( Vector3 ) )
			.Add( () => Model.Load( "models/citizen/citizen.vmdl" ) )
			.Add( () => Material.Load( "materials/skybox/skybox_workshop.vmat" ) )
			.Add( () => Texture.White );
	}
}
