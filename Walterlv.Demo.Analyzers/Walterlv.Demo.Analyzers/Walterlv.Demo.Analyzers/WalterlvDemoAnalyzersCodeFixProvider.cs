using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Rename;
using Microsoft.CodeAnalysis.Text;

namespace Walterlv.Demo.Analyzers
{
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(WalterlvDemoAnalyzersCodeFixProvider)), Shared]
    public class WalterlvDemoAnalyzersCodeFixProvider : CodeFixProvider
    {
        private const string _title = "ת��Ϊ��֪ͨ����";

        public sealed override ImmutableArray<string> FixableDiagnosticIds =>
            ImmutableArray.Create(WalterlvDemoAnalyzersAnalyzer.DiagnosticId);

        public sealed override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

        public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
            var diagnostic = context.Diagnostics.First();
            var declaration = (PropertyDeclarationSyntax)root.FindNode(diagnostic.Location.SourceSpan);

            context.RegisterCodeFix(
                CodeAction.Create(
                    title: _title,
                    createChangedSolution: ct => ConvertToNotificationProperty(context.Document, declaration, ct),
                    equivalenceKey: _title),
                diagnostic);
        }

        private async Task<Solution> ConvertToNotificationProperty(Document document,
            PropertyDeclarationSyntax propertyDeclarationSyntax, CancellationToken cancellationToken)
        {
            // ��ȡ�ĵ����﷨�ڵ㡣
            var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);

            // ���ɿ�֪ͨ���Ե��﷨�ڵ㼯�ϡ�
            var type = propertyDeclarationSyntax.Type;
            var propertyName = propertyDeclarationSyntax.Identifier.ValueText;
            var fieldName = $"_{char.ToLower(propertyName[0])}{propertyName.Substring(1)}";
            var newNodes = CreateNotificationProperty(type, propertyName, fieldName);

            // ����֪ͨ���Ե��﷨�ڵ���뵽ԭ�ĵ����γ�һ���м��ĵ���
            var intermediateRoot = root
                .InsertNodesAfter(
                    root.FindNode(propertyDeclarationSyntax.Span),
                    newNodes);

            // ���м��ĵ��е��Զ������Ƴ��γ�һ�������ĵ���
            var newRoot = intermediateRoot
                .RemoveNode(intermediateRoot.FindNode(propertyDeclarationSyntax.Span), SyntaxRemoveOptions.KeepNoTrivia);

            // ��ԭ����������еĴ˷��ĵ��������ĵ����γ��µĽ��������
            return document.Project.Solution.WithDocumentSyntaxRoot(document.Id, newRoot);
        }

        private SyntaxNode[] CreateNotificationProperty(TypeSyntax type, string propertyName, string fieldName)
            => new SyntaxNode[]
            {
                SyntaxFactory.FieldDeclaration(
                    new SyntaxList<AttributeListSyntax>(),
                    new SyntaxTokenList(SyntaxFactory.Token(SyntaxKind.PrivateKeyword)),
                    SyntaxFactory.VariableDeclaration(
                        type,
                        SyntaxFactory.SeparatedList(new []
                        {
                            SyntaxFactory.VariableDeclarator(
                                SyntaxFactory.Identifier(fieldName)
                            )
                        })
                    ),
                    SyntaxFactory.Token(SyntaxKind.SemicolonToken)
                ),
                SyntaxFactory.PropertyDeclaration(
                    type,
                    SyntaxFactory.Identifier(propertyName)
                )
                .AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword))
                .AddAccessorListAccessors(
                    SyntaxFactory.AccessorDeclaration(
                        SyntaxKind.GetAccessorDeclaration
                    )
                    .WithExpressionBody(
                        SyntaxFactory.ArrowExpressionClause(
                            SyntaxFactory.Token(SyntaxKind.EqualsGreaterThanToken),
                            SyntaxFactory.IdentifierName(fieldName)
                        )
                    )
                    .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)),
                    SyntaxFactory.AccessorDeclaration(
                        SyntaxKind.SetAccessorDeclaration
                    )
                    .WithExpressionBody(
                        SyntaxFactory.ArrowExpressionClause(
                            SyntaxFactory.Token(SyntaxKind.EqualsGreaterThanToken),
                            SyntaxFactory.InvocationExpression(
                                SyntaxFactory.IdentifierName("SetValue"),
                                SyntaxFactory.ArgumentList(
                                    SyntaxFactory.Token(SyntaxKind.OpenParenToken),
                                    SyntaxFactory.SeparatedList(new []
                                    {
                                        SyntaxFactory.Argument(
                                            SyntaxFactory.IdentifierName(fieldName)
                                        )
                                        .WithRefKindKeyword(
                                            SyntaxFactory.Token(SyntaxKind.RefKeyword)
                                        ),
                                        SyntaxFactory.Argument(
                                            SyntaxFactory.IdentifierName("value")
                                        ),
                                    }),
                                    SyntaxFactory.Token(SyntaxKind.CloseParenToken)
                                )
                            )
                        )
                    )
                    .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken))
                ),
            };
    }
}
