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

        private BlockSyntax GetSyntaxBlockForNonVoidMethod(ClassDeclarationSyntax classSynt, MethodDeclarationSyntax method)
        {

            SyntaxTree tree = CSharpSyntaxTree.ParseText(method.ToFullString());

            ///////////////////////////////////////////////////////////////////////////////////////////////////////
            var assemblyPath = Path.ChangeExtension(Path.GetTempFileName(), "exe");

            var compilation = CSharpCompilation.Create(Path.GetFileName(assemblyPath))
                .WithOptions(new CSharpCompilationOptions(OutputKind.ConsoleApplication))
                .AddReferences(MetadataReference.CreateFromFile(typeof(object).Assembly.Location))
                .AddSyntaxTrees(tree);

            SemanticModel sm = compilation.GetSemanticModel(tree);

            //UsingDirectiveSyntax usingSystem = root.Usings[0];
            //NameSyntax systemName = usingSystem.Name;
            //SymbolInfo nameInfo = sm.GetSymbolInfo(systemName);
            ///////////////////////////////////////////////////////////////////////////////////////////////////////

            MethodDeclarationSyntax newMethod = (MethodDeclarationSyntax)tree.GetCompilationUnitRoot().Members.ElementAt(0);

            List<Tuple<string, string>> paramList = new List<Tuple<string, string>>();
            foreach (var parameter in newMethod.ParameterList.Parameters)
            {
                IParameterSymbol nameInfo = sm.GetDeclaredSymbol(parameter);
                ///////////////////////////////////////////////////
                string paramType = parameter.Type.ToFullString();
                string paramName = parameter.Identifier.ValueText;
                paramList.Add(new Tuple<string, string>(paramType, paramName));
            }

            StringBuilder defaultInitialization = new StringBuilder();
            foreach (var param in paramList)
            {
                string paramType = param.Item1;
                string paramName = param.Item2;
                Type obj = Type.GetType(paramType);
                defaultInitialization.Append(
                    paramType + " " + 
                    paramName + " = " + 
                    Activator.CreateInstance(Type.GetType(paramType)).ToString() +
                    "\n");
            }

            return SyntaxFactory.Block(
                                    SyntaxFactory.ParseStatement(defaultInitialization.ToString())
                            );
        }

        private SyntaxList<MemberDeclarationSyntax> GetFormattedMethods(ClassDeclarationSyntax classSynt)
        {
            int i = 0;
            MemberDeclarationSyntax[] methods = classSynt.Members.ToArray();
            foreach (var method in methods)
            {
                if (method.IsKind(SyntaxKind.MethodDeclaration)){
                    var attributes =
                    method.AttributeLists.Add(
                         SyntaxFactory.AttributeList(
                             SyntaxFactory.SingletonSeparatedList<AttributeSyntax>(
                                 SyntaxFactory.Attribute(
                                     SyntaxFactory.IdentifierName("Test")
                     ))));

                    methods[i] = methods[i].WithAttributeLists(attributes);
                    MethodDeclarationSyntax castMethod = (MethodDeclarationSyntax)methods[i];

                    methods[i] = castMethod.WithIdentifier(SyntaxFactory.Identifier(castMethod.Identifier.ValueText + "Test"));

                    castMethod = (MethodDeclarationSyntax)methods[i];

                    if (castMethod.ReturnType.ToString().Equals("void"))
                    {
                        methods[i] = castMethod.WithBody(
                                SyntaxFactory.Block(
                                    SyntaxFactory.ParseStatement("Assert.Fail(\"autogenerated\");")
                            )
                       );
                    } else {
                        GetSyntaxBlockForNonVoidMethod(classSynt, castMethod);
                        //methods[i] = castMethod.WithBody(GetSyntaxBlockForNonVoidMethod(classSynt, castMethod));
                    }
                    
                }

                i++;
            }

            return new SyntaxList<MemberDeclarationSyntax>(methods.ToList());
        }

        private MemberDeclarationSyntax GetInnerClassObject(string testClassName, string testObjectName)
        {
            string parseExpression = "private " + testClassName + " " + testObjectName + ";";
            return SyntaxFactory.ParseMemberDeclaration(parseExpression);
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
                string testObjectName = "_" + classDecl.Identifier.ValueText.ToLower();
                string testClassName = classDecl.Identifier.ValueText;

                classes[i] = classDecl.WithIdentifier(SyntaxFactory.Identifier(testClassName + "Test"));

                var attributes =
                classes[i].AttributeLists.Add(
                    SyntaxFactory.AttributeList(
                        SyntaxFactory.SingletonSeparatedList<AttributeSyntax>(
                            SyntaxFactory.Attribute(
                                SyntaxFactory.IdentifierName("TestFixture")
                ))));

                classes[i] = classes[i].WithAttributeLists(attributes);
                classes[i] = classes[i].AddMembers(new MemberDeclarationSyntax[1] { GetInnerClassObject(testClassName, testObjectName) });
                classes[i] = classes[i].WithMembers(GetFormattedMethods(classes[i]));

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
