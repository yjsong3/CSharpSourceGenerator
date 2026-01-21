
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Linq;
using System.Text;

namespace SourceGenerator
{
  [Generator]
  public class LogGenerator : IIncrementalGenerator
  {
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
      // 1. [AutoLog] 어트리뷰트 정의 코드를 미리 생성 (PostInitialization)
      context.RegisterPostInitializationOutput(i => i.AddSource(
          "AutoLogAttribute.g.cs",
          SourceText.From(@"
                  using System;
                  namespace MyExtensions
                  {
                      [AttributeUsage(AttributeTargets.Class)]
                      public class AutoLogAttribute : Attribute { }
                  }"
          , Encoding.UTF8)));

      // 2. [AutoLog]가 붙은 클래스 필터링
      var classDeclarations = context.SyntaxProvider
          .CreateSyntaxProvider(
              predicate: (s, _) => s is ClassDeclarationSyntax { AttributeLists.Count: > 0 },
              transform: (ctx, _) => GetSemanticTargetForGeneration(ctx))
          .Where(static m => m is not null);

      // 3. 찾은 대상에 대해 소스 생성 실행
      context.RegisterSourceOutput(classDeclarations,
                static (spc, source) => Execute(spc, source!.Value));
    }

    private static (ClassDeclarationSyntax Class, string Namespace)? GetSemanticTargetForGeneration(GeneratorSyntaxContext context)
    {
      var classDeclaration = (ClassDeclarationSyntax)context.Node;

      foreach (var attributeList in classDeclaration.AttributeLists)
      {
        foreach (var attribute in attributeList.Attributes)
        {
          // 단순 이름 비교가 아닌 세만틱 체크
          var typeSymbol = context.SemanticModel.GetTypeInfo(attribute).Type;
          if (typeSymbol?.ToDisplayString() == "MyExtensions.AutoLogAttribute")
          {
            // 실제 클래스의 네임스페이스 추출 (없으면 빈 문자열)
            var ns = context.SemanticModel.GetDeclaredSymbol(classDeclaration)?
                          .ContainingNamespace?.ToDisplayString();

            // 전역 네임스페이스인 경우 <global namespace>라고 나오므로 처리
            if (ns == null || ns.Contains("<global namespace>")) ns = "";

            return (classDeclaration, ns);
          }
        }
      }
      return null;
    }

    private static void Execute(SourceProductionContext context, (ClassDeclarationSyntax Class, string Namespace) target)
    {
      var classShell = target.Class;
      var ns = target.Namespace;
      string className = classShell.Identifier.Text;

      // 네임스페이스가 있는 경우와 없는 경우를 모두 대응하는 템플릿
      var hasNamespace = !string.IsNullOrWhiteSpace(ns);

      var source = new StringBuilder();
      source.AppendLine("using System;");

      if (hasNamespace)
      {
        source.AppendLine($"namespace {ns}");
        source.AppendLine("{");
      }

      source.AppendLine($@"
          public partial class {className}
          {{
              public void LogGenerated() 
              {{
                  Console.WriteLine(""[SUCCESS] {className} 클래스에 소스 생성기가 코드를 주입했습니다!"");
              }}
          }}");

      if (hasNamespace)
      {
        source.AppendLine("}");
      }

      context.AddSource($"{className}_Generated.g.cs", SourceText.From(source.ToString(), Encoding.UTF8));
    }
  }
}
