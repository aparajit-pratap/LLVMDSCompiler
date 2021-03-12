using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using LLVMSharp;

namespace LlvmDSCompiler
{
    class DSVisitor : DesignScriptBaseVisitor<CompilerResult>
    {
        
        private LLVMModuleRef module;
        private LLVMBuilderRef builder;
        private LLVMValueRef function;
        private string functionName;
        private LLVMTypeRef returnType;
        private LLVMPassManagerRef passManager;
        private LLVMExecutionEngineRef engine;

        private readonly Dictionary<string, LLVMValueRef> namedValues = new Dictionary<string, LLVMValueRef>();

        private readonly Stack<LLVMValueRef> valueStack = new Stack<LLVMValueRef>();

        // C# delegate used to call into compiled DS code
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void Bar(double x, double y, double z);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void PrintVectorDelegate(double x, double y, double z);

        [AllowReversePInvokeCalls]
        public static void PrintVector(double x, double y, double z)
        {
            Console.WriteLine("Vector for {0}, {1}, {2} is: {3}", x, y, z, Vector.Vector.ByCoordinates(x, y, z));
            //return Vector.Vector.ByCoordinates(x, y, z);
        }

        private static LLVMValueRef ReversePInvoke(LLVMBuilderRef builder, LLVMTypeRef callTargetTypeRef, 
            LLVMValueRef delegateAddressParameter, LLVMValueRef[] args)
        {
            var ptrType = LLVM.PointerType(callTargetTypeRef, 0);
            var delegateAddress = LLVM.BuildAlloca(builder, LLVM.Int64Type(), "delegate.value");
            var alloca = LLVM.BuildAlloca(builder, ptrType, "delegate.addr");
            LLVM.BuildStore(builder, delegateAddressParameter, delegateAddress);

            LLVM.BuildStore(builder, 
                LLVM.BuildIntToPtr(builder, 
                    LLVM.BuildLoad(builder, delegateAddress, "delegate.addr.load"), 
                    ptrType, 
                    "delegate.funcptr"), 
                alloca);

            return LLVM.BuildCall(builder, LLVM.BuildLoad(builder, alloca, "delegate"), args, string.Empty);
        }

        public override CompilerResult VisitProgram(DesignScriptParser.ProgramContext context)
        {

            var success = new LLVMBool(0);
            module = LLVM.ModuleCreateWithName("DesignScript");
            builder = LLVM.CreateBuilder();

            LLVM.LinkInMCJIT();
            LLVM.InitializeX86TargetInfo();
            LLVM.InitializeX86Target();
            LLVM.InitializeX86TargetMC();

            LLVM.InitializeX86AsmParser();
            LLVM.InitializeX86AsmPrinter();

            LLVMMCJITCompilerOptions options = new LLVMMCJITCompilerOptions { NoFramePointerElim = 1 };
            LLVM.InitializeMCJITCompilerOptions(options);
            if (LLVM.CreateExecutionEngineForModule(out engine, module, out var errorMessage).Value == 1)
            {
                Console.WriteLine(errorMessage);
                return new NullCompilerResult();
            }

#region Add optimization passes
            // Create a function pass manager for this engine
            passManager = LLVM.CreateFunctionPassManagerForModule(module);

            // Do simple "peephole" optimizations and bit-twiddling optzns.
            LLVM.AddInstructionCombiningPass(passManager);

            // Reassociate expressions.
            LLVM.AddReassociatePass(passManager);

            // Eliminate Common SubExpressions.
            LLVM.AddGVNPass(passManager);

            // Simplify the control flow graph (deleting unreachable blocks, etc).
            LLVM.AddCFGSimplificationPass(passManager);

            LLVM.InitializeFunctionPassManager(passManager);
            #endregion

            #region External function declarations

            // TODO: hardcoded param and return types
            var @params = new[] {LLVMTypeRef.DoubleType(), LLVMTypeRef.DoubleType(), LLVMTypeRef.DoubleType()};

            //var vectorType = LLVM.StructType(@params, false);
            var retType = LLVM.VoidType();
            var funcDecl = LLVM.AddFunction(
                module, "PrintVector", LLVM.FunctionType(retType, @params, false));
            LLVM.SetLinkage(funcDecl, LLVMLinkage.LLVMExternalLinkage);

            #endregion

            base.VisitProgram(context);

            if (LLVM.VerifyModule(module, LLVMVerifierFailureAction.LLVMPrintMessageAction, out var error) != success)
            {
                Console.WriteLine($"Error: {error}");
            }

            var barMethod = (Bar)Marshal.GetDelegateForFunctionPointer(LLVM.GetPointerToGlobal(engine, valueStack.Peek()), typeof(Bar));
            barMethod(10, 2, 3);

            if (LLVM.WriteBitcodeToFile(module, "program.bc") != 0)
            {
                Console.WriteLine("error writing bitcode to file, skipping");
            }
            LLVM.DumpModule(module);
            LLVM.DisposeBuilder(builder);
            LLVM.DisposeExecutionEngine(engine);

            return new NullCompilerResult();
        }

        public override CompilerResult VisitFuncDef(DesignScriptParser.FuncDefContext context)
        {
            //var parentBlock = LLVM.GetInsertBlock(builder);

            // TODO: This assumes namedValues only contains identifiers for function args right now.
            namedValues.Clear();

            var typedIdent = context.typedIdent();
            var funcSig = VisitTypedIdent(typedIdent) as TypedIdentCompilerResult;
            functionName = funcSig.Name;
            returnType = (LLVMTypeRef)funcSig.Type;

            VisitFuncDefArgList(context.funcDefArgList());

            function = valueStack.Pop();

            // Create a new basic block to start insertion into.
            LLVMBasicBlockRef entry = LLVM.AppendBasicBlock(function, "entry");
            LLVM.PositionBuilderAtEnd(this.builder, entry);

            var stmts = context.coreStmt();
            foreach (var stmt in stmts)
            {
                VisitCoreStmt(stmt);
            }
            // If last statement is not an explicit return statement,
            // add a return instr explicitly to close the basic block.
            if (stmts.Last().returnStmt() == null)
            {
                LLVM.BuildRet(this.builder, new LLVMValueRef());
            }

            // Validate the generated code, checking for consistency.
            LLVM.VerifyFunction(function, LLVMVerifierFailureAction.LLVMPrintMessageAction);

            LLVM.RunFunctionPassManager(this.passManager, function);

            this.valueStack.Push(function);

            //LLVM.PositionBuilderAtEnd(this.builder, parentBlock);

            return new NullCompilerResult();
        }

        public override CompilerResult VisitTypedIdent(DesignScriptParser.TypedIdentContext context)
        {
            var name = context.Ident().GetText();

            var typeContext = context.typeNameWithRank();

            if(typeContext == null) 
                return new TypedIdentCompilerResult(name, null);

            var type = VisitTypeNameWithRank(typeContext) as TypeCompilerResult;

            return new TypedIdentCompilerResult(name, type.Type);
        }

        public override CompilerResult VisitFuncDefArgList(DesignScriptParser.FuncDefArgListContext context)
        {
            var args = context.funcDefArg();

            function = LLVM.GetNamedFunction(module, functionName);
            // If F conflicted, there was already something named 'Name'.  If it has a
            // body, don't allow redefinition or reextern.
            /*if (function.Pointer != IntPtr.Zero)
            {
                // If F already has a body, reject this.
                if (LLVM.CountBasicBlocks(function) != 0)
                {
                    throw new Exception("redefinition of function.");
                }

                // If F took a different number of args, reject.
                if (LLVM.CountParams(function) != args.Length)
                {
                    throw new Exception("redefinition of function with different # args");
                }
            }*/

            var arguments = new LLVMTypeRef[args.Length];
            var argNames = new string[args.Length];
            for(var i = 0; i < args.Length; i++)
            {
                var type = VisitFuncDefArg(args[i]) as TypedIdentCompilerResult;
                arguments[i] = (LLVMTypeRef)type.Type;
                argNames[i] = type.Name;
            }
            function = LLVM.AddFunction(this.module, functionName, LLVM.FunctionType(returnType, arguments, false));
            LLVM.SetLinkage(function, LLVMLinkage.LLVMExternalLinkage);

            for (int i = 0; i < args.Length; ++i)
            {
                string argumentName = argNames[i];

                LLVMValueRef param = LLVM.GetParam(function, (uint)i);
                LLVM.SetValueName(param, argumentName);

                this.namedValues[argumentName] = param;
            }

            this.valueStack.Push(function);

            return new NullCompilerResult();
        }

        public override CompilerResult VisitFuncDefArg(DesignScriptParser.FuncDefArgContext context)
        {
            // TODO: ignore default expression for now.
            return VisitTypedIdent(context.typedIdent());
        }

        public override CompilerResult VisitCoreStmt(DesignScriptParser.CoreStmtContext context)
        {
            var returnStmt = context.returnStmt();
            if (returnStmt != null)
            {
                VisitReturnStmt(returnStmt);
                return new NullCompilerResult();

            }
            
            return base.VisitCoreStmt(context);
        }

        public override CompilerResult VisitReturnStmt(DesignScriptParser.ReturnStmtContext context)
        {
            var exprList = context.exprList();

            VisitExprList(exprList);

            LLVM.BuildRet(this.builder, this.valueStack.Pop());

            return new NullCompilerResult();
        }

        public override CompilerResult VisitExprList(DesignScriptParser.ExprListContext context)
        {
            foreach (var exprContext in context.expr())
            {
                VisitExpr(exprContext);
            }

            return new NullCompilerResult();
        }

        public override CompilerResult VisitExpr(DesignScriptParser.ExprContext context)
        {
            if (context is DesignScriptParser.PrimaryExprContext primaryExprContext)
            {
                VisitPrimaryExpr(primaryExprContext);
            }
            else if (context is DesignScriptParser.FuncCallExprContext funcCallExprContext)
            {
                VisitFuncCallExpr(funcCallExprContext);
            }
            else if (context is DesignScriptParser.MulDivModExprContext mulDivModExprContext)
            {
                VisitMulDivModExpr(mulDivModExprContext);
            }
            else if (context is DesignScriptParser.AddSubExprContext addSubExprContext)
            {
                VisitAddSubExpr(addSubExprContext);
            }

            return new NullCompilerResult();
        }

        public override CompilerResult VisitPrimaryExpr(DesignScriptParser.PrimaryExprContext context)
        {
            var primary = context.primary();
            if (primary is DesignScriptParser.IdentContext identContext)
            {
                VisitIdent(identContext);
            }
            else if (primary is DesignScriptParser.LitExprContext litExprContext)
            {
                VisitLitExpr(litExprContext);
            }

            return new NullCompilerResult();
        }

        public override CompilerResult VisitFuncCallExpr(DesignScriptParser.FuncCallExprContext context)
        {
            var qualifiedIdentContext = context.qualifiedIdent();
            var funcName = qualifiedIdentContext.Ident(0).GetText();

            
            var calleeF = LLVM.GetNamedFunction(this.module, funcName);
            if (calleeF.Pointer == IntPtr.Zero)
            {
                throw new Exception("Unknown function referenced");
            }

            var argumentCount = LLVM.CountParams(calleeF);
            var argsV = new LLVMValueRef[argumentCount];
            var args = context.exprList();
            var argTypes = new LLVMTypeRef[argumentCount];

            VisitExprList(args);
            for(var i = 0; i < argumentCount; i++)
            {
                var arg = valueStack.Pop();
                argsV[argumentCount - 1 - i] = arg;
                argTypes[argumentCount - 1 - i] = LLVM.TypeOf(arg);
            }

            //valueStack.Push(LLVM.BuildCall(this.builder, calleeF, argsV, "calltmp"));
            // TODO: hardcoded return type
            var callTargetType = LLVM.FunctionType(returnType, argTypes, false);
            var delegateAddress = (ulong)Marshal.GetFunctionPointerForDelegate(new PrintVectorDelegate(PrintVector));
            var delegateAddrParam = LLVM.ConstInt(LLVM.Int64Type(), delegateAddress, true);
            var externFuncCall = ReversePInvoke(builder, callTargetType, delegateAddrParam, argsV);
            valueStack.Push(externFuncCall);

            return new NullCompilerResult();
        }

        public override CompilerResult VisitMulDivModExpr(DesignScriptParser.MulDivModExprContext context)
        {
            VisitExpr(context.expr()[0]);
            var lhs = valueStack.Pop();

            VisitExpr(context.expr()[1]);
            var rhs = valueStack.Pop();
            
            LLVMValueRef val = default;

            if (context.Op.Text == "*")
                val = LLVM.BuildFMul(builder, lhs, rhs, "multmp");
            else if (context.Op.Text == "/")
                val = LLVM.BuildFDiv(builder, lhs, rhs, "divtmp");
            else if (context.Op.Text == "%")
                val = LLVM.BuildFRem(builder, lhs, rhs, "modtmp");

            valueStack.Push(val);

            return new NullCompilerResult();
        }

        public override CompilerResult VisitAddSubExpr(DesignScriptParser.AddSubExprContext context)
        {
            VisitExpr(context.expr()[0]);
            var lhs = valueStack.Pop();

            VisitExpr(context.expr()[1]);
            var rhs = valueStack.Pop();

            LLVMValueRef val = default;

            if (context.Op.Text == "+")
                val = LLVM.BuildFAdd(builder, lhs, rhs, "addtmp");
            else if (context.Op.Text == "-")
                val = LLVM.BuildFSub(builder, lhs, rhs, "subtmp");

            valueStack.Push(val);

            return new NullCompilerResult();
        }

        public override CompilerResult VisitLitExpr(DesignScriptParser.LitExprContext context)
        {
            var litContext = context.lit();
            if (litContext is DesignScriptParser.DoubleLitContext doubleLitContext)
            {
                VisitDoubleLit(doubleLitContext);
            }
            else if (litContext is DesignScriptParser.IntLitContext intLitContext)
            {
                VisitIntLit(intLitContext);
            }
            return new NullCompilerResult();
        }

        public override CompilerResult VisitDoubleLit(DesignScriptParser.DoubleLitContext context)
        {
            var val = LLVM.ConstReal(LLVM.DoubleType(), double.Parse(context.DoubleLit().GetText()));
            valueStack.Push(val);

            return new NullCompilerResult();
        }

        public override CompilerResult VisitIntLit(DesignScriptParser.IntLitContext context)
        {
            var val = LLVM.ConstInt(LLVM.Int32Type(), uint.Parse(context.IntLit().GetText()), true);
            valueStack.Push(val);

            return new NullCompilerResult();
        }


        public override CompilerResult VisitAssignStmt(DesignScriptParser.AssignStmtContext context)
        {
            /*var list = context.typedIdentList();

            // Assume typedIdentList has just one identifier right now.
            var lhs = list.typedIdent(0);
            var typedIdentResult = VisitTypedIdent(lhs);*/

            return base.VisitAssignStmt(context);
        }



        public override CompilerResult VisitTypeNameWithRank(DesignScriptParser.TypeNameWithRankContext context)
        {
            if (context is DesignScriptParser.TypeNameRankContext rankContext)
                return VisitTypeNameRank(rankContext);
            
            return new NullCompilerResult();
        }

        public override CompilerResult VisitTypeNameArbitraryRank(DesignScriptParser.TypeNameArbitraryRankContext context)
        {
            return base.VisitTypeNameArbitraryRank(context);
        }

        public override CompilerResult VisitTypeNameRankOneOrMore(DesignScriptParser.TypeNameRankOneOrMoreContext context)
        {
            return base.VisitTypeNameRankOneOrMore(context);
        }

        public override CompilerResult VisitTypeNameRank(DesignScriptParser.TypeNameRankContext context)
        {
            var type = VisitTypeName(context.typeName()) as TypeCompilerResult;
            var rank = context.LBRACK().Length;
            if(rank == 0) return type;

            var arrayType = LLVM.ArrayType((LLVMTypeRef)type.Type, 0);

            return new TypeCompilerResult(arrayType);
        }
        
        public override CompilerResult VisitTypeName(DesignScriptParser.TypeNameContext context)
        {
            LLVMTypeRef? type = null;
            switch (context.GetText())
            {
                case "void":
                    type = LLVMTypeRef.VoidType();
                    break;
                case "double":
                    type = LLVMTypeRef.DoubleType();
                    break;
            }
            return new TypeCompilerResult(type);
        }

        public override CompilerResult VisitIdent(DesignScriptParser.IdentContext context)
        {
            // Look this variable up.
            if (this.namedValues.TryGetValue(context.Ident().GetText(), out LLVMValueRef value))
            {
                this.valueStack.Push(value);
            }
            else
            {
                throw new Exception("Unknown variable name");
            }

            return new NullCompilerResult();
        }

        
    }

}
