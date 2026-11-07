using System;
using System.Linq;
using System.Text;
using Microsoft.AspNetCore.Identity;

namespace Schemata.Identity.Skeleton.Services;

/// <summary>Enforces RFC 8265 UsernameCaseMapped with the pinned Unicode 6.3 PRECIS tables.</summary>
public sealed class UsernameCaseMappedNormalizer : ILookupNormalizer
{
    public string? NormalizeEmail(string? email) => email?.ToUpperInvariant();

    public string? NormalizeName(string? name) {
        if (name is null) {
            return null;
        }

        EnsureValidUtf16(name);
        var current = name;
        for (var pass = 0; pass < 4; pass++) {
            var mapped = NormalizeUserparts(current);
            if (string.Equals(mapped, current, StringComparison.Ordinal)) {
                return mapped;
            }

            current = mapped;
        }

        throw new FormatException("The username does not stabilize under RFC 8265 UsernameCaseMapped.");
    }

    private static string NormalizeUserparts(string input) {
        if (input.Length == 0) {
            throw Invalid();
        }

        var result = new StringBuilder(input.Length);
        var start = 0;
        for (var index = 0; index <= input.Length; index++) {
            if (index < input.Length && input[index] != ' ') {
                continue;
            }

            if (index == start) {
                throw Invalid();
            }

            if (result.Length > 0) {
                result.Append(' ');
            }

            result.Append(NormalizeUserpart(input.Substring(start, index - start)));
            start = index + 1;
        }

        if (result.Length == 0) {
            throw Invalid();
        }

        return result.ToString();
    }

    public static string PrepareComparison(string input) {
        EnsureValidUtf16(input);
        return Prepare(Width(input));
    }

    private static string Width(string input) {
        var width = new StringBuilder(input.Length);
        foreach (var rune in input.EnumerateRunes()) {
            var mapped = PrecisTables.Width(rune.Value);
            if (mapped is not null) {
                width.Append(mapped);
            } else {
                width.Append(rune.ToString());
            }
        }

        return width.ToString();
    }

    private static string Prepare(string prepared) {
        var lowered = new StringBuilder(prepared.Length);
        foreach (var rune in prepared.EnumerateRunes()) {
            var mapped = PrecisTables.Lower(rune.Value);
            if (mapped is not null) {
                lowered.Append(mapped);
            } else {
                lowered.Append(rune.ToString());
            }
        }

        string normalized;
        try {
            normalized = lowered.ToString().Normalize(NormalizationForm.FormC);
        } catch (ArgumentException error) {
            throw new FormatException("The username is not valid Unicode.", error);
        }

        return normalized;
    }

    private static string NormalizeUserpart(string input) {
        var width = Width(input);
        foreach (var rune in width.EnumerateRunes()) {
            if (PrecisTables.Property(rune.Value) == PrecisProperty.Disallowed) throw Invalid();
        }
        var normalized = Prepare(width);
        if (normalized.Length == 0) {
            throw Invalid();
        }

        var points = normalized.EnumerateRunes().Select(rune => rune.Value).ToArray();
        if (points.Length == 0) {
            throw Invalid();
        }

        ValidateBidi(points);
        for (var index = 0; index < points.Length; index++) {
            switch (PrecisTables.Property(points[index])) {
                case PrecisProperty.Valid:
                    continue;
                case PrecisProperty.ContextO when ValidContextO(points, index):
                    continue;
                case PrecisProperty.ContextJ when ValidContextJ(points, index):
                    continue;
                default:
                    throw Invalid();
            }
        }

        return normalized;
    }

    private static bool ValidContextO(int[] points, int index) {
        var point = points[index];
        return point switch {
            0x00B7 => index > 0 && index + 1 < points.Length && points[index - 1] == 0x006C && points[index + 1] == 0x006C,
            0x0375 => index + 1 < points.Length && PrecisTables.Script(points[index + 1]) == PrecisScript.Greek,
            0x05F3 or 0x05F4 => index > 0 && PrecisTables.Script(points[index - 1]) == PrecisScript.Hebrew,
            0x30FB => points.Any(value => PrecisTables.Script(value) is PrecisScript.Hiragana or PrecisScript.Katakana or PrecisScript.Han),
            >= 0x0660 and <= 0x0669 => !points.Any(value => value is >= 0x06F0 and <= 0x06F9),
            >= 0x06F0 and <= 0x06F9 => !points.Any(value => value is >= 0x0660 and <= 0x0669),
            _ => false,
        };
    }

    private static bool ValidContextJ(int[] points, int index) {
        if (index == 0) {
            return false;
        }

        if (PrecisTables.IsVirama(points[index - 1])) {
            return true;
        }

        if (points[index] != 0x200C) {
            return false;
        }

        var before = index - 1;
        while (before >= 0 && PrecisTables.Joining(points[before]) == PrecisJoining.Transparent) {
            before--;
        }

        var after = index + 1;
        while (after < points.Length && PrecisTables.Joining(points[after]) == PrecisJoining.Transparent) {
            after++;
        }

        return before >= 0 && after < points.Length
            && PrecisTables.Joining(points[before]) is PrecisJoining.Left or PrecisJoining.Dual
            && PrecisTables.Joining(points[after]) is PrecisJoining.Right or PrecisJoining.Dual;
    }

    private static void ValidateBidi(int[] points) {
        var classes = points.Select(PrecisTables.Bidi).ToArray();
        if (!classes.Any(value => value is PrecisBidi.Right or PrecisBidi.ArabicLetter or PrecisBidi.ArabicNumber)) {
            return;
        }

        var first = classes[0];
        if (first is not (PrecisBidi.Left or PrecisBidi.Right or PrecisBidi.ArabicLetter)) {
            throw Invalid();
        }

        var end = classes.Length - 1;
        while (end >= 0 && classes[end] == PrecisBidi.NonSpacingMark) {
            end--;
        }

        if (end < 0) {
            throw Invalid();
        }

        if (first == PrecisBidi.Left) {
            if (classes.Any(value => value is not (PrecisBidi.Left or PrecisBidi.EuropeanNumber or PrecisBidi.EuropeanSeparator
                    or PrecisBidi.CommonSeparator or PrecisBidi.EuropeanTerminator or PrecisBidi.OtherNeutral
                    or PrecisBidi.BoundaryNeutral or PrecisBidi.NonSpacingMark))
                || classes[end] is not (PrecisBidi.Left or PrecisBidi.EuropeanNumber)) {
                throw Invalid();
            }

            return;
        }

        if (classes.Any(value => value is not (PrecisBidi.Right or PrecisBidi.ArabicLetter or PrecisBidi.ArabicNumber
                or PrecisBidi.EuropeanNumber or PrecisBidi.EuropeanSeparator or PrecisBidi.CommonSeparator
                or PrecisBidi.EuropeanTerminator or PrecisBidi.OtherNeutral or PrecisBidi.BoundaryNeutral
                or PrecisBidi.NonSpacingMark))
            || classes[end] is not (PrecisBidi.Right or PrecisBidi.ArabicLetter or PrecisBidi.EuropeanNumber
                or PrecisBidi.ArabicNumber)
            || classes.Contains(PrecisBidi.EuropeanNumber) && classes.Contains(PrecisBidi.ArabicNumber)) {
            throw Invalid();
        }
    }

    private static void EnsureValidUtf16(string input) {
        for (var index = 0; index < input.Length; index++) {
            var value = input[index];
            if (char.IsHighSurrogate(value)) {
                if (++index >= input.Length || !char.IsLowSurrogate(input[index])) {
                    throw Invalid();
                }
            } else if (char.IsLowSurrogate(value)) {
                throw Invalid();
            }
        }
    }

    private static FormatException Invalid() {
        return new("The username does not conform to RFC 8265 UsernameCaseMapped.");
    }
}
