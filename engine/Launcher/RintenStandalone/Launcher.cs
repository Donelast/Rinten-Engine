using System;
using System.Linq;

namespace Rinten;

public static class Launcher
{
	public static int Main()
	{
		//Our own engine and nothing else: our window, our renderer, our frame.
		var app = new CoreAppSystem();

		app.Run();

		//Runs that are a build step rather than a game - baking the shaders is one - answer with whether
		//they worked, because whoever started them is a script rather than a person watching a window.
		return app.ExitCode;
	}
}
