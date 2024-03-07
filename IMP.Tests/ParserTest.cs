#undef DISABLE_PARALLEL

using System.Diagnostics;
using Xunit.Abstractions;

#if DISABLE_PARALLEL
[assembly: CollectionBehavior(DisableTestParallelization = true)]
#endif
namespace IMP.Tests;

public class ParserTest(ITestOutputHelper _output)
{
	[Fact]
	public void TestResultTree()
	{
		Assert.Equal("()", new Parser<string, string>.ResultTree().ToString());
	}

	[Fact]
	public void TestParseClassic()
	{
		Rule<string, string>.RuleTerminal Terminal(string s) => new(s);
		Rule<string, string>.RuleNonTerminal NonTerminal(string s) => new(s);

		Lexer<string> lexer = new() { { "a", "a+" }, { "b", "b+" } };
		Parser<string, string> parser =
			new(
				[
					new("S") { Terminal("a"), NonTerminal("S") },
					new("S") { Terminal("b"), NonTerminal("S") },
					new("S") {  }
				],
				lexer
			);

		var result = parser.ParseClassic("S", "aaba");
		Assert.True(result.HasValue);
		Assert.True(result.Value.IsRoot);
		Assert.True(result.Value.Root.Token == "S");
		Assert.True(result.Value.Root.Lexeme == "aaba");
	}

	[Fact]
	public void TestParse()
	{
		Rule<string, string>.RuleTerminal Terminal(string s) => new(s);
		Rule<string, string>.RuleNonTerminal NonTerminal(string s) => new(s);

		Lexer<string> lexer = new() { { "a", "a+" }, { "b", "b+" } };
		Parser<string, string> parser =
			new(
				[
					new("S") { Terminal("a"), NonTerminal("S") },
					new("S") { Terminal("b"), NonTerminal("S") },
					new("S") {  }
				],
				lexer
			);

		var result = parser.Parse("S", "aaba")?.GetTrees("aaba").ToArray() ?? [];
		Assert.Equal(2, result.Length);
		Assert.Single(result.Where(r => r.IsRoot && r.Children.FirstOrDefault().Leaf?.Lexeme == "a"));
		Assert.Single(result.Where(r => r.IsRoot && r.Children.FirstOrDefault().Leaf?.Lexeme == "aa"));
		Assert.All(result, r => Assert.True(r.IsRoot));
		Assert.All(result, r => Assert.True(r.Root!.Token == "S" && r.Root!.Lexeme == "aaba"));
	}

#if DISABLE_PARALLEL
	[Fact]
#else
#pragma warning disable xUnit1004 // Test methods should not be skipped
	[Fact(Skip = "Unreliable when run in parallel.")]
#endif
	public void StressParse()
	{
		Rule<string, string>.RuleTerminal Terminal(string s) => new(s);
		Rule<string, string>.RuleNonTerminal NonTerminal(string s) => new(s);

		Lexer<string> lexer = new() { { "a", "a+" } };
		Parser<string, string> parser = new([new("As") { Terminal("a"), NonTerminal("As") }, new("As") { Terminal("a") }], lexer);

		string program = new('a', 15);

		var sw = Stopwatch.StartNew();
		var sppf = parser.Parse("As", program);
		double parseTime = sw.Elapsed.TotalSeconds;
		Assert.All(sppf!.GetTrees(program), r => Assert.True(r.Root?.Extents.End.Value == program.Length));
		double totalTime = sw.Elapsed.TotalSeconds;

		_output.WriteLine($"Parse took {parseTime:0.00}s");
		Assert.InRange(parseTime, 0, 0.5);
		_output.WriteLine($"Trees took {totalTime - parseTime:0.00}s");
		Assert.InRange(totalTime - parseTime, 0, 0.5);
	}
}
