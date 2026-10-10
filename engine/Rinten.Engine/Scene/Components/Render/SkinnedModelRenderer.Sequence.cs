using System;
using System.Text.Json.Nodes;

namespace Rinten;

partial class SkinnedModelRenderer
{
	SequenceAccessor _sequence;

	public bool ShouldShowSequenceEditor
	{
		get
		{
			if ( Model.IsValid() == false ) return false;
			if ( Model.AnimationCount <= 0 ) return false;

			return true;
		}
	}

	public sealed class SequenceAccessor : IJsonPopulator
	{
		private readonly SkinnedModelRenderer _renderer;
		private string _name;
		private bool _looping = true;
		private bool _blending = false;

		[Hide]
		private AnimationSequence CurrentSequence
		{
			get
			{
				if ( !_renderer.IsValid() )
					return null;

				if ( !_renderer.SceneModel.IsValid() )
					return null;

				return _renderer.SceneModel.CurrentSequence;
			}
		}

		[Hide] public float Duration => CurrentSequence.Duration;

		[Hide] public bool IsFinished => CurrentSequence.IsFinished;

		[Hide] public float TimeNormalized { get => CurrentSequence.TimeNormalized; set => CurrentSequence.TimeNormalized = value; }

		[Hide] public float Time { get => CurrentSequence.Time; set => CurrentSequence.Time = value; }

		[Hide] public IReadOnlyList<string> SequenceNames
		{
			get
			{
				if ( _renderer.Model.IsValid() && _renderer.Model.AnimationCount > 0 )
					return _renderer.Model.AnimationNames;

				return CurrentSequence?.SequenceNames ?? Array.Empty<string>();
			}
		}

		[Title( "Sequence" ), SequenceName]
		public string Name
		{
			get => _name;
			set
			{
				_name = value;

				if ( !string.IsNullOrWhiteSpace( _name ) )
					_renderer.UseAnimGraph = false;

				if ( CurrentSequence is not null )
					CurrentSequence.Name = _name;
			}
		}

		public bool Looping
		{
			get => _looping;
			set
			{
				_looping = value;

				if ( CurrentSequence is not null )
					CurrentSequence.Looping = _looping;
			}
		}

		public bool Blending
		{
			get => _blending;
			set
			{
				_blending = value;

				if ( CurrentSequence is not null )
					CurrentSequence.Blending = _blending;
			}
		}

		/// <summary>Control playback rate of sequence.</summary>
		[Hide]
		public float PlaybackRate
		{
			get => _renderer.PlaybackRate;
			set => _renderer.PlaybackRate = value;
		}

		internal SequenceAccessor( SkinnedModelRenderer renderer )
		{
			_renderer = renderer;
		}

		internal void Apply()
		{
			var currentSequence = CurrentSequence;
			if ( currentSequence is null )
				return;

			currentSequence.Name = Name;
			currentSequence.Looping = Looping;
			currentSequence.Blending = Blending;
		}

		JsonNode IJsonPopulator.Serialize()
		{
			return new JsonObject
			{
				["Name"] = Name,
				["Looping"] = Looping,
				["Blending"] = Blending,
			};
		}

		void IJsonPopulator.Deserialize( JsonNode e )
		{
			if ( e is not JsonObject jso )
				return;

			Name = jso["Name"]?.GetValue<string>() ?? default;
			Looping = jso["Looping"]?.GetValue<bool>() ?? true;
			Blending = jso["Blending"]?.GetValue<bool>() ?? false;
		}
	}
}
