namespace Rinten.Mounting;

public interface IMountEvents
{
	void OnMountEnabled( BaseGameMount source ) { }
	void OnMountDisabled( BaseGameMount source ) { }
}
