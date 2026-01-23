
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace SourceGenerator
{
  [Generator]
  public class LogGenerator : IIncrementalGenerator
  {
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
      // [STEP 1] 어트리뷰트 선언 코드를 미리 생성 (Post-Initialization)
      // 이 코드는 소스 분석과 상관없이 빌드 시작 시 무조건 한 번 실행됩니다.
      context.RegisterPostInitializationOutput(i => i.AddSource(
          "AutoLogAttribute.g.cs",
          SourceText.From(@"
using System;
namespace MyExtensions
{
    [AttributeUsage(AttributeTargets.Class)]
    public class AutoLogClassAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Property)]
    public class AutoLogPropertyAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    public class AutoLogMethodAttribute : Attribute { }
}", Encoding.UTF8)));

      // [STEP 2] 필터링 파이프라인 구축 (Syntax Provider)
      // 모든 코드 노드(s) 중에서 [AutoLog] 어트리뷰트가 '있을 것 같은' 노드만 1차로 걸러냅니다.
      // Initialize 내부의 파이프라인 설정
      var targets = context.SyntaxProvider
    .CreateSyntaxProvider(
        predicate: (s, _) => s is MemberDeclarationSyntax { AttributeLists.Count: > 0 },
        transform: (ctx, _) => GetSemanticTarget(ctx)) // 우리가 방금 만든 함수!
    .Where(static m => m is not null);

      // [STEP 3] 핵심: Collect()를 사용하여 모든 타겟을 하나로 모음
      IncrementalValueProvider<ImmutableArray<(MemberDeclarationSyntax Member, string Namespace, string AttributeType)?>> collectedTargets = targets.Collect();

      // [STEP 4] 모인 바구니를 Execute로 전달
      context.RegisterSourceOutput(collectedTargets,
          static (spc, source) => Execute(spc, source));
    }

    private static bool IsSyntaxTarget(SyntaxNode node)
    {
      // 클래스, 필드, 속성, 메서드 중 하나이면서 + 어트리뷰트 리스트가 하나라도 있는 녀석인가?
      return node is MemberDeclarationSyntax { AttributeLists.Count: > 0 };
    }    

    private static (MemberDeclarationSyntax Member, string Namespace, string AttributeType)? GetSemanticTarget(GeneratorSyntaxContext context)
    {
      // 1. 노드를 멤버 선언(Class, Property, Method 등)으로 캐스팅
      var member = (MemberDeclarationSyntax)context.Node;

      // 2. 이 멤버에 붙은 어트리뷰트들을 순회
      foreach (var attributeList in member.AttributeLists)
      {
        foreach (var attribute in attributeList.Attributes)
        {
          // 3. 세만틱 모델을 통해 어트리뷰트의 실제 타입을 확인
          var typeSymbol = context.SemanticModel.GetTypeInfo(attribute).Type;
          if (typeSymbol == null) continue;

          string fullName = typeSymbol.ToDisplayString();

          // 4. 어트리뷰트 종류에 따라 판별
          string targetAttr = fullName switch
          {
            "MyExtensions.AutoLogClassAttribute" => "Class",
            "MyExtensions.AutoLogPropertyAttribute" => "Property",
            "MyExtensions.AutoLogMethodAttribute" => "Method",
            _ => null
          };

          // 우리가 찾는 어트리뷰트 중 하나라면 정보를 추출해서 반환
          if (targetAttr != null)
          {
            // 네임스페이스 추출 로직
            var symbol = context.SemanticModel.GetDeclaredSymbol(member);
            var ns = symbol?.ContainingNamespace?.ToDisplayString() ?? "";
            if (ns.Contains("<global namespace>")) ns = "";

            return (member, ns, targetAttr);
          }
        }
      }

      return null;
    }

    // Execute 메서드의 시그니처를 이렇게 변경하세요
    private static void Execute(
        SourceProductionContext context,
        ImmutableArray<(MemberDeclarationSyntax Member, string Namespace, string AttributeType)?> targets)
    {
      // 1. 데이터가 비어있는지 체크
      if (targets.IsDefaultOrEmpty) return;

      // 2. 클래스별로 그룹화 (클래스 하나당 파일 하나를 만들기 위함)
      var groups = targets
          .Where(t => t.HasValue)
          .GroupBy(t => new {
            ClassName = GetClassName(t!.Value.Member),
            Namespace = t.Value.Namespace
          });

      foreach (var group in groups)
      {
        if (group.Key.ClassName == null) continue;

        var bodyBuilder = new StringBuilder();

        foreach (var item in group)
        {
          var member = item!.Value.Member;
          var attrType = item.Value.AttributeType;

          string memberName = GetMemberName(member, group.Key.ClassName);

          // 타입별 코드 스니펫 생성
          string snippet = attrType switch
          {
            "Class" => $@"        public void LogClass() => Console.WriteLine(""[CLASS] {group.Key.ClassName} initialized."");",
            "Property" => $@"        public void LogProp_{memberName}() => Console.WriteLine(""[PROP] {memberName} changed."");",
            "Method" => $@"        public void LogMethod_{memberName}() => Console.WriteLine(""[METHOD] {memberName} executed."");",
            _ => ""
          };
          bodyBuilder.AppendLine(snippet);
        }

        // 템플릿에 합쳐서 파일 생성
        string finalSource = GenerateClassTemplate(group.Key.Namespace, group.Key.ClassName, bodyBuilder.ToString());
        context.AddSource($"{group.Key.ClassName}_Generated.g.cs", SourceText.From(finalSource, Encoding.UTF8));
      }
    }

    private static string GetClassName(MemberDeclarationSyntax member) =>
    member is ClassDeclarationSyntax cds
    ? cds.Identifier.Text
    : member.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault()?.Identifier.Text ?? "Unknown";

    private static string GetMemberName(MemberDeclarationSyntax member, string defaultName) =>
        member switch
        {
          MethodDeclarationSyntax m => m.Identifier.Text,
          PropertyDeclarationSyntax p => p.Identifier.Text,
          FieldDeclarationSyntax f => f.Declaration.Variables[0].Identifier.Text,
          _ => defaultName
        };


    private static string GenerateClassTemplate(string ns, string className, string content)
    {
      var sb = new StringBuilder();
      sb.AppendLine("// <auto-generated/>");
      sb.AppendLine("using System;");
      sb.AppendLine("using System.ComponentModel;");

      if (!string.IsNullOrEmpty(ns))
      {
        sb.AppendLine($"namespace {ns}");
        sb.AppendLine("{");
      }

      sb.AppendLine($@"    public partial class {className}
    {{
{content}
    }}");

      if (!string.IsNullOrEmpty(ns))
      {
        sb.AppendLine("}");
      }

      return sb.ToString();
    }
  }
}

