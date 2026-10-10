using Rinten.VR;

namespace Editor;

public static partial class EditorUtility
{
	public class VR
	{
		public static bool Enabled
		{
			get => VRSystem.IsActive;
			set
			{
				if ( value == VRSystem.IsActive )
					return;

				if ( value )
				{
					if ( VRSystem.ProbeHeadset() ) RestartEditor( enterVr: true );
				}
				else
					VRSystem.Disable();
			}
		}

		public static bool HasHeadset => VRSystem.HasHeadset;
	}
}
