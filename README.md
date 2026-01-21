# CSharpSourceGenerator

Visual Studio에서 코드 자동 생성기를 위한 소스코드

프로젝트 참조방법 - Analyzer에 레퍼런스가 되어야 한다.
<ItemGroup>
  <ProjectReference Include="..\SourceGenerator\SourceGenerator.csproj" 
                    OutputItemType="Analyzer" 
                    ReferenceOutputAssembly="false" />
</ItemGroup>

사용방법
[AutoLog]
public partial class UserService 
{
    public void Test()
    {
        // 생성기가 만든 메서드가 호출되는지 확인!
        this.LogGenerated(); 
    }
}

[AutoLog]를 추가하려고 하는 클래스에 넣어준다.
그럼 Anayzers -> SourceGenerator.LogGenerator 레퍼런스를 열어서 보면 
AtuoLogAttribute.g.cs
UserService_Generated.g.cs
파일이 보인다.
