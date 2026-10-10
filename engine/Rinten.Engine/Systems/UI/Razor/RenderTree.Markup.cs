using Rinten.Html;

namespace Rinten.UI;

/// <summary>This is a tree renderer for panels. If we ever use razor on other ui we'll want to make a copy of this class and do the specific things to that.</summary>
public partial class PanelRenderTreeBuilder : Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder
{
	/// <summary>Add markup to the current element</summary>
	public override void AddMarkupContent( int sequence, string markupContent )
	{
		//Quick ignore for empty strings
		if ( string.IsNullOrWhiteSpace( markupContent ) )
			return;

		var parent = CurrentScope.Element ?? Parent;

		var scope = CurrentScope;
		scope.Sequence = sequence;
		scope.Loop = CurrentBlock.Increment( sequence );
		scope.Hash = HashCode.Combine( scope.Loop, scope.Sequence );

		var block = GetBlock( scope.Hash );

		//already created - these are static so won't ever need changing but make sure they haven't been deleted and make sure their child order is correct
		if ( block.MarkupPanels != null && block.MarkupPanels.All( x => x.IsValid ) )
		{
			foreach ( var panel in block.MarkupPanels )
			{
				Assert.True( panel.Parent == parent );
				parent.SetChildIndex( panel, CurrentScope.ChildIndex );
				CurrentScope.ChildIndex++;
			}
			return;
		}

		//Make sure we don't reach this point every again
		block.MarkupPanels = new List<Panel>();

		//Don't create an element if it's just newlines and whitespace
		if ( markupContent.All( x => char.IsWhiteSpace( x ) || x == '\n' || x == '\r' ) )
			return;

		FlushContent();

		var root = Rinten.Html.Node.Parse( markupContent );

		if ( root.NodeType == Rinten.Html.NodeType.Document )
		{
			//Only the first tag of the run is where the compiler said the run is; the rest were squeezed
			//together with their whitespace gone, and their positions are of that text, not the file's.
			var first = true;

			foreach ( var e in root.ChildNodes )
			{
				var panel = CreateNodeMarkup( e, parent, first );
				first = false;

				if ( panel != null )
				{
					block.MarkupPanels.Add( panel );

					parent.SetChildIndex( panel, CurrentScope.ChildIndex );

					CurrentScope.ChildIndex++;
				}
			}
		}

		return;
	}

	//Source location of a markup tag; only the run's first tag is exact, others get column -1
	void PlaceInSource( Panel panel, bool exactly )
	{
		panel.SourceFile = sourceFile;
		panel.SourceLine = sourceLine;
		panel.SourceColumn = exactly ? sourceColumn : -1;
	}

	Panel CreateNodeMarkup( Rinten.Html.Node node, Panel parent, bool exactly = false )
	{
		if ( node.NodeType == Rinten.Html.NodeType.Element )
		{
			//Built-in tags first, then the game's type library, which may not exist (launcher, editor shell)
			var panel = Block.Builtin( node.Name )
				?? Game.TypeLibrary?.Create<Panel>( node.Name, false )
				?? new Panel();

			panel.ElementName = node.Name;
			panel.Parent = parent;
			PlaceInSource( panel, exactly );

			string slot = null;

			foreach ( var attr in node.Attributes )
			{
				if ( attr.Name == "slot" )
				{
					slot = attr.Value;
					continue;
				}

				panel.SetProperty( attr.Name, attr.Value );
			}

			foreach ( var e in node.ChildNodes )
			{
				CreateNodeMarkup( e, panel );
			}

			if ( slot != null )
			{
				panel.Parent?.OnTemplateSlot( node, slot, panel );
			}

			return panel;
		}

		if ( node is TextNode textNode )
		{
			//Don't bother with empty content
			var content = textNode.InnerHtml;
			if ( string.IsNullOrWhiteSpace( content ) )
				return null;

			if ( parent is Label )
			{
				parent.SetContent( content );
				return null;
			}
			else
			{
				//Markup text is always a Label; a plain panel can't hold text
				var panel = new Label();

				panel.ElementName = "label";
				panel.Parent = parent;
				panel.SetContent( content );
				PlaceInSource( panel, exactly: false );

				return panel;
			}
		}

		return null;

	}
}
