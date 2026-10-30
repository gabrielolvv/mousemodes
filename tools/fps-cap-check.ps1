param([string]$Exe = (Join-Path $env:LOCALAPPDATA 'Programs\MouseModes\MouseModes.exe'))
# Teste do limite de FPS do MouseModes numa placa NVIDIA ou Intel Arc (FPS limit test on an NVIDIA or Intel Arc card).
#
# Grava um limite de 77 FPS para um executável que não existe (mousemodes-fps-check.exe), relê, tira e relê de novo.
# Nenhum jogo de verdade é mexido, e no fim o driver fica como estava. Rode no PowerShell como administrador:
#   powershell -ExecutionPolicy Bypass -File .\fps-cap-check.ps1
# e mande o texto que aparecer (ele também fica em fps-cap-check.txt, na Área de Trabalho).
$lines = @()
function Say([string]$text) { $script:lines += $text; Write-Host $text }
$test = 'mousemodes-fps-check.exe'
try {
  $asm = [Reflection.Assembly]::LoadFrom($Exe)
  $principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
  Say ("MouseModes " + $asm.GetName().Version + " · admin: " + $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator) +
       " · 64 bits: " + [Environment]::Is64BitProcess)
  Get-CimInstance Win32_VideoController | ForEach-Object { Say ("placa / card: " + $_.Name + " (driver " + $_.DriverVersion + ")") }
  $static = [Reflection.BindingFlags]'Static,NonPublic'
  $limiter = $null
  foreach ($type in @('MouseModes.NvidiaLimiter', 'MouseModes.IntelLimiter')) {
    $found = $asm.GetType($type).GetMethod('TryCreate', $static).Invoke($null, @())
    Say ($type + ": " + $(if ($found -eq $null) { 'não encontrado / not found' } else { 'ok' }))
    if ($limiter -eq $null -and $found -ne $null) { $limiter = $found }
  }
  if ($limiter -eq $null) { Say 'Nenhum limitador / no limiter: fim.' }
  else {
    foreach ($step in @(@('ler / read', -1), @('gravar / write 77', 77), @('ler / read', -1), @('tirar / clear', 0), @('ler / read', -1))) {
      if ($step[1] -lt 0) {
        $read = [object[]]@($test, 0)
        $error1 = $limiter.GetType().GetMethod('Read').Invoke($limiter, $read)
        Say ($step[0] + ": " + $(if ($error1 -eq $null) { [string]$read[1] + ' FPS' } else { 'erro / error: ' + $error1 }))
      } else {
        $write = [object[]]@($test, [int]$step[1], $false)
        $error2 = $limiter.GetType().GetMethod('Write').Invoke($limiter, $write)
        Say ($step[0] + ": " + $(if ($error2 -eq $null) { 'ok' } else { 'erro / error: ' + $error2 + ' (admin: ' + $write[2] + ')' }))
      }
    }
    Say 'Esperado / expected: 0 FPS, ok, 77 FPS, ok, 0 FPS.'
  }
} catch { Say ("erro / error: " + $_.Exception.ToString()) }
$lines | Out-File -Encoding utf8 (Join-Path ([Environment]::GetFolderPath('Desktop')) 'fps-cap-check.txt')
