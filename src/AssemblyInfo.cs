// Só usado pelo build.sh (o MouseModes.csproj gera estes dados sozinho). A versão vem do arquivo VERSION (Version.g.cs é gerado).
#if !SDK_BUILD
using System.Reflection;

[assembly: AssemblyTitle("MouseModes")]
[assembly: AssemblyDescription("Alterna o mouse entre modo Performance e modo Dia a dia.")]
[assembly: AssemblyProduct("MouseModes")]
[assembly: AssemblyCompany("gabrielolvv")]
#endif
