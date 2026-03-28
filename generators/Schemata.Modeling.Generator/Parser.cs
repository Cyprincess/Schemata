using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Parlot;
using Parlot.Fluent;
using Schemata.Modeling.Generator.Expressions;
using static Parlot.Fluent.Parsers;

namespace Schemata.Modeling.Generator;

internal static class Parser
{
    // ── Shared sub-parsers (reused by later tasks) ──────────────────────

    internal static readonly Parser<TextSpan> Identifier;

    internal static readonly Parser<string> QualifiedName;

    // ── Entry-point parsers ─────────────────────────────────────────────

    internal static readonly Parser<IExpression> Literal;

    internal static readonly Parser<IExpression> Expression;

    // ── Annotation & composition parsers ──────────────────────────────

    internal static readonly Parser<Note> Note;

    internal static readonly Parser<Property> Property;

    internal static readonly Parser<EquatableArray<FieldOption>> FieldOptions;

    internal static readonly Parser<EquatableArray<ViewOption>> ViewOptions;

    internal static readonly Parser<EquatableArray<PointerOption>> PointerOptions;

    internal static readonly Parser<Use> Use;

    // ── Member parsers ──────────────────────────────────────────────────

    internal static readonly Parser<Field> Field;

    internal static readonly Parser<Pointer> Pointer;

    internal static readonly Parser<ViewField> ViewField;

    internal static readonly Parser<View> View;

    // ── Declaration parsers ───────────────────────────────────────────

    internal static readonly Parser<EnumValue> EnumValue;

    internal static readonly Parser<Enumeration> Enumeration;

    internal static readonly Parser<string> Namespace;

    internal static readonly Parser<Entity> Entity;

    internal static readonly Parser<Trait> Trait;

    internal static readonly Parser<Document> Document;

    // ── Construction ────────────────────────────────────────────────────

    static Parser() {
        // ── Terminals ───────────────────────────────────────────────

        var dot   = Terms.Char('.');
        var comma = Terms.Char(',');
        var open  = Terms.Char('(');
        var close = Terms.Char(')');

        // ── Identifier & QualifiedName ──────────────────────────────

        Identifier = Terms.Identifier();

        QualifiedName = Identifier.And(ZeroOrMany(dot.SkipAnd(Identifier)))
                                  .Then(pair => {
                                       if (pair.Item2 == null || pair.Item2.Count == 0) {
                                           return pair.Item1.ToString();
                                       }

                                       var parts = new string[pair.Item2.Count + 1];
                                       parts[0] = pair.Item1.ToString();
                                       for (var i = 0; i < pair.Item2.Count; i++) {
                                           parts[i + 1] = pair.Item2[i].ToString();
                                       }

                                       return string.Join(".", parts);
                                   });

        // ── String literals ─────────────────────────────────────────

        // Triple-single-quoted: '''...'''
        var tripleSingleQuote = Terms.Text("'''");
        var tripleSingleString = tripleSingleQuote
                                .SkipAnd(AnyCharBefore(tripleSingleQuote, true, consumeDelimiter: true))
                                .Then<IExpression>(span => new Literal(span.ToString()));

        // Triple-double-quoted: """..."""
        var tripleDoubleQuote = Terms.Text("\"\"\"");
        var tripleDoubleString = tripleDoubleQuote
                                .SkipAnd(AnyCharBefore(tripleDoubleQuote, true, consumeDelimiter: true))
                                .Then<IExpression>(span => new Literal(span.ToString()));

        // Standard single or double quoted
        var quoted = Terms.String().Then<IExpression>(span => new Literal(span.ToString()));

        // Triple-quoted must be tried BEFORE standard quoted
        var @string = tripleSingleString.Or(tripleDoubleString).Or(quoted);

        // ── Number literal ──────────────────────────────────────────

        var number = Terms.Decimal().Then<IExpression>(n => new NumberLiteral(n.ToString()));

        // ── Boolean literal ─────────────────────────────────────────

        var @true = Terms.Text("true", true).Then<IExpression>(_ => new BooleanLiteral(true));

        var @false = Terms.Text("false", true).Then<IExpression>(_ => new BooleanLiteral(false));

        var boolean = @true.Or(@false);

        // ── Null literal ────────────────────────────────────────────

        var @null = Terms.Text("null", true).Then<IExpression>(_ => new NullLiteral());

        // ── Literal (union) ─────────────────────────────────────────

        // Order: string first (consume quotes), then boolean/null (keywords),
        // then number (digits / sign).
        Literal = @string.Or(boolean).Or(@null).Or(number);

        // ── Expression (recursive) ──────────────────────────────────

        var expression = Deferred<IExpression>();

        // Function call: name( [arg {, arg}] )
        var args = Separated(comma, expression);

        var call = QualifiedName.And(Between(open, ZeroOrOne(args), close))
                                .Then<IExpression>(pair => {
                                     var                         name = pair.Item1;
                                     var                         args = pair.Item2;
                                     ImmutableArray<IExpression> array;
                                     if (args == null || args.Count == 0) {
                                         array = ImmutableArray<IExpression>.Empty;
                                     } else {
                                         var builder = ImmutableArray.CreateBuilder<IExpression>(args.Count);
                                         for (var i = 0; i < args.Count; i++) {
                                             builder.Add(args[i]);
                                         }

                                         array = builder.MoveToImmutable();
                                     }

                                     return new FunctionCall(name, array);
                                 });

        // Reference: qualified name without parens
        var reference = QualifiedName.Then<IExpression>(name => new Reference(name));

        // Expression = literal | call | reference
        // Literal first (handles strings, booleans, null, numbers).
        // FunctionCall before reference (both start with identifier,
        // but function call requires parens).
        expression.Parser = Literal.Or(call).Or(reference);

        Expression = expression.Compile();

        // ── String value (returns string, not IExpression) ────────────

        var tripleSingleStringValue = tripleSingleQuote
                                     .SkipAnd(AnyCharBefore(tripleSingleQuote, true, consumeDelimiter: true))
                                     .Then(span => span.ToString());

        var tripleDoubleStringValue = tripleDoubleQuote
                                     .SkipAnd(AnyCharBefore(tripleDoubleQuote, true, consumeDelimiter: true))
                                     .Then(span => span.ToString());

        var quotedValue = Terms.String().Then(span => span.ToString());

        var stringValue = tripleSingleStringValue.Or(tripleDoubleStringValue).Or(quotedValue);

        // ── Note ──────────────────────────────────────────────────────

        Note = Terms.Text("Note", true).SkipAnd(stringValue).Then(text => new Note(text));

        // ── Property ──────────────────────────────────────────────────

        Property = Identifier.And(expression).Then(pair => new Property(pair.Item1.ToString(), pair.Item2));

        // ── Option parsers ────────────────────────────────────────────

        var lbracket = Terms.Char('[');
        var rbracket = Terms.Char(']');

        // An option entry is one or more identifiers (space-separated words)
        // collected until we hit ',' or ']'.
        var entry = OneOrMany(Identifier)
           .Then(spans => {
                var words = new string[spans.Count];
                for (var i = 0; i < spans.Count; i++) {
                    words[i] = spans[i].ToString();
                }

                return NormalizeOption(string.Join(" ", words));
            });

        var options = Separated(comma, entry);

        FieldOptions = Between(lbracket, options, rbracket)
           .Then(list => {
                var builder = ImmutableArray.CreateBuilder<FieldOption>(list.Count);
                for (var i = 0; i < list.Count; i++) {
                    builder.Add(ParseFieldOption(list[i]));
                }

                return (EquatableArray<FieldOption>)builder.MoveToImmutable();
            });

        ViewOptions = Between(lbracket, options, rbracket)
           .Then(list => {
                var builder = ImmutableArray.CreateBuilder<ViewOption>(list.Count);
                for (var i = 0; i < list.Count; i++) {
                    builder.Add(ParseViewOption(list[i]));
                }

                return (EquatableArray<ViewOption>)builder.MoveToImmutable();
            });

        PointerOptions = Between(lbracket, options, rbracket)
           .Then(list => {
                var builder = ImmutableArray.CreateBuilder<PointerOption>(list.Count);
                for (var i = 0; i < list.Count; i++) {
                    builder.Add(ParsePointerOption(list[i]));
                }

                return (EquatableArray<PointerOption>)builder.MoveToImmutable();
            });

        // ── Use ───────────────────────────────────────────────────────

        var names = Separated(comma, QualifiedName);

        Use = Terms.Text("Use", true)
                   .SkipAnd(names)
                   .Then(list => {
                        var builder = ImmutableArray.CreateBuilder<string>(list.Count);
                        for (var i = 0; i < list.Count; i++) {
                            builder.Add(list[i]);
                        }

                        return new Use(builder.MoveToImmutable());
                    });

        // ── Braces ──────────────────────────────────────────────────────

        var lbrace   = Terms.Char('{');
        var rbrace   = Terms.Char('}');
        var equals   = Terms.Char('=');
        var question = Terms.Char('?');

        // ── Field ───────────────────────────────────────────────────────

        // type specifier = qualifiedName [ "?" ]
        // field = typeSpecifier identifier [fieldOptions] ["{" {note | property} "}"]

        var noteOrProperty = Note.Then<object>(n => n).Or(Property.Then<object>(p => p));

        var fieldBody = Between(lbrace, ZeroOrMany(noteOrProperty), rbrace);

        Field = QualifiedName.And(ZeroOrOne(question))
                             .And(Identifier)
                             .And(ZeroOrOne(FieldOptions))
                             .And(ZeroOrOne(fieldBody))
                             .Then(parts => {
                                  var type     = parts.Item1;
                                  var nullable = parts.Item2 != 0;
                                  var name     = parts.Item3.ToString();
                                  var options  = parts.Item4;
                                  var body     = parts.Item5;

                                  var notes      = ImmutableArray.CreateBuilder<Note>();
                                  var properties = ImmutableArray.CreateBuilder<Property>();

                                  if (body != null) {
                                      for (var i = 0; i < body.Count; i++) {
                                          switch (body[i]) {
                                              case Note n:
                                                  notes.Add(n);
                                                  break;
                                              case Property p:
                                                  properties.Add(p);
                                                  break;
                                          }
                                      }
                                  }

                                  return new Field(type, nullable, name, options, notes.ToImmutable(),
                                                   (EquatableArray<Property>)properties.ToImmutable());
                              });

        // ── Pointer ─────────────────────────────────────────────────────

        // pointer = "Index" identifier {identifier} [pointerOptions] ["{" {note} "}"]

        var pointerBody = Between(lbrace, ZeroOrMany(Note), rbrace);

        Pointer = Terms.Text("Index", true)
                       .SkipAnd(OneOrMany(Identifier))
                       .And(ZeroOrOne(PointerOptions))
                       .And(ZeroOrOne(pointerBody))
                       .Then(parts => {
                            var spans   = parts.Item1;
                            var options = parts.Item2;
                            var body    = parts.Item3;

                            var columns = ImmutableArray.CreateBuilder<string>(spans.Count);
                            for (var i = 0; i < spans.Count; i++) {
                                columns.Add(spans[i].ToString());
                            }

                            var notes = ImmutableArray.CreateBuilder<Note>();
                            if (body != null) {
                                for (var i = 0; i < body.Count; i++) {
                                    notes.Add(body[i]);
                                }
                            }

                            return new Pointer(columns.ToImmutable(), options,
                                               (EquatableArray<Note>)notes.ToImmutable());
                        });

        // ── ViewField (recursive) ───────────────────────────────────────

        // view field = qualifiedName ["?"] [identifier] [viewOptions]
        //              ["{" {note | viewField} "}"] ["=" expression]

        var viewField = Deferred<ViewField>();

        var noteOrViewField = Note.Then<object>(n => n).Or(viewField.Then<object>(vf => vf));

        var viewFieldBody = Between(lbrace, ZeroOrMany(noteOrViewField), rbrace);

        var assignment = equals.SkipAnd(expression);

        // Nullable typed: qualifiedName ? identifier [viewOptions] [body] [= expr]
        // The '?' unambiguously marks the first qualifiedName as a type.
        var nullable = QualifiedName.And(question)
                                    .And(Identifier)
                                    .And(ZeroOrOne(ViewOptions))
                                    .And(ZeroOrOne(viewFieldBody))
                                    .And(ZeroOrOne(assignment))
                                    .Then<ViewField>(parts => {
                                         var type       = parts.Item1;
                                         var name       = parts.Item3.ToString();
                                         var options    = parts.Item4;
                                         var body       = parts.Item5;
                                         var assignment = parts.Item6;

                                         return BuildViewField(type, true, name, options, body, assignment);
                                     });

        // Typed with continuation: qualifiedName identifier (options | body | assignment)
        // The continuation token ([, {, =) confirms the second identifier is the name
        // of the same field, not the start of a new field.
        var typed = QualifiedName.And(Identifier)
                                 .And(ZeroOrOne(ViewOptions))
                                 .And(ZeroOrOne(viewFieldBody))
                                 .And(ZeroOrOne(assignment))
                                 .Then<ViewField>(parts => {
                                      var type       = parts.Item1;
                                      var name       = parts.Item2.ToString();
                                      var options    = parts.Item3;
                                      var body       = parts.Item4;
                                      var assignment = parts.Item5;

                                      return BuildViewField(type, false, name, options, body, assignment);
                                  });

        // Untyped: qualifiedName [viewOptions] [body] [= expr]
        // (name only, no type specifier)
        var untyped = QualifiedName.And(ZeroOrOne(ViewOptions))
                                   .And(ZeroOrOne(viewFieldBody))
                                   .And(ZeroOrOne(assignment))
                                   .Then<ViewField>(parts => {
                                        var name       = parts.Item1;
                                        var options    = parts.Item2;
                                        var body       = parts.Item3;
                                        var assignment = parts.Item4;

                                        return BuildViewField(null, false, name, options, body, assignment);
                                    });

        // Body context (conservative): nullable -> typed -> untyped.
        // The typed variant checks that after consuming qualifiedName + identifier,
        // the scanner is NOT at another identifier character. This prevents
        // "id name" (two fields) from being consumed as one typed field.
        var bodyTyped = QualifiedName.And(Identifier)
                                     .When((ctx, _) => {
                                          // After consuming qualifiedName + identifier, peek at the scanner.
                                          // If the next non-whitespace char starts an identifier (letter/underscore),
                                          // then the second "identifier" is actually the start of the next field,
                                          // so this typed match is invalid.
                                          var scanner = ctx.Scanner;
                                          var cursor  = scanner.Cursor;
                                          var saved   = cursor.Position;

                                          // Skip whitespace to find next meaningful character
                                          while (!cursor.Eof && char.IsWhiteSpace(cursor.Current)) {
                                              cursor.Advance();
                                          }

                                          bool result;
                                          if (cursor.Eof) {
                                              result = true; // end of input, typed is valid
                                          } else {
                                              var c = cursor.Current;
                                              // Valid typed-field continuation chars: [, {, =
                                              // These unambiguously confirm the second identifier is the
                                              // field name, not the start of the next field.
                                              result = c == '[' || c == '{' || c == '=';
                                          }

                                          // Restore cursor position (we were just peeking)
                                          cursor.ResetPosition(saved);
                                          return result;
                                      })
                                     .And(ZeroOrOne(ViewOptions))
                                     .And(ZeroOrOne(viewFieldBody))
                                     .And(ZeroOrOne(assignment))
                                     .Then<ViewField>(parts => {
                                          var pair       = parts.Item1;
                                          var type       = pair.Item1;
                                          var name       = pair.Item2.ToString();
                                          var options    = parts.Item2;
                                          var body       = parts.Item3;
                                          var assignment = parts.Item4;

                                          return BuildViewField(type, false, name, options, body, assignment);
                                      });

        // In body context: nullable, then typed (validated), then untyped.
        viewField.Parser = nullable.Or(bodyTyped).Or(untyped);

        // Standalone context (greedy): nullable -> typed -> untyped.
        // Used when parsing a single ViewField from a string.
        ViewField = nullable.Or(typed).Or(untyped);

        // ── View ────────────────────────────────────────────────────────

        // view = "Object" identifier "{" {note | viewField} "}"

        var viewBody = Between(lbrace, ZeroOrMany(noteOrViewField), rbrace);

        View = Terms.Text("Object", true)
                    .SkipAnd(Identifier)
                    .And(viewBody)
                    .Then(parts => {
                         var name = parts.Item1.ToString();
                         var body = parts.Item2;

                         var notes  = ImmutableArray.CreateBuilder<Note>();
                         var fields = ImmutableArray.CreateBuilder<ViewField>();

                         if (body != null) {
                             for (var i = 0; i < body.Count; i++) {
                                 switch (body[i]) {
                                     case Note n:
                                         notes.Add(n);
                                         break;
                                     case ViewField vf:
                                         fields.Add(vf);
                                         break;
                                 }
                             }
                         }

                         return new View(name, notes.ToImmutable(), (EquatableArray<ViewField>)fields.ToImmutable());
                     });

        // ── EnumValue ────────────────────────────────────────────────────

        // enum value = identifier ["=" literal] ["{" {note} "}"]

        var enumValueBody = Between(lbrace, ZeroOrMany(Note), rbrace);

        EnumValue = Identifier.And(ZeroOrOne(equals.SkipAnd(Literal)))
                              .And(ZeroOrOne(enumValueBody))
                              .Then(parts => {
                                   var name       = parts.Item1.ToString();
                                   var assignment = parts.Item2;
                                   var body       = parts.Item3;

                                   var notes = ImmutableArray.CreateBuilder<Note>();
                                   if (body != null) {
                                       for (var i = 0; i < body.Count; i++) {
                                           notes.Add(body[i]);
                                       }
                                   }

                                   return new EnumValue(name, assignment, notes.ToImmutable());
                               });

        // ── Enumeration ──────────────────────────────────────────────────

        // enumeration = "Enum" identifier "{" {[","] (note | enumValue)} "}"

        var noteOrEnumValue = Note.Then<object>(n => n).Or(EnumValue.Then<object>(v => v));

        var enumBody = Between(lbrace, ZeroOrMany(ZeroOrOne(comma).SkipAnd(noteOrEnumValue)), rbrace);

        Enumeration = Terms.Text("Enum", true)
                           .SkipAnd(Identifier)
                           .And(enumBody)
                           .Then(parts => {
                                var name = parts.Item1.ToString();
                                var body = parts.Item2;

                                var notes  = ImmutableArray.CreateBuilder<Note>();
                                var values = ImmutableArray.CreateBuilder<EnumValue>();

                                if (body != null) {
                                    for (var i = 0; i < body.Count; i++) {
                                        switch (body[i]) {
                                            case Note n:
                                                notes.Add(n);
                                                break;
                                            case EnumValue v:
                                                values.Add(v);
                                                break;
                                        }
                                    }
                                }

                                return new Enumeration(name, notes.ToImmutable(),
                                                       (EquatableArray<EnumValue>)values.ToImmutable());
                            });

        // ── Namespace ────────────────────────────────────────────────────

        Namespace = Terms.Text("Namespace", true).SkipAnd(QualifiedName);

        // ── Name list (for inheritance) ──────────────────────────────────

        var colon = Terms.Char(':');
        var bases = colon.SkipAnd(Separated(comma, QualifiedName));

        // ── Trait ────────────────────────────────────────────────────────

        // trait = "Trait" identifier [":" names] "{" {traitMember} "}"
        // trait member = note | use | field

        var traitMember = Note.Then<object>(n => n).Or(Use.Then<object>(u => u)).Or(Field.Then<object>(f => f));

        var traitBody = Between(lbrace, ZeroOrMany(traitMember), rbrace);

        Trait = Terms.Text("Trait", true)
                     .SkipAnd(Identifier)
                     .And(ZeroOrOne(bases))
                     .And(traitBody)
                     .Then(parts => {
                          var name  = parts.Item1.ToString();
                          var bases = parts.Item2;
                          var body  = parts.Item3;

                          var basesBuilder = ImmutableArray.CreateBuilder<string>();
                          if (bases != null) {
                              for (var i = 0; i < bases.Count; i++) {
                                  basesBuilder.Add(bases[i]);
                              }
                          }

                          var notes  = ImmutableArray.CreateBuilder<Note>();
                          var uses   = ImmutableArray.CreateBuilder<Use>();
                          var fields = ImmutableArray.CreateBuilder<Field>();

                          if (body != null) {
                              for (var i = 0; i < body.Count; i++) {
                                  switch (body[i]) {
                                      case Note n:
                                          notes.Add(n);
                                          break;
                                      case Use u:
                                          uses.Add(u);
                                          break;
                                      case Field f:
                                          fields.Add(f);
                                          break;
                                  }
                              }
                          }

                          return new Trait(name, basesBuilder.ToImmutable(), (EquatableArray<Note>)notes.ToImmutable(),
                                           (EquatableArray<Use>)uses.ToImmutable(),
                                           (EquatableArray<Field>)fields.ToImmutable());
                      });

        // ── Entity ───────────────────────────────────────────────────────

        // entity = "Entity" identifier [":" names] "{" {entityMember} "}"
        // entity member = note | use | enumeration | trait | view | pointer | field

        var entityMember = Note.Then<object>(n => n)
                               .Or(Use.Then<object>(u => u))
                               .Or(Enumeration.Then<object>(e => e))
                               .Or(Trait.Then<object>(t => t))
                               .Or(View.Then<object>(v => v))
                               .Or(Pointer.Then<object>(p => p))
                               .Or(Field.Then<object>(f => f));

        var entityBody = Between(lbrace, ZeroOrMany(entityMember), rbrace);

        Entity = Terms.Text("Entity", true)
                      .SkipAnd(Identifier)
                      .And(ZeroOrOne(bases))
                      .And(entityBody)
                      .Then(parts => {
                           var name  = parts.Item1.ToString();
                           var bases = parts.Item2;
                           var body  = parts.Item3;

                           var basesBuilder = ImmutableArray.CreateBuilder<string>();
                           if (bases != null) {
                               for (var i = 0; i < bases.Count; i++) {
                                   basesBuilder.Add(bases[i]);
                               }
                           }

                           var notes    = ImmutableArray.CreateBuilder<Note>();
                           var uses     = ImmutableArray.CreateBuilder<Use>();
                           var enums    = ImmutableArray.CreateBuilder<Enumeration>();
                           var traits   = ImmutableArray.CreateBuilder<Trait>();
                           var views    = ImmutableArray.CreateBuilder<View>();
                           var pointers = ImmutableArray.CreateBuilder<Pointer>();
                           var fields   = ImmutableArray.CreateBuilder<Field>();

                           if (body != null) {
                               for (var i = 0; i < body.Count; i++) {
                                   switch (body[i]) {
                                       case Note n:
                                           notes.Add(n);
                                           break;
                                       case Use u:
                                           uses.Add(u);
                                           break;
                                       case Enumeration e:
                                           enums.Add(e);
                                           break;
                                       case Trait t:
                                           traits.Add(t);
                                           break;
                                       case View v:
                                           views.Add(v);
                                           break;
                                       case Pointer p:
                                           pointers.Add(p);
                                           break;
                                       case Field f:
                                           fields.Add(f);
                                           break;
                                   }
                               }
                           }

                           return new Entity(name, basesBuilder.ToImmutable(),
                                             (EquatableArray<Note>)notes.ToImmutable(),
                                             (EquatableArray<Use>)uses.ToImmutable(),
                                             (EquatableArray<Enumeration>)enums.ToImmutable(),
                                             (EquatableArray<Trait>)traits.ToImmutable(),
                                             (EquatableArray<View>)views.ToImmutable(),
                                             (EquatableArray<Pointer>)pointers.ToImmutable(),
                                             (EquatableArray<Field>)fields.ToImmutable());
                       })
                      .WithComments(comments => {
                           comments.WithWhiteSpaceOrNewLine();
                           comments.WithSingleLine("//");
                           comments.WithMultiLine("/*", "*/");
                       });

        // ── Document (root) ──────────────────────────────────────────────

        // model = [namespace] {entity | trait | enumeration}

        var declaration = Entity.Then<object>(e => e)
                                .Or(Trait.Then<object>(t => t))
                                .Or(Enumeration.Then<object>(e => e));

        Document = ZeroOrOne(Namespace)
                  .And(ZeroOrMany(declaration))
                  .Then(parts => {
                       var ns           = parts.Item1;
                       var declarations = parts.Item2;

                       var entities = ImmutableArray.CreateBuilder<Entity>();
                       var traits   = ImmutableArray.CreateBuilder<Trait>();
                       var enums    = ImmutableArray.CreateBuilder<Enumeration>();

                       if (declarations != null) {
                           for (var i = 0; i < declarations.Count; i++) {
                               switch (declarations[i]) {
                                   case Entity e:
                                       entities.Add(e);
                                       break;
                                   case Trait t:
                                       traits.Add(t);
                                       break;
                                   case Enumeration e:
                                       enums.Add(e);
                                       break;
                               }
                           }
                       }

                       return new Document(ns, entities.ToImmutable(), (EquatableArray<Trait>)traits.ToImmutable(),
                                           (EquatableArray<Enumeration>)enums.ToImmutable());
                   })
                  .WithComments(comments => {
                       comments.WithWhiteSpaceOrNewLine();
                       comments.WithSingleLine("//");
                       comments.WithMultiLine("/*", "*/");
                   })
                  .Compile();
    }

    // ── Helper methods ──────────────────────────────────────────────────

    private static ViewField BuildViewField(
        string?                    type,
        bool                       nullable,
        string                     name,
        EquatableArray<ViewOption> options,
        IReadOnlyList<object>?     body,
        IExpression?               assignment
    ) {
        var notes    = ImmutableArray.CreateBuilder<Note>();
        var children = ImmutableArray.CreateBuilder<ViewField>();

        if (body != null) {
            for (var i = 0; i < body.Count; i++) {
                switch (body[i]) {
                    case Note n:
                        notes.Add(n);
                        break;
                    case ViewField vf:
                        children.Add(vf);
                        break;
                }
            }
        }

        return new(type, nullable, name, options, (EquatableArray<Note>)notes.ToImmutable(),
                   (EquatableArray<ViewField>)children.ToImmutable(), assignment);
    }

    private static string NormalizeOption(string input) {
        // Lowercase the entire input, split on spaces/underscores, capitalize each word.
        // "primary key" → "primarykey", "PrimaryKey" → "primarykey",
        // "b tree" → "btree", "BTree" → "btree", "not null" → "notnull"
        return input.ToLowerInvariant().Replace(" ", "").Replace("_", "");
    }

    private static FieldOption ParseFieldOption(string normalized) {
        return normalized switch {
            "required" or "notnull" => FieldOption.Required,
            "unique"                => FieldOption.Unique,
            "primarykey"            => FieldOption.PrimaryKey,
            "autoincrement"         => FieldOption.AutoIncrement,
            "btree"                 => FieldOption.BTree,
            "hash"                  => FieldOption.Hash,
            var _                   => throw new InvalidOperationException($"Unknown field option: {normalized}"),
        };
    }

    private static ViewOption ParseViewOption(string normalized) {
        return normalized switch {
            "omit"    => ViewOption.Omit,
            "omitall" => ViewOption.OmitAll,
            var _     => throw new InvalidOperationException($"Unknown view option: {normalized}"),
        };
    }

    private static PointerOption ParsePointerOption(string normalized) {
        return normalized switch {
            "unique" => PointerOption.Unique,
            "btree"  => PointerOption.BTree,
            "hash"   => PointerOption.Hash,
            var _    => throw new InvalidOperationException($"Unknown pointer option: {normalized}"),
        };
    }
}
