using System.Text.RegularExpressions;

namespace IMP.Tests;

public class LexerTest
{
	[Fact]
	public void TestLex()
	{
		string input = "aa aa";
		Lexer<string> l = new() { { "A", @"a+" } };
		var tmp = l.Lex(input, new Regex[] { new(@"^\s+") }).ToArray();
		Assert.Equal(6, tmp.Length);
		Assert.All(tmp, l => Assert.InRange(l.Extents.Start.Value, 0, input.Length));
		Assert.All(tmp, l => Assert.InRange(l.Extents.End.Value, 0, input.Length));
		Assert.All(new[] { 0..1, 0..3, 1..3, 3..4, 4..5, 3..5 }, e => Assert.Single(tmp.Where(t => (t.Extents.Start.Value, t.Extents.End.Value) == (e.Start.Value, e.End.Value))));
	}
}