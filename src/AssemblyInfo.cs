// Só usado pelo build.sh (o MouseModes.csproj gera estes dados sozinho). A versão vem do arquivo VERSION (Version.g.cs é gerado).
// A exceção é a busca das DLLs nativas, no fim: vale para os dois builds.
#if !SDK_BUILD
using System.Reflection;

[assembly: AssemblyTitle("MouseModes")]
[assembly: AssemblyDescription("Alterna o mouse entre modo Performance e modo Dia a dia.")]
[assembly: AssemblyProduct("MouseModes")]
[assembly: AssemblyCompany("gabrielolvv")]
#endif

// Toda DLL nativa do app (kernel32, user32, powrprof, nvapi64, ControlLib...) vem só do System32: nunca da pasta do
// exe, que é do usuário. Sem isto, uma DLL com o mesmo nome deixada ao lado do MouseModes.exe seria carregada pela
// cópia que roda como administrador (--fps-caps).
[assembly: System.Runtime.InteropServices.DefaultDllImportSearchPaths(System.Runtime.InteropServices.DllImportSearchPath.System32)]
