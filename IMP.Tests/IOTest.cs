using IMP.IO;

namespace IMP.Tests;

public class IOTest
{
	[Fact]
	public void TestBlank() => Assert.Empty(Specification.Parse(""));

	[Fact]
	public void TestSimple()
	{
		string specification = "A ::= 'a'";
		var spec = Specification.Parse(specification);
		Assert.NotEmpty(spec);
		Assert.Single(spec.Lexer);

		AssertUnambiguous(spec.Parser.Parse("A", "a"));
	}

	[Fact]
	public void TestCompound()
	{
		string specification = @"A ::= 'a' A | ε";
		var spec = Specification.Parse(specification);
		Assert.NotEmpty(spec);
		Assert.Single(spec.Lexer);

		for (int i = 0; i < 10; ++i)
			AssertUnambiguous(spec.Parser.Parse("A", new('a', i)));
	}

	[Fact]
	public void TestAmbiguous()
	{
		string specification = @"A ::= 'a' A | A 'a' | ε";
		var spec = Specification.Parse(specification);
		Assert.NotEmpty(spec);
		Assert.Single(spec.Lexer);

		Assert.Equal(4, TreeCount(spec.Parser.Parse("A", "aa")));
	}

	[Fact]
	public void TestFirst()
	{
		var spec = Specification.Parse(@"A ::= 'a' A | ε");
		var first = spec.Parser._rules.FirstNonTerm("A").ToArray();

		Assert.Equal(2, first.Length);
		Assert.Contains("'a'", first);
		Assert.Contains(null, first);
	}

	[Fact]
	public void TestFollow()
	{
		var spec = Specification.Parse(@"S ::= A A
A ::= 'a' A | ε");
		var follow = spec.Parser._rules.Follow("A", startSymbol: "S").ToArray();

		Assert.Equal(2, follow.Length);
		Assert.Contains("'a'", follow);
		Assert.Contains(null, follow);
	}

	[Fact]
	public void TestLexRule()
	{
		var spec = Specification.Parse(@"S ::= eps
eps := ε|#");
		Assert.NotEmpty(spec);
		Assert.Single(spec.Lexer);
		var rule = Assert.Single(Assert.Single(spec.Parser._rules).Value);
		Assert.IsType<Rule<string, string>.RuleTerminal>(Assert.Single(rule));
	}

	[Fact]
	public void TestQuine()
	{
		string specStr = File.ReadAllText("bnf.bnf");

		var spec = Specification.Parse(specStr, true);
		Assert.Equal(9, spec.Parser._rules.Count);
		Assert.Equal(8, spec.Lexer.Where(l => l.Key == l.Key.Trim('\'', '"')).Count()); // Explicit lex rules.
		Assert.Equal(3, spec.Lexer.Where(l => l.Key != l.Key.Trim('\'', '"')).Count()); // Literals
		Assert.NotNull(spec.Absorb);
		Assert.Single(spec.Absorb);
		Assert.Equal("Spec", spec.StartSymbol);

		AssertUnambiguous(spec.Parser.Parse(spec.StartSymbol, specStr, spec.Absorb, classicalLexing: true));
	}

	[Fact]
	public void TestStrict()
	{
		Assert.Throws<ArgumentException>(() => Specification.Parse("strict\nA::=B\nB::=A"));
		Assert.Equal("A", Specification.Parse("A::=B\nB::=A").StartSymbol);
		Assert.Equal("B", Specification.Parse("strict\nstart B\nA::=B\nB::=A").StartSymbol);
	}

	private static void AssertUnambiguous(Parser<string, string>.SPPFNode? rootNode) =>
		Assert.Equal(1, TreeCount(rootNode));

	private static int TreeCount(Parser<string, string>.SPPFNode? rootNode)
	{
		Assert.NotNull(rootNode);
		return rootNode!.GetTrees("").Length;
	}

	[Fact]
	public void TestPointless()
	{
		Specification.Parse(@"
			Text ::= anything Text | ε
			anything := .*");
	}
}