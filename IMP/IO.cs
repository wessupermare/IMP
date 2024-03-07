using System.Collections;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace IMP.IO;

public class Specification : IEnumerable<Rule<string, string>>
{
	public Parser Parser => new(this, Lexer);

	public Lexer<string> Lexer { get; }

	public string StartSymbol { get; private init; }

	public IEnumerable<Regex>? Absorb { get; init; }

	private readonly List<Rule<string, string>> _rules;

	private Specification(List<Rule<string, string>> rules, string startRule, Lexer<string> lexer) =>
		(_rules, StartSymbol, Lexer) = (rules, new(startRule), lexer);

	public Specification WithStart(string startNt) => new(_rules, startNt, Lexer) { Absorb = Absorb };

	public static Specification Parse(string specification, bool fast = false)
	{
		// Shorthand differentiators for the metarule adding.
		Rule<string, string>.RuleNonTerminal nt(string name) => new(name);
		Rule<string, string>.RuleTerminal t(string name) => new(name);

#if DEBUG
		System.Diagnostics.Debug.WriteLine("Building metaparser");
#endif

		static Regex keyword([StringSyntax(StringSyntaxAttribute.Regex)] string name) =>
			new($"^({name})\\z", RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant | RegexOptions.Compiled | RegexOptions.Singleline);

		Lexer<string> metaruleLexer = new()
		{
			{ "::=",    @"::=" },
			{ "|",      @"\|" },
			{ "eps",    @"ε|#" },
			{ "start",  keyword(@"start")},
			{ "strict",  keyword(@"strict")},
			{ "absorb",  keyword(@"absorb")},
			{ "casedLiteral",   @"'([^']|\\')*'" },
			{ "uncasedLiteral", @"""([^""]|\\"")*""" },
			{ "id",     @"([^'""\|\sε#:=]|:[^:=])+" },
			{ "regex",  @":=[^\n]*\n" },
		};

		List<Rule<string, string>> metarules =
		[
			new("Spec") { },
			new("Spec") { nt("Directive"), nt("Spec") },
			new("Spec") { nt("Rules") },

			new("Rules") { nt("ParseRule") },
			new("Rules") { nt("ParseRule"), nt("Rules") },
			new("Rules") { nt("LexRule") },
			new("Rules") { nt("LexRule"), nt("Rules") },

			new("Directive") { t("start"), t("id") },
			new("Directive") { t("strict") },
			new("Directive") { t("absorb"), nt("AltTail") },

			new("ParseRule") { t("id"), t("::="), nt("Alternates") },
			new("Alternates") { nt("Alternate") },
			new("Alternates") { nt("Alternate"), t("|"), nt("Alternates") },
			new("Alternate") { t("eps") },
			new("Alternate") { t("casedLiteral"), nt("AltTail") },
			new("Alternate") { t("uncasedLiteral"), nt("AltTail") },
			new("Alternate") { t("id"), nt("AltTail") },
			new("AltTail") { t("casedLiteral"), nt("AltTail") },
			new("AltTail") { t("uncasedLiteral"), nt("AltTail") },
			new("AltTail") { t("id"), nt("AltTail") },
			new("AltTail") {  },

			new("LexRule") { t("id"), t("regex") }
		];

		Parser ruleParser = new(
			metarules,
			metaruleLexer
		);

		specification += specification.TrimEnd().Length == 0 ? "" : Environment.NewLine;

#if DEBUG
		System.Diagnostics.Debug.WriteLine("Parsing specification");
#endif

		// Get a tree for the provided specification.
		var sppf = ruleParser.Parse("Spec", specification, [new(@"\s+"), new(@"//[^\n]*\n")], Parser.LongestMatchDeadBranchPrune, classicalLexing: fast) ?? throw new ArgumentException("Invalid specification.", nameof(specification));

		var spec = sppf.GetTrees(specification).Single();

#if DEBUG
		System.Diagnostics.Debug.WriteLine("Analysing parsed specification");
#endif

		Dictionary<string, Regex> lexerRules = [];
		HashSet<Rule<string, string>> parserRules = [];

		RegexOptions regexOptions = RegexOptions.CultureInvariant | RegexOptions.Singleline;

        string? startSymbol = null;
		HashSet<string> absorb = [];
		HashSet<string> safeLexerRules = [];
		bool strict = false;

		while (spec.Children.Length != 0 && spec.Children[0].IsRoot)
		{
			switch (spec.Children[0].Root!.Token)
			{
				case "Directive":
					var directive = spec.Children[0];
					switch (directive.Children[0].Leaf!.Token)
					{
						case "start":
							startSymbol = directive.Children[1].Leaf!.Lexeme.Trim();
							break;

						case "strict":
							strict = true;
							break;

						case "absorb":
							var symbols = directive.Children[1];
							while (symbols.Children.Length > 1)
							{
								var (_, token, lexeme) = symbols.Children[0].Leaf!;
								lexeme = lexeme.Trim();

								switch (token)
								{
									case "id":
										absorb.Add(lexeme);
										break;

									case "casedLiteral":
										if (!lexerRules.ContainsKey(lexeme))
											lexerRules.Add(lexeme, new(Regex.Escape(lexeme[1..^1]), regexOptions));
										absorb.Add(lexeme);
										break;

									case "uncasedLiteral":
										if (!lexerRules.ContainsKey(lexeme))
											lexerRules.Add(lexeme, new(Regex.Escape(lexeme[1..^1]), regexOptions | RegexOptions.IgnoreCase));
										absorb.Add(lexeme);
										break;
								}

								symbols = symbols.Children[1];
							}
							break;
					}
					break;

				case "Rules":
					var rules = spec.Children[0];
					while (rules.Children.Length != 0)
					{
						var rule = rules.Children[0];
						if (rule.Root!.Token == "ParseRule")
						{
							var header = rule.Children[0];
							string nonterminal = header.Leaf!.Lexeme.Trim();

							var alternates = rule.Children[2];
							var alternate = alternates.Children[0];
							while (alternates.Children.Length != 0)
							{
								Rule<string, string> production = new(nonterminal);

								while (alternate.Children.Length > 1)
								{
									var (_, token, lexeme) = alternate.Children[0].IsLeaf ? alternate.Children[0].Leaf! : alternate.Children[0].Root!;
									lexeme = lexeme.Trim();
									switch (token)
									{
										case "casedLiteral":
											if (!lexerRules.ContainsKey(lexeme))
												lexerRules.Add(lexeme, new(Regex.Escape(lexeme[1..^1]), regexOptions));

											safeLexerRules.Add(lexeme);
											production.AddTerminal(lexeme);
											break;

										case "uncasedLiteral":
											if (!lexerRules.ContainsKey(lexeme))
												lexerRules.Add(lexeme, new(Regex.Escape(lexeme[1..^1]), regexOptions | RegexOptions.IgnoreCase));

											safeLexerRules.Add(lexeme);
											production.AddTerminal(lexeme);
											break;

										case "id":
											safeLexerRules.Add(lexeme);
											production.AddNonTerminal(lexeme);
											break;
									}

									alternate = alternate.Children[1];
								}

								parserRules.Add(production);

								// Advance to the next alternate as appropriate
								if (alternates.Children.Length == 3)
								{
									alternates = alternates.Children[2];
									alternate = alternates.Children[0];
								}
								else
									break;
							}
						}
						else if (rule.Root!.Token == "LexRule")
						{
							string terminal = rule.Children[0].Leaf!.Lexeme.Trim();

							if (!lexerRules.TryAdd(terminal, new(rule.Children[1].Leaf!.Lexeme[2..].Trim(), regexOptions)))
								throw new ArgumentException($"Duplicate definitions of terminal {rule.Root.Lexeme.Trim()}.", nameof(specification));
						}

						if (rules.Children.Length == 1)
							break;

						rules = rules.Children[1];
					}
					break;
			}

			if (spec.Children.Length == 1)
				break;

			spec = spec.Children[1];
		}

		// Remap lex rules from NTs into Ts.
		Rule<string, string> remapLexRule(Rule<string, string> rule)
		{
			Rule<string, string> retval = new(rule.NonTerminal);

			foreach (var elem in rule)
				if (elem is Rule<string, string>.RuleNonTerminal rnt && lexerRules.ContainsKey(rnt.NonTerminal))
					retval.AddTerminal(rnt.NonTerminal);
				else
					retval.Add(elem);

			return retval;
		}

		Regex findAbsorbRule(string terminal)
		{
			if (!lexerRules.TryGetValue(terminal, out Regex? retval))
				throw new ArgumentException($"Could not find definition of absorbed terminal {terminal}.", nameof(terminal));

			return retval;
		}

		parserRules = parserRules.Select(remapLexRule).ToHashSet();

		if (parserRules.Count != 0)
		{
			if (startSymbol is null)
			{
				IEnumerable<string> nonTerms = parserRules.Select(r => r.NonTerminal).Distinct();

				if (strict && nonTerms.Count() > 1)
					throw new ArgumentException("Strict-enabled specifications must either have a single rule or a start directive.", nameof(specification));
				else
					startSymbol = nonTerms.First();
			}

			if (!parserRules.Any(r => r.NonTerminal == startSymbol))
				throw new ArgumentException($"Could not find definition of designated start symbol {startSymbol}.", nameof(specification));
		}
		else
			startSymbol ??= "";

		if (!strict)
		{
			lexerRules.Add("__IMP_WHITESPACE__", new(@"\s+", RegexOptions.Compiled));
			absorb.Add("__IMP_WHITESPACE__");
		}

#if DEBUG
		System.Diagnostics.Debug.WriteLine("Generating final parser");
#endif

		return new([.. parserRules], startSymbol, new Lexer<string>(lexerRules.Where(kvp => safeLexerRules.Contains(kvp.Key)))) { Absorb = absorb.Select(findAbsorbRule).ToImmutableArray()};
	}

	public void Export(string path) => File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(new SerializableSpecification(_rules, StartSymbol, Lexer, Absorb)));

	public static Specification? Import(string path) => System.Text.Json.JsonSerializer.Deserialize<SerializableSpecification>(File.ReadAllText(path))?.ToSpecification();

	private class SerializableSpecification
	{
		public Rule[] Rules { get; set; } = [];
		public Dictionary<string, string> LexerRegexes { get; set; } = [];
		public string[]? AbsorbRegexes { get; set; } = null;
		public string StartRule { get; set; } = "";

		[JsonConstructor]
		public SerializableSpecification() { }

		public SerializableSpecification(IEnumerable<Rule<string, string>> rules, string start, Lexer<string> lexer, IEnumerable<Regex>? absorb)
		{
			Rules = rules.Select(UnpackRule).Select(r => new Rule(r.Name, r.Rules)).ToArray();
			LexerRegexes = new(lexer.Select(r => new KeyValuePair<string, string>(r.Key, r.Value.ToString())));
			AbsorbRegexes = absorb?.Select(r => r.ToString())?.ToArray();
			StartRule = start;
		}

		public Specification ToSpecification() => new(Rules.Select(r => PackRule(r.Name, r.Items)).ToList(), StartRule, new(LexerRegexes.Select(kvp => new KeyValuePair<string, Regex>(kvp.Key, new(kvp.Value))))) { Absorb = AbsorbRegexes?.Select(r => new Regex(r, RegexOptions.Compiled))?.ToArray()};

		(string Name, RuleItem[] Rules) UnpackRule(Rule<string, string> rule)
		{
			List<RuleItem> items = [];

			foreach (var item in rule)
			{
				if (item is Rule<string, string>.RuleNonTerminal rnt)
					items.Add(new(false, rnt.NonTerminal));
				else if (item is Rule<string, string>.RuleTerminal rt)
					items.Add(new(true, rt.Terminal));
				else
					throw new NotImplementedException();
			}

			return (rule.NonTerminal, items.ToArray());
		}

		static Rule<string, string> PackRule(string name, RuleItem[] ruleItems)
		{
			Rule<string, string> retval = new(name);

			foreach (RuleItem item in ruleItems)
				if (item.Terminal)
					retval.AddTerminal(item.Value);
				else
					retval.AddNonTerminal(item.Value!);

			return retval;
		}

		public record RuleItem(bool Terminal, string? Value) { }

		public record Rule(string Name, RuleItem[] Items) { }
	}

	public Parser<string, string>.SPPFNode? Parse(string input) =>
		Parser.Parse(StartSymbol, input, Absorb);

    public Parser<string, string>.SPPFNode? Parse(string input, Func<IEnumerable<TweTriple<string>>, IEnumerable<TweTriple<string>>> chooser) =>
        Parser.Parse(StartSymbol, input, Absorb, chooser);

    public bool TryParse(string input, [NotNullWhen(true)] out Parser<string, string>.SPPFNode? parseForest, [NotNullWhen(false)] out (int Index, Rule<string, string>? ParsePoint)? failurePoint) =>
        Parser.TryParse(StartSymbol, input, out parseForest, out failurePoint, Absorb);

    public bool TryParse(string input, Func<IEnumerable<TweTriple<string>>, IEnumerable<TweTriple<string>>> chooser, [NotNullWhen(true)] out Parser<string, string>.SPPFNode? parseForest, [NotNullWhen(false)] out (int Index, Rule<string, string>? ParsePoint)? failurePoint) =>
        Parser.TryParse(StartSymbol, input, out parseForest, out failurePoint, Absorb, chooser);

    #region IEnumerable Fudging
    public IEnumerator<Rule<string, string>> GetEnumerator() => _rules.GetEnumerator();
	IEnumerator IEnumerable.GetEnumerator() => _rules.GetEnumerator();
	#endregion
}
