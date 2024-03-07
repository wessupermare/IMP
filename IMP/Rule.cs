using System.Collections;

namespace IMP;

public class Rule<TNonTerm, TTerm> : IEnumerable<Rule<TNonTerm, TTerm>.RuleItem> where TNonTerm : notnull where TTerm : notnull
{
	/// <summary>The next item in this rule.</summary>
	public RuleItem? Head => _production.Skip(SlotPosition).FirstOrDefault();

	/// <summary>Returns a rule that is one slot after this rule.</summary>
	/// <remarks>If this rule is already in the last slot, this function returns a copy.</remarks>
	public Rule<TNonTerm, TTerm> Tail => new(NonTerminal, _production) { SlotPosition = Math.Min(SlotPosition + 1, _production.Count) };

	/// <summary>Returns a rule that is one slot before this rule.</summary>
	/// <remarks>If this rule is already in the first slot, this function returns a copy.</remarks>
	public Rule<TNonTerm, TTerm> Previous => new(NonTerminal, _production) { SlotPosition = Math.Max(SlotPosition - 1, 0) };
	public int SlotPosition { get; init; } = 0;

	public readonly TNonTerm NonTerminal;
	private readonly List<RuleItem> _production;

	public Rule(TNonTerm nonTerminal) => (NonTerminal, _production) = (nonTerminal, []);
	private Rule(TNonTerm nonTerminal, IEnumerable<RuleItem> production) => (NonTerminal, _production) = (nonTerminal, new(production));

	public void Add(RuleItem item) => _production.Add(item);

	public void Add(TTerm? terminal) => AddTerminal(terminal);
	public void AddTerminal(TTerm? terminal) => _production.Add(new RuleTerminal(terminal));
	public void Add(TNonTerm nonTerminal) => AddNonTerminal(nonTerminal);
	public void AddNonTerminal(TNonTerm nonTerminal) => _production.Add(new RuleNonTerminal(nonTerminal));

	public override bool Equals(object? obj) =>
		obj is Rule<TNonTerm, TTerm> other && GetHashCode() == other.GetHashCode();

	private int? _hashcodeCache = null;
	public override int GetHashCode()
	{
		_hashcodeCache ??= HashCode.Combine(NonTerminal, SlotPosition, _production.Aggregate(0, (p, i) => HashCode.Combine(p, i.GetHashCode())));
		return _hashcodeCache!.Value;
	}

	public override string ToString() =>
		$"{NonTerminal} ::= {string.Join(' ', _production.Take(SlotPosition).Select(i => i.ToString()))}·{string.Join(' ', _production.Skip(SlotPosition).Select(i => i.ToString()))}";

	#region IEnumerable Fudging
	public IEnumerator<Rule<TNonTerm, TTerm>.RuleItem> GetEnumerator() => _production.Skip(SlotPosition).GetEnumerator();
	IEnumerator IEnumerable.GetEnumerator() => _production.Skip(SlotPosition).GetEnumerator();
	public abstract record RuleItem { public abstract override int GetHashCode(); public abstract override string ToString(); }
	public record RuleTerminal(TTerm? Terminal) : RuleItem
	{
		private int? _hashcodeCache = null;
		public override int GetHashCode()
		{
			_hashcodeCache ??= Terminal?.GetHashCode();
			return _hashcodeCache ?? 0;
		}

		public override string ToString() => Terminal?.ToString() ?? "ε";
	}

	public record RuleNonTerminal(TNonTerm NonTerminal) : RuleItem
	{
		private int? _hashcodeCache = null;
		public override int GetHashCode()
		{
			_hashcodeCache ??= NonTerminal?.GetHashCode();
			return _hashcodeCache ?? 0;
		}

		public override string ToString() => NonTerminal?.ToString() ?? "";
	}
	#endregion
}

public class RuleSet<TNonTerm, TTerm> : Dictionary<TNonTerm, HashSet<Rule<TNonTerm, TTerm>>> where TNonTerm : notnull where TTerm : notnull
{
	public RuleSet(IEnumerable<Rule<TNonTerm, TTerm>> rules)
	{
		foreach (var r in rules)
		{
			if (TryGetValue(r.NonTerminal, out var s))
				s.Add(r);
			else
				Add(r.NonTerminal, [r]);
		}
	}

	public IEnumerable<TTerm?> First(Rule<TNonTerm, TTerm>.RuleItem item) =>
		item switch {
			Rule<TNonTerm, TTerm>.RuleTerminal term => First(term.Terminal),
			Rule<TNonTerm, TTerm>.RuleNonTerminal nonterm => First(nonterm.NonTerminal),
			_ => throw new ArgumentException("Unknown rule item", nameof(item))
		};

	public TTerm?[] First(TTerm? terminal) => FirstTerm(terminal);
	public TTerm?[] FirstTerm(TTerm? terminal) => [terminal];

	private readonly Dictionary<(TNonTerm nt, TNonTerm[] exc), TTerm?[]> _firstCache = [];
	public TTerm?[] First(TNonTerm nonTerminal, IEnumerable<TNonTerm>? exclusions = null) =>
		FirstNonTerm(nonTerminal, exclusions);
	public TTerm?[] FirstNonTerm(TNonTerm nonTerminal, IEnumerable<TNonTerm>? exclusions = null)
	{
		TNonTerm[] excl = exclusions?.ToArray() ?? [];

		if (_firstCache.TryGetValue((nonTerminal, excl), out TTerm?[]? rv))
			return rv;

		HashSet<TTerm?> output = [];

		foreach (var alt in this[nonTerminal])
		{
			switch (alt.Head)
			{
				case null:
					output.Add(default);
					break;

				case Rule<TNonTerm, TTerm>.RuleTerminal rt:
					output.Add(rt.Terminal);
					break;

				case Rule<TNonTerm, TTerm>.RuleNonTerminal rnt when !excl.Contains(rnt.NonTerminal):
					HashSet<TTerm?> recursiveFirst = [.. First(rnt.NonTerminal, excl.Append(nonTerminal))];

					var altTail = alt.Tail;
					while (recursiveFirst.Contains(default) && altTail.Head is not null)
					{
						recursiveFirst.Remove(default);
						recursiveFirst.UnionWith(altTail.Head switch {
							Rule<TNonTerm, TTerm>.RuleNonTerminal nt => First(nt.NonTerminal, excl.Append(nonTerminal)),
							Rule<TNonTerm, TTerm>.RuleTerminal t => [t.Terminal],
							_ => throw new Exception()
						});
						altTail = altTail.Tail;
					}

					output.UnionWith(recursiveFirst);
					break;
			}
		}

		TTerm?[] retval = [.. output];
		_firstCache.Add((nonTerminal, excl), retval);
		return retval;
	}

	public IEnumerable<TTerm?> Follow(TNonTerm nonTerminal, IEnumerable<TNonTerm>? exclusions = null, TNonTerm? startSymbol = default)
	{
		exclusions ??= [];

		IEnumerable<TTerm?> fw(Rule<TNonTerm, TTerm> rule)
		{
			if (rule.Head is null)
				return
					rule.NonTerminal.Equals(startSymbol)
					? (exclusions.Contains(rule.NonTerminal) ? [] : Follow(rule.NonTerminal, exclusions!.Append(nonTerminal))).Append(default)
					: exclusions.Contains(rule.NonTerminal) ? [] : Follow(rule.NonTerminal, exclusions!.Append(nonTerminal));
			else if (rule.Head is Rule<TNonTerm, TTerm>.RuleNonTerminal nt)
			{
				var res = First(nt.NonTerminal, exclusions!.Append(nonTerminal)).ToArray();
				if (res.All(r => r is not null))
					return res.Select(r => r!);
				else
					return new HashSet<TTerm?>(res.Where(r => r is not null).Select(r => r!)).Union(fw(rule.Tail));
			}
			else
				return First(((Rule<TNonTerm, TTerm>.RuleTerminal)rule.Head).Terminal);
		}

		Rule<TNonTerm, TTerm> preprocess(Rule<TNonTerm, TTerm> rule)
		{
			while (rule.Head is not null && (rule.Head is not Rule<TNonTerm, TTerm>.RuleNonTerminal rnt || !rnt.NonTerminal.Equals(nonTerminal)))
				rule = rule.Tail;
			if (rule.Head is null || rule.Head is not Rule<TNonTerm, TTerm>.RuleNonTerminal nonterm || !nonterm.NonTerminal.Equals(nonTerminal))
				throw new Exception();

			return rule.Tail;
		}

		return Values
			.SelectMany(rs => rs.Where(r => r.Any(i => i is Rule<TNonTerm, TTerm>.RuleNonTerminal rnt && rnt.NonTerminal.Equals(nonTerminal))))
			.Select(preprocess)
			.Aggregate(new HashSet<TTerm?>(), (s, r) => s.Union(fw(r).Where(i => i is not null || r.NonTerminal.Equals(startSymbol))).ToHashSet());
	}

	public IEnumerable<TTerm?> Test(TNonTerm nonTerminal)
	{
		var first = First(nonTerminal);

		if (first.Any(f => f is null))
			return first.Where(f => f is not null).Union(Follow(nonTerminal));

		return first;
	}
}