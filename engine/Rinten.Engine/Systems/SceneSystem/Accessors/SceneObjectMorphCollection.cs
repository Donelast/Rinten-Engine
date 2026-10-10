using System.Collections.Generic;
using Rinten;

//Hidden class, addon code should only access MorphCollection. Holds only the set morph weights
internal sealed class SceneObjectMorphCollection( SceneModel sceneObject ) : MorphCollection
{
	const float FadeTime = 0.05f;

	private readonly Dictionary<int, float> weights = new();

	//Whether anything has set a weight at all - what decides whether the model is posed by its shapes this frame.
	internal bool Any => weights.Count > 0;

	public override void ResetAll()
	{
		ResetAll( FadeTime );
	}

	public override void ResetAll( float fadeTime )
	{
		weights.Clear();
	}

	public override void Reset( int i )
	{
		Reset( i, FadeTime );
	}

	public override void Reset( string name )
	{
		Reset( name, FadeTime );
	}

	public override void Reset( int i, float fadeTime )
	{
		weights.Remove( i );
	}

	public override void Reset( string name, float fadeTime )
	{
		var index = IndexOf( name );

		if ( index >= 0 ) weights.Remove( index );
	}

	public override void Set( int i, float weight )
	{
		Set( i, weight, FadeTime );
	}

	public override void Set( string name, float weight )
	{
		Set( name, weight, FadeTime );
	}

	public override void Set( int i, float weight, float fadeTime )
	{
		if ( i < 0 ) return;

		weights[i] = MathF.Max( 0.0f, weight );
	}

	public override void Set( string name, float weight, float fadeTime )
	{
		Set( IndexOf( name ), weight, fadeTime );
	}

	public override float Get( int i )
	{
		return weights.TryGetValue( i, out var weight ) ? weight : 0.0f;
	}

	public override float Get( string name )
	{
		return Get( IndexOf( name ) );
	}

	public override string GetName( int index )
	{
		return sceneObject.Model.GetMorphName( index );
	}

	public override int Count => sceneObject.Model.MorphCount;

	internal override Model Model => sceneObject.Model;

	//Which morph a name is, or -1.
	private int IndexOf( string name )
	{
		var model = sceneObject.Model;
		if ( !model.IsValid() || string.IsNullOrEmpty( name ) ) return -1;

		return model.Morphs.GetIndex( name );
	}

	//Morph weights in morph order for the renderer, or null when none are set
	internal float[] Weights()
	{
		if ( weights.Count == 0 ) return null;

		var model = sceneObject.Model;
		if ( !model.IsValid() ) return null;

		var count = model.MorphCount;
		if ( count <= 0 ) return null;

		var all = new float[count];

		foreach ( var (morph, weight) in weights )
		{
			if ( morph >= 0 && morph < count ) all[morph] = weight;
		}

		return all;
	}

	//The same as Weights, written into a run the caller keeps - every shape not set is nought. False, with nothing written, where none are
	//set. For the pose pass, which asks every frame for every model it poses by its shapes and has nowhere for a new array a frame to go.
	internal bool WeightsInto( Span<float> into )
	{
		if ( weights.Count == 0 || into.IsEmpty ) return false;

		into.Clear();

		foreach ( var (morph, weight) in weights )
		{
			if ( morph >= 0 && morph < into.Length ) into[morph] = weight;
		}

		return true;
	}
}
