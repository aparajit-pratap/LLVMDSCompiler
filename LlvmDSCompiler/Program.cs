using System;
using System.Text;
using Antlr4.Runtime;

namespace LlvmDSCompiler
{
    class Program
    {
        static void Main(string[] args)
        {
            var text = @"
def foo : double(a : double, b : double)
{
    return a*a + b*b - 2.0*a*b;
}
def bar : double(a : double)
{
    return foo(a, 1.0 + 3.0) + 3.0*a;
}";
            AntlrInputStream inputStream = new AntlrInputStream(text);
            DesignScriptLexer designScriptLexer = new DesignScriptLexer(inputStream);
            designScriptLexer.RemoveErrorListeners();
            CommonTokenStream commonTokenStream = new CommonTokenStream(designScriptLexer);
            DesignScriptParser designScriptParser = new DesignScriptParser(commonTokenStream);
            designScriptParser.BuildParseTree = true;
            designScriptParser.RemoveErrorListeners();
            var programContext = designScriptParser.program();

            var parseTree = programContext.ToStringTree();
            Console.WriteLine(parseTree);

            var visitor = new DSVisitor();
            //var assocBlk = programContext.Accept(visitor);
            visitor.VisitProgram(programContext);


        }
    }
}
