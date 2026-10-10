using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UnitTests.Core;

//Body group names survive the .mdl, and the same file always yields the same 64-bit mask bits
[TestClass]
public class ModelDefinitionBodyGroupTests
{
	//The same options the compiler reads a definition with.
	static readonly JsonSerializerOptions Options = new()
	{
		PropertyNameCaseInsensitive = true,
		ReadCommentHandling = JsonCommentHandling.Skip,
		AllowTrailingCommas = true,
		IncludeFields = true,
		Converters = { new JsonStringEnumConverter() },
	};

	static ModelDefinition Read( string json )
	{
		var definition = JsonSerializer.Deserialize<ModelDefinition>( json, Options );
		Assert.IsNotNull( definition );
		return definition;
	}

	//A character with a head that comes off, which is the whole idea.
	const string Character = """
	{
		"BodyGroups": [
			{
				"Name": "Head",
				"Choices": [
					{ "Name": "head_01", "Meshes": [ "head_01", "eyes_01" ] },
					{ "Name": "head_02", "Meshes": [ "head_02", "eyes_02" ] },
					{ "Name": "none", "Meshes": [] }
				]
			},
			{
				"Name": "Chest",
				"Choices": [
					{ "Name": "shirt", "Meshes": [ "shirt" ] },
					{ "Name": "none", "Meshes": [] }
				]
			}
		]
	}
	""";

	//-- what the file says ----------------------------------------------------

	[TestMethod]
	public void EveryNameSurvivesTheFile()
	{
		var groups = Read( Character ).BodyGroups;

		Assert.AreEqual( 2, groups.Count );
		Assert.AreEqual( "Head", groups[0].Name );
		Assert.IsTrue( groups[0].Enabled, "a group nobody switched off is on" );

		Assert.AreEqual( 3, groups[0].Choices.Count );
		Assert.AreEqual( "head_02", groups[0].Choices[1].Name );

		CollectionAssert.AreEqual( new[] { "head_02", "eyes_02" }, groups[0].Choices[1].Meshes.ToArray(),
			"named as the export names them, which is what the compiler looks for" );

		Assert.AreEqual( 0, groups[0].Choices[2].Meshes.Count, "the choice that takes the head off names nothing" );
	}

	//A .mdl written before any of this existed has no groups in it, and that is a model rather than a broken file.
	[TestMethod]
	public void AFileWithNoGroupsIsAModelWithNoGroups()
	{
		var definition = Read( """{ "Mesh": "models/crate.fbx" }""" );

		Assert.IsNotNull( definition.BodyGroups );
		Assert.AreEqual( 0, definition.BodyGroups.Count );

		var layout = definition.BodyGroupBits();

		Assert.IsFalse( layout.Any );
		Assert.AreEqual( 0, layout.Bits );
		Assert.AreEqual( 0ul, layout.DefaultMask );

		//And nothing said about it stays nothing said: a model with no groups has no default to fall
		//back on, and every one of its meshes is unclaimed, so the whole mask is meaningless either way.
		Assert.AreEqual( BodyMask.Unset, layout.Resolve( BodyMask.Unset ) );
	}

	//-- the bits --------------------------------------------------------------

	[TestMethod]
	public void EveryChoiceGetsABitInFileOrder()
	{
		var layout = Read( Character ).BodyGroupBits();

		Assert.AreEqual( 2, layout.Parts.Count );
		Assert.AreEqual( 5, layout.Bits, "three heads and two chests" );
		Assert.AreEqual( 0, layout.Dropped );

		var bit = 0;

		foreach ( var part in layout.Parts )
		{
			foreach ( var choice in part.Choices )
				Assert.AreEqual( 1ul << bit++, choice.Mask, $"{part.Name}/{choice.Name}" );
		}

		Assert.AreEqual( 0b00111ul, layout.Get( "Head" ).Mask, "a group owns every bit of every choice" );
		Assert.AreEqual( 0b11000ul, layout.Get( "Chest" ).Mask );

		//The first of each, which is what an artist means by putting it first and what the compiled model works out when nobody writes one down.
		Assert.AreEqual( 0b01001ul, layout.DefaultMask );
	}

	//A group switched off is not written at all, so it spends no bits - and every group after it moves up,
	//which is exactly why doing that to a model already placed in scenes changes what those scenes show.
	[TestMethod]
	public void ASwitchedOffGroupSpendsNothing()
	{
		var definition = Read( Character );
		definition.BodyGroups[0].Enabled = false;

		var layout = definition.BodyGroupBits();

		Assert.AreEqual( 1, layout.Parts.Count );
		Assert.IsNull( layout.Get( "Head" ) );
		Assert.AreEqual( 2, layout.Bits );
		Assert.AreEqual( 1ul, layout.Get( "Chest" ).Choices[0].Mask, "the chest took the head's first bit" );
	}

	//A group with no choices in it is not a group, and is not written.
	[TestMethod]
	public void AnEmptyGroupIsNotWritten()
	{
		var layout = Read( """
		{
			"BodyGroups": [
				{ "Name": "Nothing", "Choices": [] },
				{ "Name": "Hat", "Choices": [ { "Name": "hat", "Meshes": [ "hat" ] }, { "Name": "none" } ] }
			]
		}
		""" ).BodyGroupBits();

		Assert.AreEqual( 1, layout.Parts.Count );
		Assert.AreEqual( "Hat", layout.Parts[0].Name );
		Assert.AreEqual( 1ul, layout.Parts[0].Choices[0].Mask );
	}

	//Sixty-four is the whole budget for the model, not for a group: the mask is a ulong and a choice is a bit of it. Past that the
	//choices get no bit and nothing draws them - and, more to the point, nothing wraps round to claim a bit another choice already owns.
	[TestMethod]
	public void PastSixtyFourChoicesTheRestGetNoBit()
	{
		var definition = new ModelDefinition();

		for ( var g = 0; g < 10; g++ )
		{
			var group = new ModelDefinition.BodyGroup { Name = $"group_{g}" };

			for ( var c = 0; c < 7; c++ )
				group.Choices.Add( new ModelDefinition.BodyChoice { Name = $"choice_{c}" } );

			definition.BodyGroups.Add( group );
		}

		var layout = definition.BodyGroupBits();

		Assert.AreEqual( ModelDefinition.BodyGroupLayout.MaxChoices, layout.Bits );
		Assert.AreEqual( 70 - ModelDefinition.BodyGroupLayout.MaxChoices, layout.Dropped );
		Assert.AreEqual( 0, layout.Spare );

		var seen = new HashSet<ulong>();

		foreach ( var part in layout.Parts )
		{
			foreach ( var choice in part.Choices )
				Assert.IsTrue( seen.Add( choice.Mask ), "no two choices share a bit" );
		}

		Assert.AreEqual( ModelDefinition.BodyGroupLayout.MaxChoices, seen.Count );
	}

	//-- taking a part off -----------------------------------------------------

	//A choice that turns no mesh on is how a part comes off, and everything that takes one off picks the last
	//choice of the group without looking at it - see ClothingContainer.GetBodyGroups . So where it sits matters.
	[TestMethod]
	public void TheChoiceThatHidesThePartIsFoundAndPlaced()
	{
		var layout = Read( Character ).BodyGroupBits();
		var head = layout.Get( "Head" );

		Assert.IsFalse( head.Choices[0].Hides );
		Assert.IsTrue( head.Choices[2].Hides );
		Assert.AreEqual( 2, head.Hidden );
		Assert.IsTrue( head.HidesLast, "so anything taking the head off picks it" );

		var definition = Read( Character );

		//The same group with the empty choice put first: still hides, still found, but no longer where anything undressing a character looks.
		definition.BodyGroups[0].Choices.Reverse();

		var moved = definition.BodyGroupBits().Get( "Head" );

		Assert.AreEqual( 0, moved.Hidden );
		Assert.IsFalse( moved.HidesLast );
	}

	//A choice naming only blanks names nothing at all.
	[TestMethod]
	public void ABlankNameIsNotAMesh()
	{
		var layout = Read( """
		{
			"BodyGroups": [
				{ "Name": "Hat", "Choices": [ { "Name": "hat", "Meshes": [ "hat" ] }, { "Name": "none", "Meshes": [ "", "  " ] } ] }
			]
		}
		""" ).BodyGroupBits();

		Assert.IsTrue( layout.Get( "Hat" ).Choices[1].Hides );
		Assert.AreEqual( 0, layout.Get( "Hat" ).Choices[1].Meshes.Count );
	}

	//Which choices claim a mesh, for showing an artist what a mesh belongs to.
	[TestMethod]
	public void AMeshKnowsWhichChoicesDrawIt()
	{
		var layout = Read( Character ).BodyGroupBits();

		Assert.AreEqual( layout.Get( "Head" ).Choices[0].Mask, layout.ClaimOf( "eyes_01" ) );
		Assert.AreEqual( layout.Get( "Head" ).Choices[0].Mask, layout.ClaimOf( "EYES_01" ), "matched however it is typed" );

		//Nothing claims the body, so the body is always drawn - which is nearly every mesh of nearly every model.
		Assert.AreEqual( 0ul, layout.ClaimOf( "body" ) );
	}

	//-- resolving a mask ------------------------------------------------------

	//Nobody having said anything is not a mask, and becomes what the model shows by default rather than every choice of every group at once.
	[TestMethod]
	public void NothingSaidMeansWhatTheModelShows()
	{
		var layout = Read( Character ).BodyGroupBits();

		Assert.AreEqual( layout.DefaultMask, layout.Resolve( BodyMask.Unset ) );
		Assert.AreEqual( 0, layout.ChoiceOf( layout.Resolve( BodyMask.Unset ), "Head" ) );
		Assert.AreEqual( 0, layout.ChoiceOf( layout.Resolve( BodyMask.Unset ), "Chest" ) );

		//And nought stands: something deliberately showing nothing is not the same as something that has not decided.
		Assert.AreEqual( 0ul, layout.Resolve( 0 ) );
	}

	//A mask names the groups somebody changed and leaves the rest out, so the groups it says nothing about
	//get their default - read strictly, a character stored as "head off" is a character with no chest either.
	[TestMethod]
	public void AGroupNobodyMentionedKeepsItsDefault()
	{
		var layout = Read( Character ).BodyGroupBits();
		var bare = layout.Get( "Head" ).Choices[2].Mask;

		var resolved = layout.Resolve( bare );

		Assert.AreEqual( 2, layout.ChoiceOf( resolved, "Head" ), "the head it was told about" );
		Assert.AreEqual( 0, layout.ChoiceOf( resolved, "Chest" ), "and the shirt it was not" );
	}

	//Choosing one group leaves every other group showing what it was.
	[TestMethod]
	public void ChoosingOneGroupLeavesTheRestAlone()
	{
		var layout = Read( Character ).BodyGroupBits();

		var showing = layout.Resolve( BodyMask.Unset );
		var undressed = layout.WithChoice( showing, "Chest", 1 );

		Assert.AreEqual( 1, layout.ChoiceOf( undressed, "Chest" ) );
		Assert.AreEqual( 0, layout.ChoiceOf( undressed, "Head" ), "still wearing the head it was" );
		Assert.AreEqual( 0ul, undressed & layout.Get( "Chest" ).Choices[0].Mask, "and the shirt is not drawn" );

		//By index as well, which is what a body group set from code is.
		Assert.AreEqual( undressed, layout.WithChoice( showing, 1, 1 ) );

		//A choice that does not exist shows none of the group, rather than leaving whatever was showing.
		Assert.AreEqual( 0ul, layout.WithChoice( showing, "Chest", 9 ) & layout.Get( "Chest" ).Mask );

		//And a group that does not exist changes nothing at all.
		Assert.AreEqual( showing, layout.WithChoice( showing, "Tail", 0 ) );
	}

	//A mask showing none of a group's bits reads as its first choice, because that is what a caller that never set one meant.
	[TestMethod]
	public void AGroupShowingNothingReadsAsItsFirst()
	{
		var layout = Read( Character ).BodyGroupBits();

		Assert.AreEqual( 0, layout.ChoiceOf( 0, "Head" ) );
		Assert.AreEqual( 0, layout.ChoiceOf( layout.Get( "Chest" ).Choices[1].Mask, "Head" ) );
		Assert.AreEqual( 0, layout.ChoiceOf( BodyMask.Unset, "Tail" ), "and a group there is no such thing as" );
	}

	//-- the arithmetic on its own ---------------------------------------------

	//The same rules, away from any model, because the compiled side answers with these too - a preview that
	//resolved a mask differently to the drawer would show the artist a model nobody else will ever see.
	[TestMethod]
	public void TheMaskArithmeticStandsOnItsOwn()
	{
		var parts = new ulong[] { 0b0011, 0b1100 };

		Assert.AreEqual( 0ul, BodyMask.Resolve( 0, 0b0101, parts ) );
		Assert.AreEqual( 0b0101ul, BodyMask.Resolve( BodyMask.Unset, 0b0101, parts ) );
		Assert.AreEqual( BodyMask.Unset, BodyMask.Resolve( BodyMask.Unset, 0, parts ),
			"a model with no default has nothing to fall back to" );

		//The second group is mentioned, the first is not, so the first is filled in from the default.
		Assert.AreEqual( 0b1001ul, BodyMask.Resolve( 0b1000, 0b0101, parts ) );

		var choices = new ulong[] { 0b0001, 0b0010 };

		Assert.AreEqual( 0b1010ul, BodyMask.WithChoice( 0b1001, 0b0011, choices, 1 ) );
		Assert.AreEqual( 0b1000ul, BodyMask.WithChoice( 0b1001, 0b0011, choices, 4 ), "no such choice shows none of it" );

		Assert.AreEqual( 1, BodyMask.ChoiceOf( 0b1010, choices ) );
		Assert.AreEqual( 0, BodyMask.ChoiceOf( 0b1000, choices ) );
		Assert.AreEqual( 0, BodyMask.ChoiceOf( 0b1010, null ) );
	}
}
