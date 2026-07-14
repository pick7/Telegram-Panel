<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="{{UPSTREAM_REL}}/src/TelegramPanel.Modules.Abstractions/TelegramPanel.Modules.Abstractions.csproj" />
    <ProjectReference Include="{{UPSTREAM_REL}}/src/TelegramPanel.Core/TelegramPanel.Core.csproj" />
    <ProjectReference Include="{{UPSTREAM_REL}}/src/TelegramPanel.Data/TelegramPanel.Data.csproj" />
  </ItemGroup>

  <ItemGroup>
    <Content Remove="publish/**" />
    <None Remove="publish/**" />
    <Content Include="wwwroot\**\*" CopyToOutputDirectory="PreserveNewest" CopyToPublishDirectory="PreserveNewest" />
  </ItemGroup>
</Project>
