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

        private void SetTreeRoot(FileWithContent cSharpProgram)
        {
            string programText = cSharpProgram.Content;
            SyntaxTree tree = CSharpSyntaxTree.ParseText(programText);
            root = tree.GetCompilationUnitRoot();
        }

        private ClassDeclarationSyntax[] GetClassSyntaxNodes()
        {
            return root.DescendantNodes().OfType<ClassDeclarationSyntax>().ToArray();
        }

        public async Task<FileWithContent[]> GetTestClassFiles(FileWithContent cSharpProgram)
        {
            SetTreeRoot(cSharpProgram);

            ClassDeclarationSyntax[] classes = GetClassSyntaxNodes();

            foreach (var classNode in classes)
            {
                Console.WriteLine($"The tree is a {classNode.Kind()} node.");
                Console.WriteLine($"The tree has {classNode.Members.Count} elements in it.");
            }

            return new FileWithContent[1] { new FileWithContent(cSharpProgram.Path + ".processed", cSharpProgram.Content.ToUpper()) };
        }

        static void Main() { }
    }
}
