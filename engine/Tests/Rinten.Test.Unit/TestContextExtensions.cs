using System;
using System.Collections.Generic;
using System.IO;
using OxyPlot;

#nullable enable

public static class TestContextExtensions
{
	//Add an .svg image of the given plots as a test result attachment, created in TestContext.TestResultsDirectory.
	public static void AddResultPlot( this TestContext context, string name, IReadOnlyList<IPlotModel> plotModels, int width = 600, int height = 300, int? maxCols = null )
	{
		if ( string.IsNullOrEmpty( context.TestResultsDirectory ) || plotModels.Count == 0 )
		{
			return;
		}

		var fileName = Path.Combine( context.TestResultsDirectory, name );

		if ( !Directory.Exists( Path.GetDirectoryName( fileName ) ) )
		{
			Directory.CreateDirectory( Path.GetDirectoryName( fileName )! );
		}

		var cols = maxCols ?? plotModels.Count;
		var rows = (plotModels.Count + cols - 1) / cols;

		using ( var stream = File.Create( fileName ) )
		{
			var background = plotModels[0].Background;
			var textMeasurer = new PdfRenderContext( width * cols, height * rows, background );

			using ( var rc = new SvgRenderContext( stream, width * cols, height * rows, true, textMeasurer, background ) )
			{
				for ( var i = 0; i < plotModels.Count; ++i )
				{
					var plotModel = plotModels[i];

					var row = i / cols;
					var col = i % cols;

					plotModel.Update( true );
					plotModel.Render( rc, new OxyRect( width * col, height * row, width, height ) );
				}

				rc.Complete();
				rc.Flush();
			}
		}

		context.AddResultFile( fileName );
	}
}
