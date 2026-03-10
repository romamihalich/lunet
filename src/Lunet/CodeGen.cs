using System.Diagnostics.CodeAnalysis;

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

        if (!diagnostics.HasError)
        {
            assembly.Write(outFilePath);
            File.WriteAllText(
                path: Path.ChangeExtension(outFilePath, ".runtimeconfig.json"),
                contents: RuntimeConfig
            );
        }
    }
}

internal class Compilation
{
    private readonly TypeReference _unknownType;

    private readonly AssemblyDefinition _assembly;
    private readonly AssemblyDefinition[] _referenceAssemblies;
    private readonly Diagnostics _diagnostics;

    private readonly TypeDefinition _programClass;

    private readonly List<string> _importedNamespaces = [];
    private readonly Dictionary<string, MethodDefinition> _localFunctions = [];

    public Compilation(AssemblyDefinition assembly, AssemblyDefinition[] referenceAssemblies, Diagnostics diagnostics)
    {
        _assembly = assembly;
        _referenceAssemblies = referenceAssemblies;
        _diagnostics = diagnostics;

        _programClass = new TypeDefinition("", "Program", TypeAttributes.Class, assembly.MainModule.TypeSystem.Object);
        assembly.MainModule.Types.Add(_programClass);

        _unknownType = new TypeReference("", "UnkownType", _assembly.MainModule, _assembly.MainModule.TypeSystem.CoreLibrary);
    }

    public void Compile(Ast ast)
    {
        CollectAllFunctions(ast);
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
                throw new ArgumentOutOfRangeException(nameof(statement));
        }
    }

    private void CompileImportStatement(ImportStatement import)
    {
        var namespaceName = string.Join(".", import.Path.Path);

        var found = _referenceAssemblies
            .SelectMany(a => a.Modules)
            .Any(m => m.Types.Any(t => t.Namespace == namespaceName));
        if (!found)
        {
            _diagnostics.AddError($"Namespace '{namespaceName}' is not found in reference assemblies", import.Location);
        }
        _importedNamespaces.Add(namespaceName);
    }

    private void CollectAllFunctions(Ast ast)
    {
        foreach (var function in ast.Statements.OfType<FunctionStatement>())
        {
            var returnType = LookupType(function.ReturnType);
            if (returnType == null)
            {
                _diagnostics.AddError($"Unkown type '{function.ReturnType}'", function.ReturnTypeLocation);
            }
            returnType ??= _unknownType;
            var method = new MethodDefinition(function.Name, MethodAttributes.Private | MethodAttributes.Static, returnType);
            _programClass.Methods.Add(method);

            foreach (var parameter in function.Parameters)
            {
                var type = LookupType(parameter.Type);

                if (type == null)
                {
                    _diagnostics.AddError($"Unkown type '{parameter.Type}'", parameter.TypeLocation);
                }

                type ??= _unknownType;
                method.Parameters.Add(new ParameterDefinition(type));
            }

            if (function.Name == "main")
            {
                _assembly.MainModule.EntryPoint = method;
            }

            _localFunctions.Add(function.Name, method);
        }
    }

    private void CompileFunctionStatement(FunctionStatement function)
    {
        var method = _localFunctions[function.Name];

        var scope = new FunctionScope();

        foreach (var parameter in function.Parameters)
        {
            var name = parameter.Name;
            var type = LookupType(parameter.Type);

            scope.SetArg(name, type);
        }

        var ilProcessor = method.Body.GetILProcessor();

        CompileBlock(ilProcessor, scope, method, function.Body);

        ilProcessor.Emit(OpCodes.Ret);
    }

    private void CompileBlock(ILProcessor ilProcessor, FunctionScope scope, MethodDefinition method, IReadOnlyList<IStatement> block)
    {
        using var _ = scope.EnterScope();
        foreach (var statement in block)
        {
            switch (statement)
            {
                case ExpressionStatement expressionStatement:
                    {
                        var exprType = CompileExpression(ilProcessor, scope, expressionStatement.Expression);

                        if (exprType != null
                            && !exprType.IsSameType(_assembly.MainModule.TypeSystem.Void))
                        {
                            ilProcessor.Emit(OpCodes.Pop);
                        }
                        break;
                    }
                case VariableDefinitionStatement variableDefinitionStatement:
                    {
                        var realType = LookupType(variableDefinitionStatement.Type);
                        if (realType == null)
                        {
                            _diagnostics.AddError($"Unkown type '{variableDefinitionStatement.Type}'", variableDefinitionStatement.TypeLocation);
                        }
                        var exprType = CompileExpression(ilProcessor, scope, variableDefinitionStatement.Rvalue);
                        if (realType != null && exprType != null && !realType.IsSameType(exprType))
                        {
                            _diagnostics.AddError($"Cannot convert type '{exprType.FullName}' to '{realType.FullName}'", variableDefinitionStatement.TypeLocation);
                        }
                        method.Body.Variables.Add(new VariableDefinition(exprType));

                        var local = scope.SetLocal(variableDefinitionStatement.Name, realType);
                        ilProcessor.Emit(OpCodes.Stloc, local.Id);
                        break;
                    }
                case IfStatement ifStatement:
                    {
                        var condType = CompileExpression(ilProcessor, scope, ifStatement.Condition);
                        if (condType != null && !condType.IsSameType(_assembly.MainModule.TypeSystem.Boolean))
                        {
                            _diagnostics.AddError("Condition must be of type bool", ifStatement.Condition.Location);
                        }
                        var elseLabel = ilProcessor.Create(OpCodes.Nop);
                        var afterLabel = ilProcessor.Create(OpCodes.Nop);
                        ilProcessor.Emit(OpCodes.Brfalse, elseLabel);
                        CompileBlock(ilProcessor, scope, method, ifStatement.Block);
                        ilProcessor.Emit(OpCodes.Br, afterLabel);
                        ilProcessor.Append(elseLabel);
                        if (ifStatement.ElseBlock != null)
                        {
                            CompileBlock(ilProcessor, scope, method, ifStatement.ElseBlock);
                        }
                        ilProcessor.Append(afterLabel);
                        break;
                    }
                case WhileStatement whileStatement:
                    {
                        var loopStart = ilProcessor.Create(OpCodes.Nop);
                        var loopEnd = ilProcessor.Create(OpCodes.Nop);

                        ilProcessor.Append(loopStart);
                        var condType = CompileExpression(ilProcessor, scope, whileStatement.Condition);
                        if (condType != null && !condType.IsSameType(_assembly.MainModule.TypeSystem.Boolean))
                        {
                            _diagnostics.AddError("Condition must be of type bool", whileStatement.Condition.Location);
                        }
                        ilProcessor.Emit(OpCodes.Brfalse, loopEnd);
                        CompileBlock(ilProcessor, scope, method, whileStatement.Block);
                        ilProcessor.Emit(OpCodes.Br, loopStart);
                        ilProcessor.Append(loopEnd);
                        break;
                    }
                case AssignmentStatement(var name, var rvalue):
                    {
                        var hasArg = scope.TryGetArg(name.Ident, out var arg);
                        var hasLocal = scope.TryGetLocal(name.Ident, out var local);
                        if (!hasLocal)
                        {
                            _diagnostics.AddError($"Variable '{name}' is not found", name.Location);
                        }
                        var rvalueType = CompileExpression(ilProcessor, scope, rvalue);
                        if (rvalueType != null
                            && local.Type != null
                            && !rvalueType.IsSameType(local.Type))
                        {
                            _diagnostics.AddError($"Cannot convert type '{rvalueType.FullName}' to '{local.Type.FullName}'", rvalue.Location);
                        }
                        if (hasLocal)
                        {
                            ilProcessor.Emit(OpCodes.Stloc, local.Id);
                        }
                        if (hasArg)
                        {
                            ilProcessor.Emit(OpCodes.Starg, arg.Id);
                        }
                        break;
                    }
                case ReturnStatement returnStatement:
                    {
                        var returnType = CompileExpression(ilProcessor, scope, returnStatement.Expression);
                        if (returnType != null && !method.ReturnType.IsSameType(_unknownType) && !returnType.IsSameType(method.ReturnType))
                        {
                            _diagnostics.AddError($"Type of expression '{returnType.FullName}' does not match return type '{method.ReturnType.FullName}'", returnStatement.Expression.Location);
                        }
                        ilProcessor.Emit(OpCodes.Ret);
                        break;
                    }
                default:
                    throw new ArgumentOutOfRangeException(nameof(statement));
            }
        }
    }

    private TypeReference? CompileExpression(ILProcessor ilProcessor, FunctionScope scope, IExpression expression)
    {
        switch (expression)
        {
            case ParenthesisedExpression parenthesisedExpression:
            {
                return CompileExpression(ilProcessor, scope, parenthesisedExpression.Expression);
            }
            case IdentExpression(var name, var location):
            {
                if (scope.TryGetArg(name, out var arg))
                {
                    ilProcessor.Emit(OpCodes.Ldarg, arg.Id);
                    return arg.Type;
                }
                else if (scope.TryGetLocal(name, out var local))
                {
                    ilProcessor.Emit(OpCodes.Ldloc, local.Id);
                    return local.Type;
                }
                else
                {
                    _diagnostics.AddError($"Variable '{name}' is not found", location);
                    return null;
                }
            }
            case StringExpression(string value, _):
            {
                ilProcessor.Emit(OpCodes.Ldstr, value);
                return _assembly.MainModule.TypeSystem.String;
            }
            case IntExpression(int value, _):
            {
                ilProcessor.Emit(OpCodes.Ldc_I4, value);
                return _assembly.MainModule.TypeSystem.Int32;
            }
            case BoolExpression(bool value, _):
            {
                ilProcessor.Emit(OpCodes.Ldc_I4, value ? 1 : 0);
                return _assembly.MainModule.TypeSystem.Boolean;
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
                        if (type.IsSameType(_assembly.MainModule.TypeSystem.Boolean))
                        {
                            ilProcessor.Emit(OpCodes.Ceq);
                            return _assembly.MainModule.TypeSystem.Boolean;
                        }
                        else
                        {
                            _diagnostics.AddError($"Operator 'not' cannot be applied to operand of type '{type.FullName}'", location);
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
                if (funcCall.Expression is IdentExpression identExpression)
                {
                    var localMethod = _localFunctions[identExpression.Name];
                    if (localMethod.Parameters.Count != funcCall.Args.Count)
                    {
                        _diagnostics.AddError($"Expected {localMethod.Parameters.Count}, but was provided {funcCall.Args.Count}", funcCall.Location);
                        return null;
                    }
                    var actualParameterTypes = new List<TypeReference>();
                    foreach (var arg in funcCall.Args)
                    {
                        var parameterType = CompileExpression(ilProcessor, scope, arg);
                        if (parameterType == null)
                        {
                            return null;
                        }
                        actualParameterTypes.Add(parameterType);
                    }
                    for (int i = 0; i < actualParameterTypes.Count; i++)
                    {
                        var expectedType = localMethod.Parameters[i].ParameterType;
                        if (!expectedType.IsSameType(_unknownType)
                            && !expectedType.IsSameType(actualParameterTypes[i]))
                        {
                            _diagnostics.AddError($"Cannot convert type '{actualParameterTypes[i].FullName}' to '{expectedType.FullName}'", funcCall.Args[i].Location);
                        }
                    }
                    ilProcessor.Emit(OpCodes.Call, localMethod);
                    return !localMethod.ReturnType.IsSameType(_unknownType)
                        ? localMethod.ReturnType
                        : null;

                }

                if (funcCall.Expression is not QualifiedIdentExpression name)
                {
                    throw new NotImplementedException(funcCall.Expression.GetType().ToString());
                }

                var namespaceOrClassPath = name.Path.Path;
                if (!namespaceOrClassPath.Any())
                {
                    throw new NotImplementedException("local function not supported yet");
                }
                if (name.Ident is null)
                {
                    throw new NotImplementedException();
                }
                // ["System", "Console"] ident: "WriteLine"

                var prefix = name.Path.Path[0];
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

                var className = string.Join(".", name.Path.Path);
                var methodName = name.Ident;
                var method = FindStaticMethod(className, methodName, parameterTypes);
                if (method == null)
                {
                    _diagnostics.AddError($"Could not find method '{className}.{methodName}'", name.Location);
                    return null;
                }
                var methodRef = _assembly.MainModule.ImportReference(method);

                ilProcessor.Emit(OpCodes.Call, methodRef);

                return methodRef.ReturnType;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(expression));
        }
    }

    private TypeReference? CompileBinop(ILProcessor ilProcessor, FunctionScope scope, BinopExpression binop)
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

                if (leftType.IsSameType(_assembly.MainModule.TypeSystem.Int32)
                    && rightType.IsSameType(_assembly.MainModule.TypeSystem.Int32))
                {
                    ilProcessor.Emit(OpCodes.Mul);
                    return _assembly.MainModule.TypeSystem.Int32;
                }
                else
                {
                    _diagnostics.AddError($"Operator '{binop.Kind.GetText()}' cannot be applied to operands of type {leftType.FullName} and {rightType.FullName}", binop.Location);
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

                if (leftType.IsSameType(_assembly.MainModule.TypeSystem.Int32)
                    && rightType.IsSameType(_assembly.MainModule.TypeSystem.Int32))
                {
                    ilProcessor.Emit(OpCodes.Div);
                    return _assembly.MainModule.TypeSystem.Int32;
                }
                else
                {
                    _diagnostics.AddError($"Operator '{binop.Kind.GetText()}' cannot be applied to operands of type {leftType.FullName} and {rightType.FullName}", binop.Location);
                    return null;
                }
            }
            case BinopKind.Mod:
            {
                var leftType = CompileExpression(ilProcessor, scope, binop.Left);
                var rightType = CompileExpression(ilProcessor, scope, binop.Right);

                if (leftType == null || rightType == null)
                {
                    return null;
                }

                if (leftType.IsSameType(_assembly.MainModule.TypeSystem.Int32)
                    && rightType.IsSameType(_assembly.MainModule.TypeSystem.Int32))
                {
                    ilProcessor.Emit(OpCodes.Rem);
                    return _assembly.MainModule.TypeSystem.Int32;
                }
                else
                {
                    _diagnostics.AddError($"Operator '{binop.Kind.GetText()}' cannot be applied to operands of type {leftType.FullName} and {rightType.FullName}", binop.Location);
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

                if (leftType.IsSameType(_assembly.MainModule.TypeSystem.Int32)
                    && rightType.IsSameType(_assembly.MainModule.TypeSystem.Int32))
                {
                    ilProcessor.Emit(OpCodes.Add);
                    return _assembly.MainModule.TypeSystem.Int32;
                }
                else
                {
                    _diagnostics.AddError($"Operator '{binop.Kind.GetText()}' cannot be applied to operands of type {leftType.FullName} and {rightType.FullName}", binop.Location);
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

                if (leftType.IsSameType(_assembly.MainModule.TypeSystem.Int32)
                    && rightType.IsSameType(_assembly.MainModule.TypeSystem.Int32))
                {
                    ilProcessor.Emit(OpCodes.Sub);
                    return _assembly.MainModule.TypeSystem.Int32;
                }
                else
                {
                    _diagnostics.AddError($"Operator '{binop.Kind.GetText()}' cannot be applied to operands of type {leftType.FullName} and {rightType.FullName}", binop.Location);
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

                if (leftType.IsSameType(_assembly.MainModule.TypeSystem.String)
                    && rightType.IsSameType(_assembly.MainModule.TypeSystem.String))
                {
                    throw new NotImplementedException("string concatenation");
                }
                else
                {
                    _diagnostics.AddError($"Operator '{binop.Kind.GetText()}' cannot be applied to operands of type {leftType.FullName} and {rightType.FullName}", binop.Location);
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

                if (leftType.IsSameType(_assembly.MainModule.TypeSystem.Int32)
                    && rightType.IsSameType(_assembly.MainModule.TypeSystem.Int32))
                {
                    ilProcessor.Emit(OpCodes.Ceq);
                    return _assembly.MainModule.TypeSystem.Boolean;
                }
                else
                {
                    _diagnostics.AddError($"Operator '{binop.Kind.GetText()}' cannot be applied to operands of type {leftType.FullName} and {rightType.FullName}", binop.Location);
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

                if (leftType.IsSameType(_assembly.MainModule.TypeSystem.Int32)
                    && rightType.IsSameType(_assembly.MainModule.TypeSystem.Int32))
                {
                    ilProcessor.Emit(OpCodes.Ceq);
                    ilProcessor.Emit(OpCodes.Ldc_I4_0);
                    ilProcessor.Emit(OpCodes.Ceq);
                    return _assembly.MainModule.TypeSystem.Boolean;
                }
                else
                {
                    _diagnostics.AddError($"Operator '{binop.Kind.GetText()}' cannot be applied to operands of type {leftType.FullName} and {rightType.FullName}", binop.Location);
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

                if (leftType.IsSameType(_assembly.MainModule.TypeSystem.Boolean)
                    && rightType.IsSameType(_assembly.MainModule.TypeSystem.Boolean))
                {
                    return _assembly.MainModule.TypeSystem.Boolean;
                }
                else
                {
                    _diagnostics.AddError($"Operator '{binop.Kind.GetText()}' cannot be applied to operands of type {leftType.FullName} and {rightType.FullName}", binop.Location);
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

                if (leftType.IsSameType(_assembly.MainModule.TypeSystem.Boolean)
                    && rightType.IsSameType(_assembly.MainModule.TypeSystem.Boolean))
                {
                    return _assembly.MainModule.TypeSystem.Boolean;
                }
                else
                {
                    _diagnostics.AddError($"Operator '{binop.Kind.GetText()}' cannot be applied to operands of type {leftType.FullName} and {rightType.FullName}", binop.Location);
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

                if (leftType.IsSameType(_assembly.MainModule.TypeSystem.Int32)
                    && rightType.IsSameType(_assembly.MainModule.TypeSystem.Int32))
                {
                    ilProcessor.Emit(OpCodes.Cgt);
                    return _assembly.MainModule.TypeSystem.Boolean;
                }
                else
                {
                    _diagnostics.AddError($"Operator '{binop.Kind.GetText()}' cannot be applied to operands of type {leftType.FullName} and {rightType.FullName}", binop.Location);
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

                if (leftType.IsSameType(_assembly.MainModule.TypeSystem.Int32)
                    && rightType.IsSameType(_assembly.MainModule.TypeSystem.Int32))
                {
                    ilProcessor.Emit(OpCodes.Clt);
                    ilProcessor.Emit(OpCodes.Ldc_I4_0);
                    ilProcessor.Emit(OpCodes.Ceq);
                    return _assembly.MainModule.TypeSystem.Boolean;
                }
                else
                {
                    _diagnostics.AddError($"Operator '{binop.Kind.GetText()}' cannot be applied to operands of type {leftType.FullName} and {rightType.FullName}", binop.Location);
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

                if (leftType.IsSameType(_assembly.MainModule.TypeSystem.Int32)
                    && rightType.IsSameType(_assembly.MainModule.TypeSystem.Int32))
                {
                    ilProcessor.Emit(OpCodes.Clt);
                    return _assembly.MainModule.TypeSystem.Boolean;
                }
                else
                {
                    _diagnostics.AddError($"Operator '{binop.Kind.GetText()}' cannot be applied to operands of type {leftType.FullName} and {rightType.FullName}", binop.Location);
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

                if (leftType.IsSameType(_assembly.MainModule.TypeSystem.Int32)
                    && rightType.IsSameType(_assembly.MainModule.TypeSystem.Int32))
                {
                    ilProcessor.Emit(OpCodes.Cgt);
                    ilProcessor.Emit(OpCodes.Ldc_I4_0);
                    ilProcessor.Emit(OpCodes.Ceq);
                    return _assembly.MainModule.TypeSystem.Boolean;
                }
                else
                {
                    _diagnostics.AddError($"Operator '{binop.Kind.GetText()}' cannot be applied to operands of type {leftType.FullName} and {rightType.FullName}", binop.Location);
                    return null;
                }
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(binop.Kind));
        }
    }

    private TypeReference? LookupType(string type)
    {
        return type switch
        {
            "string" => _assembly.MainModule.TypeSystem.String,
            "int" => _assembly.MainModule.TypeSystem.Int32,
            "bool" => _assembly.MainModule.TypeSystem.Boolean,
            "void" => _assembly.MainModule.TypeSystem.Void,
            _ => null,
        };
    }

    private MethodDefinition? FindStaticMethod(string className, string methodName, IEnumerable<TypeReference> parameterTypes)
    {
        var types = _referenceAssemblies
            .SelectMany(a => a.Modules)
            .SelectMany(m => m.Types)
            .Where(t => t.FullName == className);
        var methods = types.SelectMany(t => t.Methods)
            .Where(m => m.IsPublic && m.IsStatic)
            .Where(m => m.Name == methodName)
            .Where(m => m.Parameters.Select(p => p.ParameterType).SequenceEqual(parameterTypes, TypeReferenceEqualityComparer.Instance))
            .ToArray();
        if (methods.Length != 1)
        {
            return null;
        }
        return methods.First();
    }
}

internal record struct LocalVariable(int Id, TypeReference? Type);

internal class FunctionScope
{
    private readonly Dictionary<string, LocalVariable> _args = [];
    private readonly List<Dictionary<string, LocalVariable>> _locals = [];
    private int _localId;
    private int _argId;

    public IDisposable EnterScope()
    {
        _locals.Add([]);
        return new Dummy(this);
    }

    public void LeaveScope()
    {
        _locals.RemoveAt(_locals.Count - 1);
    }

    public bool TryGetArg(string name, out LocalVariable arg)
    {
        return _args.TryGetValue(name, out arg);
    }

    public LocalVariable SetArg(string name, TypeReference? type)
    {
        return _args[name] = new(_argId++, type);
    }

    public bool TryGetLocal(string name, out LocalVariable local)
    {
        for (int i = _locals.Count - 1; i >= 0; i--)
        {
            if (_locals[i].TryGetValue(name, out local))
            {
                return true;
            }
        }
        local = default;
        return false;
    }

    public LocalVariable SetLocal(string name, TypeReference? type)
    {
        return _locals[^1][name] = new(_localId++, type);
    }

    private class Dummy(FunctionScope? scope) : IDisposable
    {
        public void Dispose()
        {
            scope?.LeaveScope();
            scope = null;
        }
    }
}

public static class TypeReferenceExtensions
{
    public static bool IsSameType(this TypeReference self, TypeReference other)
    {
        ArgumentNullException.ThrowIfNull(self);
        ArgumentNullException.ThrowIfNull(other);

        if (self == other)
        {
            return true;
        }

        if (self.FullName != other.FullName)
        {
            return false;
        }

        var selfTypeDefinition = self.Resolve();
        var otherTypeDefinition = other.Resolve();

        return selfTypeDefinition.Scope.Name == otherTypeDefinition.Scope.Name;
    }
}

public class TypeReferenceEqualityComparer : IEqualityComparer<TypeReference>
{
    public static TypeReferenceEqualityComparer Instance { get; } = new();

    public bool Equals(TypeReference? x, TypeReference? y)
    {
        if (x == null || y == null)
        {
            return x == y;
        }
        return x.IsSameType(y);
    }

    public int GetHashCode([DisallowNull] TypeReference obj)
    {
        // TODO: this is kind of bad
        return obj.GetHashCode();
    }
}