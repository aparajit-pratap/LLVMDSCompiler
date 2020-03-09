using System;
using System.Collections.Generic;
using System.Text;
using LLVMSharp;

namespace LlvmDSCompiler
{
    class CompilerResult
    {
    }

    class NullCompilerResult : CompilerResult
    {

    }

    class TypeCompilerResult : CompilerResult
    {
        public LLVMTypeRef? Type { get; }

        public TypeCompilerResult(LLVMTypeRef? type)
        {
            Type = type;
        }
    }

    class TypedIdentCompilerResult : TypeCompilerResult
    {
        public string Name { get; }

        public TypedIdentCompilerResult(string name, LLVMTypeRef? type) : base(type)
        {
            Name = name;
        }

    }
}
