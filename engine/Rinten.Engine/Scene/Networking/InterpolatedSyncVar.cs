using Rinten.Interpolation;

namespace Rinten;

internal interface IInterpolatedSyncVar
{
	public static readonly DelegateInterpolator<float> FloatInterpolator = new( ( a, b, delta ) => a.LerpTo( b, delta ) );

	//Create a new interpolator for the type of the provided value.
	public static IInterpolator<T> Create<T>( T value )
	{
		var i = value switch
		{
			IInterpolator<T> interpolator => interpolator,
			float => (IInterpolator<T>)FloatInterpolator,
			_ => null
		};

		return i;
	}

	//Query the interpolated value at the provided time.
	public object Query( double time );
}

//Contains a target value and the current interpolated value for the property it represents.
internal class InterpolatedSyncVar<T>( IInterpolator<T> interpolator ) : IInterpolatedSyncVar
{
	object IInterpolatedSyncVar.Query( double time ) => Query( time );

	private readonly InterpolationBuffer<T> _buffer = new( interpolator );

	//Query the value at the specified time.
	private T Query( double time )
	{
		return _buffer.Query( time - Networking.InterpolationTime );
	}

	/// <summary>Update the value with the latest value from the network.</summary>
	public void Update( T value )
	{
		_buffer.Add( value, Time.NowDouble );
		_buffer.CullOlderThan( Time.NowDouble - (Networking.InterpolationTime * 3f) );
	}
}
