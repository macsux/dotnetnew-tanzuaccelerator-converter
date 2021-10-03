using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using SF = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
    
namespace AcceleratorConverter
{
    public static class ExpressionHelper
    {
        public static string RewriteToSpEL(string input)
        {
            var expression = SF.ParseExpression(input);
            return new SpelRewriter().Visit(expression).ToString();
        }
        public static bool ContainsSymbols(string expression, IEnumerable<string> symbols)
        {
            var analyzer = new KnownSymbolExpressionAnalyzer(symbols.ToHashSet());
            analyzer.Visit(SF.ParseExpression(expression));
            return analyzer.DetectedSymbols.Any();
        }

        public static string Invert(string expr)
        {
            var expression = SF.ParseExpression(expr);
            if(expression is ParenthesizedExpressionSyntax parenthesized && parenthesized.Expression is PrefixUnaryExpressionSyntax)
            {
                expression = parenthesized.Expression;
            }
            if (expression is PrefixUnaryExpressionSyntax prefix && prefix.OperatorToken.Kind() == SyntaxKind.ExclamationToken)
            {
                expression = prefix.Operand;
            }
            else
            {
                expression = SF.PrefixUnaryExpression(SyntaxKind.LogicalNotExpression, SF.ParenthesizedExpression(expression));
            }

            return expression.ToString();
        }
        private class SpelRewriter : CSharpSyntaxRewriter
        {
            public override SyntaxNode VisitIdentifierName(IdentifierNameSyntax node)
            {
                return base.VisitIdentifierName(node.WithIdentifier(SF.Identifier($"#{node.Identifier}")));
            }
        }

        private class KnownSymbolExpressionAnalyzer : CSharpSyntaxRewriter
        {
            public HashSet<string> Symbols { get; }
            public HashSet<string> DetectedSymbols { get; } = new();

            public KnownSymbolExpressionAnalyzer(HashSet<string> symbols)
            {
                Symbols = symbols;
            }

            public override SyntaxNode VisitIdentifierName(IdentifierNameSyntax node)
            {
                var name = node.Identifier.ToString();
                if (Symbols.Contains(name))
                    DetectedSymbols.Add(name);
                return base.VisitIdentifierName(node);
            }


        }
    }
}