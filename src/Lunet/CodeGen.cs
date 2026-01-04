using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Lunet;

public class CodeGen
{
    private const string RuntimeConfig = """
    {
      "runtimeOptions": {
        "tfm": "net9.0",
        "framework": {
          "name": "Microsoft.NETCore.App",
          "version": "9.0.0"
        },
        "configProperties": {
          "System.Runtime.Serialization.EnableUnsafeBinaryFormatterSerialization": false
        }
      }
    }
    """;

    public static void Generate(Ast ast, string outFilePath)
    {
        var moduleName = "hello";

        // var imports = ast.Statements.OfType<ImportStatement>().ToArray();

        var coreLibrary = AssemblyDefinition.ReadAssembly(typeof(object).Assembly.Location);
        var consoleLibrary = AssemblyDefinition.ReadAssembly(typeof(System.Console).Assembly.Location);
        var referenceAssemblies = new[]
        {
            coreLibrary,
            consoleLibrary,
        };
        //
        // var importedNamespaces = new List<string>();
        //
        // foreach (var import in imports)
        // {
        //     var namespaceName = string.Join(".", import.Path);
        //
        //     var found = referenceAssemblies
        //         .SelectMany(a => a.Modules)
        //         .Any(m => m.Types.Any(t => t.Namespace == namespaceName));
        //     if (!found)
        //     {
        //         throw new Exception("bad import");
        //     }
        //     importedNamespaces.Add(namespaceName);
        // }

        var assemblyName = new AssemblyNameDefinition(moduleName, new Version(1, 0));
        var assembly = AssemblyDefinition.CreateAssembly(assemblyName, moduleName, ModuleKind.Console);

        new Compilation(assembly, referenceAssemblies).Compile(ast);

        assembly.Write(outFilePath);
        File.WriteAllText(
            path: Path.ChangeExtension(outFilePath, ".runtimeconfig.json"),
            contents: RuntimeConfig
        );
    }
}

internal class Compilation(AssemblyDefinition assembly, AssemblyDefinition[] referenceAssemblies)
{
    private readonly List<string> _importedNamespaces = [];

    public void Compile(Ast ast)
    {
        foreach (var statement in ast.Statements)
        {
            CompileTopLevelStatement(statement);
        }
    }

    private void CompileTopLevelStatement(ITopLevelStatement statement)
    {
        switch (statement)
        {
            case ImportStatement import:
            {
                CompileImportStatement(import);
                break;
            }
            case FunctionStatement function:
            {
                CompileFunctionStatement(function);
                break;
            }
            default:
                throw new Exception();
        }
    }

    private void CompileImportStatement(ImportStatement import)
    {
        var namespaceName = string.Join(".", import.Path.Path);

        var found = referenceAssemblies
            .SelectMany(a => a.Modules)
            .Any(m => m.Types.Any(t => t.Namespace == namespaceName));
        if (!found)
        {
            throw new Exception("bad import");
        }
        _importedNamespaces.Add(namespaceName);
    }

    private void CompileFunctionStatement(FunctionStatement function)
    {
        if (function.Name == "main")
        {
            // var moduleDefinition = ModuleDefinition.CreateModule("module", ModuleKind.Console);
            var programClass = new TypeDefinition("", "Program", TypeAttributes.Class, assembly.MainModule.TypeSystem.Object);
            // moduleDefinition.Types.Add(typeDefinition);
            assembly.MainModule.Types.Add(programClass);

            // var mscorlib = AssemblyDefinition.ReadAssembly("/usr/share/dotnet/sdk/9.0.110/ref/mscorlib.dll");
            // var voidType = mscorlib.MainModule.ExportedTypes.First(x => x.FullName == "System.Void");
            // var voidDef = voidType.Resolve();
            // // var voidRef = new TypeReference(voidType.Namespace, voidType.Name, mscorlib.MainModule, voidType.Scope);
            // assemblyDefinition.MainModule.ImportReference(voidDef);

            // var consoleClass = assembly.MainModule.GetType("System.Console");
            var mainMethod = new MethodDefinition("Main", MethodAttributes.Private | MethodAttributes.Static, assembly.MainModule.TypeSystem.Void);
            programClass.Methods.Add(mainMethod);
            assembly.MainModule.EntryPoint = mainMethod;

            CompileFunction(function, mainMethod);
        }
        else
        {
            throw new NotImplementedException("only main function is supporteds now");
        }
    }

    private void CompileFunction(FunctionStatement function, MethodDefinition mainMethod)
    {
        var ilProcessor = mainMethod.Body.GetILProcessor();

        foreach (var funcStatement in function.Body)
        {
            switch (funcStatement)
            {
                case ExpressionStatement expressionStatement:
                    var funcCall = expressionStatement.Expression;
                    var namespaceOrClassPath = funcCall.Name.Path.Path;
                    if (!namespaceOrClassPath.Any())
                    {
                        throw new NotImplementedException("local function not supported yet");
                    }
                    if (funcCall.Name.Ident is null)
                    {
                        throw new NotImplementedException();
                    }
                    // ["System", "Console"] ident: "WriteLine"

                    var prefix = funcCall.Name.Path.Path[0];
                    if (!_importedNamespaces.Any(x => x.Split('.').Last() == prefix))
                    {
                        throw new NotImplementedException();
                    }

                    var className = string.Join(".", funcCall.Name.Path.Path);
                    var methodName = funcCall.Name.Ident;
                    var parameterTypes = new List<string>();

                    foreach (var arg in funcCall.Args)
                    {
                        switch (arg)
                        {
                            case StringExpression(string value):
                                ilProcessor.Emit(OpCodes.Ldstr, value);
                                parameterTypes.Add("System.String");
                                break;
                            case FunctionCallExpression(QualifiedIdentExpression name, IReadOnlyList<IExpression> args):
                                throw new NotImplementedException();
                            default:
                                throw new Exception();
                        }
                    }

                    var method = FindStaticMethod(className, methodName, parameterTypes);
                    var methodRef = assembly.MainModule.ImportReference(method);

                    ilProcessor.Emit(OpCodes.Call, methodRef);
                    break;
                default:
                    throw new Exception();
            }
        }

        ilProcessor.Emit(OpCodes.Ret);
    }

    private MethodDefinition? FindStaticMethod(string className, string methodName, IEnumerable<string> parameterTypes)
    {
        var types = referenceAssemblies
            .SelectMany(a => a.Modules)
            .SelectMany(m => m.Types)
            .Where(t => t.FullName == className);
        var methods = types.SelectMany(t => t.Methods)
            .Where(m => m.IsPublic && m.IsStatic)
            .Where(m => m.Name == methodName)
            .Where(m => m.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(parameterTypes))
            .ToArray();
        if (methods.Length != 1)
        {
            throw new Exception();
        }
        return methods.First();
    }
}