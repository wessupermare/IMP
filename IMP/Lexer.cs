using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace IMP;

public class Lexer<TTerm> : Dictionary<TTerm, Regex> where TTerm : notnull
{
	public Lexer() : base() { }

	public Lexer(IEnumerable<KeyValuePair<TTerm, Regex>> other) : base()
	{
		foreach (var kvp in other)
			base.Add(kvp.Key, new("^(" + kvp.Value.ToString().TrimStart('^').TrimEnd('$') + ")\\z", kvp.Value.Options | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture | RegexOptions.Compiled));
	}

	public void Add(TTerm key, [StringSyntax(StringSyntaxAttribute.Regex)] string value) =>
		base.Add(key, new("^(" + value.TrimStart('^').TrimEnd('$') + ")\\z", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture | RegexOptions.Compiled));

	/// <summary>Performs absorption as appropriate for a single TWE triple.</summary>
	static TweTriple<TTerm> AbsorbTriple(string input, TweTriple<TTerm> raw, IEnumerable<Regex> absorb)
	{
		Queue<int> frontierInternal = new([raw.Extents.End.Value]);
		HashSet<int> exploredInternal = [];

		// Breadth first search for absorbable tokens.
		while (frontierInternal.TryDequeue(out int checkB))
		{
			exploredInternal.Add(checkB);
			string remainder = input[checkB..];

			foreach (int nb in Enumerable.Range(1, remainder.Length).Where(e => absorb!.Any(r => r.IsMatch(remainder[..e]))).Select(e => e + checkB).Where(e => !exploredInternal.Contains(e)))
				frontierInternal.Enqueue(nb);
		}

		Index newEnd = exploredInternal.Max();
        return raw with { Extents = raw.Extents.Start..newEnd };
	}

	/// <summary>Performs absorption as appropriate for a collection of TWE triples.</summary>
	static IEnumerable<TweTriple<TTerm>> Absorb(string input, IEnumerable<TweTriple<TTerm>> raw, IEnumerable<Regex>? absorb) =>
		absorb is null
		? raw
		: raw.Select(r => Lexer<TTerm>.AbsorbTriple(input, r, absorb));

	/// <summary>Generates a TWE set for the given input.</summary>
	/// <param name="input">The string to lexicalise.</param>
	/// <param name="absorb">Any tokens to use for absorption.</param>
	public HashSet<TweTriple<TTerm>> Lex(string input, IEnumerable<Regex>? absorb = null, bool lexClassic = false)
	{
		Queue<int> frontier = new([0]);
		HashSet<int> explored = [];
		HashSet<TweTriple<TTerm>> retval = new(new TweTriple<TTerm>.EqualityComparer());

		absorb = absorb?.Select(a => new Regex("^(" + a.ToString().TrimStart('^').TrimEnd('$') + ")\\z", a.Options | RegexOptions.Compiled)).ToArray();

		static IEnumerable<int> AllMatchingExtents(Regex r, string input, int offset = 0)
		{
			for (int right = offset; right <= input.Length; ++right)
				if (r.IsMatch(input[offset..right]))
					yield return right;
		}

		while (frontier.TryDequeue(out int left))
		{
			explored.Add(left);

			// Calculate available tokens and absorb as necessary
			HashSet<TweTriple<TTerm>> additions = [];
			string remainder = input[left..];
			int offset = 0;

			while (left == 0 && (absorb?.Any(r => Enumerable.Range(offset, remainder.Length - offset).Any(e => r.IsMatch(remainder[offset..e]))) ?? false))
				offset = absorb.Max(r => Enumerable.Range(offset, remainder.Length - offset).Where(e => r.IsMatch(remainder[offset..e])).OrderByDescending(k => k).FirstOrDefault());

			if (Count < 4)
			{
				foreach (var kvp in this)
					additions.UnionWith(Lexer<TTerm>.Absorb(input, AllMatchingExtents(kvp.Value, remainder, offset).Select(r => new TweTriple<TTerm>(left..(left + r), kvp.Key, remainder[offset..r]) { UnabsorbedSpan = (left + offset)..(left + r) }), absorb));
			}
			else
				additions.UnionWith(
					this.AsParallel().AsUnordered().SelectMany(kvp => 
						Lexer<TTerm>.Absorb(
							input,
							AllMatchingExtents(kvp.Value, remainder, offset).Select(r => new TweTriple<TTerm>(left..(left + r), kvp.Key, remainder[offset..r]) { UnabsorbedSpan = (left + offset)..(left + r) }),
							absorb
						)
					)
				);

			if (lexClassic && additions.Count != 0)
			{
				var addition = additions.MaxBy(a => a.Extents.End.Value)!;
				if (!explored.Contains(addition.Extents.End.Value) && !frontier.Contains(addition.Extents.End.Value))
					frontier.Enqueue(addition.Extents.End.Value);

				retval.Add(addition);
			}
			else
			{
				// Add new item extents to search.
				foreach (int right in additions.Select(a => a.Extents.End.Value).Where(n => !explored.Contains(n) && !frontier.Contains(n)))
					frontier.Enqueue(right);

				retval.UnionWith(additions);
			}
		}

		bool checkAbsorbEqual(TweTriple<TTerm> a, TweTriple<TTerm> b) =>
			a.Extents.Start.Value == b.Extents.Start.Value && a.Extents.End.Value == b.Extents.End.Value && a.Token.Equals(b.Token) &&
			a.UnabsorbedSpan.Start.Value == b.UnabsorbedSpan.Start.Value && a.UnabsorbedSpan.End.Value == b.UnabsorbedSpan.End.Value &&
			(absorb?.Any(r => r.IsMatch(a.Lexeme.Length > b.Lexeme.Length ? a.Lexeme[b.Lexeme.Length..] : b.Lexeme[a.Lexeme.Length..])) ?? false);

		var retcache = retval.ToArray();
		foreach (var r1 in retcache)
			foreach (var r2 in retcache)
				if (r1.Equals(r2) || !checkAbsorbEqual(r1, r2))
					continue;
				else if (r1.Lexeme.Length > r2.Lexeme.Length) // Absorb as little as possible.
					retval.Remove(r2);
				else
					retval.Remove(r1);


		if (!explored.Contains(input.Length))
			// Non-spanning lexicalisation
			throw new ArgumentException(explored.Max().ToString(), nameof(input));

		return retval;
	}
}

public record TweTriple<T>(Range Extents, T Token, string Lexeme)
{
	public Range UnabsorbedSpan { get; init; } = Extents;

	public override string ToString() => $"<{Extents.Start}, {(Token is null ? "ε" : Token.ToString())}, {Extents.End}>";
	public override int GetHashCode() => HashCode.Combine(Extents, Token, Lexeme);

	public class EqualityComparer : IEqualityComparer<TweTriple<T>>
	{
		bool IEqualityComparer<TweTriple<T>>.Equals(TweTriple<T>? x, TweTriple<T>? y) => x?.GetHashCode() == y?.GetHashCode();

		int IEqualityComparer<TweTriple<T>>.GetHashCode(TweTriple<T> obj) => obj.GetHashCode();
	}
}

public abstract record LexerIndentRule()
{
	public record Default() : LexerIndentRule() { }
	public record Tabs(int Width) : LexerIndentRule() { }
	public record Spaces(int Width) : LexerIndentRule() { }
}