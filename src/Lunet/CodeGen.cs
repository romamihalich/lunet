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
        var consoleLibrary = AssemblyDefinition.ReadAssembly(typeof(Console).Assembly.Location);
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

        var locals = new Dictionary<string, (TypeReference type, int id)>();
        var localId = 0;

        foreach (var statement in function.Body)
        {
            switch (statement)
            {
                case ExpressionStatement expressionStatement:
                    {
                        var exprType = CompileExpression(ilProcessor, locals, expressionStatement.Expression);

                        if (exprType != assembly.MainModule.TypeSystem.Void)
                        {
                            ilProcessor.Emit(OpCodes.Pop);
                        }
                        break;
                    }
                case VariableDefinitionStatement variableDefinitionStatement:
                    {
                        var realType = variableDefinitionStatement.Type switch
                        {
                            "string" => assembly.MainModule.TypeSystem.String,
                            "int" => assembly.MainModule.TypeSystem.Int32,
                            "bool" => assembly.MainModule.TypeSystem.Boolean,
                            _ => throw new Exception("Unknown type"),
                        };
                        var exprType = CompileExpression(ilProcessor, locals, variableDefinitionStatement.Rvalue);
                        if (realType != exprType)
                        {
                            throw new Exception("Types not match");
                        }
                        mainMethod.Body.Variables.Add(new VariableDefinition(exprType));
                        ilProcessor.Emit(OpCodes.Stloc, localId);
                        locals[variableDefinitionStatement.Name] = (realType, localId++);
                        break;
                    }
                default:
                    throw new ArgumentOutOfRangeException(nameof(statement));
            }
        }

        ilProcessor.Emit(OpCodes.Ret);
    }

    private TypeReference CompileExpression(ILProcessor ilProcessor, Dictionary<string, (TypeReference type, int id)> locals, IExpression expression)
    {
        switch (expression)
        {
            case IdentExpression(string name):
            {
                if (locals.TryGetValue(name, out var local))
                {
                    ilProcessor.Emit(OpCodes.Ldloc, local.id);
                    return local.type;
                }
                else
                {
                    throw new Exception("variable not found");
                }
            }
            case StringExpression(string value):
            {
                ilProcessor.Emit(OpCodes.Ldstr, value);
                return assembly.MainModule.TypeSystem.String;
            }
            case IntExpression(int value):
            {
                ilProcessor.Emit(OpCodes.Ldc_I4, value);
                return assembly.MainModule.TypeSystem.Int32;
            }
            case BoolExpression(bool value):
            {
                ilProcessor.Emit(OpCodes.Ldc_I4, value ? 1 : 0);
                return assembly.MainModule.TypeSystem.Boolean;
            }
            case UnaryExpression(var kind, var expr):
            {
                var type = CompileExpression(ilProcessor, locals, expr);
                switch (kind)
                {
                    case UnaryKind.Not:
                        if (type == assembly.MainModule.TypeSystem.Boolean)
                        {
                            ilProcessor.Emit(OpCodes.Ceq);
                            return assembly.MainModule.TypeSystem.Boolean;
                        }
                        else
                        {
                            throw new Exception("bad type");
                        }
                    default:
                        throw new ArgumentOutOfRangeException(nameof(kind));
                }
            }
            case BinopExpression binop:
            {
                return CompileBinop(ilProcessor, locals, binop);
            }
            case FunctionCallExpression funcCall:
            {
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
                var parameterTypes = new List<TypeReference>();

                foreach (var arg in funcCall.Args)
                {
                    parameterTypes.Add(CompileExpression(ilProcessor, locals, arg));
                }

                var method = FindStaticMethod(className, methodName, parameterTypes);
                var methodRef = assembly.MainModule.ImportReference(method);

                ilProcessor.Emit(OpCodes.Call, methodRef);

                // TODO: this is temporary
                return assembly.MainModule.TypeSystem.Void;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(expression));
        }
    }

    private TypeReference CompileBinop(ILProcessor ilProcessor, Dictionary<string, (TypeReference type, int id)> locals, BinopExpression binop)
    {
        switch (binop.Kind)
        {
            case BinopKind.Mul:
            {
                var leftType = CompileExpression(ilProcessor, locals, binop.Left);
                var rightType = CompileExpression(ilProcessor, locals, binop.Right);
                if (leftType == assembly.MainModule.TypeSystem.Int32
                    && rightType == assembly.MainModule.TypeSystem.Int32)
                {
                    ilProcessor.Emit(OpCodes.Mul);
                    return assembly.MainModule.TypeSystem.Int32;
                }
                else
                {
                    throw new Exception("types not match");
                }
            }
            case BinopKind.Div:
            {
                var leftType = CompileExpression(ilProcessor, locals, binop.Left);
                var rightType = CompileExpression(ilProcessor, locals, binop.Right);
                if (leftType == assembly.MainModule.TypeSystem.Int32
                    && rightType == assembly.MainModule.TypeSystem.Int32)
                {
                    ilProcessor.Emit(OpCodes.Div);
                    return assembly.MainModule.TypeSystem.Int32;
                }
                else
                {
                    throw new Exception("types not match");
                }
            }
            case BinopKind.Add:
            {
                var leftType = CompileExpression(ilProcessor, locals, binop.Left);
                var rightType = CompileExpression(ilProcessor, locals, binop.Right);
                if (leftType == assembly.MainModule.TypeSystem.Int32
                    && rightType == assembly.MainModule.TypeSystem.Int32)
                {
                    ilProcessor.Emit(OpCodes.Add);
                    return assembly.MainModule.TypeSystem.Int32;
                }
                else
                {
                    throw new Exception("types not match");
                }
            }
            case BinopKind.Sub:
            {
                var leftType = CompileExpression(ilProcessor, locals, binop.Left);
                var rightType = CompileExpression(ilProcessor, locals, binop.Right);
                if (leftType == assembly.MainModule.TypeSystem.Int32
                    && rightType == assembly.MainModule.TypeSystem.Int32)
                {
                    ilProcessor.Emit(OpCodes.Sub);
                    return assembly.MainModule.TypeSystem.Int32;
                }
                else
                {
                    throw new Exception("types not match");
                }
            }
            case BinopKind.Concat:
            {
                var leftType = CompileExpression(ilProcessor, locals, binop.Left);
                var rightType = CompileExpression(ilProcessor, locals, binop.Right);
                if (leftType == assembly.MainModule.TypeSystem.String
                    && rightType == assembly.MainModule.TypeSystem.String)
                {
                    throw new NotImplementedException("string concatenation");
                }
                else
                {
                    throw new Exception("types not match");
                }
            }

            case BinopKind.Equal:
            {
                var leftType = CompileExpression(ilProcessor, locals, binop.Left);
                var rightType = CompileExpression(ilProcessor, locals, binop.Right);
                if (leftType == assembly.MainModule.TypeSystem.Int32
                    && rightType == assembly.MainModule.TypeSystem.Int32)
                {
                    ilProcessor.Emit(OpCodes.Ceq);
                    return assembly.MainModule.TypeSystem.Boolean;
                }
                else
                {
                    throw new Exception("types not match");
                }
            }
            case BinopKind.NotEqual:
            {
                var leftType = CompileExpression(ilProcessor, locals, binop.Left);
                var rightType = CompileExpression(ilProcessor, locals, binop.Right);
                if (leftType == assembly.MainModule.TypeSystem.Int32
                    && rightType == assembly.MainModule.TypeSystem.Int32)
                {
                    ilProcessor.Emit(OpCodes.Ceq);
                    ilProcessor.Emit(OpCodes.Ldc_I4_0);
                    ilProcessor.Emit(OpCodes.Ceq);
                    return assembly.MainModule.TypeSystem.Boolean;
                }
                else
                {
                    throw new Exception("types not match");
                }
            }
            case BinopKind.And:
            {
                var leftType = CompileExpression(ilProcessor, locals, binop.Left);

                var falseLabel = ilProcessor.Create(OpCodes.Ldc_I4_0);
                var afterLabel = ilProcessor.Create(OpCodes.Nop);

                ilProcessor.Emit(OpCodes.Brfalse, falseLabel);

                var rightType = CompileExpression(ilProcessor, locals, binop.Right);

                ilProcessor.Emit(OpCodes.Br, afterLabel);

                ilProcessor.Append(falseLabel);
                ilProcessor.Append(afterLabel);

                if (leftType == assembly.MainModule.TypeSystem.Boolean
                    && rightType == assembly.MainModule.TypeSystem.Boolean)
                {
                    return assembly.MainModule.TypeSystem.Boolean;
                }
                else
                {
                    throw new Exception("types not match");
                }
            }
            case BinopKind.Or:
            {
                var leftType = CompileExpression(ilProcessor, locals, binop.Left);

                var trueLabel = ilProcessor.Create(OpCodes.Ldc_I4_1);
                var afterLabel = ilProcessor.Create(OpCodes.Nop);

                ilProcessor.Emit(OpCodes.Brtrue, trueLabel);

                var rightType = CompileExpression(ilProcessor, locals, binop.Right);

                ilProcessor.Emit(OpCodes.Br, afterLabel);

                ilProcessor.Append(trueLabel);
                ilProcessor.Append(afterLabel);

                if (leftType == assembly.MainModule.TypeSystem.Boolean
                    && rightType == assembly.MainModule.TypeSystem.Boolean)
                {
                    return assembly.MainModule.TypeSystem.Boolean;
                }
                else
                {
                    throw new Exception("types not match");
                }
            }

            case BinopKind.Greater:
            {
                var leftType = CompileExpression(ilProcessor, locals, binop.Left);
                var rightType = CompileExpression(ilProcessor, locals, binop.Right);
                if (leftType == assembly.MainModule.TypeSystem.Int32
                    && rightType == assembly.MainModule.TypeSystem.Int32)
                {
                    ilProcessor.Emit(OpCodes.Cgt);
                    return assembly.MainModule.TypeSystem.Boolean;
                }
                else
                {
                    throw new Exception("types not match");
                }
            }
            case BinopKind.GreaterOrEqual:
            {
                var leftType = CompileExpression(ilProcessor, locals, binop.Left);
                var rightType = CompileExpression(ilProcessor, locals, binop.Right);
                if (leftType == assembly.MainModule.TypeSystem.Int32
                    && rightType == assembly.MainModule.TypeSystem.Int32)
                {
                    ilProcessor.Emit(OpCodes.Clt);
                    ilProcessor.Emit(OpCodes.Ldc_I4_0);
                    ilProcessor.Emit(OpCodes.Ceq);
                    return assembly.MainModule.TypeSystem.Boolean;
                }
                else
                {
                    throw new Exception("types not match");
                }
            }
            case BinopKind.Less:
            {
                var leftType = CompileExpression(ilProcessor, locals, binop.Left);
                var rightType = CompileExpression(ilProcessor, locals, binop.Right);
                if (leftType == assembly.MainModule.TypeSystem.Int32
                    && rightType == assembly.MainModule.TypeSystem.Int32)
                {
                    ilProcessor.Emit(OpCodes.Clt);
                    return assembly.MainModule.TypeSystem.Boolean;
                }
                else
                {
                    throw new Exception("types not match");
                }
            }
            case BinopKind.LessOrEqual:
            {
                var leftType = CompileExpression(ilProcessor, locals, binop.Left);
                var rightType = CompileExpression(ilProcessor, locals, binop.Right);
                if (leftType == assembly.MainModule.TypeSystem.Int32
                    && rightType == assembly.MainModule.TypeSystem.Int32)
                {
                    ilProcessor.Emit(OpCodes.Cgt);
                    ilProcessor.Emit(OpCodes.Ldc_I4_0);
                    ilProcessor.Emit(OpCodes.Ceq);
                    return assembly.MainModule.TypeSystem.Boolean;
                }
                else
                {
                    throw new Exception("types not match");
                }
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(binop.Kind));
        }
    }

    private MethodDefinition? FindStaticMethod(string className, string methodName, IEnumerable<TypeReference> parameterTypes)
    {
        var types = referenceAssemblies
            .SelectMany(a => a.Modules)
            .SelectMany(m => m.Types)
            .Where(t => t.FullName == className);
        var methods = types.SelectMany(t => t.Methods)
            .Where(m => m.IsPublic && m.IsStatic)
            .Where(m => m.Name == methodName)
            .Where(m => m.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(parameterTypes.Select(p => p.FullName)))
            .ToArray();
        if (methods.Length != 1)
        {
            throw new Exception();
        }
        return methods.First();
    }
}