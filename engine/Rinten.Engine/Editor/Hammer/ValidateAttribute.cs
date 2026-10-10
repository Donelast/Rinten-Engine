using Rinten;
using Rinten.Internal;

/// <summary>Validates a property using a method.</summary>
[AttributeUsage( AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = true )]
public class ValidateAttribute : Attribute
{
	protected string _methodName;
	protected Type _methodOwnerType;
	protected LogLevel _status;
	protected string _message;

	/// <summary>Specifies a method in the same class to use for validation.</summary>
	public ValidateAttribute( string condition, string message, LogLevel status )
	{
		_methodName = condition;
		_status = status;
		_message = message;
	}

	/// <summary>Specifies a static method in another class to use for validation.</summary>
	public ValidateAttribute( Type type, string condition, string message, LogLevel status )
	{
		_methodName = condition;
		_methodOwnerType = type;
		_status = status;
		_message = message;
	}

	public record struct Result( LogLevel Status, string Message = "", bool Success = false );

	/// <summary>Validates a property value using the specified method.</summary>
	public Result Validate( object targetObject, TypeDescription td, object propertyValue )
	{
		if ( _methodOwnerType != null )
		{
			td = GlobalGameNamespace.TypeLibrary.GetType( _methodOwnerType );
		}

		var methodDesc = td.GetMethod( _methodName );
		if ( methodDesc == null )
		{
			return new Result( LogLevel.Error, $"Validation method '{_methodName}' not found on {td.Name}" );
		}

		try
		{
			bool validationPassed = methodDesc.InvokeWithReturn<bool>( _methodOwnerType != null ? null : targetObject, [propertyValue] );

			if ( validationPassed )
				return new Result( LogLevel.Info, Success: true );
			else
				return new Result( _status, _message );
		}
		catch ( Exception ex )
		{
			return new Result( LogLevel.Error, $"Error executing validation method '{_methodName}': {ex.Message}" );
		}
	}
}
