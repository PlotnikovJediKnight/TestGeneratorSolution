using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Symbols;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TestClassGeneratorProject
{
    public class TestClassGenerator
    {
        private CompilationUnitSyntax root;

        private UsingDirectiveSyntax GetUsingDeclaration(string namespaceFullName)
        {
            NameSyntax name = SyntaxFactory.IdentifierName(namespaceFullName);
            UsingDirectiveSyntax usingSystem = SyntaxFactory.UsingDirective(name);
            return usingSystem;
        }
        
        private NamespaceDeclarationSyntax CreatePerFileSyntaxRootAndGetTestNamespace()
        {
            CompilationUnitSyntax root = SyntaxFactory.CompilationUnit();

            root = root.AddUsings(GetUsingDeclaration("System.Generics"));
            root = root.AddUsings(GetUsingDeclaration("NUnit.Framework"));
            root = root.AddUsings(GetUsingDeclaration("Moq"));

            root = root.AddMembers(SyntaxFactory.NamespaceDeclaration(SyntaxFactory.IdentifierName("GeneratedTestClasses")));

            return (NamespaceDeclarationSyntax)root.Members.ElementAt(0);
        }

        private void SetTreeRoot(FileWithContent cSharpProgram)
        {
            string programText = cSharpProgram.Content;
            SyntaxTree tree = CSharpSyntaxTree.ParseText(programText);
            root = tree.GetCompilationUnitRoot();
        }

        private ClassDeclarationSyntax[] GetClassSyntaxNodes()
        {
            ClassDeclarationSyntax[] classes = root.DescendantNodes().OfType<ClassDeclarationSyntax>().ToArray();
            int i = 0;
            foreach (var classDecl in classes){
                classes[i] = classDecl.WithIdentifier(SyntaxFactory.Identifier(classDecl.Identifier.ValueText + "Test"));

                var attributes =
                classes[i].AttributeLists.Add(
                    SyntaxFactory.AttributeList(
                        SyntaxFactory.SingletonSeparatedList<AttributeSyntax>(
                            SyntaxFactory.Attribute(
                                SyntaxFactory.IdentifierName("TestFixture")
                ))));

                classes[i] = classes[i].WithAttributeLists(attributes);
                ++i;
            }
            return classes;
        }

        public async Task<FileWithContent[]> GetTestClassFiles(FileWithContent cSharpProgram)
        {
            SetTreeRoot(cSharpProgram);

            ClassDeclarationSyntax[] classes = GetClassSyntaxNodes();
            NamespaceDeclarationSyntax[] roots = new NamespaceDeclarationSyntax[classes.Length];

            FileWithContent[] toReturn = new FileWithContent[classes.Length];

            int i = 0;
            foreach (var classNode in classes)
            {
                roots[i] = CreatePerFileSyntaxRootAndGetTestNamespace();
                CompilationUnitSyntax program = (CompilationUnitSyntax)roots[i].Parent;
                NamespaceDeclarationSyntax newRoot = roots[i].WithMembers(new SyntaxList<MemberDeclarationSyntax>(classes[i]));
                program = program.ReplaceNode(roots[i], newRoot);
                program = program.NormalizeWhitespace();

                toReturn[i] = new FileWithContent("TestFilesOutput\\" + classNode.Identifier + ".cs", program.ToFullString());
                i++;
            }

            return toReturn;
        }

        static void Main() { }
    }
}
