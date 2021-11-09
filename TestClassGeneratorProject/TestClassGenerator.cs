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
        private Boolean ConstructorWithInterfaceDependencyFound { get; set; } = false;
        private List<Tuple<string, string, string>> constructorParameters = null;

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

        private SemanticModel GetSemanticModelForMethod(SyntaxTree tree, MethodDeclarationSyntax method)
        {
            var assemblyPath = Path.ChangeExtension(Path.GetTempFileName(), "exe");

            var compilation = CSharpCompilation.Create(Path.GetFileName(assemblyPath))
                .WithOptions(new CSharpCompilationOptions(OutputKind.ConsoleApplication))
                .AddReferences(MetadataReference.CreateFromFile(typeof(object).Assembly.Location))
                .AddSyntaxTrees(tree);
            return compilation.GetSemanticModel(tree);
        }

        private SemanticModel GetSemanticModelForConstructor(SyntaxTree tree, ConstructorDeclarationSyntax constructor)
        {
            var assemblyPath = Path.ChangeExtension(Path.GetTempFileName(), "exe");

            var compilation = CSharpCompilation.Create(Path.GetFileName(assemblyPath))
                .WithOptions(new CSharpCompilationOptions(OutputKind.ConsoleApplication))
                .AddReferences(MetadataReference.CreateFromFile(typeof(object).Assembly.Location))
                .AddSyntaxTrees(tree);
            return compilation.GetSemanticModel(tree);
        }

        private MethodDeclarationSyntax GetNewMethodDeclarationSyntax(SyntaxTree tree)
        {
            return (MethodDeclarationSyntax)tree.GetCompilationUnitRoot().Members.ElementAt(0);
        }

        private ConstructorDeclarationSyntax GetNewConstructorDeclarationSyntax(SyntaxTree tree)
        {
            return (ConstructorDeclarationSyntax)tree.GetCompilationUnitRoot().Members.ElementAt(0);
        }

        private BlockSyntax GetSyntaxBlockForNonVoidMethod(string testObjectName, string methodName, MethodDeclarationSyntax method)
        {
            var statements = new List<StatementSyntax>();

            SyntaxTree tree = CSharpSyntaxTree.ParseText(method.ToFullString());
            SemanticModel sm = GetSemanticModelForMethod(tree, method);
            MethodDeclarationSyntax newMethod = GetNewMethodDeclarationSyntax(tree);

            #region ArrangeSection
            //====================Arrange Section==============================//
            List<Tuple<string, string, string>> paramList = new List<Tuple<string, string, string>>();
            foreach (var parameter in newMethod.ParameterList.Parameters)
            {
                IParameterSymbol parameterInfo = sm.GetDeclaredSymbol(parameter);
                string paramType = parameter.Type.ToString().Trim();
                string paramName = parameter.Identifier.ValueText.Trim();
                string paramDefValue = GetDefaultValueLiteral(parameterInfo.Type);
                paramList.Add(new Tuple<string, string, string>(paramType, paramName, paramDefValue));
            }

            
            foreach (var param in paramList)
            {
                string paramType = param.Item1;
                string paramName = param.Item2;
                string paramDefValue = param.Item3;
                string wholeStatement =
                    paramType + " " + 
                    paramName + " = " + 
                    paramDefValue + ";";
                statements.Add(SyntaxFactory.ParseStatement(wholeStatement));
            }
            //====================Arrange Section==============================//
            #endregion

            if (statements.Count != 0)
            {
                #region ActSection
                //====================Act Section==============================//

                StringBuilder sb = new StringBuilder();
                for(int i = 0; i < paramList.Count; ++i)
                {
                    string paramName = paramList[i].Item2;
                    sb.Append(paramName);
                    if (i != paramList.Count - 1)
                        sb.Append(", ");
                }

                string wholeStatement =
                    method.ReturnType.ToString() +
                    " actual = " +
                    testObjectName +
                    "." + methodName +
                    "(" + sb.ToString() + ");";

                statements.Add(SyntaxFactory.ParseStatement(wholeStatement));

                //====================Act Section==============================//
                #endregion

                #region AssertSection
                //====================Assert Section==============================//
                TypeInfo returnTypeInfo = sm.GetTypeInfo(newMethod.ReturnType);
                string expectedStatement = method.ReturnType.ToString() + " expected = " + GetDefaultValueLiteral(returnTypeInfo.Type) + ";";
                statements.Add(SyntaxFactory.ParseStatement(expectedStatement));
                statements.Add(SyntaxFactory.ParseStatement("Assert.That(actual, Is.EqualTo(expected));"));
                statements.Add(SyntaxFactory.ParseStatement("Assert.Fail(\"autogenerated\");"));
                //====================Assert Section==============================//
                #endregion
                return SyntaxFactory.Block(statements);
            }
            else
                return SyntaxFactory.Block(
                                    SyntaxFactory.ParseStatement("Assert.Fail(\"autogenerated\");")
                            );
        }

        private MemberDeclarationSyntax[] GetConstructorArguments()
        {
            MemberDeclarationSyntax[] arguments = new MemberDeclarationSyntax[constructorParameters.Count];

            for (int i = 0; i < arguments.Length; ++i)
            {
                string typeName = constructorParameters[i].Item1;
                string fieldName = constructorParameters[i].Item2;
                if (typeName[0] == 'I')
                {
                    typeName = "Mock<" + typeName + ">";
                }
                string parseExpression = "private " + typeName + " " + fieldName + ";";
                arguments[i] = SyntaxFactory.ParseMemberDeclaration(parseExpression);
            }

            return arguments;
        }

        private BlockSyntax GetSyntaxBlockForSetUpMethod(string testObjectName, string testClassName, ConstructorDeclarationSyntax constructor)
        {
            var statements = new List<StatementSyntax>();

            SyntaxTree tree = CSharpSyntaxTree.ParseText(constructor.ToFullString());
            SemanticModel sm = GetSemanticModelForConstructor(tree, constructor);
            ConstructorDeclarationSyntax newMethod = GetNewConstructorDeclarationSyntax(tree);

            List<Tuple<string, string, string>> paramList = new List<Tuple<string, string, string>>();
            foreach (var parameter in newMethod.ParameterList.Parameters)
            {
                IParameterSymbol parameterInfo = sm.GetDeclaredSymbol(parameter);
                string paramType = parameter.Type.ToString().Trim();
                string paramName = "_" + parameter.Identifier.ValueText.Trim().ToLower();
                string paramDefValue = GetDefaultValueLiteral(parameterInfo.Type, paramType, false);
                paramList.Add(new Tuple<string, string, string>(paramType, paramName, paramDefValue));
            }

            foreach (var param in paramList)
            {
                string paramName = param.Item2;
                string paramDefValue = param.Item3;
                string wholeStatement =
                    paramName + " = " +
                    paramDefValue + ";";
                statements.Add(SyntaxFactory.ParseStatement(wholeStatement));
            }

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < paramList.Count; ++i)
            {
                string paramName = paramList[i].Item2;
                string paramType = paramList[i].Item1;
                if (paramType[0] == 'I')
                {
                    paramName += ".Object";
                }
                sb.Append(paramName);
                if (i != paramList.Count - 1)
                    sb.Append(", ");
            }

            string statement = testObjectName + " = new " + testClassName + "(" + sb.ToString() + ");";
            statements.Add(SyntaxFactory.ParseStatement(statement));

            //!!!!!
            constructorParameters = paramList;
            //!!!!!

            return SyntaxFactory.Block(statements);
        }

        private MethodDeclarationSyntax GetSetUpMethod(string testObjectName, string testClassName, ConstructorDeclarationSyntax constructor)
        {
            MethodDeclarationSyntax setup = SyntaxFactory.MethodDeclaration(SyntaxFactory.ParseTypeName("void "), "SetUp");
            var attributes =
                    setup.AttributeLists.Add(
                         SyntaxFactory.AttributeList(
                             SyntaxFactory.SingletonSeparatedList<AttributeSyntax>(
                                 SyntaxFactory.Attribute(
                                     SyntaxFactory.IdentifierName("SetUp")
                     ))));

            setup = setup.WithAttributeLists(attributes);
            setup = setup.WithBody(GetSyntaxBlockForSetUpMethod(testObjectName, testClassName, constructor));
            return setup;
        }

        private SyntaxList<MemberDeclarationSyntax> GetFormattedMethods(string testObjectName, string testClassName, ClassDeclarationSyntax classSynt)
        {
            int i = 0;
            MemberDeclarationSyntax[] methods = classSynt.Members.ToArray();
            foreach (var method in methods)
            {
                if (method.IsKind(SyntaxKind.MethodDeclaration)) {
                    var attributes =
                    method.AttributeLists.Add(
                         SyntaxFactory.AttributeList(
                             SyntaxFactory.SingletonSeparatedList<AttributeSyntax>(
                                 SyntaxFactory.Attribute(
                                     SyntaxFactory.IdentifierName("Test")
                     ))));

                    methods[i] = methods[i].WithAttributeLists(attributes);
                    MethodDeclarationSyntax castMethod = (MethodDeclarationSyntax)methods[i];

                    if (!castMethod.Modifiers[0].ValueText.Contains("public")) { methods[i++] = null; continue; }

                    string methodName = castMethod.Identifier.ValueText;
                    methods[i] = castMethod.WithIdentifier(SyntaxFactory.Identifier(methodName + "Test"));

                    castMethod = (MethodDeclarationSyntax)methods[i];

                    if (castMethod.ReturnType.ToString().Equals("void"))
                    {
                        methods[i] = castMethod.WithBody(
                                SyntaxFactory.Block(
                                    SyntaxFactory.ParseStatement("Assert.Fail(\"autogenerated\");")
                            )
                       );
                    } else {
                        methods[i] = castMethod.WithBody(GetSyntaxBlockForNonVoidMethod(testObjectName, methodName, castMethod));
                        castMethod = (MethodDeclarationSyntax)methods[i];
                        methods[i] = castMethod.WithReturnType(SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.VoidKeyword)));
                    }

                    castMethod = (MethodDeclarationSyntax)methods[i];
                    methods[i] = castMethod.WithParameterList(
                                            SyntaxFactory.ParseParameterList("()"));
                }
                else
                if ((method.IsKind(SyntaxKind.FieldDeclaration) || method.IsKind(SyntaxKind.PropertyDeclaration)) &&
                      (!method.GetText().ToString().Contains(testObjectName)))
                {
                    methods[i] = null;
                }
                else
                if (method.IsKind(SyntaxKind.ConstructorDeclaration))
                {
                    if (!ConstructorWithInterfaceDependencyFound)
                    {
                        ConstructorDeclarationSyntax constr = (ConstructorDeclarationSyntax)method;

                        if (!constr.Modifiers[0].ValueText.Contains("public")) { methods[i++] = null; continue; }

                        ParameterListSyntax paramList = constr.ParameterList;
                        foreach (var param in paramList.Parameters)
                        {
                            if (param.Type.ToFullString()[0] == 'I')
                            {
                                ConstructorWithInterfaceDependencyFound = true;
                                methods[i] = GetSetUpMethod(testObjectName, testClassName, constr);
                                break;
                            }
                        }

                        if (!ConstructorWithInterfaceDependencyFound)
                        {
                            methods[i] = null;
                        }
                    } else
                    {
                        methods[i] = null;
                    }
                }

                i++;
            }
            List<MemberDeclarationSyntax> returnList = methods.ToList();
            returnList.RemoveAll(item => item == null);
            return new SyntaxList<MemberDeclarationSyntax>(returnList);
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

                //!!!!!!!!!
                ConstructorWithInterfaceDependencyFound = false;
                constructorParameters = null;
                //!!!!!!!!!

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
                classes[i] = classes[i].WithMembers(GetFormattedMethods(testObjectName, testClassName, classes[i]));
                if (ConstructorWithInterfaceDependencyFound)
                {
                    classes[i] = classes[i].AddMembers(GetConstructorArguments());
                }


                ++i;
            }
            return classes;
        }

        public async Task<FileWithContent[]> GetTestClassFiles(FileWithContent cSharpProgram)
        {
            SetTreeRoot(cSharpProgram);
            ConstructorWithInterfaceDependencyFound = false;
            constructorParameters = null;

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

















        //i'm so sorry
        private string GetDefaultValueLiteral(ITypeSymbol type, string typeName = null, bool ignore = true)
        {
            if (!ignore && type.Name[0] == 'I' && typeName[0] == 'I')
            {
                return "new Mock<" + type.Name +">()";
            }
               
            if (type.SpecialType == SpecialType.System_String)
            {
                return "\"\"";
            }
            if (type.IsReferenceType) return "null";

            SpecialType specType = type.SpecialType;
            switch (specType)
            {
               
                case SpecialType.System_Enum:
                    return "(System.Enum)null";
                case SpecialType.System_ValueType:
                    return "(System.ValueType)null";
                case SpecialType.System_Boolean:
                    return "false";
                case SpecialType.System_Char:
                    return "'\0'";
                case SpecialType.System_SByte:
                case SpecialType.System_Byte:
                case SpecialType.System_Int16:
                case SpecialType.System_UInt16:
                case SpecialType.System_Int32:
                    return "0";
                case SpecialType.System_UInt32:
                    return "0u";
                case SpecialType.System_Int64:
                    return "0L";
                case SpecialType.System_UInt64:
                    return "0ul";
                case SpecialType.System_Decimal:
                    return "0m";
                case SpecialType.System_Single:
                    return "0f";
                case SpecialType.System_Double:
                    return "0d";
                case SpecialType.System_String:
                    return "\"\"";
                case SpecialType.System_IntPtr:
                    return "System.IntPtr.Zero";
                case SpecialType.System_UIntPtr:
                    return "System.UIntPtr.Zero";
                case SpecialType.System_Nullable_T:
                    break;
                case SpecialType.System_DateTime:
                    return "System.DateTime.Now";
                default:
                    break;
            }

            return "null";
        }
    }
}
