namespace MouseModes
{
    /// <summary>
    /// Recursos que só entram no build pessoal.
    ///   ./build.sh          versão pública (Release do GitHub): sem controle de DPI
    ///   ./build.sh pessoal  dist/MouseModes-Pessoal.exe com DPI_CONTROL (DPI do Darmoshark por modo/jogo)
    /// </summary>
    public static class Features
    {
#if DPI_CONTROL
        public static readonly bool DpiControl = true;
#else
        public static readonly bool DpiControl = false;
#endif
    }
}
