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

    public static void Generate(Ast ast, string outFilePath, Diagnostics diagnostics)
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

        new Compilation(assembly, referenceAssemblies, diagnostics).Compile(ast);

        assembly.Write(outFilePath);
        File.WriteAllText(
            path: Path.ChangeExtension(outFilePath, ".runtimeconfig.json"),
            contents: RuntimeConfig
        );
    }
}

internal class Compilation(AssemblyDefinition assembly, AssemblyDefinition[] referenceAssemblies, Diagnostics diagnostics)
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
            diagnostics.AddError($"Namespace '{namespaceName}' is not found in reference assemblies", import.Location);
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

        var scope = new Scope();

        foreach (var statement in function.Body)
        {
            switch (statement)
            {
                case ExpressionStatement expressionStatement:
                    {
                        var exprType = CompileExpression(ilProcessor, scope, expressionStatement.Expression);

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
                            _ => null,
                        };
                        if (realType == null)
                        {
                            diagnostics.AddError($"Unkown type '{variableDefinitionStatement.Type}'", variableDefinitionStatement.TypeLocation);
                        }
                        var exprType = CompileExpression(ilProcessor, scope, variableDefinitionStatement.Rvalue);
                        if (realType != null && exprType != null && realType != exprType)
                        {
                            diagnostics.AddError($"Cannot convert type '{exprType.FullName}' to '{realType.FullName}'", variableDefinitionStatement.TypeLocation);
                        }
                        mainMethod.Body.Variables.Add(new VariableDefinition(exprType));

                        var local = scope.SetLocal(variableDefinitionStatement.Name, realType);
                        ilProcessor.Emit(OpCodes.Stloc, local.Id);
                        break;
                    }
                default:
                    throw new ArgumentOutOfRangeException(nameof(statement));
            }
        }

        ilProcessor.Emit(OpCodes.Ret);
    }

    private TypeReference? CompileExpression(ILProcessor ilProcessor, Scope scope, IExpression expression)
    {
        switch (expression)
        {
            case ParenthesisedExpression parenthesisedExpression:
            {
                return CompileExpression(ilProcessor, scope, parenthesisedExpression.Expression);
            }
            case IdentExpression(var name, var location):
            {
                if (scope.TryGetLocal(name, out var local))
                {
                    ilProcessor.Emit(OpCodes.Ldloc, local.Id);
                    return local.Type;
                }
                else
                {
                    diagnostics.AddError($"Variable '{name}' is not found", location);
                    return null;
                }
            }
            case StringExpression(string value, _):
            {
                ilProcessor.Emit(OpCodes.Ldstr, value);
                return assembly.MainModule.TypeSystem.String;
            }
            case IntExpression(int value, _):
            {
                ilProcessor.Emit(OpCodes.Ldc_I4, value);
                return assembly.MainModule.TypeSystem.Int32;
            }
            case BoolExpression(bool value, _):
            {
                ilProcessor.Emit(OpCodes.Ldc_I4, value ? 1 : 0);
                return assembly.MainModule.TypeSystem.Boolean;
            }
            case UnaryExpression(var kind, var expr, var location):
            {
                var type = CompileExpression(ilProcessor, scope, expr);
                if (type == null)
                {
                    return null;
                }
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
                            diagnostics.AddError($"Operator 'not' cannot be applied to operand of type '{type.FullName}'", location);
                            return null;
                        }
                    default:
                        throw new ArgumentOutOfRangeException(nameof(kind));
                }
            }
            case BinopExpression binop:
            {
                return CompileBinop(ilProcessor, scope, binop);
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

                var parameterTypes = new List<TypeReference>();

                foreach (var arg in funcCall.Args)
                {
                    var parameterType = CompileExpression(ilProcessor, scope, arg);
                    if (parameterType == null)
                    {
                        return null;
                    }
                    parameterTypes.Add(parameterType);
                }

                var className = string.Join(".", funcCall.Name.Path.Path);
                var methodName = funcCall.Name.Ident;
                var method = FindStaticMethod(className, methodName, parameterTypes);
                if (method == null)
                {
                    diagnostics.AddError($"Could not find method '{className}.{methodName}'", funcCall.Name.Location);
                    return null;
                }
                var methodRef = assembly.MainModule.ImportReference(method);

                ilProcessor.Emit(OpCodes.Call, methodRef);

                // TODO: this is temporary
                return assembly.MainModule.TypeSystem.Void;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(expression));
        }
    }

    private TypeReference? CompileBinop(ILProcessor ilProcessor, Scope scope, BinopExpression binop)
    {
        switch (binop.Kind)
        {
            case BinopKind.Mul:
            {
                var leftType = CompileExpression(ilProcessor, scope, binop.Left);
                var rightType = CompileExpression(ilProcessor, scope, binop.Right);

                if (leftType == null || rightType == null)
                {
                    return null;
                }

                if (leftType == assembly.MainModule.TypeSystem.Int32
                    && rightType == assembly.MainModule.TypeSystem.Int32)
                {
                    ilProcessor.Emit(OpCodes.Mul);
                    return assembly.MainModule.TypeSystem.Int32;
                }
                else
                {
                    diagnostics.AddError($"Operator '{binop.Kind.GetText()}' cannot be applied to operands of type {leftType.FullName} and {rightType.FullName}", binop.Location);
                    return null;
                }
            }
            case BinopKind.Div:
            {
                var leftType = CompileExpression(ilProcessor, scope, binop.Left);
                var rightType = CompileExpression(ilProcessor, scope, binop.Right);

                if (leftType == null || rightType == null)
                {
                    return null;
                }

                if (leftType == assembly.MainModule.TypeSystem.Int32
                    && rightType == assembly.MainModule.TypeSystem.Int32)
                {
                    ilProcessor.Emit(OpCodes.Div);
                    return assembly.MainModule.TypeSystem.Int32;
                }
                else
                {
                    diagnostics.AddError($"Operator '{binop.Kind.GetText()}' cannot be applied to operands of type {leftType.FullName} and {rightType.FullName}", binop.Location);
                    return null;
                }
            }
            case BinopKind.Add:
            {
                var leftType = CompileExpression(ilProcessor, scope, binop.Left);
                var rightType = CompileExpression(ilProcessor, scope, binop.Right);

                if (leftType == null || rightType == null)
                {
                    return null;
                }

                if (leftType == assembly.MainModule.TypeSystem.Int32
                    && rightType == assembly.MainModule.TypeSystem.Int32)
                {
                    ilProcessor.Emit(OpCodes.Add);
                    return assembly.MainModule.TypeSystem.Int32;
                }
                else
                {
                    diagnostics.AddError($"Operator '{binop.Kind.GetText()}' cannot be applied to operands of type {leftType.FullName} and {rightType.FullName}", binop.Location);
                    return null;
                }
            }
            case BinopKind.Sub:
            {
                var leftType = CompileExpression(ilProcessor, scope, binop.Left);
                var rightType = CompileExpression(ilProcessor, scope, binop.Right);

                if (leftType == null || rightType == null)
                {
                    return null;
                }

                if (leftType == assembly.MainModule.TypeSystem.Int32
                    && rightType == assembly.MainModule.TypeSystem.Int32)
                {
                    ilProcessor.Emit(OpCodes.Sub);
                    return assembly.MainModule.TypeSystem.Int32;
                }
                else
                {
                    diagnostics.AddError($"Operator '{binop.Kind.GetText()}' cannot be applied to operands of type {leftType.FullName} and {rightType.FullName}", binop.Location);
                    return null;
                }
            }
            case BinopKind.Concat:
            {
                var leftType = CompileExpression(ilProcessor, scope, binop.Left);
                var rightType = CompileExpression(ilProcessor, scope, binop.Right);

                if (leftType == null || rightType == null)
                {
                    return null;
                }

                if (leftType == assembly.MainModule.TypeSystem.String
                    && rightType == assembly.MainModule.TypeSystem.String)
                {
                    throw new NotImplementedException("string concatenation");
                }
                else
                {
                    diagnostics.AddError($"Operator '{binop.Kind.GetText()}' cannot be applied to operands of type {leftType.FullName} and {rightType.FullName}", binop.Location);
                    return null;
                }
            }

            case BinopKind.Equal:
            {
                var leftType = CompileExpression(ilProcessor, scope, binop.Left);
                var rightType = CompileExpression(ilProcessor, scope, binop.Right);

                if (leftType == null || rightType == null)
                {
                    return null;
                }

                if (leftType == assembly.MainModule.TypeSystem.Int32
                    && rightType == assembly.MainModule.TypeSystem.Int32)
                {
                    ilProcessor.Emit(OpCodes.Ceq);
                    return assembly.MainModule.TypeSystem.Boolean;
                }
                else
                {
                    diagnostics.AddError($"Operator '{binop.Kind.GetText()}' cannot be applied to operands of type {leftType.FullName} and {rightType.FullName}", binop.Location);
                    return null;
                }
            }
            case BinopKind.NotEqual:
            {
                var leftType = CompileExpression(ilProcessor, scope, binop.Left);
                var rightType = CompileExpression(ilProcessor, scope, binop.Right);

                if (leftType == null || rightType == null)
                {
                    return null;
                }

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
                    diagnostics.AddError($"Operator '{binop.Kind.GetText()}' cannot be applied to operands of type {leftType.FullName} and {rightType.FullName}", binop.Location);
                    return null;
                }
            }
            case BinopKind.And:
            {
                var leftType = CompileExpression(ilProcessor, scope, binop.Left);

                var falseLabel = ilProcessor.Create(OpCodes.Ldc_I4_0);
                var afterLabel = ilProcessor.Create(OpCodes.Nop);

                ilProcessor.Emit(OpCodes.Brfalse, falseLabel);

                var rightType = CompileExpression(ilProcessor, scope, binop.Right);

                ilProcessor.Emit(OpCodes.Br, afterLabel);

                ilProcessor.Append(falseLabel);
                ilProcessor.Append(afterLabel);

                if (leftType == null || rightType == null)
                {
                    return null;
                }

                if (leftType == assembly.MainModule.TypeSystem.Boolean
                    && rightType == assembly.MainModule.TypeSystem.Boolean)
                {
                    return assembly.MainModule.TypeSystem.Boolean;
                }
                else
                {
                    diagnostics.AddError($"Operator '{binop.Kind.GetText()}' cannot be applied to operands of type {leftType.FullName} and {rightType.FullName}", binop.Location);
                    return null;
                }
            }
            case BinopKind.Or:
            {
                var leftType = CompileExpression(ilProcessor, scope, binop.Left);

                var trueLabel = ilProcessor.Create(OpCodes.Ldc_I4_1);
                var afterLabel = ilProcessor.Create(OpCodes.Nop);

                ilProcessor.Emit(OpCodes.Brtrue, trueLabel);

                var rightType = CompileExpression(ilProcessor, scope, binop.Right);

                ilProcessor.Emit(OpCodes.Br, afterLabel);

                ilProcessor.Append(trueLabel);
                ilProcessor.Append(afterLabel);

                if (leftType == null || rightType == null)
                {
                    return null;
                }

                if (leftType == assembly.MainModule.TypeSystem.Boolean
                    && rightType == assembly.MainModule.TypeSystem.Boolean)
                {
                    return assembly.MainModule.TypeSystem.Boolean;
                }
                else
                {
                    diagnostics.AddError($"Operator '{binop.Kind.GetText()}' cannot be applied to operands of type {leftType.FullName} and {rightType.FullName}", binop.Location);
                    return null;
                }
            }

            case BinopKind.Greater:
            {
                var leftType = CompileExpression(ilProcessor, scope, binop.Left);
                var rightType = CompileExpression(ilProcessor, scope, binop.Right);

                if (leftType == null || rightType == null)
                {
                    return null;
                }

                if (leftType == assembly.MainModule.TypeSystem.Int32
                    && rightType == assembly.MainModule.TypeSystem.Int32)
                {
                    ilProcessor.Emit(OpCodes.Cgt);
                    return assembly.MainModule.TypeSystem.Boolean;
                }
                else
                {
                    diagnostics.AddError($"Operator '{binop.Kind.GetText()}' cannot be applied to operands of type {leftType.FullName} and {rightType.FullName}", binop.Location);
                    return null;
                }
            }
            case BinopKind.GreaterOrEqual:
            {
                var leftType = CompileExpression(ilProcessor, scope, binop.Left);
                var rightType = CompileExpression(ilProcessor, scope, binop.Right);

                if (leftType == null || rightType == null)
                {
                    return null;
                }

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
                    diagnostics.AddError($"Operator '{binop.Kind.GetText()}' cannot be applied to operands of type {leftType.FullName} and {rightType.FullName}", binop.Location);
                    return null;
                }
            }
            case BinopKind.Less:
            {
                var leftType = CompileExpression(ilProcessor, scope, binop.Left);
                var rightType = CompileExpression(ilProcessor, scope, binop.Right);

                if (leftType == null || rightType == null)
                {
                    return null;
                }

                if (leftType == assembly.MainModule.TypeSystem.Int32
                    && rightType == assembly.MainModule.TypeSystem.Int32)
                {
                    ilProcessor.Emit(OpCodes.Clt);
                    return assembly.MainModule.TypeSystem.Boolean;
                }
                else
                {
                    diagnostics.AddError($"Operator '{binop.Kind.GetText()}' cannot be applied to operands of type {leftType.FullName} and {rightType.FullName}", binop.Location);
                    return null;
                }
            }
            case BinopKind.LessOrEqual:
            {
                var leftType = CompileExpression(ilProcessor, scope, binop.Left);
                var rightType = CompileExpression(ilProcessor, scope, binop.Right);

                if (leftType == null || rightType == null)
                {
                    return null;
                }

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
                    diagnostics.AddError($"Operator '{binop.Kind.GetText()}' cannot be applied to operands of type {leftType.FullName} and {rightType.FullName}", binop.Location);
                    return null;
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
            return null;
        }
        return methods.First();
    }
}

internal record struct LocalVariable(int Id, TypeReference? Type);

internal class Scope
{
    private readonly Dictionary<string, LocalVariable> _locals = [];
    private int _localId;

    public bool TryGetLocal(string name, out LocalVariable local)
    {
        return _locals.TryGetValue(name, out local);
    }

    public LocalVariable SetLocal(string name, TypeReference? type)
    {
        // if (_locals.ContainsKey(name))
        // {
        //     return null;
        // }
        return _locals[name] = new(_localId++, type);
    }
}