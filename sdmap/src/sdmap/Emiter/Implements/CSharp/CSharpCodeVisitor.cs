﻿using sdmap.Emiter.Implements.Common;
using sdmap.Functional;
using sdmap.Parser.G4;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Antlr4.Runtime.Misc;
using Antlr4.Runtime.Tree;
using sdmap.Utils;
using static sdmap.Parser.G4.SdmapParser;

namespace sdmap.Emiter.Implements.CSharp
{
    internal class CSharpCodeVisitor : SdmapParserBaseVisitor<Result>
    {
        private readonly CodeEmiterConfig _config;
        private readonly CSharpDefine _define;
        private readonly IndentWriter _writer;
        private readonly Dictionary<string, Func<IndentWriter, Result>> _unnamedSqls =
            new Dictionary<string, Func<IndentWriter, Result>>();

        internal CSharpCodeVisitor(
            TextWriter writer,
            CodeEmiterConfig config,
            CSharpDefine define)
        {
            _writer = new IndentWriter(writer, 0);
            _config = config;
            _define = define;
        }

        public Result StartVisit([NotNull] RootContext context)
        {
            if (context.ChildCount == 0)
            {
                return Result.Fail("Empty source.");
            }
            return Visit(context);
        }

        public override Result VisitRoot([NotNull] RootContext context)
        {
            foreach (var usingItem in _define.CommonUsings())
            {
                _writer.WriteIndentLine($"using {usingItem};");
            }
            var result = base.VisitRoot(context);
            _writer.Flush();
            return result;
        }

        public override Result VisitNamespace([NotNull] NamespaceContext context)
        {
            _writer.WriteLine();
            _writer.WriteIndentLine(                     // _ namespace {id} <CRLF>
                $"namespace {context.nsSyntax().GetText()}");
            return _writer.UsingIndent("{", "}", () =>
            {
                return base.VisitNamespace(context);
            });
        }

        public override Result VisitNamedSql([NotNull] NamedSqlContext context)
        {
            _writer.WriteLine();
            _writer.WriteIndentLine(
                $"{_config.AccessModifier} class {context.SYNTAX().GetText()}");
            _writer.UsingIndent(() =>
            {                                              // _ internal class {id} <CRLF>
                _writer.WriteIndentLine($": {nameof(ISdmapEmiter)}");
            });                                            // _ _ : IBase
            return _writer.UsingIndent("{", "}", () =>
            {
                return ClassGeneration();
            });


            Result ClassGeneration()
            {
                _writer.WriteIndentLine(      // _ internal res B()
                    $"{_config.AccessModifier} Result<string> BuildText()");
                return _writer.UsingIndent("{", "}", () =>
                {
                    return MethodGeneration();
                });
            }

            Result MethodGeneration()
            {
                _writer.WriteIndentLine("var sb = new StringBuilder();");
                var r = base.VisitNamedSql(context);
                _writer.WriteIndentLine("return Result.Ok(sb.ToString());");
                return r;
            }
        }

        public override Result VisitPlainText([NotNull] PlainTextContext context)
        {
            var sqlText = SqlTextUtil.Parse(context.GetToken(SQLText, 0).GetText());
            var csharpText = SqlTextUtil.ToCSharpString(sqlText);
            _writer.WriteIndentLine($"sb.Append({csharpText});");
            return Result.Ok();
        }

        public override Result VisitMacro([NotNull] MacroContext context)
        {
            return _writer.UsingIndent("{", "}", () =>
            {
                _writer.WriteIndentLine($"var result = {context.SYNTAX()}(");
                _writer.UsingIndent(() =>
                {
                    var parameterCtxs = context.macroParameter();
                    for (var i = 0; i < parameterCtxs.Length; ++i)
                    {
                        var parameter = parameterCtxs[i];

                        if (parameter.nsSyntax() != null)
                        {
                            _writer.WriteIndent(
                                SqlTextUtil.ToCSharpString(parameter.nsSyntax().GetText()));
                        }
                        else if (parameter.STRING() != null)
                        {
                            var result = StringUtil.Parse(parameter.STRING().GetText());
                            if (result.IsFailure) return result;

                            _writer.WriteIndent(SqlTextUtil.ToCSharpString(result.Value));
                        }
                        else if (parameter.NUMBER() != null)
                        {
                            // sdmap number are compatible with C# double
                            _writer.WriteIndent(parameter.NUMBER().GetText());
                        }
                        else if (parameter.DATE() != null)
                        {
                            var result = DateUtil.Parse(parameter.DATE().GetText());
                            if (result.IsFailure) return result;
                            var date = result.Value;
                            _writer.WriteIndent(
                                $"new DateTime({date.Year}, {date.Month}, {date.Day})");
                        }
                        else if (parameter.Bool() != null)
                        {
                            // sdmap bool are compatible with C# bool
                            _writer.WriteIndent(parameter.Bool().GetText());
                        }
                        else if (parameter.unnamedSql() != null)
                        {
                            var parseTree = parameter.unnamedSql();
                            var id = NameUtil.GetFunctionName(parseTree);
                            if (!_unnamedSqls.ContainsKey(id))
                            {
                                _unnamedSqls[id] = (writer) =>
                                {

                                    return Result.Ok();
                                };
                            }
                            _writer.WriteIndent($"{id}()");
                        }

                        // every parameter should follow by a "," separator, 
                        // except last parameter.
                        if (i < parameterCtxs.Length - 1)
                        {
                            _writer.WriteLine(", ");
                        }
                    }
                    _writer.WriteIndentLine(");");
                    return Result.Ok();
                });
                _writer.WriteIndentLine($"if (result.{nameof(Result.IsSuccess)})");
                _writer.UsingIndent("{", "}", () =>
                {
                    _writer.WriteIndentLine(
                        $"sb.Append(result.{nameof(Result<int>.Value)});");
                });
                _writer.WriteIndentLine("else");
                _writer.UsingIndent("{", "}", () =>
                {
                    _writer.WriteIndentLine("return result;");
                });
                return Result.Ok();
            });
        }

        protected override Result AggregateResult(Result aggregate, Result nextResult)
        {
            return Result.Combine(new[]
            {
                aggregate,
                nextResult
            });
        }
    }
}