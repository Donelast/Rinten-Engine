using Refit;

namespace Rinten.Services;

public partial class ServiceApi
{
	public interface IUtilityApi
	{
		[Get( "/utility/translate" )]
		Task<string> Translate( string language, string input );

		[Get( "/utility/avatars" )]
		Task<string[]> RandomAvatars();
	}
}
