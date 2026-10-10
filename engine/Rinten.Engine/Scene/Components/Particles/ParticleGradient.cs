using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rinten;

[Expose]
[JsonConverter( typeof( ParticleGradientConverter ) )]
public struct ParticleGradient
{
	public ParticleGradient()
	{
	}

	public ValueType Type { readonly get; set; }
	public EvaluationType Evaluation { readonly get; set; }

	//Shared instance; Color.White here would allocate a Gradient per construction
	static readonly Gradient WhiteGradient = Color.White;

	public Gradient GradientA { readonly get; set; } = WhiteGradient;
	public Gradient GradientB { readonly get; set; } = WhiteGradient;
	public Color ConstantA { readonly get; set; } = Color.White;
	public Color ConstantB { readonly get; set; } = Color.White;

	public Color ConstantValue
	{
		readonly get => ConstantA;
		set => ConstantA = value;
	}


	public static implicit operator ParticleGradient( Color color )
	{
		return new ParticleGradient { Type = ValueType.Constant, Evaluation = EvaluationType.Life, ConstantValue = color };
	}

	[Expose]
	public enum ValueType
	{
		Constant,
		Range,
		Gradient
	}

	[Expose]
	public enum EvaluationType
	{
		Life,
		Frame,
		Particle,

		/// <summary>By where the particle is among its emitter's places, nought to one. See Particle.IndexDelta.</summary>
		Index,
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public readonly Color Evaluate( in float delta, in float randomFixed )
	{
		var d = Evaluation switch
		{
			EvaluationType.Life => delta,
			EvaluationType.Frame => Random.Shared.Float( 0, 1 ),
			EvaluationType.Particle => randomFixed,
			_ => delta,
		};

		switch ( Type )
		{
			case ValueType.Constant:
				{
					return ConstantValue;
				}

			case ValueType.Range:
				{
					return Color.Lerp( ConstantA, ConstantB, d );
				}

			case ValueType.Gradient:
				{
					return GradientA.Evaluate( d );
				}
		}

		return ConstantValue;
	}

	/// <summary>Whether this colour is the same colour whatever is asked of it - see ParticleFloat.IsPlainConstant.</summary>
	[JsonIgnore]
	public readonly bool IsPlainConstant
	{
		[MethodImpl( MethodImplOptions.AggressiveInlining )]
		get => Type == ValueType.Constant && Evaluation != EvaluationType.Frame;
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public readonly Color Evaluate( Particle p, int seed, [CallerLineNumber] int line = 0 )
	{
		if ( IsPlainConstant ) return ConstantValue;

		return Evaluate( Evaluation == EvaluationType.Index ? p.IndexDelta : p.LifeDelta, p.Rand( seed, line ) );
	}
}

//JsonConverter for ParticleGradient that omits unused properties. Will serialize as just a Color string for Constant.
file sealed class ParticleGradientConverter : JsonConverter<ParticleGradient>
{
	private struct Model
	{
		public ParticleGradient.ValueType Type { get; init; }
		public ParticleGradient.EvaluationType Evaluation { get; init; }

		[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
		public Color? ConstantA { get; set; }
		[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
		public Color? ConstantB { get; set; }

		[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
		public Gradient? GradientA { get; set; }
		[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
		public Gradient? GradientB { get; set; }
	}

	public override ParticleGradient Read( ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options )
	{
		//If a string, read as a Constant

		if ( reader.TokenType == JsonTokenType.String )
		{
			return JsonSerializer.Deserialize<Color>( ref reader, options );
		}

		var model = JsonSerializer.Deserialize<Model>( ref reader, options );

		return new ParticleGradient
		{
			Type = model.Type,
			Evaluation = model.Evaluation,
			ConstantA = model.ConstantA ?? Color.White,
			ConstantB = model.ConstantB ?? Color.White,
			GradientA = model.GradientA ?? Color.White,
			GradientB = model.GradientB ?? Color.White
		};
	}

	public override void Write( Utf8JsonWriter writer, ParticleGradient value, JsonSerializerOptions options )
	{
		//If just a Constant, write as a color string

		if ( value.Type == ParticleGradient.ValueType.Constant )
		{
			JsonSerializer.Serialize( writer, value.ConstantValue, options );
			return;
		}

		var model = new Model { Type = value.Type, Evaluation = value.Evaluation };

		switch ( value.Type )
		{
			case ParticleGradient.ValueType.Range:
				model.ConstantA = value.ConstantA;
				model.ConstantB = value.ConstantB;
				break;

			case ParticleGradient.ValueType.Gradient:
				model.GradientA = value.GradientA;
				model.GradientB = value.GradientB;
				break;

			default:
				throw new NotImplementedException( $"Serializing {value.Type} not implemented yet." );
		}

		JsonSerializer.Serialize( writer, model, options );
	}
}
